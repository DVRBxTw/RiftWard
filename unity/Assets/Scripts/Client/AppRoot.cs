using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RiftWard.Sim;
using UnityEngine;

namespace RiftWard.Client
{
    /// Application entry point and screen router: account → story/training → home ↔ armory/admin → mission.
    public sealed class AppRoot : MonoBehaviour
    {
        public GameClient Client { get; private set; }
        public Screens Screens { get; private set; }
        public AccountApi Api { get; private set; }
        public Profile Profile { get; private set; }
        public AdminSettings AdminSettings { get; } = new AdminSettings();
        public int SelectedRegion { get; private set; }
        /// Difficulty tier for the next deployment (the room leader's choice when in a room).
        public int SelectedDifficulty { get; private set; }
        public bool CanDeploy => Profile != null && (!Net.Welcomed || leaderId == Net.LocalId);
        public NetClient Net { get; } = new NetClient();
        /// Room code shown to the party; empty when playing alone.
        public string RoomCode { get; private set; } = "";
        public bool InRoom => RoomCode != "";
        public bool IsLeader => leaderId == Net.LocalId;
        public int PartySize => partySize;

        public SocialOverlay Overlay { get; private set; }
        public ChatHud Chat { get; private set; }
        /// The latest invitation that arrived during a mission, offered again on the 社交 page.
        public (int from, string name, string code)? PendingInvite { get; private set; }

        public void SendChat(byte channel, string text)
        {
            if (!Net.Welcomed) { ChatLog.Notice("離線模式無法聊天，請連線到遊戲伺服器"); return; }
            Net.Send(Msg.Chat, w => { w.Write(channel); w.Write(text); });
        }

        public void InviteFriend(int accountId) { if (Net.Welcomed) Net.Send(Msg.InviteFriend, w => w.Write(accountId)); else Overlay.Toast("離線模式無法邀請好友"); }
        public void RefreshFriends() { if (Net.Welcomed) Net.Send(Msg.RefreshFriends); }

        public void AcceptInvite(string code)
        {
            PendingInvite = null;
            if (inNetMission) { Overlay.Toast("請先結束目前的任務再加入房間"); return; }
            JoinRoom(code);
        }

        public void DeclineInvite(int from)
        {
            PendingInvite = null;
            if (Net.Welcomed) Net.Send(Msg.DeclineInvite, w => w.Write(from));
        }

        public void CreateRoom() { if (Net.Welcomed) Net.Send(Msg.CreateRoom); }
        public void JoinRoom(string code) { if (Net.Welcomed && !string.IsNullOrWhiteSpace(code)) Net.Send(Msg.JoinRoom, w => w.Write(code.Trim().ToUpperInvariant())); }
        public void LeaveRoom() { if (Net.Welcomed) Net.Send(Msg.LeaveRoom); }
        int leaderId = -1, partySize, gamePort = Protocol.DefaultPort;
        /// Highest tier cleared in the deployed region when the mission began; the result sheet compares against it.
        int clearedAtDeploy = -1;
        bool inNetMission, netTest;
        string lobbyMode = "lobby";
        string homeStatus = "離線模式 · 單人任務";
        bool training;
        int trainingStep;
        Vector3 trainingOrigin;
        float trainingYaw;

        static string Arg(string name, string fallback = null)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : (i >= 0 ? "" : fallback);
        }

        void Start()
        {
            if (Arg("-server") != null)
            {
                gameObject.AddComponent<GameServer>();
                enabled = false;
                return;
            }
            Client = gameObject.AddComponent<GameClient>();
            Client.Setup();
            Api = new AccountApi(this);
            Screens = new Screens(this);
            Overlay = new SocialOverlay { Join = AcceptInvite, Decline = DeclineInvite };
            Chat = new ChatHud { Send = SendChat };
            Net.Message += OnNetMessage;
            Net.Disconnected += OnNetDisconnected;
            var env = Environment.GetEnvironmentVariable("RIFTWARD_TEST_API_PORT");
            if (!string.IsNullOrEmpty(env)) { Api.Host = Environment.GetEnvironmentVariable("RIFTWARD_TEST_API_HOST") ?? "127.0.0.1"; Api.Port = int.Parse(env); }
            var gp = Environment.GetEnvironmentVariable("RIFTWARD_TEST_GAME_PORT");
            if (!string.IsNullOrEmpty(gp)) gamePort = int.Parse(gp);
            netTest = Arg("-net-test") != null;
            if (netTest) { OnAuthenticated(Environment.GetEnvironmentVariable("RIFTWARD_TEST_TOKEN") ?? ""); return; }
            Client.MissionEnded += OnMissionEnded;
            GameSettings.Load();
            GameSettings.Apply(Arg("-capture") == null && !Application.isBatchMode);
            Client.Menus.SettingsPanel = GameSettings.BuildPanel;
            Client.WeaponRarity = id => Profile?.Loadout.RarityOf(id) ?? 1;
            Client.ShowcaseWeapon = () => Profile != null && Profile.Loadout.Equipped.Count > 0 ? Profile.Loadout.Equipped[0] : "rifle";
            Client.Menus.LeaveRequested = ReturnHome;
            Client.Menus.RetryRequested = Deploy;
            var bench = Arg("-bench");
            if (bench != null) { StartCoroutine(Bench(bench)); return; }
            var capture = Arg("-capture");
            if (capture != null) { StartCoroutine(Capture(string.IsNullOrEmpty(capture) ? "." : capture)); return; }
            var token = SessionStore.Load(Api.Host);
            if (token != null) { Screens.ShowBusy("正在恢復登入，請稍候…"); OnAuthenticated(token); }
            else Screens.ShowAccount();
        }

        // ------------------------------------------------------------ account

