using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using RiftWard.Sim;
using UnityEngine;

namespace RiftWard.Client
{
    /// Headless authoritative game server (run with -batchmode -nographics -server).
    public sealed class GameServer : MonoBehaviour
    {
        sealed class Player
        {
            public Connection Conn;
            public Profile Profile;
            public bool Pending;
            public int[] OfferSent;
            public float LastInput, LastPing = -9f;
            public AdminSettings Admin = new AdminSettings();
            public Room Room;
            /// Region and difficulty picked while playing alone (no room).
            public int SoloRegion, SoloDifficulty;
            /// Account ids of this player's friends (from the account service).
            public List<int> Friends = new List<int>();
            /// Send times of recent chat messages, for the rate limit.
            public readonly Queue<float> ChatTimes = new Queue<float>();
            public int AccountId => Profile?.Id ?? 0;
            public string ChatName => Profile == null ? "" : Profile.Username;
        }

        /// A party with its own leader, mission and snapshots. Players outside a room are alone.
        sealed class Room
        {
            public string Code;
            public readonly List<int> Members = new List<int>();
            public bool Private;
            public GameSim Sim;
            public int Selected, Difficulty, Leader = -1;
            public string Mode = "lobby";
            public float SnapshotTimer;
            public bool Finishing;
            public int FastWave = -1;
            /// Account ids invited by a member: they may join even when the room is private.
            public readonly HashSet<int> Invited = new HashSet<int>();
            /// Set by the pause menu; the mission freezes only while one player is in the room.
            public bool Paused;
            /// Snapshots sent this mission; every 20th (once a second) carries names and weapon kits, as does the
            /// next one after someone joins.
            public int SnapshotCount;
            public bool ForceFull = true;
        }

        const int MaxPlayers = 64, RoomSize = 4;
        const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        readonly Dictionary<string, Room> rooms = new Dictionary<string, Room>();
        readonly System.Random rng = new System.Random();
        TcpListener listener;
        readonly ConcurrentQueue<TcpClient> accepted = new ConcurrentQueue<TcpClient>();
        readonly Dictionary<int, Player> players = new Dictionary<int, Player>();
        int nextId = 1;
        AccountApi api;
        bool testFast;
        float clock, presenceTimer;
        string dataDir = ".";

        // Chat: recent world messages replayed to newcomers, temporary mutes and a word filter.
        const int ChatHistory = 40, ChatBurst = 5;
        const float ChatWindow = 10f;
        readonly List<byte[]> chatHistory = new List<byte[]>();
        readonly Dictionary<int, float> mutedUntil = new Dictionary<int, float>();
        string[] chatFilter = new string[0];

        static string Env(string name, string fallback) { var v = Environment.GetEnvironmentVariable(name); return string.IsNullOrEmpty(v) ? fallback : v; }

        void Start()
        {
            Application.targetFrameRate = 60;
            Time.fixedDeltaTime = 1f / 60f;
            api = new AccountApi(this)
            {
                Host = Env("RIFTWARD_API_HOST", "192.168.213.100"),
                Port = int.Parse(Env("RIFTWARD_API_PORT", "24771")),
            };
            dataDir = Env("RIFTWARD_SERVER_DATA", Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "server-data"));
            LoadChatFilter();
            string keyPath = Path.Combine(dataDir, "server.key");
            api.ServerKey = File.Exists(keyPath) ? File.ReadAllText(keyPath).Trim() : "";
            if (api.ServerKey == "")
            {
                Debug.LogError("Missing server.key — start the account service first (StartServer.cmd).");
                Application.Quit(1);
                return;
            }
            testFast = Env("RIFTWARD_TEST_FAST", "") == "1";
            int port = int.Parse(Env("RIFTWARD_GAME_PORT", Protocol.DefaultPort.ToString()));
            var bind = IPAddress.Parse(Env("RIFTWARD_BIND", "0.0.0.0"));
            try
            {
                listener = new TcpListener(bind, port);
                listener.Start();
            }
            catch (Exception e)
            {
                Debug.LogError($"Cannot listen on TCP {bind}:{port} ({e.Message}). Is another server running?");
                Application.Quit(1);
                return;
            }
            new Thread(AcceptLoop) { IsBackground = true, Name = "rw-accept" }.Start();
            Console.WriteLine($"SERVER_READY address={bind} port={port}");
            Debug.Log($"SERVER_READY address={bind} port={port}");
        }

