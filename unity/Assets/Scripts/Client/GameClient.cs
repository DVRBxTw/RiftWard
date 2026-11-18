using System;
using System.Collections.Generic;
using System.Linq;
using RiftWard.Sim;
using UnityEngine;

namespace RiftWard.Client
{
    /// Owns a mission: input, camera, rendering of the simulation, effects and HUD.
    /// Offline it also runs the authoritative GameSim locally.
    public sealed class GameClient : MonoBehaviour
    {
        const float LookSensitivity = 0.0025f;
        const float BaseFov = 75f;
        static readonly Vector3 WeaponRest = new Vector3(0.27f, -0.23f, 0.48f);
        static readonly Vector3 ThirdPersonOffset = new Vector3(0.75f, 0.35f, -3.2f);

        public GameSim Sim { get; private set; }
        public int LocalId = 1;
        public Camera Camera { get; private set; }
        public World World { get; private set; }
        public Hud Hud { get; private set; }
        public Sfx Sfx { get; private set; }
        public Menus Menus { get; private set; }

        float yaw, pitch, recoilPending, shake, zoomFov = BaseFov;
        bool thirdPerson;
        int desiredSlot, shownSlot = -1;
        float lastReloadRatio;
        int lastReloadSlot = -1;
        float heartbeat;
        bool releaseFireToResume;
        public bool Paused;
        Transform weaponHolder, muzzle, magazine;
        Vector3 magazineRest;

        readonly Dictionary<int, GameObject> actorViews = new Dictionary<int, GameObject>();
        readonly Dictionary<int, float> enemyHp = new Dictionary<int, float>();
        readonly List<int> stale = new List<int>();
        // Looked up once per enemy view instead of searching the hierarchy every frame.
        readonly Dictionary<int, (Transform laser, CharacterRig rig)> enemyParts = new Dictionary<int, (Transform, CharacterRig)>();
        readonly HashSet<int> plasmaMounted = new HashSet<int>();
        readonly Dictionary<int, List<float>> bladeSlots = new Dictionary<int, List<float>>();
        /// Render-side mission clock: advances every frame and eases toward Sim.Elapsed, which only changes per
        /// simulation tick (or per snapshot online). Orbiting gadgets use it so they move smoothly at any frame rate.
        float visualClock;
        readonly Dictionary<int, CharacterRig> actorRigs = new Dictionary<int, CharacterRig>();   // reused each frame to find views whose entity is gone
        readonly Dictionary<int, List<Transform>> droneViews = new Dictionary<int, List<Transform>>();
        readonly Dictionary<int, TextMesh> nameTags = new Dictionary<int, TextMesh>();
        readonly Dictionary<int, List<Transform>> bladeViews = new Dictionary<int, List<Transform>>();
        readonly Dictionary<int, GameObject> enemyViews = new Dictionary<int, GameObject>();
        readonly Dictionary<int, GameObject> bulletViews = new Dictionary<int, GameObject>();
        readonly Dictionary<int, GameObject> pickupViews = new Dictionary<int, GameObject>();
        readonly List<(GameObject go, float life, Vector3 velocity, float spin)> effects = new List<(GameObject, float, Vector3, float)>();
        MissionState lastState = MissionState.Idle;