        public void OnAuthenticated(string token)
        {
            Api.Token = token;
            Api.Post("/api/me", null, result =>
            {
                if (!result.Bool("ok"))
                {
                    if (result.Int("http_status") == 401) SessionStore.Forget();
                    Api.Token = "";
                    Screens.ShowAccount(result.Str("error", "登入已失效，請重新登入。"), true);
                    return;
                }
                if (!netTest) SessionStore.Save(token, Api.Host);
                UpdateProfile(Profile.From(result.Obj("profile")));
                if (!Profile.TutorialComplete) { Screens.ShowStory(0); return; }
                ConnectBase();
            });
        }

        /// Connects to the game server; falls back to offline missions if it is unreachable.
        void ConnectBase()
        {
            if (Net.Connected) { ShowHome(); return; }
            homeStatus = "正在連接基地…";
            Screens.ShowBusy("正在驗證帳號並連接基地…");
            Net.Connect(Api.Host, gamePort, Api.Token, (ok, error) =>
            {
                if (!ok) { homeStatus = "遊戲伺服器離線 · 離線單人任務"; ShowHome(); }
            });
        }

        void RefreshMe()
        {
            Api.Post("/api/me", null, result => { if (result.Bool("ok")) UpdateProfile(Profile.From(result.Obj("profile")), false); });
        }

        void OnNetMessage(Msg type, BinaryReader r)
        {
            switch (type)
            {
                case Msg.Welcome:
                    Debug.Log("UNITY_CLIENT_WELCOME id=" + Net.LocalId);
                    if (netTest) { JoinRoom("TEST"); SendChat(ChatChannel.World, "net-test hello " + Net.LocalId); }
                    break;
                case Msg.Lobby:
                    SelectedRegion = r.ReadInt32();
                    r.ReadInt32();
                    leaderId = r.ReadInt32();
                    partySize = r.ReadInt32();
                    lobbyMode = r.ReadString();
                    RoomCode = r.ReadString();
                    SelectedDifficulty = r.ReadInt32();
                    homeStatus = !InRoom ? "已連線 · 單人任務（建立或加入房間即可組隊）"
                        : leaderId == Net.LocalId ? $"房號 {RoomCode} · 隊長 · {partySize} / 4" : $"房號 {RoomCode} · 等待隊長部署 · {partySize} / 4";
                    if (Screens.Mode == "home" || Screens.Mode == "connecting") ShowHome();
                    if (netTest && leaderId == Net.LocalId && partySize >= 2 && lobbyMode == "lobby" && !inNetMission && !testDone) Net.Send(Msg.RequestDeploy, w => { w.Write(0); w.Write(0); });
                    break;
                case Msg.Deploy:
                    int region = r.ReadInt32(), difficulty = r.ReadInt32();
                    clearedAtDeploy = Profile != null && region >= 0 && region < Profile.Cleared.Count ? Profile.Cleared[region] : -1;
                    Screens.Clear();
                    Client.Menus.HideAll();
                    Client.Objective = "";
                    training = false;
                    inNetMission = true;
                    Client.StartNetworked(region, difficulty, Net.LocalId, Net);
                    Chat.Show(true);
                    Overlay.HideInvite();
                    PushAdmin();
                    Debug.Log($"UNITY_CLIENT_DEPLOY region={region} difficulty={difficulty}");
                    break;
                case Msg.Snapshot: Client.ApplySnapshot(r); break;
                case Msg.Events: Client.ApplyEvents(r); break;
                case Msg.BuffOffer:
                    int level = r.ReadInt32();
                    var offer = new int[r.ReadInt32()];
                    for (int i = 0; i < offer.Length; i++) offer[i] = r.ReadInt32();
                    Client.ApplyBuffOffer(level, offer);
                    if (netTest) { Client.ChooseBuff(offer[0]); testPicks++; }
                    break;
                case Msg.Finish:
                    bool success = r.ReadBoolean();
                    int finishedRegion = r.ReadInt32(), kills = r.ReadInt32();
                    float elapsed = r.ReadSingle();
                    int gold = r.ReadInt32();
                    string grade = r.ReadString();
                    var record = new[] { r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32() };
                    int finishedTier = r.ReadInt32();
                    Client.MarkFinished(success);
                    int finishedWave = Client.Sim?.Wave ?? 0;
                    var progress = Profile == null ? null : new Menus.ResultProgress
                    {
                        Xp = Defs.MissionXp(record[0], finishedWave, success, finishedTier), Level = Profile.Level, LevelXp = Profile.LevelXp,
                        Wave = finishedWave, BestWave = Profile.BestWave,
                        Unlocked = success && finishedTier > clearedAtDeploy && finishedTier + 1 < Defs.Difficulties.Length ? finishedTier + 1 : -1,
                    };
                    Client.Menus.ShowResult(success, finishedRegion, kills, elapsed, gold, (Profile?.Gold ?? 0) + gold, grade, record, progress, finishedTier);
                    Debug.Log($"UNITY_CLIENT_RESULT success={success} gold={gold} picks={testPicks}");
                    if (netTest) StartCoroutine(TestFinish(success, gold));
                    break;
                case Msg.ProfileSync: RefreshMe(); break;
                case Msg.Notice:
                    homeStatus = r.ReadString();
                    if (Screens.Mode == "home") ShowHome();
                    else if (!inNetMission) Overlay.Toast(homeStatus);
                    break;
                case Msg.ChatLine:
                    var chatLine = ChatLog.Read(r);
                    ChatLog.Add(chatLine);
                    if (netTest && chatLine.Channel == ChatChannel.World && Profile != null && chatLine.AccountId != Profile.Id) testChats++;
                    break;
                case Msg.Presence: FriendPresence.Read(r); break;
                case Msg.Invite:
                    int from = r.ReadInt32();
                    string fromName = r.ReadString(), code = r.ReadString();
                    if (inNetMission)
                    {
                        PendingInvite = (from, fromName, code);
                        ChatLog.Notice($"{fromName} 邀請你加入房間 {code}，任務結束後可到「社交」頁接受");
                    }
                    else Overlay.ShowInvite(from, fromName, code);
                    break;
                case Msg.Error:
                    string message = r.ReadString();
                    Net.Close();
                    LeaveNetMission();
                    Screens.ShowAccount(message, true);
                    Debug.Log("UNITY_CLIENT_ERROR " + message);
                    break;
                case Msg.BackHome:
                    if (inNetMission) { LeaveNetMission(); ShowHome(); }
                    if (netTest && testDone) { Debug.Log("UNITY_CLIENT_RETURN_PASS"); Application.Quit(0); }
                    break;
            }
        }