        void AcceptLoop()
        {
            while (true)
            {
                try { accepted.Enqueue(listener.AcceptTcpClient()); }
                catch { return; }
            }
        }

        void OnApplicationQuit() { try { listener?.Stop(); } catch { } }

        // ------------------------------------------------------------ main loop

        void FixedUpdate()
        {
            clock += Time.fixedDeltaTime;
            while (accepted.TryDequeue(out var tcp))
            {
                if (players.Count >= MaxPlayers) { tcp.Close(); continue; }
                int id = nextId++;
                players[id] = new Player { Conn = new Connection(id, tcp) };
                Debug.Log("PEER_CONNECTED " + id);
                StartCoroutine(AuthTimeout(id));
            }
            foreach (var p in players.Values.ToList())
            {
                while (p.Conn.Inbox.TryDequeue(out var msg)) Handle(p, msg.type, msg.payload);
                if (p.Conn.Closed) Drop(p.Conn.Id);
            }
            foreach (var room in rooms.Values.ToList()) TickRoom(room);
            presenceTimer += Time.fixedDeltaTime;
            if (presenceTimer >= 2f)
            {
                presenceTimer = 0;
                foreach (var p in players.Values) if (p.Profile != null && p.Friends.Count > 0) SendPresence(p);
            }
        }

        IEnumerable<KeyValuePair<int, Player>> MembersOf(Room room) =>
            room.Members.Where(players.ContainsKey).Select(id => new KeyValuePair<int, Player>(id, players[id])).ToList();

        void TickRoom(Room room)
        {
            var sim = room.Sim;
            if (sim == null || room.Mode != "playing") return;
            if (room.Paused && room.Members.Count == 1) return;   // solo pause: nothing moves, nothing is sent
            foreach (var kv in MembersOf(room))
                if (sim.Actors.TryGetValue(kv.Key, out var actor) && clock - kv.Value.LastInput > 0.25f)
                    sim.Inputs[kv.Key] = new PlayerInput { Seq = sim.Inputs.TryGetValue(kv.Key, out var old) ? old.Seq : 0, Yaw = actor.Yaw, Slot = actor.Slot };
            if (testFast && sim.Wave != room.FastWave && sim.Enemies.Count > 0 && !sim.BuffPending)
            {
                room.FastWave = sim.Wave;
                sim.Enemies.Clear();
                sim.GainXp(sim.XpNeed);
            }
            sim.Tick(Time.fixedDeltaTime);
            if (sim.Shots.Count + sim.Explosions.Count + sim.Announcements.Count > 0)
            {
                var events = Protocol.Frame(Msg.Events, w => Protocol.WriteEvents(w, sim));
                Broadcast(room, events);
                sim.Shots.Clear(); sim.Explosions.Clear(); sim.Announcements.Clear();
            }
            foreach (var kv in MembersOf(room))
            {
                if (!sim.BuffPending || !sim.BuffOffers.TryGetValue(kv.Key, out var offer) || kv.Value.OfferSent == offer) continue;
                kv.Value.OfferSent = offer;
                kv.Value.Conn.Send(Protocol.Frame(Msg.BuffOffer, w => { w.Write(sim.Level); w.Write(offer.Length); foreach (var c in offer) w.Write(c); }));
            }
            room.SnapshotTimer += Time.fixedDeltaTime;
            if (room.SnapshotTimer >= 0.05f)
            {
                room.SnapshotTimer = 0;
                bool full = room.ForceFull || room.SnapshotCount % 20 == 0;
                room.ForceFull = false;
                room.SnapshotCount++;
                Broadcast(room, Protocol.Frame(Msg.Snapshot, w => Protocol.WriteSnapshot(w, sim, full)));
            }
            if ((sim.State == MissionState.Victory || sim.State == MissionState.Defeat) && !room.Finishing) StartCoroutine(Finish(room));
        }

        System.Collections.IEnumerator AuthTimeout(int id)
        {
            yield return new WaitForSecondsRealtime(12f);
            if (players.TryGetValue(id, out var p) && p.Profile == null) { p.Conn.Close("auth timeout"); Drop(id); }
        }

