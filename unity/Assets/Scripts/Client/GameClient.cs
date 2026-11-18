using System;
using System.Collections.Generic;
using System.Linq;
using RiftWard.Sim;
using UnityEngine;
using UnityEngine.UI;

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
        /// Middle-mouse pings from you and your teammates.
        public PingMarkers Pings { get; private set; }
        float lastPing = -9f;
        /// For the automated screenshots: hold the aim-down-sights pose.
        public bool AimForCapture;
        public Menus Menus { get; private set; }

        float yaw, pitch, recoilPending, shake, zoomFov = BaseFov;
        bool thirdPerson;
        int desiredSlot, shownSlot = -1;
        float lastReloadRatio;
        int lastReloadSlot = -1;
        float heartbeat;
        bool releaseFireToResume;
        public bool Paused { get; private set; }
        /// True when opening the pause menu freezes the mission: always offline, online only while alone in the room.
        public Func<bool> CanPause = () => true;

        /// Opens or closes the pause state; online the server is told, and freezes the mission if you are alone.
        public void SetPaused(bool on)
        {
            if (Paused == on) return;
            Paused = on;
            if (net != null) net.Send(Msg.SetPause, w => w.Write(on));
        }

        /// Test aimbot (admin ability panel, client only): while aiming, the view turns onto the nearest visible
        /// enemy's head, as a view-locking cheat would. The server only sees an ordinary view direction.
        public static bool TestAimbot;
        /// Automated screenshots only: hold the blade's guard pose.
        public static bool CaptureGuard;
        /// 0 = snap instantly, 1 = slow glide.
        public static float TestAimbotSmooth = 0.3f;
        public const float TestAimbotFov = 45f;
        Transform weaponHolder, muzzle, magazine;
        Vector3 magazineRest;

        // Viewmodel moving parts, ammo bar and heat sleeve (found by name after each weapon swap).
        Transform slidePart, boltPart, pumpPart;
        Vector3 slideRest, boltRest, pumpRest;
        Quaternion boltRotRest;
        MeshRenderer[] ammoCells = new MeshRenderer[0];
        MeshRenderer heatSleeve;
        int heatStep = -1;
        int shownCells = -1;
        bool shownLow;
        /// Seconds since your last shot (drives slide / bolt / pump cycling), view-model kick and barrel heat.
        float cycle = 99f, kick, heat, lastAmmo = -1;
        /// 0 = hip, 1 = aimed down sights (eased toward the right-mouse state).
        float ads;
        bool adsWanted;
        /// Sniper breath: 1 = full lungs. Holding Shift while scoped steadies the reticle until it runs out.
        float breath = 1f, swayClock, swayYaw, swayPitch;
        bool breathSpent, holdingBreath;
        /// Shots in the current burst (drives the recoil pattern) and a small camera roll kick.
        int burstShots;
        float lastShotAt = -9f, camRoll;
        static bool reloadIdle(Actor me) => !(me.Reload > 0);
        bool hotVented;
        AudioReverbFilter reverb;

        readonly Dictionary<int, GameObject> actorViews = new Dictionary<int, GameObject>();
        readonly Dictionary<int, float> enemyHp = new Dictionary<int, float>();
        readonly List<int> stale = new List<int>();
        // Looked up once per enemy view instead of searching the hierarchy every frame.
        readonly Dictionary<int, (Transform laser, CharacterRig rig, Transform chargeGlow)> enemyParts = new Dictionary<int, (Transform, CharacterRig, Transform)>();
        readonly HashSet<int> plasmaMounted = new HashSet<int>();
        readonly Dictionary<int, List<float>> bladeSlots = new Dictionary<int, List<float>>();
        /// Render-side mission clock: advances every frame and eases toward Sim.Elapsed, which only changes per
        /// simulation tick (or per snapshot online). Orbiting gadgets use it so they move smoothly at any frame rate.
        float visualClock;
        readonly Dictionary<int, CharacterRig> actorRigs = new Dictionary<int, CharacterRig>();   // reused each frame to find views whose entity is gone
        readonly Dictionary<int, List<Transform>> droneViews = new Dictionary<int, List<Transform>>();
        readonly Dictionary<int, Text> nameTags = new Dictionary<int, Text>();
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
            reverb = camGo.AddComponent<AudioReverbFilter>();
            reverb.enabled = false;
            camGo.AddComponent<MasterLimiter>();   // after the reverb: limits the final mix
            weaponHolder = new GameObject("Weapon").transform;
            weaponHolder.SetParent(Camera.transform, false);
            weaponHolder.localPosition = WeaponRest;
            World = new World(Camera);
            Hud = new Hud();
            Hud.Show(false);
            Sfx = new Sfx(gameObject);
            Pings = new PingMarkers(Sfx);
            Hud.PingSource = Pings.Radar;
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
        public void StartNetworked(int region, int difficulty, int localId, NetClient client)
        {
            net = client;
            LocalId = localId;
            Sim = new GameSim();
            Sim.Region = region;
            Sim.Difficulty = difficulty;
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

        public void StartOffline(int region, Loadout loadout, bool admin, AdminSettings adminSettings, int difficulty = 0)
        {
            net = null;
            NetworkInput = null;
            ChooseBuffRemote = null;
            LocalId = 1;
            Sim = new GameSim();
            Sim.AddActor(LocalId, loadout, admin);
            if (admin && adminSettings != null) Sim.SetAdmin(LocalId, adminSettings);
            Sim.StartMission(region, difficulty);
            EnterMission(region);
        }

        /// Room sound per region: open grassland, a reverberant steel foundry, a deep cavernous rift.
        void SetReverb(int region)
        {
            if (reverb == null) return;
            reverb.enabled = region >= 0;
            if (region < 0) return;
            reverb.reverbPreset = AudioReverbPreset.User;
            reverb.dryLevel = 0;
            reverb.diffusion = 100; reverb.density = 100;
            switch (region)
            {
                case 1: reverb.room = -900; reverb.roomHF = -400; reverb.decayTime = 1.9f; reverb.reflectionsLevel = -500; reverb.reverbLevel = -300; break;
                case 2: reverb.room = -1200; reverb.roomHF = -1800; reverb.decayTime = 3.2f; reverb.reflectionsLevel = -1000; reverb.reverbLevel = 0; break;
                case 3: reverb.room = -1700; reverb.roomHF = -900; reverb.decayTime = 1.3f; reverb.reflectionsLevel = -800; reverb.reverbLevel = -900; break;    // fortress walls in the snow
                case 4: reverb.room = -3600; reverb.roomHF = -2000; reverb.decayTime = 0.6f; reverb.reflectionsLevel = -2400; reverb.reverbLevel = -2400; break; // open sky deck
                case 5: reverb.room = -600; reverb.roomHF = -500; reverb.decayTime = 2.8f; reverb.reflectionsLevel = -300; reverb.reverbLevel = 100; break;       // marble throne hall
                default: reverb.room = -3000; reverb.roomHF = -1500; reverb.decayTime = 0.8f; reverb.reflectionsLevel = -2000; reverb.reverbLevel = -1800; break;
            }
        }

        void EnterMission(int region)
        {
            SetReverb(region);
            ClearViews();
            World.ApplyRegion(region);
            yaw = 0; pitch = 0; desiredSlot = 0; shownSlot = -1; recoilPending = 0; SetPaused(false);
            lastState = MissionState.Playing;
            Hud.ResetTransient();
            if (showcase != null) showcase.SetActive(false);
            Hud.Show(true);
            LockCursor(true);
        }

        public void LeaveMission()
        {
            SetReverb(-1);
            Sim = null;
            net = null;
            NetworkInput = null;
            ChooseBuffRemote = null;
            ClearViews();
            Hud.Show(false);
            LockCursor(false);
        }

        /// Middle mouse (tap, double tap or hold for the wheel): a ping for the whole room.
        void Ping()
        {
            if (!Pings.Poll(Camera, Sim, out var position, out byte kind, out int target)) return;
            if (Time.time - lastPing < 0.12f) return;
            lastPing = Time.time;
            if (net != null) net.Send(Msg.Ping, w => { w.Write(position); w.Write(kind); w.Write(target); });
            else Pings.Add(LocalId, "", position, kind, target);
        }

        void ClearViews()
        {
            Pings?.Clear();
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

        bool Active => Sim != null && Sim.State == MissionState.Playing && !Paused && !Sim.BuffPending && !ChatHud.Typing && Cursor.lockState == CursorLockMode.Locked;

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
            if (Input.GetKeyDown(KeyCode.Escape) && Sim.State == MissionState.Playing && !Sim.BuffPending && !ChatHud.Typing && Time.frameCount != ChatHud.ClosedFrame)
            {
                if (Paused) Menus.ClosePause(); else Menus.OpenPause();
            }
            if (Sim.State == MissionState.Playing && !Paused && !Sim.BuffPending && !ChatHud.Typing && Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
            {
                LockCursor(true);
                releaseFireToResume = true;
            }
            if (!Input.GetMouseButton(0)) releaseFireToResume = false;
            if (Active)
            {
                // Zoomed in, the same hand movement turns less: scale by the field of view, then the player's ADS setting.
                float sens = GameSettings.Sensitivity * (zoomFov / BaseFov) * Mathf.Lerp(1f, GameSettings.AdsSensitivity, ads);
                if (Pings.WheelOpen) sens = 0f;   // the mouse picks a wheel slot instead of turning
                yaw = Mathf.Repeat(yaw + Input.GetAxisRaw("Mouse X") * LookSensitivity * 10f * sens + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;
                pitch = Mathf.Clamp(pitch + Input.GetAxisRaw("Mouse Y") * LookSensitivity * 10f * sens, -Defs.LookLimit, Defs.LookLimit);
                if (TestAimbot && Input.GetMouseButton(1)) LockOnNearest(Time.deltaTime);
                Ping();
                if (Bindings.Pressed(Bindings.Action.View)) thirdPerson = !thirdPerson;
                if (Bindings.Pressed(Bindings.Action.Slot1)) desiredSlot = 0;
                if (Bindings.Pressed(Bindings.Action.Slot2)) desiredSlot = 1;
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f && Sim.Actors.TryGetValue(LocalId, out var me))
                    desiredSlot = (desiredSlot + 1) % Mathf.Max(1, me.Loadout.Equipped.Count);
            }
            else Pings.CancelInput();
            Render(Time.deltaTime);
        }

        /// For the automated screenshots: one aimbot step as if the aim button were held.
        public void AimbotStepForCapture(float dt) => LockOnNearest(dt);

        void LockOnNearest(float dt)
        {
            if (!Sim.Actors.TryGetValue(LocalId, out var me) || !me.Alive) return;
            Vector3 eye = me.Position + Vector3.up * Defs.EyeHeight;
            Vector3 view = GameSim.LookDirection(yaw, pitch);
            Vector3? best = null;
            float bestAngle = TestAimbotFov;
            foreach (var e in Sim.Enemies.Values)
            {
                if (e.Hp <= 0) continue;
                var head = e.Position + Vector3.up * Defs.HeadHeight * e.Scale;
                var to = head - eye;
                float distance = to.magnitude;
                if (distance < 0.5f || distance > Defs.ShotRange) continue;
                float angle = Vector3.Angle(view, to);
                if (angle >= bestAngle || GameSim.RayBlock(eye, to / distance, distance) < distance - 0.05f) continue;
                bestAngle = angle; best = head;
            }
            if (best == null) return;
            var d = best.Value - eye;
            float targetYaw = Mathf.Atan2(d.x, d.z), targetPitch = Mathf.Clamp(Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude), -Defs.LookLimit, Defs.LookLimit);
            float k = TestAimbotSmooth <= 0.01f ? 1f : 1f - Mathf.Exp(-dt * Mathf.Lerp(40f, 4f, TestAimbotSmooth));
            yaw += Mathf.DeltaAngle(yaw * Mathf.Rad2Deg, targetYaw * Mathf.Rad2Deg) * Mathf.Deg2Rad * k;
            yaw = Mathf.Repeat(yaw + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;
            pitch += (targetPitch - pitch) * k;
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
                Dash = active && Bindings.Held(Bindings.Action.Dash) && !(me != null && me.WeaponId == "sniper" && ads > 0.5f),   // scoped: Shift holds breath
                Jump = active && Bindings.Held(Bindings.Action.Jump),
                Descend = active && Bindings.Held(Bindings.Action.Descend),
                Reload = active && Bindings.Held(Bindings.Action.Reload),
                Skill = active && Bindings.Held(Bindings.Action.Skill),
                Interact = active && Bindings.Held(Bindings.Action.Interact),
                // Right mouse: guard with a blade, aim down sights with a gun (the sim tightens spread and slows the walk).
                Block = me != null && (active && Input.GetMouseButton(1) || AimForCapture && !me.Melee),
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
            if (!Paused) Sim.Tick(Time.fixedDeltaTime);   // offline: the pause menu freezes the mission
        }

        void DrainEvents()
        {
            // A shotgun blast arrives as one event per pellet: sound and muzzle flash play once per shooter,
            // and your hit confirmation once per batch (a headshot if any pellet was one).
            firedThisBatch.Clear();
            localHit = localHead = localKill = localSlash = false;
            foreach (var shot in Sim.Shots) ShotEffect(shot);
            if (localSlash) Sfx.Play("saber_hit", 0.55f);
            else if (localHit || localHead) Sfx.Play(localHead ? "head" : "hit", 0.45f);
            if (localKill) Sfx.Play("kill", 0.5f);
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

        readonly HashSet<int> firedThisBatch = new HashSet<int>();
        bool localHit, localHead, localKill, localSlash;
        bool swingLeft;
        Vector3 meleeEuler, meleeOffset;
        float blockPose;

        /// A sword swing: one light ribbon and swing sound per swinger and batch, a burst of sparks on each enemy hit.
        void SwingEffect(ShotEvent shot, Actor swinger)
        {
            int rarity = swinger.Loadout.RarityOf(swinger.WeaponId);
            Color blade = Models.BladeColor(rarity);
            bool mine = shot.Shooter == LocalId;
            if (firedThisBatch.Add(shot.Shooter))
            {
                if (mine) { ApplyRecoil(); swingLeft = !swingLeft; }
                bool firstPerson = mine && !thirdPerson;
                Vector3 centre = firstPerson ? Camera.transform.position + Vector3.down * 0.25f : shot.Origin + Vector3.down * 0.35f;
                WeaponFx.Slash(centre, swinger.Yaw, firstPerson ? 1.25f : Mathf.Max(1.2f, swinger.Range * 0.7f), blade, mine ? swingLeft : true, rarity);
                if (mine) Sfx.Play(Sfx.FireName("saber", rarity), 0.55f);
                else Sfx.PlayAt(Sfx.FireName("saber", rarity), shot.Origin, 0.5f);
                if (mine) Hud.OnLocalShot(shot, Camera);
            }
            if (!shot.Hit) return;
            if (mine) { localHit = true; localKill |= shot.Killed; localSlash = true; }
            Burst.Sphere(shot.End, Color.Lerp(blade, Color.white, 0.4f), 0.85f, 5f, 0.15f, rarity >= 3 ? 1.1f : 0.7f, 0.12f).GetComponent<Burst>().AddLight(blade, 3f, 4f);
            // 傳奇+: an expanding ring of light where the blade connects.
            if (rarity >= 3) Burst.Sphere(shot.End, blade, 0.35f, 3f, 0.3f, 1.8f, 0.25f);
            for (int i = 0; i < 6; i++)
            {
                var spark = Build.Cube(null, shot.End, Vector3.one * 0.05f, Color.Lerp(blade, Color.white, 0.5f), 5f, false);
                effects.Add((spark, 0.3f, UnityEngine.Random.onUnitSphere * 5f + Vector3.up * 2f, 700f));
            }
        }

        void ShotEffect(ShotEvent shot)
        {
            if (shot.Shooter < 0) { GadgetEffect(shot); return; }
            Vector3 origin = shot.Shooter == LocalId && muzzle != null && !thirdPerson ? muzzle.position : shot.Origin;
            if (actorViews.TryGetValue(shot.Shooter, out var shooterView) && shooterView.activeSelf)
            {
                var shooterRig = shooterView.GetComponent<CharacterRig>();
                bool blade = Sim.Actors.TryGetValue(shot.Shooter, out var holder) && holder.Melee;
                // A swing is one event per enemy hit: animate it once per batch. Guns kick on every shot.
                if (shooterRig != null && blade && !firedThisBatch.Contains(shot.Shooter)) shooterRig.Swing();
                else if (shooterRig != null && !blade) shooterRig.Fire();
                if (shooterRig != null && shooterRig.Muzzle != null) origin = shooterRig.Muzzle.position;
            }
            if (Sim.Actors.TryGetValue(shot.Shooter, out var swinger) && swinger.Melee) { SwingEffect(shot, swinger); return; }
            // A miss that stopped short of full range hit a wall or the ground.
            if (!shot.Hit && Vector3.Distance(shot.Origin, shot.End) < Defs.ShotRange - 0.5f) ImpactEffect(shot.End, shot.End - shot.Origin);
            int rarity = Sim.Actors.TryGetValue(shot.Shooter, out var shooter) ? shooter.Loadout.RarityOf(shooter.WeaponId) : 1;
            bool firstOfBatch = firedThisBatch.Add(shot.Shooter);
            if (firstOfBatch) ShotSound(shot, shooter, rarity);
            if (shot.Shooter == LocalId) { localHit |= shot.Hit; localHead |= shot.Head; localKill |= shot.Killed; }
            if (rarity >= 3) { RarityShot(shot, origin, rarity); if (shot.Shooter == LocalId) { Hud.OnLocalShot(shot, Camera); ApplyRecoil(); } return; }
            if (shooter != null && firstOfBatch) MuzzleFlash(shooter.WeaponId, origin, shot.Shooter == LocalId && !thirdPerson);
            WeaponFx.Tracer(shooter?.WeaponId ?? "rifle", origin, shot.End);
            if (shot.Hit)
            {
                var spark = Build.Sphere(null, shot.End, shot.Head ? 0.6f : 0.4f, shot.Head ? Palette.Amber : Palette.Hex("ff8855"), 2.5f, false);
                effects.Add((spark, 0.12f, Vector3.zero, 0));
                Art.HitDebris(effects, shot.End, origin, shot.Head ? Palette.Amber : Palette.Hex("ff8855"), shot.Head);
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
            if (mine) Sfx.Play(Sfx.FireName(shooter.WeaponId, rarity), 0.55f);
            else Sfx.PlayAt(Sfx.FireName(shooter.WeaponId, rarity), shot.Origin, 0.5f);   // teammates: from where they stand
        }

        /// Muzzle flash for ordinary weapons (傳奇+ have their own): a bright core, a softer bloom and a short
        /// point light that lights up nearby walls, ground and the weapon. Sized per gun.
        void MuzzleFlash(string weaponId, Vector3 origin, bool firstPerson)
        {
            float size; Color color;
            switch (weaponId)
            {
                case "shotgun": size = 0.55f; color = Palette.Hex("ff9a4a"); break;
                case "sniper": size = 0.6f; color = Palette.Hex("c9a6ff"); break;
                case "rifle": size = 0.36f; color = Palette.Hex("ffc46a"); break;
                default: size = 0.28f; color = Palette.Hex("ffd98a"); break;
            }
            float scale = firstPerson ? 0.5f : 1f;
            var core = Burst.Sphere(origin, Color.Lerp(color, Color.white, 0.6f), 0.95f, 5f, size * 0.35f * scale, size * 0.6f * scale, 0.045f);
            core.GetComponent<Burst>().AddLight(color, firstPerson ? 2.2f : 3.5f, firstPerson ? 4f : 7f);
            Burst.Sphere(origin, color, 0.55f, 4f, size * 0.5f * scale, size * 1.1f * scale, 0.07f);
        }

        /// Bullet striking cover or ground: a brief flash, a puff of dust and a few sparks bouncing back.
        void ImpactEffect(Vector3 at, Vector3 incoming)
        {
            Vector3 back = -incoming.normalized;
            WeaponFx.Scorch(at, WeaponFx.SurfaceNormal(at, incoming));
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
            // Drawn on the HUD in the game font (the old 3D text used a Latin-only font, so Chinese titles fell
            // back to a system font, and fog and bloom softened it further).
            if (!nameTags.TryGetValue(a.Id, out var tag))
            {
                tag = Ui.Label(Hud.Canvas.transform, "", 15, Palette.Teammate, TextAnchor.LowerCenter, true).Outlined(0.85f);
                tag.rectTransform.sizeDelta = new Vector2(320, 24);
                tag.rectTransform.pivot = new Vector2(0.5f, 0f);
                tag.rectTransform.anchorMin = tag.rectTransform.anchorMax = Vector2.zero;
                tag.rectTransform.SetAsFirstSibling();   // under the HUD panels
                nameTags[a.Id] = tag;
            }
            tag.text = a.Alive ? a.Name : a.Name + $"  [{Bindings.Name(Bindings.Action.Interact)} 救援]";
            tag.color = a.Alive ? Palette.Teammate : Palette.Danger;
            Vector3 screen = Camera.WorldToScreenPoint(view.transform.position + Vector3.up * 2.6f);
            bool visible = screen.z > 0.2f && Hud.Canvas.gameObject.activeInHierarchy;
            tag.enabled = visible;
            if (visible)
            {
                float scale = Hud.Canvas.scaleFactor > 0 ? Hud.Canvas.scaleFactor : 1f;
                tag.rectTransform.anchoredPosition = new Vector2(Mathf.Round(screen.x / scale), Mathf.Round(screen.y / scale));
                tag.fontSize = Mathf.RoundToInt(Mathf.Clamp(15f * 12f / Mathf.Max(4f, screen.z), 11f, 16f));   // smaller far away
            }
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
            // Learnable pattern: the first shots climb straight up, then the muzzle weaves left and right.
            if (Time.time - lastShotAt > 0.32f) burstShots = 0;
            burstShots++;
            lastShotAt = Time.time;
            float steady = ads > 0.5f ? 0.72f : 1f;
            float kick = w.Recoil * (burstShots <= 6 ? 1f : 0.75f) * UnityEngine.Random.Range(0.92f, 1.08f) * steady;
            float weave = Mathf.Sin(burstShots * 0.55f + 0.6f) * (burstShots > 4 ? 1f : 0.3f);
            pitch = Mathf.Clamp(pitch + kick, -Defs.LookLimit, Defs.LookLimit);
            yaw += (weave * 2.2f + UnityEngine.Random.Range(-0.35f, 0.35f)) * w.Sway * steady;
            camRoll += (weave >= 0 ? 1f : -1f) * kick * Mathf.Rad2Deg * 0.9f;
            if (!me.Melee) Hud.ScopeKick(kick);
            recoilPending += kick * 0.65f;
            shake = 0.02f + kick * 0.9f;
            cycle = 0f;
            this.kick = me.Melee ? 0.35f : 1f;
            if (me.Melee) return;   // a blade has no heat or magazine
            heat = Mathf.Min(1.2f, heat + (me.WeaponId == "sniper" ? 0.4f : me.WeaponId == "shotgun" ? 0.26f : me.WeaponId == "rifle" ? 0.055f : 0.08f));
            hotVented = false;
            if (me.Ammo > 0 && me.Ammo <= Mathf.Max(2, Mathf.CeilToInt(me.Mag * 0.2f))) Sfx.Play("lowammo", 0.35f);
        }

        void ExplosionEffect(ExplosionEvent ex)
        {
            Color color = ex.Color;
            float r = ex.Radius;
            Burst.Sphere(ex.Position, Color.white, 0.95f, 5f, r * 0.2f, r * 0.9f, 0.09f).GetComponent<Burst>().AddLight(color, 8f, r * 4f);
            Burst.Sphere(ex.Position, color, 0.7f, 3.5f, r * 0.4f, r * 1.5f, 0.3f);
            Burst.Sphere(ex.Position, Color.Lerp(color, Color.white, 0.4f), 0.5f, 4f, r * 0.2f, r * 1.2f, 0.18f);
            Burst.Ring(new Vector3(ex.Position.x, 0.06f, ex.Position.z), color, r * 1.8f, 0.4f);
            if (ex.Position.y < 1.5f) Art.ScorchMark(ex.Position, r);
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
            Sfx.PlayAt("explosion", ex.Position, 1f);
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
            Pings.Update(Camera, Sim, me != null ? me.Position : Camera.transform.position);
            foreach (var a in Sim.Actors.Values)
            {
                if (!actorViews.TryGetValue(a.Id, out var view))
                {
                    view = Models.Soldier(a.Id == LocalId ? Palette.Player : Art.TeamColors[Mathf.Abs(a.Id) % Art.TeamColors.Length]);
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
                    if (e.Elite) { Models.MakeElite(view); Art.GroundRing(view.transform, Palette.Hex("ffcf4a"), 0.8f); }
                    if (e.Boss) Art.GroundRing(view.transform, Palette.Danger, 1.0f);
                    view.transform.position = e.Position;
                    enemyViews[e.Id] = view;
                    enemyParts[e.Id] = (CharacterRig.Find(view.transform, "Laser"), view.GetComponent<CharacterRig>(), CharacterRig.Find(view.transform, "ChargeGlow"));
                }
                var (laser, enemyRig, chargeGlow) = enemyParts[e.Id];
                // Imperial Guard wind-up (1) and rush (3): red warning glow so the charge can be dodged.
                if (chargeGlow != null) chargeGlow.gameObject.SetActive(e.State == 1 || e.State == 3);
                view.transform.position = Vector3.Lerp(view.transform.position, e.Position, 1f - Mathf.Exp(-14f * dt));
                view.transform.rotation = Quaternion.Euler(0, e.Yaw * Mathf.Rad2Deg, 0);
                if (enemyHp.TryGetValue(e.Id, out var lastHp) && e.Hp < lastHp - 0.5f)
                {
                    if (enemyRig != null) enemyRig.Hit();
                    HitFlash.On(view).Flash();
                }
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
                if (b.Velocity.sqrMagnitude > 0.01f) view.transform.rotation = Quaternion.LookRotation(b.Velocity);   // sniper rounds and missiles point along their flight
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
            string toast = Sim.State == MissionState.Playing && !Paused && !Sim.BuffPending && !ChatHud.Typing && Cursor.lockState != CursorLockMode.Locked ? "點擊畫面恢復滑鼠瞄準" : Objective;
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
            camRoll = Mathf.Lerp(camRoll, 0f, 1f - Mathf.Exp(-10f * dt));
            var rotation = Quaternion.Euler(-pitch * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg, camRoll);
            Vector3 eye = actorViews.TryGetValue(LocalId, out var view) ? view.transform.position + Vector3.up * Defs.EyeHeight : me.Position + Vector3.up * Defs.EyeHeight;
            if (!me.Alive) eye = me.Position + Vector3.up * 0.5f;
            Camera.transform.rotation = rotation;
            Camera.transform.position = thirdPerson ? eye + rotation * ThirdPersonOffset : eye;
            if (thirdPerson && Camera.transform.position.y < 0.4f) Camera.transform.position = new Vector3(Camera.transform.position.x, 0.4f, Camera.transform.position.z);

            bool showWeapon = me.Alive && !thirdPerson && Sim.State == MissionState.Playing;
            weaponHolder.gameObject.SetActive(showWeapon);
            if (me.Slot != shownSlot)
            {
                if (shownSlot >= 0) Sfx.Play("switch", 0.5f);
                shownSlot = me.Slot;
                desiredSlot = me.Slot;
                muzzle = Models.Viewmodel(me.WeaponId, weaponHolder, WeaponRarity?.Invoke(me.WeaponId) ?? 1, true);
                magazine = weaponHolder.Find("Mag");
                if (magazine != null) magazineRest = magazine.localPosition;
                slidePart = weaponHolder.Find("Slide"); boltPart = weaponHolder.Find("Bolt"); pumpPart = weaponHolder.Find("Pump");
                if (slidePart != null) slideRest = slidePart.localPosition;
                if (boltPart != null) { boltRest = boltPart.localPosition; boltRotRest = boltPart.localRotation; }
                if (pumpPart != null) pumpRest = pumpPart.localPosition;
                // Found by name anywhere in the gun (the pistol's bar rides on its slide).
                var named = new Dictionary<string, MeshRenderer>();
                foreach (var r in weaponHolder.GetComponentsInChildren<MeshRenderer>(true)) named[r.name] = r;
                ammoCells = new MeshRenderer[Models.AmmoCells];
                for (int i = 0; i < ammoCells.Length; i++) named.TryGetValue("Ammo" + i, out ammoCells[i]);
                heatSleeve = weaponHolder.Find("Heat")?.GetComponent<MeshRenderer>();
                shownCells = -1; heatStep = -1;
                cycle = 99f; heat = 0f;
            }
            float dashT = me.DashCooldown > 0 ? Defs.DashCooldown - me.DashCooldown : 99f;
            float dash = me.Alive && dashT < 0.32f ? Mathf.Sin(Mathf.PI * dashT / 0.32f) : 0f;
            bool zooming = (Input.GetMouseButton(1) && Active || AimForCapture) && showWeapon;
            zoomFov = Mathf.Lerp(zoomFov, BaseFov * (zooming ? me.Zoom : 1f), 1f - Mathf.Exp(-14f * dt));
            Camera.fieldOfView = zoomFov + 7f * dash;
            // Aim down sights: bring the gun's sight onto the view axis while right mouse is held.
            bool wantAds = zooming && !me.Melee && reloadIdle(me);
            if (wantAds != adsWanted && showWeapon && !AimForCapture) Sfx.Play(wantAds ? "ads_in" : "ads_out", 0.45f);
            adsWanted = wantAds;
            ads = Mathf.Lerp(ads, wantAds ? 1f : 0f, 1f - Mathf.Exp(-16f * dt));
            bool sniper = me.WeaponId == "sniper";
            Hud.SetAds(ads, sniper);
            UpdateSway(sniper && ads > 0.6f, dt);
            // Sniper: once the scope fills the screen, hide the gun model so it doesn't poke into the glass.
            if (weaponHolder != null) weaponHolder.gameObject.SetActive(showWeapon && !(me.WeaponId == "sniper" && ads > 0.8f));
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
            AnimateWeapon(me, dt);
            var hipPos = WeaponRest + new Vector3(0.02f, -0.045f, -0.1f) * dash + new Vector3(0, 0, -shake)
                + new Vector3(-0.03f, -0.07f, -0.04f) * tilt + new Vector3(0, 0.012f, 0) * seat + new Vector3(0, 0.006f, -0.035f) * kick + meleeOffset;
            var hipRot = Quaternion.Euler(new Vector3(6f, 2f, -6f) * dash + new Vector3(-14f, 18f, 32f) * tilt + new Vector3(-4f, 0, 0) * seat
                + new Vector3(-5f, 0, 0) * kick + meleeEuler);
            // Aimed: the sight sits dead centre; shots push the gun straight back and tip the muzzle up a touch.
            var adsPos = Models.AdsPose(me.WeaponId) + new Vector3(0, 0.003f, -0.03f) * kick + new Vector3(0, 0, -shake * 0.4f);
            var adsRot = Quaternion.Euler(-2.2f * kick, 0, 0);
            float aimed = Mathf.SmoothStep(0, 1, ads);
            weaponHolder.localPosition = Vector3.Lerp(hipPos, adsPos, aimed);
            weaponHolder.localRotation = Quaternion.Slerp(hipRot, adsRot, aimed);
        }

        /// Aim sway: a slow figure-eight while aimed (wide on the sniper scope). Holding Shift while scoped steadies it
        /// for up to four seconds; run out and the sway doubles until the lungs refill halfway.
        void UpdateSway(bool scoped, float dt)
        {
            if (!Active) return;   // paused or typing: the view holds still
            bool wantHold = scoped && Bindings.Held(Bindings.Action.Dash);
            holdingBreath = wantHold && !breathSpent && breath > 0f;
            breath = Mathf.Clamp01(breath + (holdingBreath ? -dt / 4f : dt / 3f));
            if (holdingBreath && breath <= 0f) { breathSpent = true; holdingBreath = false; Sfx.Play("breath_out", 0.5f); }
            if (breathSpent && breath >= 0.5f) breathSpent = false;
            float amp = scoped ? (holdingBreath ? 0.0003f : breathSpent ? 0.009f : 0.0045f) : ads > 0.5f ? 0.0009f : 0f;
            if (AimForCapture) amp = 0f;
            swayClock += dt * (breathSpent ? 1.6f : 1f);
            float sx = Mathf.Sin(swayClock * 0.9f) * amp, sy = Mathf.Sin(swayClock * 1.8f) * amp * 0.6f;
            // Move the view by the change in offset, so the player's own aim is never pulled away.
            float k = 1f - Mathf.Exp(-6f * dt);
            float nx = Mathf.Lerp(swayYaw, sx, k), ny = Mathf.Lerp(swayPitch, sy, k);
            yaw += nx - swayYaw;
            pitch = Mathf.Clamp(pitch + ny - swayPitch, -Defs.LookLimit, Defs.LookLimit);
            swayYaw = nx; swayPitch = ny;
            Hud.SetBreath(scoped, breath, holdingBreath, breathSpent);
        }

        /// For the automated screenshots: pose the weapon at a given heat and time since the last shot.
        public void SetWeaponStateForCapture(float heatLevel, float sinceShot)
        {
            heat = heatLevel; cycle = sinceShot; hotVented = true;
        }

        /// Slide, bolt and pump cycling after each shot, the ammo bar and the barrel heat glow.
        void AnimateWeapon(Actor me, float dt)
        {
            cycle += dt;
            kick *= Mathf.Exp(-dt * 16f);
            if (me.Melee)
            {
                // Swing: a fast sweep across the view (alternating sides), then back to rest. Block: blade raised
                // across the body while the guard is held.
                float t = cycle / 0.2f, side = swingLeft ? 1f : -1f;
                float sweep = t < 1f ? Mathf.Lerp(-1f, 1f, 1f - (1f - t) * (1f - t)) : 0f;
                float arc = t < 1f ? Mathf.Sin(Mathf.PI * t) : t < 1.8f ? 0f : 0f;
                float settle = t >= 1f && t < 1.8f ? 1f - (t - 1f) / 0.8f : 0f;
                meleeEuler = new Vector3(-25f * arc + 10f * settle, 75f * sweep * side * (t < 1f ? 1f : settle), -55f * arc * side);
                meleeOffset = new Vector3(-0.14f * sweep * side * (t < 1f ? 1f : settle), 0.04f * arc, 0.08f * arc);
                bool guarding = CaptureGuard || Active && Input.GetMouseButton(1) && !Input.GetMouseButton(0);
                blockPose = Mathf.MoveTowards(blockPose, guarding ? 1f : 0f, dt * 7f);
                meleeEuler += new Vector3(-10f, -20f, 80f) * blockPose;
                meleeOffset += new Vector3(-0.15f, 0.1f, 0.02f) * blockPose;
            }
            else { meleeEuler = Vector3.zero; meleeOffset = Vector3.zero; blockPose = 0f; }
            // Fast back-and-return: snaps back in a few ms, eases home.
            float Snap(float t, float back, float total) => t < back ? t / back : Mathf.Max(0f, 1f - (t - back) / (total - back));
            if (slidePart != null) slidePart.localPosition = slideRest + new Vector3(0, 0, -0.04f) * Snap(cycle, 0.025f, 0.11f);
            if (pumpPart != null)
            {
                float t = (cycle - 0.14f) / 0.36f;   // the pump is racked just after the blast
                pumpPart.localPosition = pumpRest + new Vector3(0, 0, -0.075f) * (t > 0f && t < 1f ? Mathf.Sin(Mathf.PI * t) : 0f);
            }
            if (boltPart != null)
            {
                if (me.WeaponId == "sniper")
                {
                    // Bolt action: lift, pull back, push forward, lock down.
                    float t = (cycle - 0.18f) / 0.7f;
                    float lift = 0f, pull = 0f;
                    if (t > 0f && t < 1f)
                    {
                        lift = t < 0.2f ? t / 0.2f : t > 0.8f ? (1f - t) / 0.2f : 1f;
                        pull = t < 0.2f || t > 0.8f ? 0f : Mathf.Sin(Mathf.PI * (t - 0.2f) / 0.6f);
                    }
                    boltPart.localRotation = boltRotRest * Quaternion.Euler(0, 0, 65f * lift);
                    boltPart.localPosition = boltRest + new Vector3(0, 0, -0.07f) * pull;
                }
                else boltPart.localPosition = boltRest + new Vector3(0, 0, -0.03f) * Snap(cycle, 0.02f, 0.085f);
            }
            // Ammo bar: lit cells for rounds left, red near the end, dark while reloading.
            if (ammoCells.Length > 0)
            {
                int lit = me.Reload > 0 ? 0 : Mathf.CeilToInt(Models.AmmoCells * (float)me.Ammo / Mathf.Max(1, me.Mag));
                bool low = me.Ammo <= Mathf.Max(2, Mathf.CeilToInt(me.Mag * 0.2f));
                if (lit != shownCells || low != shownLow)
                {
                    shownCells = lit; shownLow = low;
                    var on = Build.Mat(low ? Models.AmmoLow : Models.AmmoLit, 5f);
                    var off = Build.Mat(Models.AmmoOff);
                    for (int i = 0; i < ammoCells.Length; i++) if (ammoCells[i] != null) ammoCells[i].sharedMaterial = i < lit ? on : off;
                }
            }
            // Heat: builds with fire, cools when you stop; the barrel tip glows and vents steam once when very hot.
            heat = Mathf.Max(0f, heat - dt * (cycle > 0.35f ? 0.55f : 0.12f));
            if (heatSleeve != null)
            {
                // Eight glow steps, each a cached translucent material; hidden when cold.
                int step = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01((heat - 0.35f) / 0.65f) * 8f), 0, 8);
                if (step != heatStep)
                {
                    heatStep = step;
                    heatSleeve.enabled = step > 0;
                    if (step > 0) heatSleeve.sharedMaterial = Build.Mat(Palette.Hex("ff4a14"), 3f, step / 8f * 0.6f);
                }
                if (!hotVented && heat > 0.85f && cycle > 0.3f)
                {
                    hotVented = true;
                    WeaponFx.Steam(heatSleeve.transform.position, 0.6f, 3);
                    Sfx.Play("vent", 0.3f);
                }
            }
        }

        void UpdateAudio(Actor me, float dt)
        {
            if (me == null) return;
            float ratio = me.Reload > 0 ? me.Reload / Mathf.Max(0.01f, me.ReloadTime) : 0f;
            if (me.Alive && me.Slot == lastReloadSlot)
            {
                if (lastReloadRatio <= 0 && ratio > 0)
                {
                    Sfx.Play("reload_out");
                    Sfx.Play("vent", 0.35f);
                    if (heatSleeve != null && !thirdPerson) WeaponFx.Steam(heatSleeve.transform.position, 0.7f, 4);
                    heat *= 0.3f;
                }
                else if (lastReloadRatio > 0.4f && ratio <= 0.4f && ratio > 0) Sfx.Play("reload_in");
                else if (lastReloadRatio > 0 && ratio <= 0 && me.Ammo >= me.Mag) Sfx.Play("reload_rack");
            }
            if (me.Alive && me.Slot == lastReloadSlot && lastAmmo > 0 && me.Ammo == 0) Sfx.Play("dry", 0.55f);
            lastAmmo = me.Ammo;
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