        int testPicks, testChats;
        bool testDone;

        IEnumerator TestFinish(bool success, int gold)
        {
            yield return new WaitForSecondsRealtime(1.5f);
            Debug.Log(success && gold > 0 && testPicks == Defs.WavesPerMission && Profile.Gold > 0 && testChats > 0
                ? $"UNITY_CLIENT_PASS gold={gold} picks={testPicks} total={Profile.Gold} chats={testChats}"
                : $"UNITY_CLIENT_FAIL success={success} gold={gold} picks={testPicks} total={Profile?.Gold} chats={testChats}");
            testDone = true;
            Net.Send(Msg.RequestHome);
            yield return new WaitForSecondsRealtime(10f);
            Application.Quit(1);
        }

        void OnNetDisconnected(string reason)
        {
            FriendPresence.Clear();
            Overlay.HideInvite();
            PendingInvite = null;
            homeStatus = "與遊戲伺服器中斷連線 · 離線單人任務";
            if (inNetMission) { LeaveNetMission(); ShowHome(); }
            else if (Screens.Mode == "home" || Screens.Mode == "connecting") ShowHome();
        }

        void LeaveNetMission()
        {
            if (!inNetMission) return;
            inNetMission = false;
            Chat.Show(false);
            Client.Menus.HideAll();
            Client.LeaveMission();
        }

        public void UpdateProfile(Profile profile) => UpdateProfile(profile, true);

        public void UpdateProfile(Profile profile, bool notifyServer)
        {
            if (profile == null) return;
            Profile = profile;
            if (notifyServer && Net.Welcomed) Net.Send(Msg.RefreshProfile);

            Client.Menus.AdminPanel = profile.Admin ? (Action<Transform>)Screens.AbilityPanel : null;
        }

        public void Logout()
        {
            Api.Post("/api/logout", null, _ => { });
            Net.Close();
            SessionStore.Forget();
            Api.Token = "";
            Profile = null;
            Screens.ShowAccount("", true);
        }

        public void Quit() => Application.Quit();

        // ------------------------------------------------------------ home

        public void ShowHome()
        {
            if (Profile == null) { Screens.ShowAccount(); return; }
            Client.ShowShowcase(Mathf.Clamp(SelectedRegion, 0, Profile.Unlocked));
            Screens.ShowHome(homeStatus);
        }

        public void SelectRegion(int region) => SelectMission(region, SelectedDifficulty);

        public void SelectDifficulty(int tier) => SelectMission(SelectedRegion, tier);

        /// The requested tier, lowered until it is open in the region (the server applies the same rule).
        int OpenTier(int region, int tier)
        {
            tier = Mathf.Clamp(tier, 0, Defs.Difficulties.Length - 1);
            while (tier > 0 && !Profile.DifficultyOpen(region, tier)) tier--;
            return tier;
        }

        void SelectMission(int region, int tier)
        {
            region = Mathf.Clamp(region, 0, Profile.Unlocked);
            tier = OpenTier(region, tier);
            if (Net.Welcomed)
            {
                if (leaderId == Net.LocalId) Net.Send(Msg.SelectRegion, w => { w.Write(region); w.Write(tier); });
                if (!InRoom) { SelectedRegion = region; SelectedDifficulty = tier; ShowHome(); }
                return;
            }
            SelectedRegion = region;
            SelectedDifficulty = tier;
            ShowHome();
        }

        public void Deploy()
        {
            if (Net.Welcomed)
            {
                Client.Menus.HideAll();
                if (leaderId == Net.LocalId) Net.Send(Msg.RequestDeploy, w => { w.Write(SelectedRegion); w.Write(SelectedDifficulty); });
                else if (inNetMission) { LeaveNetMission(); ShowHome(); }
                return;
            }
            Client.Menus.HideAll();
            Screens.Clear();
            training = false;
            Client.Objective = "";
            int offlineRegion = Mathf.Clamp(SelectedRegion, 0, Profile.Unlocked);
            Client.StartOffline(offlineRegion, Profile.Loadout, Profile.Admin, AdminSettings, OpenTier(offlineRegion, SelectedDifficulty));
            Client.Sim?.ApplyProfileBonus(Client.LocalId, Profile.BonusOf("hp"), Profile.BonusOf("damage"), Profile.BonusOf("speed"), Profile.BonusOf("reload"), Profile.BonusOf("crit"));
        }

        void ReturnHome()
        {
            if (inNetMission)
            {
                if (Client.Sim != null && Client.Sim.State == MissionState.Playing) Net.Send(Msg.LeaveMission);
                else Net.Send(Msg.RequestHome);
                LeaveNetMission();
                ShowHome();
                return;
            }
            Client.Menus.HideAll();
            Client.LeaveMission();
            if (training) { training = false; Client.Objective = ""; Screens.ShowStory(2); return; }
            ShowHome();
        }

