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
            Application.targetFrameRate = 144;
            Time.fixedDeltaTime = 1f / 60f;
            var camGo = new GameObject("Camera");
            Camera = camGo.AddComponent<Camera>();
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
            showcase.SetActive(true);
            weaponHolder.gameObject.SetActive(false);
            Camera.fieldOfView = BaseFov;
        }

        public void SetThirdPerson(bool on) => thirdPerson = on;
        public void SelectSlot(int slot) => desiredSlot = slot;
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
                yaw = Mathf.Repeat(yaw + Input.GetAxisRaw("Mouse X") * LookSensitivity * 10f + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;
                pitch = Mathf.Clamp(pitch + Input.GetAxisRaw("Mouse Y") * LookSensitivity * 10f, -Defs.LookLimit, Defs.LookLimit);
                if (Input.GetKeyDown(KeyCode.V)) thirdPerson = !thirdPerson;
                if (Input.GetKeyDown(KeyCode.Alpha1)) desiredSlot = 0;
                if (Input.GetKeyDown(KeyCode.Alpha2)) desiredSlot = 1;
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
                Move = active ? new Vector2((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0), (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0)) : Vector2.zero,
                Yaw = yaw, Pitch = pitch,
                Aim = origin + GameSim.LookDirection(yaw, pitch) * Defs.ShotRange,
                Fire = active && Input.GetMouseButton(0) && !releaseFireToResume,
                Dash = active && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)),
                Jump = active && Input.GetKey(KeyCode.Space),
                Descend = active && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)),
                Reload = active && Input.GetKey(KeyCode.R),
                Skill = active && Input.GetKey(KeyCode.Q),
                Interact = active && Input.GetKey(KeyCode.E),
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
            Sfx.Play(shot.Head ? "head" : shot.Hit ? "hit" : "shot", 0.6f);
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
                // Jagged bolt: jitter the midpoints so each strike looks different.
                line.positionCount = 7;
                for (int i = 0; i < 7; i++)
                {
                    float t = i / 6f;
                    var jitter = i == 0 || i == 6 ? Vector3.zero : UnityEngine.Random.insideUnitSphere * 0.35f;
                    line.SetPosition(i, Vector3.Lerp(shot.Origin, shot.End, t) + jitter);
                }
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

        void RenderGadgets(Actor a, GameObject view)
        {
            if (!droneViews.TryGetValue(a.Id, out var drones)) droneViews[a.Id] = drones = new List<Transform>();
            while (drones.Count < a.Drones) drones.Add(Models.Drone().transform);
            while (drones.Count > a.Drones) { Destroy(drones[drones.Count - 1].gameObject); drones.RemoveAt(drones.Count - 1); }
            float t = Sim.Elapsed;
            for (int i = 0; i < drones.Count; i++)
            {
                var offset = Defs.DroneOffset(i, drones.Count, t);
                bool firstPerson = a.Id == LocalId && !thirdPerson;
                // Keep your own drones out of the first-person camera: wider, higher orbit and smaller.
                if (firstPerson) offset = new Vector3(offset.x * 1.9f, offset.y + 0.9f, offset.z * 1.9f);
                drones[i].localScale = Vector3.one * (firstPerson ? 0.7f : 1f);
                drones[i].position = view.transform.position + offset;
                drones[i].rotation = Quaternion.Euler(0, t * 140f + i * 90f, 0);
                drones[i].gameObject.SetActive(a.Alive);
            }
            var cannon = view.transform.Find("Plasma");
            if (a.Plasma && cannon == null) Models.PlasmaCannon(view.transform);
            if (!bladeViews.TryGetValue(a.Id, out var blades)) bladeViews[a.Id] = blades = new List<Transform>();
            int bladeCount = a.Alive ? Defs.BladeCount(a.OrbitLevel) : 0;
            while (blades.Count < bladeCount) blades.Add(Models.Blade().transform);
            while (blades.Count > bladeCount) { Destroy(blades[blades.Count - 1].gameObject); blades.RemoveAt(blades.Count - 1); }
            for (int i = 0; i < blades.Count; i++)
            {
                blades[i].position = view.transform.position + Defs.BladeOffset(i, blades.Count, t);
                blades[i].rotation = Quaternion.Euler(0, -t * 600f, 0);
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
            var ball = Build.Sphere(null, ex.Position, ex.Radius * 1.2f, ex.Color, 2.5f, false);
            ball.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(ex.Color, 2.5f, 0.55f);
            effects.Add((ball, 0.22f, Vector3.zero, 0));
            if (Sim.Actors.TryGetValue(LocalId, out var me) && Vector3.Distance(me.Position, ex.Position) < ex.Radius * 3) shake = 0.08f;
            Sfx.Play("explosion", 0.8f);
        }

        /// Enemies collapse into tumbling fragments instead of vanishing.
        void DeathEffect(GameObject view, Color color, float scale)
        {
            Vector3 center = view.transform.position + Vector3.up * scale;
            for (int i = 0; i < 10; i++)
            {
                var shard = Build.Cube(null, center + UnityEngine.Random.insideUnitSphere * 0.4f * scale, Vector3.one * UnityEngine.Random.Range(0.12f, 0.3f) * scale, i % 2 == 0 ? color : Palette.Hex("3a2a26"), 0.4f, false);
                var velocity = (UnityEngine.Random.insideUnitSphere + Vector3.up * 1.2f) * 4f;
                effects.Add((shard, 0.9f, velocity, UnityEngine.Random.Range(-360f, 360f)));
            }
            var flash = Build.Sphere(null, center, 1.2f * scale, color, 2f, false);
            flash.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(color, 2f, 0.5f);
            effects.Add((flash, 0.12f, Vector3.zero, 0));
        }

        // ------------------------------------------------------------ rendering

        void Render(float dt)
        {
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
                view.transform.rotation = Quaternion.Euler(lying, a.Yaw * Mathf.Rad2Deg, 0);
                view.SetActive(a.Id != LocalId || thirdPerson);
                RenderGadgets(a, view);
                RenderNameTag(a, view);
            }
            foreach (var id in actorViews.Keys.Where(id => !Sim.Actors.ContainsKey(id)).ToList())
            {
                Destroy(actorViews[id]); actorViews.Remove(id);
                if (droneViews.TryGetValue(id, out var ds)) { foreach (var d in ds) Destroy(d.gameObject); droneViews.Remove(id); }
                if (nameTags.TryGetValue(id, out var tag)) { Destroy(tag.gameObject); nameTags.Remove(id); }
                if (bladeViews.TryGetValue(id, out var bs)) { foreach (var b in bs) Destroy(b.gameObject); bladeViews.Remove(id); }
            }

            foreach (var e in Sim.Enemies.Values)
            {
                if (!enemyViews.TryGetValue(e.Id, out var view))
                {
                    view = Models.Enemy(e.Kind);
                    if (e.Elite) Models.MakeElite(view);
                    view.transform.position = e.Position;
                    enemyViews[e.Id] = view;
                }
                view.transform.position = Vector3.Lerp(view.transform.position, e.Position, 1f - Mathf.Exp(-14f * dt));
                view.transform.rotation = Quaternion.Euler(0, e.Yaw * Mathf.Rad2Deg, 0);
                var laser = view.transform.Find("Laser");
                if (laser != null) laser.gameObject.SetActive(e.State == 1);
            }
            foreach (var id in enemyViews.Keys.Where(id => !Sim.Enemies.ContainsKey(id)).ToList())
            {
                var view = enemyViews[id];
                DeathEffect(view, Palette.Hex("ff8855"), view.transform.localScale.x);
                Destroy(view);
                enemyViews.Remove(id);
            }

            foreach (var b in Sim.Bullets.Values)
            {
                if (!bulletViews.TryGetValue(b.Id, out var view)) bulletViews[b.Id] = view = Models.Projectile(b.Kind);
                view.transform.position = b.Position;
            }
            foreach (var id in bulletViews.Keys.Where(id => !Sim.Bullets.ContainsKey(id)).ToList()) { Destroy(bulletViews[id]); bulletViews.Remove(id); }

            foreach (var p in Sim.Pickups.Values)
            {
                if (!pickupViews.TryGetValue(p.Id, out var view)) pickupViews[p.Id] = view = Models.Pickup(p.Kind);
                view.transform.position = p.Position + Vector3.up * (0.6f + Mathf.Sin(Time.time * 3 + p.Id) * 0.12f);
                view.transform.Rotate(0, dt * 115f, 0);
            }
            foreach (var id in pickupViews.Keys.Where(id => !Sim.Pickups.ContainsKey(id)).ToList()) { Destroy(pickupViews[id]); pickupViews.Remove(id); }

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
                muzzle = Models.Viewmodel(me.WeaponId, weaponHolder);
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