        void Broadcast(Room room, byte[] frame)
        {
            foreach (var kv in MembersOf(room)) if (kv.Value.Profile != null) kv.Value.Conn.Send(frame);
        }

        void Drop(int id)
        {
            if (!players.TryGetValue(id, out var p)) return;
            LeaveRoom(p, false);
            players.Remove(id);
            Debug.Log("PEER_LEFT " + id);
        }

        // ------------------------------------------------------------ rooms

        Room CreateRoom(Player p, bool isPrivate)
        {
            LeaveRoom(p, false);
            string code;
            do code = new string(Enumerable.Range(0, 4).Select(_ => CodeChars[rng.Next(CodeChars.Length)]).ToArray());
            while (rooms.ContainsKey(code));
            var room = new Room { Code = code, Private = isPrivate, Leader = p.Conn.Id };
            rooms[code] = room;
            room.Members.Add(p.Conn.Id);
            p.Room = room;
            Debug.Log($"ROOM_CREATED {code} by={p.Conn.Id} private={isPrivate}");
            SendLobby(room);
            return room;
        }

        void JoinRoom(Player p, string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (testFast && code == "TEST" && !rooms.ContainsKey(code))
            {
                LeaveRoom(p, false);
                var test = rooms[code] = new Room { Code = code, Leader = p.Conn.Id };
                test.Members.Add(p.Conn.Id);
                p.Room = test;
                SendLobby(test);
                return;
            }
            if (!rooms.TryGetValue(code, out var room) || room.Private && !room.Invited.Contains(p.AccountId)) { Notice(p, "找不到房號 " + code); return; }
            if (room == p.Room) return;
            if (room.Members.Count >= RoomSize) { Notice(p, "房間已滿（最多 4 人）"); return; }
            if (room.Mode == "result") { Notice(p, "房間正在結算，請稍後再試"); return; }
            LeaveRoom(p, false);
            room.Members.Add(p.Conn.Id);
            p.Room = room;
            Debug.Log($"ROOM_JOINED {code} id={p.Conn.Id}");
            room.Invited.Remove(p.AccountId);
            room.Paused = false;   // a party never stays paused
            SendLobby(room);
            RoomNotice(room, $"{p.ChatName} 加入了房間");
            if (room.Mode == "playing") JoinMission(room, p.Conn.Id);
        }

        void LeaveRoom(Player p, bool notify)
        {
            var room = p.Room;
            if (room == null) return;
            int id = p.Conn.Id;
            room.Members.Remove(id);
            p.Room = null;
            if (room.Sim != null && room.Sim.Actors.ContainsKey(id))
            {
                room.Sim.RemoveActor(id);
                if (notify) p.Conn.Send(Protocol.Frame(Msg.BackHome));
            }
            if (room.Members.Count == 0) { rooms.Remove(room.Code); Debug.Log("ROOM_CLOSED " + room.Code); }
            else
            {
                RoomNotice(room, $"{p.ChatName} 離開了房間");
                if (room.Leader == id) room.Leader = room.Members[0];
                if (room.Mode == "playing" && room.Sim != null && room.Sim.Actors.Count == 0) EndToLobby(room);
                else SendLobby(room);
            }
            if (notify) SendSoloLobby(p);
        }

        void Notice(Player p, string text) => p.Conn.Send(Protocol.Frame(Msg.Notice, w => w.Write(text)));

        void SendLobby(Room room)
        {
            int unlocked = players.TryGetValue(room.Leader, out var l) && l.Profile != null ? l.Profile.Unlocked : 0;
            int count = MembersOf(room).Count(kv => kv.Value.Profile != null);
            string code = room.Private ? "" : room.Code;
            Broadcast(room, Protocol.Frame(Msg.Lobby, w => { w.Write(room.Selected); w.Write(unlocked); w.Write(room.Leader); w.Write(count); w.Write(room.Mode); w.Write(code); w.Write(room.Difficulty); }));
        }

        /// A player outside any room is the leader of a party of one.
        void SendSoloLobby(Player p)
        {
            p.Conn.Send(Protocol.Frame(Msg.Lobby, w => { w.Write(p.SoloRegion); w.Write(p.Profile.Unlocked); w.Write(p.Conn.Id); w.Write(1); w.Write("lobby"); w.Write(""); w.Write(p.SoloDifficulty); }));
        }