        void OnMissionEnded(GameSim sim)
        {
            if (training) return;
            bool success = sim.State == MissionState.Victory;
            sim.Actors.TryGetValue(Client.LocalId, out var me);
            var record = me == null ? new int[4] : new[] { me.StatKills, me.StatHeadshots, me.StatShots, me.StatHits };
            Client.Menus.ShowResult(success, sim.Region, sim.Kills, sim.Elapsed, -1, Profile?.Gold ?? 0, sim.MissionGrade(), record, null, sim.Difficulty);
        }

        public void PushAdmin()
        {
            if (Profile == null || !Profile.Admin) return;
            if (Net.Welcomed) Net.Send(Msg.Admin, w => w.Write(AdminSettings));
            else Client.Sim?.SetAdmin(Client.LocalId, AdminSettings);
        }

        // ------------------------------------------------------------ training

        public void StartTraining()
        {
            Screens.Clear();
            training = true;
            trainingStep = 0;
            Client.StartTraining();
            trainingOrigin = Client.Sim.Actors[Client.LocalId].Position;
            trainingYaw = Client.Sim.Actors[Client.LocalId].Yaw;
        }

        static readonly string[] TrainingSteps =
        {
            "訓練 1/3：移動滑鼠，轉動視角", "訓練 2/3：按 WASD 移動一段距離", "訓練 3/3：射擊前方的訓練機器人", "訓練完成，正在儲存…",
        };

        AudioSource music;

        void UpdateMusic()
        {
            if (music == null)
            {
                music = gameObject.AddComponent<AudioSource>();
                music.clip = Sfx.LobbyMusic();
                music.loop = true;
                music.volume = 0;
                music.Play();
            }
            float target = Client.Sim == null ? 0.35f * GameSettings.Music : 0f;
            music.volume = Mathf.MoveTowards(music.volume, target, Time.deltaTime * (target > 0 ? 0.25f : 0.6f));
        }

        void Update()
        {
            UpdateMusic();
            Net.Poll();
            Overlay.Update();
            Chat.Update(inNetMission, Client.Sim != null && Client.Sim.State == MissionState.Playing && !Client.Paused && !Client.Sim.BuffPending);
            if (!training || Client.Sim == null) return;
            var me = Client.Sim.Actors[Client.LocalId];
            me.Hp = me.MaxHp;
            if (trainingStep == 0 && Mathf.Abs(Mathf.DeltaAngle(me.Yaw * Mathf.Rad2Deg, trainingYaw * Mathf.Rad2Deg)) > 15f) trainingStep = 1;
            if (trainingStep == 1 && Vector3.Distance(me.Position, trainingOrigin) > 2f) trainingStep = 2;
            if (trainingStep == 2 && Client.Sim.Kills > 0) { trainingStep = 3; CompleteTraining(); }
            Client.Objective = TrainingSteps[trainingStep];
        }

        void CompleteTraining()
        {
            Api.Post("/api/tutorial", null, result =>
            {
                training = false;
                Client.Objective = "";
                Client.LeaveMission();
                if (result.Bool("ok")) { UpdateProfile(Profile.From(result.Obj("profile"))); ConnectBase(); }
                else Screens.ShowAccount("教學完成，但保存失敗：" + result.Str("error", "請重試"), true);
            });
        }

        // ------------------------------------------------------------ automated screenshots

        /// Repeatable heavy-combat run (all gadgets, mythic weapon, endless waves, auto-fire while sweeping the view)
        /// that writes frame-time statistics: average FPS, 1% low, worst frame and GC collections.
        IEnumerator Bench(string output)
        {
            var kit = new Loadout { Owned = new List<string> { "rifle", "shotgun" }, Equipped = new List<string> { "rifle", "shotgun" } };
            kit.Rarity["rifle"] = 4;
            Client.WeaponRarity = id => kit.RarityOf(id);
            Client.StartOffline(0, kit, true, new AdminSettings { God = true, InfiniteAmmo = true });
            var sim = Client.Sim;
            sim.EnemyVariety = true;
            foreach (int buff in new[] { Defs.BuffDrone + 200, Defs.BuffPlasma, Defs.BuffOrbit + 200, Defs.BuffMissile + 200, Defs.BuffLightning + 200, Defs.BuffMine + 200 })
                sim.ApplyBuff(Client.LocalId, buff);
            sim.Wave = 8;
            sim.SpawnWave();
            Client.BenchFire = true;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;   // measure raw cost, not the cap
            yield return Settle(2f);
            var times = new List<float>();
            int gc0 = System.GC.CollectionCount(0);
            float end = Time.realtimeSinceStartup + 25f, yawT = 0;
            while (Time.realtimeSinceStartup < end)
            {
                if (sim.Enemies.Count < 12) sim.SpawnWave();
                yawT += Time.unscaledDeltaTime;
                Client.SetView(Mathf.Sin(yawT * 0.7f) * 1.2f, -0.05f);
                yield return null;
                times.Add(Time.unscaledDeltaTime * 1000f);
            }
            times.Sort();
            float avg = 0; foreach (var t in times) avg += t; avg /= Mathf.Max(1, times.Count);
            var low = times.GetRange(times.Count * 99 / 100, Mathf.Max(1, times.Count - times.Count * 99 / 100));
            float lowAvg = 0; foreach (var t in low) lowAvg += t; lowAvg /= low.Count;
            int spikes = times.FindAll(t => t > 33f).Count;
            string report = $"frames={times.Count} avg_fps={1000f / avg:0.0} avg_ms={avg:0.00} low1_fps={1000f / lowAvg:0.0} worst_ms={times[times.Count - 1]:0.0} spikes_over_33ms={spikes} gc0={System.GC.CollectionCount(0) - gc0} kills={sim.Kills}";
            System.IO.File.WriteAllText(output, report);
            Application.Quit();
        }