        public event Action<GameSim> MissionEnded;
        /// Persistent objective line (training steps); shown in the HUD toast slot when set.
        public string Objective = "";
        GameObject showcase;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindAnyObjectByType<GameClient>() != null || FindAnyObjectByType<AppRoot>() != null) return;
            new GameObject("RiftWard").AddComponent<AppRoot>();
        }

        public void Setup()
        {
            // Frame rate / vSync come from GameSettings.Apply (幀率上限).
            Time.fixedDeltaTime = 1f / 60f;
            var camGo = new GameObject("Camera");
            Camera = camGo.AddComponent<Camera>();
            Camera.cullingMask &= ~(1 << ModelPreview.Layer);
            Camera.fieldOfView = BaseFov;
            Camera.nearClipPlane = 0.05f;
            Camera.farClipPlane = 260f;
            PostFx.Attach(Camera);
            camGo.AddComponent<AudioListener>();
            weaponHolder = new GameObject("Weapon").transform;
            weaponHolder.SetParent(Camera.transform, false);
            weaponHolder.localPosition = WeaponRest;
            World = new World(Camera);
            Hud = new Hud();
            Hud.Show(false);
            Sfx = new Sfx(gameObject);
            Menus = new Menus(this);
        }

        // ------------------------------------------------------------ missions

        /// Safe training ground: one stationary target, no waves.
        public void StartTraining()
        {
            Sim = new GameSim();
            Sim.AddActor(LocalId, null, false);
            Sim.StartMission(0);
            Sim.WaveDelay = float.MaxValue;
            var me = Sim.Actors[LocalId];
            var dummy = new Enemy { Id = 9999, Position = me.Position + new Vector3(0, 0, 9), Hp = 56, MaxHp = 56, Speed = 0, Shoots = false, RangedOnly = true, MeleeCooldown = float.MaxValue, Xp = 0 };
            Sim.Enemies[dummy.Id] = dummy;
            EnterMission(0);
        }

        // ------------------------------------------------------------ networked missions

        NetClient net;
        int inputSeq;
        float predictedSpeed = Defs.BaseSpeed;
        bool predictedFly;
        readonly List<PlayerInput> pendingInputs = new List<PlayerInput>();

        /// Mirror mission driven by server snapshots; the local player is predicted and reconciled.
        public void StartNetworked(int region, int localId, NetClient client)
        {
            net = client;
            LocalId = localId;
            Sim = new GameSim();
            Sim.Region = region;
            Sim.State = MissionState.Playing;
            pendingInputs.Clear();
            NetworkInput = input =>
            {
                input.Seq = ++inputSeq;
                net.Send(Msg.Input, w => w.Write(input));
                pendingInputs.Add(input);
                if (pendingInputs.Count > 120) pendingInputs.RemoveAt(0);
                if (Sim.Actors.TryGetValue(LocalId, out var me) && !Paused) GameSim.MoveActor(me, input, Time.fixedDeltaTime, predictedSpeed, predictedFly);
                return true;
            };
            ChooseBuffRemote = code => net.Send(Msg.ChooseBuff, w => w.Write(code));
            EnterMission(region);
        }

        public void ApplySnapshot(System.IO.BinaryReader r)
        {
            if (Sim == null) return;
            int ack = Protocol.ReadSnapshot(r, Sim, LocalId, out predictedSpeed, out predictedFly);
            pendingInputs.RemoveAll(i => i.Seq <= ack);
            if (Sim.Actors.TryGetValue(LocalId, out var me) && me.Alive && !Paused)
                foreach (var input in pendingInputs) GameSim.MoveActor(me, input, Time.fixedDeltaTime, predictedSpeed, predictedFly);
        }

        public void ApplyEvents(System.IO.BinaryReader r) { if (Sim != null) Protocol.ReadEvents(r, Sim); }

        public void ApplyBuffOffer(int level, int[] offer)
        {
            if (Sim == null) return;
            Sim.Level = level;
            Sim.BuffPending = true;
            Sim.BuffOffers.Clear();
            Sim.BuffOffers[LocalId] = offer;
        }

        /// Server reported the mission result; the result sheet is shown by the app.
        public void MarkFinished(bool success)
        {
            if (Sim == null) return;
            Sim.State = success ? MissionState.Victory : MissionState.Defeat;
            lastState = Sim.State;
        }

        public void StartOffline(int region, Loadout loadout, bool admin, AdminSettings adminSettings)
        {
            net = null;
            NetworkInput = null;
            ChooseBuffRemote = null;
            LocalId = 1;
            Sim = new GameSim();
            Sim.AddActor(LocalId, loadout, admin);
            if (admin && adminSettings != null) Sim.SetAdmin(LocalId, adminSettings);
            Sim.StartMission(region);
            EnterMission(region);
        }

        void EnterMission(int region)
        {
            ClearViews();
            World.ApplyRegion(region);
            yaw = 0; pitch = 0; desiredSlot = 0; shownSlot = -1; recoilPending = 0; Paused = false;
            lastState = MissionState.Playing;
            Hud.ResetTransient();
            if (showcase != null) showcase.SetActive(false);
            Hud.Show(true);
            LockCursor(true);
        }

        public void LeaveMission()
        {
            Sim = null;
            net = null;
            NetworkInput = null;
            ChooseBuffRemote = null;
            ClearViews();
            Hud.Show(false);
            LockCursor(false);
        }

        void ClearViews()
        {
            foreach (var d in new[] { actorViews, enemyViews, bulletViews, pickupViews })
            {
                foreach (var v in d.Values) Destroy(v);
                d.Clear();
            }
            foreach (var e in effects) Destroy(e.go);
            effects.Clear();
            // Gadgets and name tags are separate world objects, not children of the actor view.
            foreach (var list in droneViews.Values) foreach (var t in list) if (t != null) Destroy(t.gameObject);
            foreach (var list in bladeViews.Values) foreach (var t in list) if (t != null) Destroy(t.gameObject);
            foreach (var tag in nameTags.Values) if (tag != null) Destroy(tag.gameObject);
            droneViews.Clear(); bladeViews.Clear(); nameTags.Clear(); bladeSlots.Clear();
            enemyHp.Clear(); enemyParts.Clear(); actorRigs.Clear(); plasmaMounted.Clear();
        }

        /// Lobby backdrop: the selected region with a slowly orbiting camera and a large soldier.
        public void ShowShowcase(int region)
        {
            World.ApplyRegion(region);
            if (showcase == null)
            {
                showcase = Models.Soldier(Palette.Player);
                showcase.transform.position = new Vector3(10, 0, 0);
                showcase.transform.rotation = Quaternion.Euler(0, 215, 0);
                showcase.transform.localScale = Vector3.one * 2.5f;
            }
            // Refreshed on every visit so a new loadout or copy shows up (SetWeapon skips unchanged weapons).
            string shown = ShowcaseWeapon?.Invoke() ?? "rifle";
            showcase.GetComponent<CharacterRig>()?.SetWeapon(shown, WeaponRarity?.Invoke(shown) ?? 1);
            showcase.SetActive(true);
            weaponHolder.gameObject.SetActive(false);
            Camera.fieldOfView = BaseFov;
        }

        public void SetThirdPerson(bool on) => thirdPerson = on;
        /// Benchmark mode: hold the trigger without a mouse.
        public bool BenchFire;
        public void SelectSlot(int slot) => desiredSlot = slot;
        /// Rarity of the local player's weapons (from the account profile) for the first-person finish.
        public Func<string, int> WeaponRarity;
        /// Weapon the lobby showcase soldier holds (the player's first equipped gun).
        public Func<string> ShowcaseWeapon;
        public void SetView(float newYaw, float newPitch) { yaw = newYaw; pitch = newPitch; }

        public static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        bool Active => Sim != null && Sim.State == MissionState.Playing && !Paused && !Sim.BuffPending && Cursor.lockState == CursorLockMode.Locked;

        // ------------------------------------------------------------ input & simulation

        void Update()
        {
            if (Sim == null)
            {
                if (showcase != null && showcase.activeSelf)
                {
                    float t = Time.time * 0.08f;
                    Camera.transform.position = new Vector3(16 + Mathf.Sin(t) * 2, 11, -19);
                    Camera.transform.LookAt(new Vector3(0, 2, 6));
                }
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape) && Sim.State == MissionState.Playing && !Sim.BuffPending)
            {
                if (Paused) Menus.ClosePause(); else Menus.OpenPause();
            }
            if (Sim.State == MissionState.Playing && !Paused && !Sim.BuffPending && Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
            {
                LockCursor(true);
                releaseFireToResume = true;
            }
            if (!Input.GetMouseButton(0)) releaseFireToResume = false;
            if (Active)
            {
                yaw = Mathf.Repeat(yaw + Input.GetAxisRaw("Mouse X") * LookSensitivity * 10f * GameSettings.Sensitivity + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;
                pitch = Mathf.Clamp(pitch + Input.GetAxisRaw("Mouse Y") * LookSensitivity * 10f * GameSettings.Sensitivity, -Defs.LookLimit, Defs.LookLimit);
                if (Bindings.Pressed(Bindings.Action.View)) thirdPerson = !thirdPerson;
                if (Bindings.Pressed(Bindings.Action.Slot1)) desiredSlot = 0;
                if (Bindings.Pressed(Bindings.Action.Slot2)) desiredSlot = 1;
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f && Sim.Actors.TryGetValue(LocalId, out var me))
                    desiredSlot = (desiredSlot + 1) % Mathf.Max(1, me.Loadout.Equipped.Count);
            }
            Render(Time.deltaTime);
        }

        void FixedUpdate()
        {
            if (Sim == null) return;
            bool active = Active;
            Vector3 eye = Sim.Actors.TryGetValue(LocalId, out var me) ? me.Position + Vector3.up * Defs.EyeHeight : Camera.transform.position;
            Vector3 origin = thirdPerson ? Camera.transform.position : eye;
            var input = new PlayerInput
            {
                Move = active ? new Vector2((Bindings.Held(Bindings.Action.Right) ? 1 : 0) - (Bindings.Held(Bindings.Action.Left) ? 1 : 0), (Bindings.Held(Bindings.Action.Forward) ? 1 : 0) - (Bindings.Held(Bindings.Action.Back) ? 1 : 0)) : Vector2.zero,
                Yaw = yaw, Pitch = pitch,
                Aim = origin + GameSim.LookDirection(yaw, pitch) * Defs.ShotRange,
                Fire = BenchFire || (active && Input.GetMouseButton(0) && !releaseFireToResume),
                Dash = active && Bindings.Held(Bindings.Action.Dash),
                Jump = active && Bindings.Held(Bindings.Action.Jump),
                Descend = active && Bindings.Held(Bindings.Action.Descend),
                Reload = active && Bindings.Held(Bindings.Action.Reload),
                Skill = active && Bindings.Held(Bindings.Action.Skill),
                Interact = active && Bindings.Held(Bindings.Action.Interact),
                Slot = desiredSlot,
            };
            SubmitInput(input);
            DrainEvents();
            if (Sim.State != lastState && net == null)
            {
                lastState = Sim.State;
                if (Sim.State == MissionState.Victory || Sim.State == MissionState.Defeat) MissionEnded?.Invoke(Sim);
            }
        }

        /// Offline: feed the local simulation. Networked sessions override this through NetworkInput.
        public Func<PlayerInput, bool> NetworkInput;

        void SubmitInput(PlayerInput input)
        {
            if (NetworkInput != null && NetworkInput(input)) return;
            Sim.Inputs[LocalId] = input;
            Sim.Tick(Time.fixedDeltaTime);
        }

        void DrainEvents()
        {
            foreach (var shot in Sim.Shots) ShotEffect(shot);
            foreach (var ex in Sim.Explosions) ExplosionEffect(ex);
            foreach (var text in Sim.Announcements) Hud.Announce(text);
            Sim.Shots.Clear();
            Sim.Explosions.Clear();
            Sim.Announcements.Clear();
            if (Sim.BuffPending && Sim.BuffOffers.TryGetValue(LocalId, out var offer) && offer != null) Menus.ShowBuffs(offer, Sim.Level);
            else if (!Sim.BuffPending) Menus.HideBuffs();
        }

        public void ChooseBuff(int code)
        {
            if (Sim == null) return;
            if (ChooseBuffRemote != null) ChooseBuffRemote(code);
            else Sim.ChooseBuff(LocalId, code);
            if (ChooseBuffRemote != null && Sim.BuffOffers.ContainsKey(LocalId)) Sim.BuffOffers[LocalId] = null;
        }

        public Action<int> ChooseBuffRemote;

        // ------------------------------------------------------------ effects

        void ShotEffect(ShotEvent shot)
        {
            if (shot.Shooter < 0) { GadgetEffect(shot); return; }
            Vector3 origin = shot.Shooter == LocalId && muzzle != null && !thirdPerson ? muzzle.position : shot.Origin;
            if (actorViews.TryGetValue(shot.Shooter, out var shooterView) && shooterView.activeSelf)
            {
                var shooterRig = shooterView.GetComponent<CharacterRig>();
                if (shooterRig != null) { shooterRig.Fire(); if (shooterRig.Muzzle != null) origin = shooterRig.Muzzle.position; }
            }
            // A miss that stopped short of full range hit a wall or the ground.
            if (!shot.Hit && Vector3.Distance(shot.Origin, shot.End) < Defs.ShotRange - 0.5f) ImpactEffect(shot.End, shot.End - shot.Origin);
            int rarity = Sim.Actors.TryGetValue(shot.Shooter, out var shooter) ? shooter.Loadout.RarityOf(shooter.WeaponId) : 1;
            ShotSound(shot, shooter, rarity);
            if (rarity >= 3) { RarityShot(shot, origin, rarity); if (shot.Shooter == LocalId) { Hud.OnLocalShot(shot, Camera); ApplyRecoil(); } return; }
            var beam = new GameObject("Beam");
            var line = beam.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.SetPosition(0, origin);
            line.SetPosition(1, shot.End);
            line.startWidth = line.endWidth = 0.035f;
            line.sharedMaterial = Build.Mat(Palette.Amber, 3f);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            effects.Add((beam, 0.06f, Vector3.zero, 0));
            if (shot.Hit)
            {
                var spark = Build.Sphere(null, shot.End, shot.Head ? 0.6f : 0.4f, shot.Head ? Palette.Amber : Palette.Hex("ff8855"), 2.5f, false);
                effects.Add((spark, 0.12f, Vector3.zero, 0));
            }
            if (shot.Shooter != LocalId) return;
            Hud.OnLocalShot(shot, Camera);
            ApplyRecoil();
        }

        void GadgetEffect(ShotEvent shot)
        {
            int type = GameSim.GadgetType(shot.Shooter);
            bool plasma = type == GameSim.GadgetPlasma, lightning = type == GameSim.GadgetLightning, laser = type == GameSim.GadgetLaser;
            var beam = new GameObject("GadgetBeam");
            var line = beam.AddComponent<LineRenderer>();
            if (lightning)
            {
                LightningBolt(shot.Origin, shot.End, line);
                Destroy(beam);
                return;
            }
            else
            {
                line.positionCount = 2;
                line.SetPosition(0, shot.Origin);
                line.SetPosition(1, shot.End);
            }
            line.startWidth = plasma ? 0.22f : lightning ? 0.07f : laser ? 0.06f : 0.025f;
            line.endWidth = plasma ? 0.1f : lightning ? 0.04f : laser ? 0.06f : 0.025f;
            var color = plasma ? Palette.Hex("9b7bff") : lightning ? Palette.Hex("bfe4ff") : laser ? Palette.Hex("ff3b5c") : Palette.Hex("5ee6ff");
            line.sharedMaterial = Build.Mat(color, 4f);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            effects.Add((beam, plasma ? 0.14f : lightning ? 0.12f : 0.05f, Vector3.zero, 0));
            if (GameSim.GadgetOwner(shot.Shooter) == LocalId) Sfx.Play(plasma ? "explosion" : lightning ? "head" : "shot", plasma ? 0.35f : 0.12f);
        }

        /// Chain-lightning arc: a white-hot core plus two wider blue strands, each jagged differently, small forks
        /// branching off, and a flash with light where it lands.
        void LightningBolt(Vector3 from, Vector3 to, LineRenderer unused)
        {
            void Strand(float width, Color color, float glow, float jitter, Vector3 a, Vector3 b, int points)
            {
                var go = new GameObject("Bolt");
                var line = go.AddComponent<LineRenderer>();
                line.positionCount = points;
                for (int i = 0; i < points; i++)
                {
                    float t = i / (points - 1f);
                    var offset = i == 0 || i == points - 1 ? Vector3.zero : UnityEngine.Random.insideUnitSphere * jitter * Mathf.Sin(t * Mathf.PI);
                    line.SetPosition(i, Vector3.Lerp(a, b, t) + offset);
                }
                line.startWidth = width; line.endWidth = width * 0.7f;
                line.sharedMaterial = Build.Mat(color, glow, 0.9f);
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                effects.Add((go, UnityEngine.Random.Range(0.1f, 0.18f), Vector3.zero, 0));
            }
            int n = Mathf.Clamp((int)(Vector3.Distance(from, to) * 2.5f), 6, 24);
            Strand(0.14f, Palette.Hex("5aa8ff"), 3f, 0.45f, from, to, n);
            Strand(0.09f, Palette.Hex("9fd0ff"), 4f, 0.35f, from, to, n);
            Strand(0.035f, Color.white, 5f, 0.2f, from, to, n);
            for (int f = 0; f < 2; f++)
            {
                var start = Vector3.Lerp(from, to, UnityEngine.Random.Range(0.3f, 0.7f));
                Strand(0.04f, Palette.Hex("bfe4ff"), 4f, 0.2f, start, start + UnityEngine.Random.onUnitSphere * 1.2f + Vector3.down * 0.3f, 5);
            }
            Burst.Sphere(to, Palette.Hex("bfe4ff"), 0.85f, 5f, 0.2f, 1.1f, 0.14f).GetComponent<Burst>().AddLight(Palette.Hex("7fb8ff"), 6f, 5f);
            for (int i = 0; i < 6; i++)
            {
                var spark = Build.Cube(null, to, Vector3.one * 0.06f, Palette.Hex("e6f3ff"), 5f, false);
                effects.Add((spark, 0.3f, UnityEngine.Random.onUnitSphere * 6f + Vector3.up * 2f, 900f));
            }
        }

        /// Each gun has its own fire sound; your hits add a short confirmation blip, teammates are quieter.
        void ShotSound(ShotEvent shot, Actor shooter, int rarity)
        {
            if (shooter == null) return;
            bool mine = shot.Shooter == LocalId;
            Sfx.Play(Sfx.FireName(shooter.WeaponId, rarity), mine ? 0.55f : 0.22f);
            if (mine && (shot.Hit || shot.Head)) Sfx.Play(shot.Head ? "head" : "hit", 0.45f);
        }

        /// Bullet striking cover or ground: a brief flash, a puff of dust and a few sparks bouncing back.
        void ImpactEffect(Vector3 at, Vector3 incoming)
        {
            Vector3 back = -incoming.normalized;
            Burst.Sphere(at, Palette.Hex("fff1c0"), 0.9f, 4f, 0.05f, 0.25f, 0.06f);
            Burst.Sphere(at + back * 0.05f, Palette.Hex("9a948a"), 0.45f, 0f, 0.1f, 0.5f, 0.45f, back * 0.6f + Vector3.up * 0.4f);
            for (int i = 0; i < 3; i++)
            {
                var spark = Build.Cube(null, at, Vector3.one * 0.04f, Palette.Hex("ffd27a"), 5f, false);
                effects.Add((spark, 0.25f, (back + UnityEngine.Random.insideUnitSphere * 0.8f) * 4f + Vector3.up * 1.5f, 600f));
            }
        }

        void RenderGadgets(Actor a, GameObject view)
        {
            if (!droneViews.TryGetValue(a.Id, out var drones)) droneViews[a.Id] = drones = new List<Transform>();
            while (drones.Count < a.Drones) drones.Add(Models.Drone().transform);
            while (drones.Count > a.Drones) { Destroy(drones[drones.Count - 1].gameObject); drones.RemoveAt(drones.Count - 1); }
            float t = visualClock;
            for (int i = 0; i < drones.Count; i++)
            {
                var offset = Defs.DroneOffset(i, drones.Count, t);
                bool firstPerson = a.Id == LocalId && !thirdPerson;
                // Keep your own drones out of the first-person camera: wider, higher orbit and smaller.
                if (firstPerson) offset = new Vector3(offset.x * 1.9f, offset.y + 0.9f, offset.z * 1.9f);
                drones[i].localScale = Vector3.one * (firstPerson ? 0.7f : 1f);
                Vector3 target = view.transform.position + offset;
                drones[i].position = Vector3.Lerp(drones[i].position, target, 1f - Mathf.Exp(-9f * Time.deltaTime));
                float angle = t * 1.6f + i * Mathf.PI * 2f / Mathf.Max(1, drones.Count);
                var tangent = new Vector3(-Mathf.Sin(angle), 0, Mathf.Cos(angle));
                var heading = Quaternion.LookRotation(tangent) * Quaternion.Euler(9f, 0, 20f);
                drones[i].rotation = Quaternion.Slerp(drones[i].rotation, heading, 1f - Mathf.Exp(-6f * Time.deltaTime));
                drones[i].gameObject.SetActive(a.Alive);
            }
            if (a.Plasma && !plasmaMounted.Contains(a.Id) && view.transform.Find("Plasma") == null) Models.PlasmaCannon(view.transform);
            if (a.Plasma) plasmaMounted.Add(a.Id);
            if (!bladeViews.TryGetValue(a.Id, out var blades)) bladeViews[a.Id] = blades = new List<Transform>();
            int bladeCount = a.Alive ? Defs.BladeCount(a.OrbitLevel) : 0;
            while (blades.Count < bladeCount)
            {
                var blade = Models.Blade().transform;
                blade.localScale = Vector3.one * 0.05f;
                blade.position = view.transform.position;
                blades.Add(blade);
            }
            while (blades.Count > bladeCount) { Destroy(blades[blades.Count - 1].gameObject); blades.RemoveAt(blades.Count - 1); }
            // Smooth orbit: the clock is continuous (see visualClock), slot spacing glides when a blade is added
            // or removed instead of snapping, and new blades grow in.
            if (!bladeSlots.TryGetValue(a.Id, out var slots)) bladeSlots[a.Id] = slots = new List<float>();
            while (slots.Count < blades.Count) slots.Add(slots.Count == 0 ? 0f : slots[slots.Count - 1]);
            while (slots.Count > blades.Count) slots.RemoveAt(slots.Count - 1);
            float glide = 1f - Mathf.Exp(-8f * Time.deltaTime);
            Vector3 center = view.transform.position;
            for (int i = 0; i < blades.Count; i++)
            {
                slots[i] = Mathf.Lerp(slots[i], i * Mathf.PI * 2f / blades.Count, glide);
                float angle = t * 3.2f + slots[i];
                blades[i].position = center + new Vector3(Mathf.Cos(angle) * 2.4f, 1.0f + Mathf.Sin(t * 2.6f + i) * 0.06f, Mathf.Sin(angle) * 2.4f);
                blades[i].rotation = Quaternion.Euler(10f, -t * 1100f + i * 40f, 0);
                blades[i].localScale = Vector3.one * Mathf.MoveTowards(blades[i].localScale.x, 1f, Time.deltaTime * 4f);
            }
        }

        void RenderNameTag(Actor a, GameObject view)
        {
            if (a.Id == LocalId || string.IsNullOrEmpty(a.Name)) return;
            if (!nameTags.TryGetValue(a.Id, out var tag))
            {
                var go = new GameObject("NameTag");
                tag = go.AddComponent<TextMesh>();
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                tag.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                tag.anchor = TextAnchor.LowerCenter;
                tag.alignment = TextAlignment.Center;
                tag.fontSize = 48;
                tag.characterSize = 0.045f;
                nameTags[a.Id] = tag;
            }
            tag.text = a.Alive ? a.Name : a.Name + "  [E 救援]";
            tag.color = a.Alive ? Palette.Teammate : Palette.Danger;
            tag.transform.position = view.transform.position + Vector3.up * 2.75f;
            tag.transform.rotation = Camera.transform.rotation;
        }

        /// 傳奇 / 神話 weapons only: a thick glowing tracer with a white core, a coloured muzzle burst and impact
        /// sparks; 神話 adds a spiralling energy helix and a ring of shards on every hit.
        void RarityShot(ShotEvent shot, Vector3 origin, int rarity)
        {
            bool myth = rarity >= 4;
            Color color = Gear.RarityColors[rarity];
            LineRenderer Line(string name, int points, float width, Color c, float glow)
            {
                var go = new GameObject(name);
                var line = go.AddComponent<LineRenderer>();
                line.positionCount = points;
                line.startWidth = width; line.endWidth = width * 0.6f;
                line.sharedMaterial = Build.Mat(c, glow);
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                effects.Add((go, myth ? 0.14f : 0.1f, Vector3.zero, 0));
                return line;
            }
            var outer = Line("RarityBeam", 2, myth ? 0.11f : 0.08f, color, myth ? 5f : 4f);
            outer.SetPosition(0, origin); outer.SetPosition(1, shot.End);
            var core = Line("RarityCore", 2, 0.025f, Color.white, 3f);
            core.SetPosition(0, origin); core.SetPosition(1, shot.End);
            if (myth)
            {
                // Energy helix wrapped around the tracer.
                Vector3 dir = (shot.End - origin);
                float length = dir.magnitude;
                dir /= Mathf.Max(0.001f, length);
                Vector3 side = Vector3.Cross(dir, Vector3.up).normalized;
                if (side == Vector3.zero) side = Vector3.right;
                Vector3 up = Vector3.Cross(side, dir);
                int n = Mathf.Clamp((int)(length * 4f), 8, 160);
                var helix = Line("Helix", n, 0.03f, Palette.Hex("ffb0b0"), 4f);
                for (int i = 0; i < n; i++)
                {
                    float t = i / (n - 1f), a = t * length * 5f;
                    helix.SetPosition(i, origin + dir * length * t + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * 0.12f);
                }
            }
            bool firstPerson = shot.Shooter == LocalId && !thirdPerson;
            var flash = Build.Sphere(null, origin, (myth ? 0.4f : 0.3f) * (firstPerson ? 0.25f : 1f), color, 4f, false);
            flash.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(color, 4f, 0.6f);
            effects.Add((flash, 0.07f, Vector3.zero, 0));
            if (!shot.Hit) return;
            var burst = Build.Sphere(null, shot.End, shot.Head ? 0.8f : 0.55f, color, 4f, false);
            burst.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(color, 4f, 0.55f);
            effects.Add((burst, 0.12f, Vector3.zero, 0));
            int shards = myth ? 10 : 5;
            for (int i = 0; i < shards; i++)
            {
                var spark = Build.Cube(null, shot.End, Vector3.one * (myth ? 0.09f : 0.07f), color, 4f, false);
                var v = UnityEngine.Random.onUnitSphere * (myth ? 7f : 5f) + Vector3.up * 2f;
                effects.Add((spark, 0.35f, v, 600f));
            }
        }

        float lastRecoil;
        void ApplyRecoil()
        {
            if (Time.time - lastRecoil < 0.03f || !Sim.Actors.TryGetValue(LocalId, out var me)) return;
            lastRecoil = Time.time;
            var w = Defs.Weapons[me.WeaponId];
            float kick = w.Recoil * UnityEngine.Random.Range(0.85f, 1.15f);
            pitch = Mathf.Clamp(pitch + kick, -Defs.LookLimit, Defs.LookLimit);
            yaw += UnityEngine.Random.Range(-1f, 1f) * w.Sway;
            recoilPending += kick * 0.65f;
            shake = 0.02f + kick * 0.9f;
        }

        void ExplosionEffect(ExplosionEvent ex)
        {
            Color color = ex.Color;
            float r = ex.Radius;
            Burst.Sphere(ex.Position, Color.white, 0.95f, 5f, r * 0.2f, r * 0.9f, 0.09f).GetComponent<Burst>().AddLight(color, 8f, r * 4f);
            Burst.Sphere(ex.Position, color, 0.7f, 3.5f, r * 0.4f, r * 1.5f, 0.3f);
            Burst.Sphere(ex.Position, Color.Lerp(color, Color.white, 0.4f), 0.5f, 4f, r * 0.2f, r * 1.2f, 0.18f);
            Burst.Ring(new Vector3(ex.Position.x, 0.06f, ex.Position.z), color, r * 1.8f, 0.4f);
            for (int i = 0; i < 5; i++)
            {
                var drift = new Vector3(UnityEngine.Random.Range(-0.6f, 0.6f), UnityEngine.Random.Range(1.2f, 2.2f), UnityEngine.Random.Range(-0.6f, 0.6f));
                Burst.Sphere(ex.Position + UnityEngine.Random.insideUnitSphere * r * 0.4f, Palette.Hex("8a847c"), 0.35f, 0f, r * 0.3f, r * 1.1f, UnityEngine.Random.Range(0.9f, 1.4f), drift);
            }
            for (int i = 0; i < 14; i++)
            {
                var spark = Build.Cube(null, ex.Position, Vector3.one * 0.08f, Color.Lerp(color, Palette.Hex("fff1c0"), (i % 3) * 0.5f), 5f, false);
                effects.Add((spark, UnityEngine.Random.Range(0.35f, 0.6f), UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(5f, 10f) + Vector3.up * 4f, 720f));
            }
            if (Sim.Actors.TryGetValue(LocalId, out var me) && Vector3.Distance(me.Position, ex.Position) < ex.Radius * 3) shake = 0.08f;
            Sfx.Play("explosion", 0.8f);
        }

        /// Enemies collapse into tumbling fragments instead of vanishing.
        void DeathEffect(GameObject view, Color color, float scale)
        {
            Vector3 center = view.transform.position + Vector3.up * scale;
            // The body breaks apart: every armour piece detaches and tumbles away from the centre.
            int pieces = 0;
            foreach (var r in view.GetComponentsInChildren<MeshRenderer>())
            {
                if (pieces++ >= 22 || !r.gameObject.activeInHierarchy) continue;
                var piece = r.transform;
                piece.SetParent(null, true);
                var away = (piece.position - center); away.y = 0;
                var velocity = away.normalized * UnityEngine.Random.Range(2.5f, 5f) + Vector3.up * UnityEngine.Random.Range(3f, 6f);
                effects.Add((piece.gameObject, UnityEngine.Random.Range(0.9f, 1.3f), velocity, UnityEngine.Random.Range(-420f, 420f)));
            }
            for (int i = 0; i < 6; i++)
            {
                var spark = Build.Cube(null, center, Vector3.one * 0.07f * scale, color, 4f, false);
                effects.Add((spark, 0.5f, UnityEngine.Random.onUnitSphere * 6f + Vector3.up * 3f, 720f));
            }
            var flash = Build.Sphere(null, center, 1.2f * scale, color, 2f, false);
            flash.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(color, 2f, 0.5f);
            effects.Add((flash, 0.12f, Vector3.zero, 0));
        }

        // ------------------------------------------------------------ rendering

        void Render(float dt)
        {
            // Frozen while the sim is (paused, choosing a buff, mission over), so gadgets do not run ahead and snap back.
            if (!Paused && !Sim.BuffPending && Sim.State == MissionState.Playing) visualClock += dt;
            float drift = Sim.Elapsed - visualClock;
            if (Mathf.Abs(drift) > 0.5f) visualClock = Sim.Elapsed;          // big jump (start, pause, lag): snap
            else visualClock += drift * (1f - Mathf.Exp(-4f * dt));       // otherwise ease toward the sim
            Sim.Actors.TryGetValue(LocalId, out var me);
            foreach (var a in Sim.Actors.Values)
            {
                if (!actorViews.TryGetValue(a.Id, out var view))
                {
                    view = Models.Soldier(a.Id == LocalId ? Palette.Player : Palette.Teammate);
                    view.transform.position = a.Position;
                    actorViews[a.Id] = view;
                }
                view.transform.position = Vector3.Lerp(view.transform.position, a.Position, 1f - Mathf.Exp(-20f * dt));
                float lying = a.Alive ? 0f : -80f;
                var current = view.transform.rotation;
                view.transform.rotation = Quaternion.Slerp(current, Quaternion.Euler(lying, a.Yaw * Mathf.Rad2Deg, 0), a.Alive ? 1f : 1f - Mathf.Exp(-6f * dt));
                if (!actorRigs.TryGetValue(a.Id, out var rig)) actorRigs[a.Id] = rig = view.GetComponent<CharacterRig>();
                if (rig != null)
                {
                    rig.Alive = a.Alive;
                    rig.Airborne = a.Position.y > 0.08f;
                    rig.Pitch = a.Id == LocalId ? pitch : a.Pitch;
                    rig.Reload = a.Reload > 0 && a.ReloadTime > 0 ? 1f - a.Reload / a.ReloadTime : 0f;
                    int weaponRarity = a.Id == LocalId ? (WeaponRarity?.Invoke(a.WeaponId) ?? 1) : a.Loadout.RarityOf(a.WeaponId);
                    rig.SetWeapon(a.WeaponId, weaponRarity);
                }
                view.SetActive(a.Id != LocalId || thirdPerson);
                RenderGadgets(a, view);
                RenderNameTag(a, view);
            }
            stale.Clear(); foreach (var key in actorViews.Keys) if (!Sim.Actors.ContainsKey(key)) stale.Add(key);
            foreach (var id in stale)
            {
                Destroy(actorViews[id]); actorViews.Remove(id); actorRigs.Remove(id);
                if (droneViews.TryGetValue(id, out var ds)) { foreach (var d in ds) Destroy(d.gameObject); droneViews.Remove(id); }
                if (nameTags.TryGetValue(id, out var tag)) { Destroy(tag.gameObject); nameTags.Remove(id); }
                if (bladeViews.TryGetValue(id, out var bs)) { foreach (var b in bs) Destroy(b.gameObject); bladeViews.Remove(id); }
                bladeSlots.Remove(id);
            }

            foreach (var e in Sim.Enemies.Values)
            {
                if (!enemyViews.TryGetValue(e.Id, out var view))
                {
                    view = Models.Enemy(e.Kind);
                    if (e.Elite) Models.MakeElite(view);
                    view.transform.position = e.Position;
                    enemyViews[e.Id] = view;
                    enemyParts[e.Id] = (CharacterRig.Find(view.transform, "Laser"), view.GetComponent<CharacterRig>());
                }
                var (laser, enemyRig) = enemyParts[e.Id];
                view.transform.position = Vector3.Lerp(view.transform.position, e.Position, 1f - Mathf.Exp(-14f * dt));
                view.transform.rotation = Quaternion.Euler(0, e.Yaw * Mathf.Rad2Deg, 0);
                if (enemyHp.TryGetValue(e.Id, out var lastHp) && e.Hp < lastHp - 0.5f && enemyRig != null) enemyRig.Hit();
                enemyHp[e.Id] = e.Hp;
                if (laser != null) laser.gameObject.SetActive(e.State == 1);
            }
            stale.Clear(); foreach (var key in enemyViews.Keys) if (!Sim.Enemies.ContainsKey(key)) stale.Add(key);
            foreach (var id in stale)
            {
                var view = enemyViews[id];
                DeathEffect(view, Palette.Hex("ff8855"), view.transform.localScale.x);
                Destroy(view);
                enemyViews.Remove(id);
                enemyHp.Remove(id);
                enemyParts.Remove(id);
            }

            foreach (var b in Sim.Bullets.Values)
            {
                if (!bulletViews.TryGetValue(b.Id, out var view)) bulletViews[b.Id] = view = Models.Projectile(b.Kind);
                view.transform.position = b.Position;
                if (b.Kind == BulletKind.Missile && b.Velocity.sqrMagnitude > 0.01f) view.transform.rotation = Quaternion.LookRotation(b.Velocity);
            }
            stale.Clear(); foreach (var key in bulletViews.Keys) if (!Sim.Bullets.ContainsKey(key)) stale.Add(key);
            foreach (var id in stale) { Destroy(bulletViews[id]); bulletViews.Remove(id); }

            foreach (var p in Sim.Pickups.Values)
            {
                if (!pickupViews.TryGetValue(p.Id, out var view)) pickupViews[p.Id] = view = Models.Pickup(p.Kind);
                view.transform.position = p.Position + Vector3.up * (0.6f + Mathf.Sin(Time.time * 3 + p.Id) * 0.12f);
                view.transform.Rotate(0, dt * 115f, 0);
            }
            stale.Clear(); foreach (var key in pickupViews.Keys) if (!Sim.Pickups.ContainsKey(key)) stale.Add(key);
            foreach (var id in stale) { Destroy(pickupViews[id]); pickupViews.Remove(id); }

            for (int i = effects.Count - 1; i >= 0; i--)
            {
                var (go, life, velocity, spin) = effects[i];
                life -= dt;
                if (life <= 0) { Destroy(go); effects.RemoveAt(i); continue; }
                if (velocity != Vector3.zero)
                {
                    velocity += Vector3.down * 14f * dt;
                    go.transform.position += velocity * dt;
                    go.transform.Rotate(spin * dt, spin * dt * 0.7f, 0);
                    go.transform.localScale *= 1f - dt * 0.9f;
                }
                effects[i] = (go, life, velocity, spin);
            }

            UpdateCamera(me, dt);
            UpdateAudio(me, dt);
            string toast = Sim.State == MissionState.Playing && !Paused && !Sim.BuffPending && Cursor.lockState != CursorLockMode.Locked ? "點擊畫面恢復滑鼠瞄準" : Objective;
            Hud.Tick(dt, Sim, me, Camera, thirdPerson, toast);
        }

        void UpdateCamera(Actor me, float dt)
        {
            if (me == null) return;
            if (recoilPending > 0.0001f)
            {
                float back = recoilPending * Mathf.Min(1f, dt * 9f);
                pitch = Mathf.Clamp(pitch - back, -Defs.LookLimit, Defs.LookLimit);
                recoilPending -= back;
            }
            var rotation = Quaternion.Euler(-pitch * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg, 0);
            Vector3 eye = actorViews.TryGetValue(LocalId, out var view) ? view.transform.position + Vector3.up * Defs.EyeHeight : me.Position + Vector3.up * Defs.EyeHeight;
            if (!me.Alive) eye = me.Position + Vector3.up * 0.5f;
            Camera.transform.rotation = rotation;
            Camera.transform.position = thirdPerson ? eye + rotation * ThirdPersonOffset : eye;
            if (thirdPerson && Camera.transform.position.y < 0.4f) Camera.transform.position = new Vector3(Camera.transform.position.x, 0.4f, Camera.transform.position.z);

            bool showWeapon = me.Alive && !thirdPerson && Sim.State == MissionState.Playing;
            weaponHolder.gameObject.SetActive(showWeapon);
            if (me.Slot != shownSlot)
            {
                shownSlot = me.Slot;
                desiredSlot = me.Slot;
                muzzle = Models.Viewmodel(me.WeaponId, weaponHolder, WeaponRarity?.Invoke(me.WeaponId) ?? 1);
                magazine = weaponHolder.Find("Mag");
                if (magazine != null) magazineRest = magazine.localPosition;
            }
            float dashT = me.DashCooldown > 0 ? Defs.DashCooldown - me.DashCooldown : 99f;
            float dash = me.Alive && dashT < 0.32f ? Mathf.Sin(Mathf.PI * dashT / 0.32f) : 0f;
            bool zooming = Input.GetMouseButton(1) && showWeapon && Active;
            zoomFov = Mathf.Lerp(zoomFov, BaseFov * (zooming ? me.Zoom : 1f), 1f - Mathf.Exp(-14f * dt));
            Camera.fieldOfView = zoomFov + 7f * dash;
            shake = Mathf.Max(0, shake - dt);
            // Reload: tilt the weapon, drop the magazine out of frame, seat a fresh one, then settle.
            float reload = me.Reload > 0 && me.ReloadTime > 0 ? 1f - me.Reload / me.ReloadTime : 0f;
            float tilt = reload > 0 ? Mathf.Sin(Mathf.PI * Mathf.Clamp01(reload)) : 0f;
            float magDrop = 0f;
            if (reload > 0)
            {
                if (reload < 0.15f) magDrop = 0f;
                else if (reload < 0.4f) magDrop = Mathf.SmoothStep(0, 1, (reload - 0.15f) / 0.25f);
                else if (reload < 0.6f) magDrop = 1f;
                else if (reload < 0.82f) magDrop = 1f - Mathf.SmoothStep(0, 1, (reload - 0.6f) / 0.22f);
            }
            if (magazine != null)
            {
                magazine.localPosition = magazineRest + new Vector3(0, -0.32f, -0.04f) * magDrop;
                magazine.gameObject.SetActive(magDrop < 0.98f);
            }
            float seat = reload > 0.8f && reload < 0.92f ? Mathf.Sin(Mathf.PI * (reload - 0.8f) / 0.12f) : 0f;
            weaponHolder.localPosition = WeaponRest + new Vector3(0.02f, -0.045f, -0.1f) * dash + new Vector3(0, 0, -shake)
                + new Vector3(-0.03f, -0.07f, -0.04f) * tilt + new Vector3(0, 0.012f, 0) * seat;
            weaponHolder.localRotation = Quaternion.Euler(new Vector3(6f, 2f, -6f) * dash + new Vector3(-14f, 18f, 32f) * tilt + new Vector3(-4f, 0, 0) * seat);
        }

        void UpdateAudio(Actor me, float dt)
        {
            if (me == null) return;
            float ratio = me.Reload > 0 ? me.Reload / Mathf.Max(0.01f, me.ReloadTime) : 0f;
            if (me.Alive && me.Slot == lastReloadSlot)
            {
                if (lastReloadRatio <= 0 && ratio > 0) Sfx.Play("reload_out");
                else if (lastReloadRatio > 0.4f && ratio <= 0.4f && ratio > 0) Sfx.Play("reload_in");
                else if (lastReloadRatio > 0 && ratio <= 0 && me.Ammo >= me.Mag) Sfx.Play("reload_rack");
            }
            lastReloadRatio = ratio;
            lastReloadSlot = me.Slot;
            // Heartbeat when shields are low.
            if (me.Alive && me.Hp <= me.MaxHp * 0.3f)
            {
                heartbeat -= dt;
                if (heartbeat <= 0) { Sfx.Play("heartbeat", 0.9f); heartbeat = 0.85f; }
            }
            else heartbeat = 0;
        }
    }
}