        /// The requested tier, lowered until it is open for this player in the region.
        static int OpenTier(Profile profile, int region, int tier)
        {
            tier = Mathf.Clamp(tier, 0, Defs.Difficulties.Length - 1);
            while (tier > 0 && !profile.DifficultyOpen(region, tier)) tier--;
            return tier;
        }

        // ------------------------------------------------------------ messages

        void Handle(Player p, Msg type, byte[] payload)
        {
            using var r = Connection.Reader(payload);
            try
            {
                if (p.Profile == null)
                {
                    if (type == Msg.Hello && !p.Pending) Authenticate(p, r.ReadString(), r.ReadString());
                    return;
                }
                int id = p.Conn.Id;
                var room = p.Room;
                var sim = room?.Sim;
                string mode = room?.Mode ?? "lobby";
                switch (type)
                {
                    case Msg.CreateRoom:
                        if (mode != "playing") CreateRoom(p, false);
                        break;
                    case Msg.JoinRoom:
                        string code = r.ReadString();
                        if (mode != "playing") JoinRoom(p, code);
                        break;
                    case Msg.LeaveRoom:
                        LeaveRoom(p, true);
                        break;
                    case Msg.Input:
                        var input = r.ReadInput();
                        if (sim != null && mode == "playing" && sim.Actors.ContainsKey(id) && (!sim.Inputs.TryGetValue(id, out var prev) || input.Seq >= prev.Seq))
                        {
                            sim.Inputs[id] = input;
                            p.LastInput = clock;
                        }
                        break;
                    case Msg.ChooseBuff:
                        sim?.ChooseBuff(id, r.ReadInt32());
                        break;
                    case Msg.Admin:
                        var settings = r.ReadAdmin();
                        if (!p.Profile.Admin) break;
                        p.Admin = settings;
                        sim?.SetAdmin(id, settings);
                        break;
                    case Msg.SelectRegion:
                        int region = r.ReadInt32(), tier = r.ReadInt32();
                        if (room == null)
                        {
                            p.SoloRegion = Mathf.Clamp(region, 0, p.Profile.Unlocked);
                            p.SoloDifficulty = OpenTier(p.Profile, p.SoloRegion, tier);
                            SendSoloLobby(p);
                        }
                        else if (id == room.Leader && mode != "playing")
                        {
                            room.Selected = Mathf.Clamp(region, 0, p.Profile.Unlocked);
                            room.Difficulty = OpenTier(p.Profile, room.Selected, tier);
                            SendLobby(room);
                        }
                        break;
                    case Msg.RequestDeploy:
                        int target = r.ReadInt32(), targetTier = r.ReadInt32();
                        if (room == null) room = CreateRoom(p, true);
                        if (id == room.Leader && room.Mode == "lobby") StartMission(room, target, targetTier);
                        break;
                    case Msg.LeaveMission:
                        if (mode == "playing" && sim != null && sim.Actors.ContainsKey(id))
                        {
                            sim.RemoveActor(id);
                            p.Conn.Send(Protocol.Frame(Msg.BackHome));
                            if (sim.Actors.Count == 0) EndToLobby(room);
                        }
                        break;
                    case Msg.RequestHome:
                        if (mode == "result") EndToLobby(room);
                        break;
                    case Msg.RefreshProfile:
                        RefreshProfile(p);
                        break;
                    case Msg.Chat:
                        byte channel = r.ReadByte();
                        HandleChat(p, channel, r.ReadString());
                        break;
                    case Msg.SetPause:
                        bool pauseOn = r.ReadBoolean();
                        if (room != null) room.Paused = pauseOn && room.Members.Count == 1;
                        break;
                    case Msg.RefreshFriends:
                        FetchFriends(p, true);
                        break;
                    case Msg.Ping:
                        var pingAt = r.ReadVector3();
                        byte pingKind = r.ReadByte();
                        if (mode != "playing" || sim == null || pingKind > 4 || clock - p.LastPing < 0.35f) break;
                        if (float.IsNaN(pingAt.x + pingAt.y + pingAt.z) || pingAt.sqrMagnitude > 1e6f) break;
                        p.LastPing = clock;
                        Broadcast(room, Protocol.Frame(Msg.PingMark, w => { w.Write(id); w.Write(p.ChatName); w.Write(pingAt); w.Write(pingKind); }));
                        break;
                    case Msg.InviteFriend:
                        int invitee = r.ReadInt32();
                        if (mode != "result") Invite(p, invitee);
                        break;
                    case Msg.DeclineInvite:
                        int inviter = r.ReadInt32();
                        var host = Online(inviter);
                        if (host != null && host.Friends.Contains(p.AccountId))
                        {
                            Notice(host, $"{p.ChatName} 婉拒了你的邀請");
                            SystemLine(host, $"{p.ChatName} 婉拒了你的組隊邀請");
                        }
                        break;
                }
            }
            catch (Exception e) { Debug.LogWarning($"Bad message from {p.Conn.Id}: {e.Message}"); p.Conn.Close("protocol error"); }
        }