        IEnumerator Capture(string folder)
        {
            Hints.Enabled = false;   // keep page shots clean; the hint gets its own shot
            Directory.CreateDirectory(folder);
            Screen.SetResolution(1280, 800, FullScreenMode.Windowed);
            Screens.ShowAccount();
            yield return Settle(0.5f);
            yield return Shot(folder, "unity-account");

            var lo = new Loadout { Owned = new List<string> { "pistol", "rifle", "sniper" }, Equipped = new List<string> { "pistol", "sniper" } };
            lo.Levels["sniper"] = new Dictionary<string, int> { ["damage"] = 2, ["mag"] = 1 };
            lo.Levels["pistol"] = new Dictionary<string, int> { ["damage"] = 3 };
            UpdateProfile(new Profile { Id = 1, Username = "DVRBXTW", Unlocked = 1, Gold = 1240, TutorialComplete = true, Admin = true, Loadout = lo });
            ShowHome();
            yield return Settle(0.8f);
            yield return Shot(folder, "unity-home");
            Screens.ShowArmory();
            yield return Settle(0.4f);
            yield return Shot(folder, "unity-armory");
            Screens.ShowAdmin();
            yield return Settle(0.4f);
            yield return Shot(folder, "unity-admin");
            var demo = Profile.From(new Dictionary<string, object>
            {
                ["id"] = 1.0, ["username"] = "DVRBXTW", ["unlocked"] = 1.0, ["gold"] = 1240.0, ["tutorial_complete"] = true, ["admin"] = true,
                ["level"] = 7.0, ["level_xp"] = 260.0, ["level_need"] = 450.0, ["keys"] = 12.0, ["talent_points"] = 2.0,
                ["talents"] = new Dictionary<string, object> { ["hp"] = 2.0, ["damage"] = 3.0, ["crit"] = 1.0 },
                ["inventory"] = new List<object>
                {
                    new Dictionary<string, object> { ["id"] = "a", ["slot"] = "armor", ["rarity"] = 3.0, ["level"] = 3.0, ["stats"] = new Dictionary<string, object> { ["hp"] = 32.0, ["crit"] = 4.0 } },
                    new Dictionary<string, object> { ["id"] = "b", ["slot"] = "module", ["rarity"] = 4.0, ["level"] = 5.0, ["stats"] = new Dictionary<string, object> { ["damage"] = 16.0, ["speed"] = 6.0 } },
                    new Dictionary<string, object> { ["id"] = "c", ["slot"] = "boots", ["rarity"] = 1.0, ["stats"] = new Dictionary<string, object> { ["speed"] = 3.0 } },
                    new Dictionary<string, object> { ["id"] = "d", ["slot"] = "chip", ["rarity"] = 2.0, ["stats"] = new Dictionary<string, object> { ["crit"] = 5.0, ["hp"] = 10.0 } },
                    new Dictionary<string, object> { ["id"] = "e", ["slot"] = "armor", ["rarity"] = 0.0, ["stats"] = new Dictionary<string, object> { ["hp"] = 6.0 } },
                    new Dictionary<string, object> { ["id"] = "f", ["slot"] = "armor", ["rarity"] = 2.0, ["level"] = 1.0, ["stats"] = new Dictionary<string, object> { ["hp"] = 14.0, ["speed"] = 2.0 } },
                },
                ["equipment"] = new Dictionary<string, object> { ["armor"] = "a", ["module"] = "b" },
                ["cleared"] = new List<object> { 1.0, -1.0, -1.0 },
                ["daily"] = new Dictionary<string, object>
                {
                    ["resets_in"] = 18720.0,
                    ["tasks"] = new List<object>
                    {
                        new Dictionary<string, object> { ["id"] = "kills", ["text"] = "擊殺 150 名敵人", ["goal"] = 150.0, ["progress"] = 150.0, ["done"] = true, ["claimed"] = true, ["reward"] = new Dictionary<string, object> { ["gold"] = 300.0 } },
                        new Dictionary<string, object> { ["id"] = "headshots", ["text"] = "爆頭命中 40 次", ["goal"] = 40.0, ["progress"] = 40.0, ["done"] = true, ["claimed"] = false, ["reward"] = new Dictionary<string, object> { ["gold"] = 250.0, ["keys"] = 1.0 } },
                        new Dictionary<string, object> { ["id"] = "hard", ["text"] = "在困難以上難度完成 1 場任務", ["goal"] = 1.0, ["progress"] = 0.0, ["done"] = false, ["claimed"] = false, ["reward"] = new Dictionary<string, object> { ["weapon_keys"] = 1.0 } },
                    },
                    ["bonus"] = new Dictionary<string, object> { ["reward"] = new Dictionary<string, object> { ["keys"] = 2.0, ["weapon_keys"] = 1.0 }, ["ready"] = false, ["claimed"] = false },
                },
                ["bonus"] = new Dictionary<string, object> { ["hp"] = 42.0, ["damage"] = 22.0, ["speed"] = 6.0, ["crit"] = 5.0 },
                ["loadout"] = new Dictionary<string, object>
                {
                    ["owned"] = new List<object> { "pistol", "rifle", "sniper" }, ["equipped"] = new List<object> { "pistol", "sniper" },
                    ["rarity"] = new Dictionary<string, object> { ["pistol"] = 4.0, ["rifle"] = 0.0, ["sniper"] = 3.0 },
                    ["copies"] = new Dictionary<string, object> { ["pistol"] = new List<object> { 1.0, 4.0, 0.0, 2.0 }, ["rifle"] = new List<object> { 0.0 }, ["sniper"] = new List<object> { 3.0 } },
                    ["active"] = new Dictionary<string, object> { ["pistol"] = 1.0, ["rifle"] = 0.0, ["sniper"] = 0.0 },
                },
            });
            // Achievements in every state: unclaimed tiers, partial progress, finished with a title.
            var specs = new[]
            {
                ("kills", "combat", "殲滅行動", "累計擊殺 {0} 名敵人", 3120, new[] { 500, 2500, 10000 }, 1, "殲滅者"),
                ("headshots", "combat", "一槍入魂", "累計爆頭命中 {0} 次", 4210, new[] { 200, 1000, 4000 }, 3, "神射手"),
                ("bosses", "combat", "巨人殺手", "擊倒 {0} 名 Boss", 7, new[] { 5, 25, 100 }, 1, "巨人殺手"),
                ("elites", "combat", "精英獵人", "擊敗 {0} 名精英敵人", 4, new[] { 10, 50, 200 }, 0, ""),
                ("victories", "clear", "百戰老兵", "完成 {0} 場任務", 18, new[] { 5, 25, 100 }, 1, "老兵"),
                ("hard", "clear", "迎難而上", "在困難以上難度完成 {0} 場任務", 2, new[] { 1, 10, 50 }, 0, ""),
                ("nightmare", "clear", "裂隙征服者", "在 {0} 個區域通關噩夢", 0, new[] { 1, 2, 3 }, 0, "裂隙征服者"),
                ("flawless", "clear", "不死之身", "零倒地完成 {0} 場任務", 1, new[] { 1, 5, 20 }, 1, "不死之身"),
                ("grades", "clear", "完美評價", "以 S 評分完成 {0} 場任務", 0, new[] { 1, 10, 30 }, 0, ""),
                ("revives", "support", "戰地天使", "救援隊友 {0} 次", 12, new[] { 5, 30, 100 }, 1, "戰地天使"),
                ("daily", "support", "勤務兵", "領取 {0} 項每日任務獎勵", 9, new[] { 10, 50, 200 }, 0, "勤務兵"),
                ("level", "support", "步步高升", "帳號等級達到 {0}", 7, new[] { 10, 25, 50 }, 0, "指揮官"),
                ("legends", "collect", "軍火大亨", "擁有 {0} 種傳奇以上武器", 2, new[] { 1, 2, 4 }, 2, "軍火大亨"),
                ("mythic", "collect", "神話收藏家", "持有 {0} 件神話武器或裝備", 2, new[] { 1, 3, 8 }, 1, ""),
                ("forged", "collect", "千錘百鍊", "將 {0} 件裝備強化到 +5", 1, new[] { 1, 3, 8 }, 0, ""),
                ("draws", "collect", "補給常客", "開啟補給 {0} 次", 64, new[] { 10, 100, 500 }, 1, ""),
            };
            DailyReward[] tierRewards = { new DailyReward { Gold = 300 }, new DailyReward { Gold = 600, Keys = 2 }, new DailyReward { Keys = 3, WeaponKeys = 2 } };
            foreach (var (id, cat, name, text, progress, goals, claimed, title) in specs)
                demo.Achievements.Add(new Achievement
                {
                    Id = id, Category = cat, Name = name, Text = string.Format(text, goals[Mathf.Min(claimed, 2)]), Title = title, Progress = progress, Goals = goals,
                    Claimed = claimed, Reached = goals.Count(g => progress >= g), Reward = claimed < 3 ? tierRewards[claimed] : default,
                });
            demo.TitleId = "headshots"; demo.Title = "神射手";
            UpdateProfile(demo);
            Screens.ShowCharacter();
            yield return Settle(0.4f);
            yield return Shot(folder, "unity-character");
            Screens.SetGearFilter("armor");
            yield return Settle(0.3f);
            yield return Shot(folder, "unity-character-armor");
            Screens.SetGearFilter("");
            Screens.ShowShop();
            yield return Settle(0.4f);
            yield return Shot(folder, "unity-shop");
            Screens.ShowDaily();
            yield return Settle(0.4f);
            yield return Shot(folder, "unity-daily");
            Screens.ShowAchievements();
            yield return Settle(0.4f);
            yield return Shot(folder, "unity-achievements");
            Screens.ShowTitlePickerForCapture();
            yield return Settle(0.3f);
            yield return Shot(folder, "unity-titles");
            // Social pages with sample friends, mail and chat.
            Dictionary<string, object> Person(int id, string name, int level, string title) =>
                new Dictionary<string, object> { ["id"] = (double)id, ["username"] = name, ["level"] = (double)level, ["title"] = title };
            FriendPresence.States[11] = (PresenceState.InMission, "");
            FriendPresence.States[12] = (PresenceState.InRoom, "K7QM");
            FriendPresence.States[13] = (PresenceState.Lobby, "");
            Screens.DemoFriends(new Dictionary<string, object>
            {
                ["friends"] = new List<object> { Person(11, "NightOwl", 23, "神射手"), Person(12, "Kairo_77", 15, ""), Person(13, "LunaFrost", 31, "戰地天使"), Person(14, "IronMoss", 8, ""), Person(15, "Rex_Tw", 12, "") },
                ["incoming"] = new List<object> { Person(21, "SkyRunner", 6, "") },
                ["outgoing"] = new List<object> { Person(22, "ZeroPoint", 19, "老兵") },
            });
            yield return Settle(0.4f);
            yield return Shot(folder, "unity-friends");
            double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Dictionary<string, object> Mail(int id, string title, string body, bool read, bool claimed, double age, Dictionary<string, object> reward) =>
                new Dictionary<string, object> { ["id"] = (double)id, ["title"] = title, ["body"] = body, ["read"] = read, ["claimed"] = claimed, ["created"] = now - age, ["expires"] = now - age + 30 * 86400, ["reward"] = reward };
            Screens.DemoMail(new List<Dictionary<string, object>>
            {
                Mail(3, "11/18 維護補償", "各位士兵：\n\n感謝大家耐心等候今天的伺服器維護。指揮部準備了補償，請在 30 天內領取。\n\n本次更新新增難度分級、每日任務、成就與社交功能，祝大家玩得愉快！", false, false, 3600,
                    new Dictionary<string, object> { ["gold"] = 1000.0, ["keys"] = 3.0, ["weapon_keys"] = 1.0 }),
                Mail(2, "週末雙倍活動預告", "本週六、日所有任務金幣加倍，記得揪好友一起出擊。", true, true, 86400 * 2, new Dictionary<string, object>()),
                Mail(1, "歡迎加入裂隙守衛", "士兵，歡迎報到！", true, true, 86400 * 6, new Dictionary<string, object> { ["keys"] = 3.0, ["weapon_keys"] = 1.0 }),
            });
            yield return Settle(0.4f);
            yield return Shot(folder, "unity-mail");
            ChatLog.Add(new ChatLog.Line { Channel = ChatChannel.World, AccountId = 11, Name = "NightOwl", Title = "神射手", Text = "有人要打熔核工廠噩夢嗎？缺一個" });
            ChatLog.Add(new ChatLog.Line { Channel = ChatChannel.World, AccountId = 13, Name = "LunaFrost", Title = "戰地天使", Text = "+1 我可以補，等我換狙擊槍" });
            ChatLog.Add(new ChatLog.Line { Channel = ChatChannel.System, Text = "Kairo_77 加入了房間" });
            ChatLog.Add(new ChatLog.Line { Channel = ChatChannel.Room, AccountId = 12, Name = "Kairo_77", Text = "第 10 波 Boss 記得先打護盾兵" });
            ChatLog.Add(new ChatLog.Line { Channel = ChatChannel.World, AccountId = 1, Name = "DVRBXTW", Title = "神射手", Text = "伺服器今晚 23:00 維護 30 分鐘", Admin = true });
            ChatLog.Add(new ChatLog.Line { Channel = ChatChannel.World, AccountId = 14, Name = "IronMoss", Text = "<size=80>測試</size> 標籤不會生效" });
            Screens.ShowChat();
            yield return Settle(0.4f);
            yield return Shot(folder, "unity-chat");
            Overlay.ShowInvite(11, "NightOwl", "K7QM");
            yield return Settle(0.3f);
            yield return Shot(folder, "unity-invite");
            Overlay.HideInvite();
            Screens.ShowAdmin();
            yield return Settle(0.3f);
            Screens.DemoAudit(new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { ["username"] = "Rex_Tw", ["created"] = now - 600, ["reason"] = "金幣 4200 超過可得上限 1790；作戰時間過短（31 秒到達第 20 波）", ["rejected"] = 1.0 },
                new Dictionary<string, object> { ["username"] = "IronMoss", ["created"] = now - 7200, ["reason"] = "瞄準方向與視角不符 52 次；命中率異常（398/402）", ["rejected"] = 0.0 },
            });
            yield return Settle(0.3f);
            yield return Shot(folder, "unity-admin-console");
            GameClient.TestAimbot = true;
            Screens.ShowAbilitiesForCapture();
            yield return Settle(0.3f);
            yield return Shot(folder, "unity-abilities");
            GameClient.TestAimbot = false;
            SelectRegion(0);
            SelectDifficulty(1);
            yield return Settle(0.6f);
            yield return Shot(folder, "unity-home-difficulty");
            SelectDifficulty(0);
            Screens.ShowArmory();
            yield return Settle(0.5f);
            yield return Shot(folder, "unity-armory-rarity");
            Hints.Maybe(Screens.Root, "armory", true);
            yield return Settle(0.3f);
            yield return Shot(folder, "unity-hint");
            Screens.ShowArmory();
            yield return Settle(0.3f);
            Screens.BulkSalvage("weapon");
            yield return Settle(0.3f);
            yield return Shot(folder, "unity-salvage-weapon");
            Screens.ShowCharacter();
            Screens.BulkSalvage("gear");
            yield return Settle(0.4f);
            yield return Shot(folder, "unity-salvage-gear");
            Screens.ShowSettings();
            yield return Settle(0.3f);
            yield return Shot(folder, "unity-settings");
            foreach (var sr in Screens.Root.GetComponentsInChildren<UnityEngine.UI.ScrollRect>()) sr.verticalNormalizedPosition = 0f;
            yield return Settle(0.2f);
            yield return Shot(folder, "unity-settings-keys");
            Screens.PlayChestDemo(new List<GearItem>(demo.Inventory), 4);
            yield return Settle(1.0f);
            yield return Shot(folder, "unity-chest-charge");
            yield return Settle(2.2f);
            yield return Shot(folder, "unity-chest-open");
            // Weapon draw reveal: every card shows its turning weapon model (傳奇 / 神話 finishes included).
            Screens.ShowCharacter();
            var weaponDraw = new List<GearItem>();
            var drawn = new[] { ("rifle", 4), ("sniper", 3), ("shotgun", 3), ("pistol", 2), ("rifle", 1), ("shotgun", 0), ("pistol", 4), ("sniper", 1), ("rifle", 2), ("pistol", 0) };
            foreach (var (w, r) in drawn)
                weaponDraw.Add(new GearItem { Id = w, Slot = "weapon", Rarity = r, Title = Gear.WeaponName(w, r), Detail = "已存入武器庫" });
            Screens.PlayChestDemo(weaponDraw, 4);
            yield return Settle(4.5f);
            yield return Shot(folder, "unity-chest-weapons");
            Screens.ShowStory(0);
            yield return Settle(0.3f);
            yield return Shot(folder, "unity-story");
            Screens.Clear();