        void Authenticate(Player p, string token, string version)
        {
            if (token.Length > 128) { p.Conn.Close("token"); return; }
            p.Pending = true;
            int id = p.Conn.Id;
            api.Post("/api/server/validate", new Dictionary<string, object> { ["token"] = token, ["version"] = version }, result =>
            {
                p.Pending = false;
                if (!players.ContainsKey(id)) return;
                if (!result.Bool("ok")) { Reject(p, result.Str("error", "登入已失效")); return; }
                var profile = Profile.From(result.Obj("profile"));
                if (!profile.TutorialComplete) { Reject(p, "請先完成新手教學"); return; }
                if (players.Values.Any(o => o != p && o.Profile != null && o.Profile.Id == profile.Id)) { Reject(p, "這個帳號已在其他視窗登入遊戲"); return; }
                p.Profile = profile;
                p.Conn.Send(Protocol.Frame(Msg.Welcome, w => w.Write(id)));
                Debug.Log("ACCOUNT_PEER_VERIFIED id=" + id);
                SendSoloLobby(p);
                foreach (var line in chatHistory) p.Conn.Send(line);
                FetchFriends(p);
            });
        }

        void Reject(Player p, string message)
        {
            p.Conn.Send(Protocol.Frame(Msg.Error, w => w.Write(message)));
            StartCoroutine(CloseSoon(p.Conn));
        }