            var kit = new Loadout { Owned = new List<string> { "pistol", "rifle", "sniper" }, Equipped = new List<string> { "rifle", "sniper" } };
            kit.Rarity["rifle"] = 4; kit.Rarity["sniper"] = 3;
            Client.WeaponRarity = id => kit.RarityOf(id);
            Client.StartOffline(0, kit, false, null, 1);
            var sim = Client.Sim;
            sim.EnemyVariety = true;
            sim.ApplyBuff(Client.LocalId, Defs.BuffDrone + 200);
            sim.ApplyBuff(Client.LocalId, Defs.BuffPlasma);
            sim.ApplyBuff(Client.LocalId, Defs.BuffOrbit + 200);
            sim.ApplyBuff(Client.LocalId, Defs.BuffMissile + 200);
            sim.ApplyBuff(Client.LocalId, Defs.BuffLightning + 200);
            sim.ApplyBuff(Client.LocalId, Defs.BuffMine + 200);
            sim.Wave = 5;
            sim.SpawnWave();
            var me = sim.Actors[Client.LocalId];
            int i = 0;
            foreach (var e in sim.Enemies.Values)
            {
                e.Position = me.Position + new Vector3(-7 + (i++ % 6) * 2.8f, 0, 9 + (i % 3) * 3f);
                e.MeleeCooldown = 99f; e.ShotCooldown = 99f; e.Speed = 0;
            }
            sim.SpawnPickup(me.Position + new Vector3(1.5f, 0, 4), PickupKind.Health);
            me.Hp = 64;
            yield return Settle(0.6f);
            sim.Fire(Client.LocalId, sim.Enemies.Values.First().Position + Vector3.up * Defs.HeadHeight);
            yield return Settle(0.05f);
            yield return Shot(folder, "unity-combat");
            Chat.Show(true);
            Chat.OpenForCapture();
            for (int f = 0; f < 10; f++) { Chat.Update(true, false); yield return null; }
            yield return Shot(folder, "unity-chat-hud");
            Chat.Show(false);
            // Test aimbot: the view turns from straight ahead onto the nearest enemy's head.
            Client.SetView(0.6f, 0.1f);
            for (int f = 0; f < 30; f++) { Client.AimbotStepForCapture(1f / 60f); yield return null; }
            yield return Shot(folder, "unity-aimbot");
            Client.SetView(0, 0);
            Client.Hud.ResetTransient();
            yield return Settle(0.4f);
            yield return Shot(folder, "unity-rifle");
            me.Reload = me.ReloadTime * 0.55f;
            yield return Settle(0.02f);
            yield return Shot(folder, "unity-reload");
            me.Reload = 0;
            Client.SelectSlot(1);
            yield return Settle(0.4f);
            yield return Shot(folder, "unity-sniper");
            Client.Menus.OpenPause();
            yield return Settle(0.2f);
            yield return Shot(folder, "unity-pause");
            Client.Menus.HideAll();
            Client.Menus.ShowResult(true, 0, 86, 742, 740, 1980, "A", new[] { 41, 13, 290, 176 }, new Menus.ResultProgress { Xp = 301, Level = 3, LevelXp = 180, Wave = 20, BestWave = 14, Unlocked = 2 }, 1);
            yield return Settle(0.2f);
            yield return Shot(folder, "unity-result");
            Client.Menus.HideAll();
            Client.SetThirdPerson(true);
            yield return Settle(0.3f);
            yield return Shot(folder, "unity-third-0");
            for (int k = 0; k < 4; k++)
            {
                foreach (var e in sim.Enemies.Values) { e.Speed = 2f; }
                sim.Explosions.Add(new ExplosionEvent { Position = me.Position + new Vector3(2.5f - k * 1.5f, 1f, 6f + k), Radius = 3f, Color = new Color32(0xff, 0xb3, 0x47, 255) });
                yield return Settle(0.12f);
                yield return Shot(folder, "unity-fx-" + k);
            }
            // Animation check: soldiers and every trooper type walking past a fixed camera.
            {
                var cast = new List<GameObject>();
                var walker = Models.Soldier(Palette.Player);
                walker.GetComponent<CharacterRig>().SetWeapon("rifle", 4);
                cast.Add(walker);
                var reloader = Models.Soldier(Palette.Teammate);
                reloader.GetComponent<CharacterRig>().SetWeapon("shotgun", 3);
                reloader.GetComponent<CharacterRig>().Reload = 0.45f;
                cast.Add(reloader);
                foreach (int kind in new[] { Defs.KindGrunt, Defs.KindCharger, Defs.KindShield, Defs.KindHeavyBoss }) cast.Add(Models.Enemy(kind));
                for (int ci = 0; ci < cast.Count; ci++)
                {
                    cast[ci].transform.position = new Vector3(-6.5f + ci * 2.3f + (ci == 5 ? 1.2f : 0), 0, 16f);
                    cast[ci].transform.rotation = Quaternion.Euler(0, 180, 0);
                }
                Client.enabled = false;   // stop the game camera from overriding ours
                for (int f = 0; f < 50; f++)
                {
                    for (int ci = 0; ci < cast.Count; ci++) if (ci != 1) cast[ci].transform.position += Vector3.back * (ci == 5 ? 1.6f : 3.4f) * Time.deltaTime;
                    Client.Camera.transform.position = new Vector3(-0.5f, 5.2f, cast[0].transform.position.z - 8.5f);
                    Client.Camera.transform.LookAt(new Vector3(-0.5f, 0.9f, cast[0].transform.position.z));
                    yield return null;
                }
                yield return Shot(folder, "unity-rig");
                foreach (var c in cast) Destroy(c);
                Client.enabled = true;
            }
            for (int region = 1; region <= 2; region++)
            {
                Client.World.ApplyRegion(region);
                yield return Settle(0.3f);
                yield return Shot(folder, "unity-third-" + region);
            }
            Application.Quit();
        }

        IEnumerator Settle(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        IEnumerator Shot(string folder, string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"));
            yield return null;
            yield return null;
            Debug.Log("CAPTURED " + name);
        }
    }
}