        System.Collections.IEnumerator CloseSoon(Connection c)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            c.Close("rejected");
        }

        void RefreshProfile(Player p)
        {
            api.Post("/api/server/profile", new Dictionary<string, object> { ["account_id"] = p.Profile.Id }, result =>
            {
                if (!result.Bool("ok") || !players.ContainsValue(p)) return;
                p.Profile = Profile.From(result.Obj("profile"));
            });
        }

        // ------------------------------------------------------------ social

        Player Online(int accountId) => players.Values.FirstOrDefault(o => o.Profile != null && o.AccountId == accountId);

        /// <param name="cascade">A friendship changed: also refetch every online player on either side of it,
        /// so the other account sees the change (and this player's presence) without logging in again.</param>
        void FetchFriends(Player p, bool cascade = false)
        {
            api.Post("/api/server/friends", new Dictionary<string, object> { ["account_id"] = p.AccountId }, result =>
            {
                if (!result.Bool("ok") || !players.ContainsValue(p)) return;
                var before = p.Friends;
                p.Friends = (result.Arr("friends") ?? new List<object>()).Select(o => o is double d ? (int)d : 0).Where(i => i > 0).ToList();
                SendPresence(p);
                if (!cascade) return;
                foreach (int fid in before.Union(p.Friends).Distinct())
                {
                    var other = Online(fid);
                    if (other != null && other != p) FetchFriends(other);
                }
            });
        }

        void SendPresence(Player p)
        {
            var friends = p.Friends.ToList();
            p.Conn.Send(Protocol.Frame(Msg.Presence, w =>
            {
                w.Write(friends.Count);
                foreach (int fid in friends)
                {
                    var f = Online(fid);
                    byte state = f == null ? PresenceState.Offline : f.Room == null ? PresenceState.Lobby : f.Room.Mode == "playing" ? PresenceState.InMission : PresenceState.InRoom;
                    w.Write(fid); w.Write(state);
                    // Only rooms anyone can join show their code; private ones are reached by invitation.
                    w.Write(f?.Room != null && !f.Room.Private ? f.Room.Code : "");
                }
            }));
        }

        void Invite(Player p, int accountId)
        {
            if (!p.Friends.Contains(accountId)) { Notice(p, "只能邀請好友"); return; }
            var friend = Online(accountId);
            if (friend == null) { Notice(p, "好友目前不在線上"); return; }
            if (friend.Room != null && friend.Room == p.Room) { Notice(p, $"{friend.ChatName} 已經在你的房間"); return; }
            var room = p.Room ?? CreateRoom(p, false);
            if (room.Members.Count >= RoomSize) { Notice(p, "房間已滿（最多 4 人）"); return; }
            room.Invited.Add(accountId);
            friend.Conn.Send(Protocol.Frame(Msg.Invite, w => { w.Write(p.AccountId); w.Write(p.ChatName); w.Write(room.Code); }));
            Notice(p, $"已邀請 {friend.ChatName}");
            Debug.Log($"INVITE from={p.AccountId} to={accountId} room={room.Code}");
        }

        void LoadChatFilter()
        {
            try
            {
                string path = Path.Combine(dataDir, "chat-filter.txt");
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(dataDir);
                    File.WriteAllText(path, "# 聊天過濾字詞，每行一個（不分大小寫），出現時會被替換成 ＊\n");
                }
                chatFilter = File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToArray();
            }
            catch (Exception e) { Debug.LogWarning("chat filter: " + e.Message); }
        }

        /// Trims, removes control characters and masks filtered words.
        string CleanChat(string text)
        {
            string clean = new string((text ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (clean.Length > ChatChannel.MaxLength) clean = clean.Substring(0, ChatChannel.MaxLength);
            foreach (var word in chatFilter)
            {
                int at;
                while ((at = clean.IndexOf(word, StringComparison.OrdinalIgnoreCase)) >= 0)
                    clean = clean.Substring(0, at) + new string('＊', word.Length) + clean.Substring(at + word.Length);
            }
            return clean;
        }

        void HandleChat(Player p, byte channel, string text)
        {
            text = CleanChat(text);
            if (text.Length == 0) return;
            if (p.Profile.Admin && text.StartsWith("/") && AdminCommand(p, text)) return;
            if (mutedUntil.TryGetValue(p.AccountId, out var until) && clock < until)
            {
                SystemLine(p, $"你已被禁言，{Mathf.CeilToInt((until - clock) / 60f)} 分鐘後解除");
                return;
            }
            while (p.ChatTimes.Count > 0 && clock - p.ChatTimes.Peek() > ChatWindow) p.ChatTimes.Dequeue();
            if (p.ChatTimes.Count >= ChatBurst) { SystemLine(p, "發言太頻繁，請稍候再試"); return; }
            p.ChatTimes.Enqueue(clock);
            string title = p.Profile.Title ?? "";
            if (channel == ChatChannel.Room)
            {
                if (p.Room == null) { SystemLine(p, "你不在房間中，訊息未送出（可改用全頻）"); return; }
                Broadcast(p.Room, Protocol.Frame(Msg.ChatLine, w => WriteLine(w, ChatChannel.Room, p.AccountId, p.ChatName, title, text, p.Profile.Admin)));
                return;
            }
            var frame = Protocol.Frame(Msg.ChatLine, w => WriteLine(w, ChatChannel.World, p.AccountId, p.ChatName, title, text, p.Profile.Admin));
            chatHistory.Add(frame);
            if (chatHistory.Count > ChatHistory) chatHistory.RemoveAt(0);
            foreach (var o in players.Values) if (o.Profile != null) o.Conn.Send(frame);
        }

        /// Admin chat commands: /mute 帳號 [分鐘], /unmute 帳號. Returns false for anything else (sent as a message).
        bool AdminCommand(Player p, string text)
        {
            var parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string cmd = parts[0].ToLowerInvariant();
            if (cmd != "/mute" && cmd != "/unmute") return false;
            var target = parts.Length > 1 ? players.Values.FirstOrDefault(o => o.Profile != null && string.Equals(o.ChatName, parts[1], StringComparison.OrdinalIgnoreCase)) : null;
            if (target == null) { SystemLine(p, "找不到線上玩家：" + (parts.Length > 1 ? parts[1] : "")); return true; }
            if (cmd == "/unmute") { mutedUntil.Remove(target.AccountId); SystemLine(p, $"已解除 {target.ChatName} 的禁言"); SystemLine(target, "你的禁言已解除"); return true; }
            int minutes = parts.Length > 2 && int.TryParse(parts[2], out var m) ? Mathf.Clamp(m, 1, 1440) : 10;
            mutedUntil[target.AccountId] = clock + minutes * 60f;
            SystemLine(p, $"已禁言 {target.ChatName} {minutes} 分鐘");
            SystemLine(target, $"你已被管理員禁言 {minutes} 分鐘");
            Debug.Log($"CHAT_MUTE by={p.AccountId} target={target.AccountId} minutes={minutes}");
            return true;
        }

        static void WriteLine(BinaryWriter w, byte channel, int accountId, string name, string title, string text, bool admin)
        {
            w.Write(channel); w.Write(accountId); w.Write(name ?? ""); w.Write(title ?? ""); w.Write(text ?? ""); w.Write(admin);
        }

        void SystemLine(Player p, string text) => p.Conn.Send(Protocol.Frame(Msg.ChatLine, w => WriteLine(w, ChatChannel.System, 0, "", "", text, false)));

        void RoomNotice(Room room, string text)
        {
            if (room.Private && room.Members.Count <= 1) return;
            Broadcast(room, Protocol.Frame(Msg.ChatLine, w => WriteLine(w, ChatChannel.System, 0, "", "", text, false)));
        }

        // ------------------------------------------------------------ missions

        void StartMission(Room room, int region, int difficulty)
        {
            var ready = MembersOf(room).Where(kv => kv.Value.Profile != null).ToList();
            if (ready.Count == 0) return;
            if (ready.Any(kv => region > kv.Value.Profile.Unlocked))
            {
                Notice(players[room.Leader], "隊友尚未解鎖此區域，請先完成前一區域");
                return;
            }
            difficulty = Mathf.Clamp(difficulty, 0, Defs.Difficulties.Length - 1);
            if (ready.Any(kv => !kv.Value.Profile.DifficultyOpen(region, difficulty)))
            {
                Notice(players[room.Leader], ready.Count > 1 ? $"有隊員尚未解鎖{Defs.Difficulty(difficulty).Name}難度，請先在此區域通關前一難度" : $"{Defs.Difficulty(difficulty).Name}難度尚未解鎖");
                return;
            }
            room.Selected = region;
            room.Difficulty = difficulty;
            var sim = room.Sim = new GameSim();
            foreach (var kv in ready) AddToSim(sim, kv.Key, kv.Value);
            sim.StartMission(region, difficulty);
            foreach (var kv in ready) sim.SetAdmin(kv.Key, kv.Value.Admin);
            room.Mode = "playing";
            room.Finishing = false;
            room.ForceFull = true;
            room.SnapshotCount = 0;
            room.FastWave = -1;
            Broadcast(room, Protocol.Frame(Msg.Deploy, w => { w.Write(region); w.Write(difficulty); }));
            SendLobby(room);
            Debug.Log($"MISSION_START room={room.Code} region={region} difficulty={difficulty} players={ready.Count}");
        }

        void AddToSim(GameSim sim, int id, Player p)
        {
            var lo = new Loadout { Owned = new List<string>(p.Profile.Loadout.Owned), Equipped = new List<string>(p.Profile.Loadout.Equipped) };
            foreach (var kv in p.Profile.Loadout.Levels) lo.Levels[kv.Key] = new Dictionary<string, int>(kv.Value);
            lo.Rarity = new Dictionary<string, int>(p.Profile.Loadout.Rarity);
            sim.AddActor(id, lo, p.Profile.Admin).Name = string.IsNullOrEmpty(p.Profile.Title) ? p.Profile.Username : $"〔{p.Profile.Title}〕{p.Profile.Username}";
            var b = p.Profile;
            sim.ApplyProfileBonus(id, b.BonusOf("hp"), b.BonusOf("damage"), b.BonusOf("speed"), b.BonusOf("reload"), b.BonusOf("crit"));
            p.OfferSent = null;
            p.LastInput = clock;
        }

        void JoinMission(Room room, int id)
        {
            var sim = room.Sim;
            AddToSim(sim, id, players[id]);
            sim.SetAdmin(id, players[id].Admin);
            room.ForceFull = true;   // the newcomer needs everyone's names and weapons now
            players[id].Conn.Send(Protocol.Frame(Msg.Deploy, w => { w.Write(sim.Region); w.Write(sim.Difficulty); }));
        }

        void EndToLobby(Room room)
        {
            room.Mode = "lobby";
            room.Sim = null;
            Broadcast(room, Protocol.Frame(Msg.BackHome));
            if (room.Private)
            {
                foreach (var kv in MembersOf(room)) { kv.Value.Room = null; SendSoloLobby(kv.Value); }
                rooms.Remove(room.Code);
            }
            else SendLobby(room);
            Debug.Log("RETURNED_HOME");
        }

        System.Collections.IEnumerator Finish(Room room)
        {
            room.Finishing = true;
            var sim = room.Sim;
            bool success = sim.State == MissionState.Victory;
            int gold = sim.MissionGold();
            int region = sim.Region, kills = sim.Kills, wave = sim.Wave, difficulty = sim.Difficulty;
            float elapsed = sim.Elapsed;
            string grade = sim.MissionGrade();
            var records = new Dictionary<int, int[]>();
            var feats = new Dictionary<int, int[]>();
            foreach (var a in sim.Actors.Values)
            {
                records[a.Id] = new[] { a.StatKills, a.StatHeadshots, a.StatShots, a.StatHits };
                feats[a.Id] = new[] { a.StatDowns, a.StatRevives, a.StatBossKills, a.StatEliteKills, a.AimFlags };
            }
            Broadcast(room, Protocol.Frame(Msg.Snapshot, w => Protocol.WriteSnapshot(w, sim)));
            foreach (var kv in MembersOf(room).Where(kv => kv.Value.Profile != null).ToList())
            {
                var p = kv.Value;
                bool done = false;
                var reward = new Dictionary<string, object> { ["account_id"] = p.Profile.Id, ["gold"] = gold };
                if (records.TryGetValue(kv.Key, out var rec))
                {
                    var feat = feats[kv.Key];
                    reward["stats"] = new Dictionary<string, object>
                    {
                        ["kills"] = rec[0], ["headshots"] = rec[1], ["shots"] = rec[2], ["hits"] = rec[3], ["wave"] = wave, ["victory"] = success, ["difficulty"] = difficulty,
                        ["downs"] = feat[0], ["revives"] = feat[1], ["boss_kills"] = Mathf.Min(2, feat[2]), ["elite_kills"] = Mathf.Min(20, feat[3]), ["grade"] = grade,
                        // Mission summary for the account service's anti-cheat cross-check.
                        ["team_kills"] = kills, ["level"] = sim.Level, ["region"] = region, ["elapsed"] = (double)elapsed, ["aim_flags"] = feat[4],
                    };
                }
                api.Post("/api/server/reward", reward, result =>
                {
                    if (result.Bool("ok")) p.Profile = Profile.From(result.Obj("profile")) ?? p.Profile;
                    done = true;
                });
                while (!done) yield return null;
                if (success)
                {
                    done = false;
                    api.Post("/api/server/progress", new Dictionary<string, object> { ["account_id"] = p.Profile.Id, ["mission"] = region, ["difficulty"] = difficulty }, result =>
                    {
                        if (result.Bool("ok")) p.Profile = Profile.From(result.Obj("profile")) ?? p.Profile;
                        done = true;
                    });
                    while (!done) yield return null;
                }
                p.Conn.Send(Protocol.Frame(Msg.ProfileSync));
            }
            room.Mode = "result";
            foreach (var kv in MembersOf(room).Where(kv => kv.Value.Profile != null))
            {
                var rec = records.TryGetValue(kv.Key, out var r) ? r : new int[4];
                kv.Value.Conn.Send(Protocol.Frame(Msg.Finish, w =>
                {
                    w.Write(success); w.Write(region); w.Write(kills); w.Write(elapsed); w.Write(gold); w.Write(grade);
                    foreach (var v in rec) w.Write(v);
                    w.Write(difficulty);
                }));
            }
            if (players.TryGetValue(room.Leader, out var l) && l.Profile != null)
            {
                room.Selected = Mathf.Min(room.Selected, l.Profile.Unlocked);
                room.Difficulty = OpenTier(l.Profile, room.Selected, room.Difficulty);
            }
            SendLobby(room);
            Debug.Log($"MISSION_RESULT success={success} region={region} kills={kills} gold={gold}");
        }
    }
}
