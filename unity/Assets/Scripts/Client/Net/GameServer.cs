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
            /// Upgrade stacks last sent to this player (BuffStacks message), to send only on change.
            public string StacksSent = "";
            /// Last time anything arrived on this connection (clients send a latency probe every second).
            public float LastSeen;
            public float LastInput, LastPing = -9f, PingWindow, LastFriendNotice = -9f;
            public int PingCount;
            public bool ReceivedMissionInput;
            /// Last real action (movement, aim, fire, keys) and whether the computer is playing for them now.
            public float LastActive;
            public float LastYaw, LastPitch;
            public int LastSlot, ClientSeq;
            public bool Bot;
            public AdminSettings Admin = new AdminSettings();
            public Room Room;
            /// Region and difficulty picked while playing alone (no room).
            public int SoloRegion, SoloDifficulty;
            /// Account ids of this player's friends (from the account service).
            public List<int> Friends = new List<int>();
            /// Send times of recent chat messages, for the rate limit.
            public readonly Queue<float> ChatTimes = new Queue<float>();
            /// One-shot keys (Q, Shift, X) seen since the last tick: a short tap can arrive and be overwritten by the next
            /// input before the sim runs, so they are held here until a tick has used them.
            public bool TapSkill, TapDash, TapUlt;
            /// When this player joined the PvP queue (-1 = not queued).
            public float QueuedAt = -1f;
            public int AccountId => Profile?.Id ?? 0;
            public string ChatName => Profile == null ? "" : Profile.Username;
        }

        /// A party with its own leader, mission and snapshots. Players outside a room are alone.
        /// Seconds without input during a mission before a player is treated as disconnected.
        const float DisconnectTimeout = 10f;
        const float DeploymentTimeout = 45f;
        /// How long a player who dropped mid-mission keeps their seat (and soldier) for a reconnect.
        const float ReconnectGrace = 60f;
        /// Players who dropped mid-mission, by account id, waiting to reconnect: their soldier stands still in the room.
        readonly Dictionary<int, (Player player, float since)> ghosts = new Dictionary<int, (Player, float)>();

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
            /// Members who already left the result sheet; the room returns to the lobby once all have.
            public readonly HashSet<int> Home = new HashSet<int>();
            /// The running (or last) match is a PvP Free For All.
            public bool Pvp;
        }

        const int MaxPlayers = 64, RoomSize = 4;
        const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        readonly Dictionary<string, Room> rooms = new Dictionary<string, Room>();
        /// PvP matchmaking queue (connection ids, oldest first).
        readonly List<int> pvpQueue = new List<int>();
        float pvpStatusTimer;
        readonly System.Random rng = new System.Random();
        TcpListener listener;
        readonly ConcurrentQueue<TcpClient> accepted = new ConcurrentQueue<TcpClient>();
        readonly Dictionary<int, Player> players = new Dictionary<int, Player>();
        int nextId = 1;
        AccountApi api;
        bool testFast;
        float clock, presenceTimer;
        string dataDir = ".";

        // Chat: live only (no backlog for newcomers), temporary mutes and a word filter.
        const int ChatBurst = 5;
        const float ChatWindow = 10f;
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
            foreach (var g in ghosts.ToList())
            {
                var gp = g.Value.player;
                bool seated = gp.Room != null && rooms.TryGetValue(gp.Room.Code, out var gr) && gr == gp.Room && gr.Mode == "playing" && gr.Sim != null && gr.Sim.Actors.ContainsKey(gp.Conn.Id);
                if (seated && clock - g.Value.since < ReconnectGrace) continue;
                ghosts.Remove(g.Key);
                ForgetGhost(gp);
            }
            foreach (var room in rooms.Values.ToList()) TickRoom(room);
            TickPvpQueue();
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
            // A member whose game stopped sending input (network gone, socket not yet closed) counts as disconnected:
            // they leave the mission without its reward, and the rest play on.
            foreach (var kv in MembersOf(room))
                if (!room.Finishing && sim.Actors.ContainsKey(kv.Key) && clock - kv.Value.LastInput >
                    (kv.Value.ReceivedMissionInput ? DisconnectTimeout : DeploymentTimeout))
                {
                    Debug.Log("PEER_TIMEOUT " + kv.Key);
                    kv.Value.Conn.Close("timeout");   // dropped next tick; the seat is held for a reconnect
                    Drop(kv.Key);
                }
            if (room.Sim != sim || room.Mode != "playing" || room.Members.Count == 0) return;
            // Seats held for players reconnecting: alone, the run freezes until they return; with a team, the computer
            // plays for them (and picks their upgrades) so nobody waits on a dropped connection.
            var away = ghosts.Values.Select(g => g.player).Where(g => g.Room == room && sim.Actors.ContainsKey(g.Conn.Id)).ToList();
            if (away.Count > 0)
            {
                if (!MembersOf(room).Any(kv => sim.Actors.ContainsKey(kv.Key))) return;
                var stand = new HashSet<int>(away.Select(g => g.Conn.Id));
                foreach (var g in away)
                {
                    sim.Inputs[g.Conn.Id] = sim.BotInput(sim.Actors[g.Conn.Id], g.ClientSeq, stand);
                    if (sim.BuffPending && sim.BuffOffers.TryGetValue(g.Conn.Id, out var picks) && picks.Length > 0)
                        sim.ChooseBuff(g.Conn.Id, picks[UnityEngine.Random.Range(0, picks.Length)]);
                }
            }
            if (room.Paused && room.Members.Count == 1) return;   // solo pause: nothing moves, nothing is sent
            foreach (var kv in MembersOf(room))
                if (sim.Actors.TryGetValue(kv.Key, out var actor) && clock - kv.Value.LastInput > 0.25f)
                    sim.Inputs[kv.Key] = new PlayerInput { Seq = sim.Inputs.TryGetValue(kv.Key, out var old) ? old.Seq : 0, Yaw = actor.Yaw, Slot = actor.Slot };
            // 掛機代打: idle for Defs.AfkTakeover seconds in a team mission, the computer plays for them; its level-up
            // picks are random.
            if (room.Members.Count > 1 && !room.Pvp)
            {
                var idle = new HashSet<int>(MembersOf(room).Where(kv => kv.Value.Bot).Select(kv => kv.Key));
                foreach (var kv in MembersOf(room))
                {
                    var m = kv.Value;
                    if (!sim.Actors.TryGetValue(kv.Key, out var body)) continue;
                    if (!m.Bot && m.ReceivedMissionInput && clock - m.LastActive > Defs.AfkTakeover && sim.State == MissionState.Playing)
                    {
                        m.Bot = true;
                        idle.Add(kv.Key);
                        RoomNotice(room, $"{m.ChatName} 已閒置超過 1 分鐘，由電腦代打");
                    }
                    if (!m.Bot) continue;
                    sim.Inputs[kv.Key] = sim.BotInput(body, m.ClientSeq, idle);
                    if (sim.BuffPending && sim.BuffOffers.TryGetValue(kv.Key, out var choices) && choices.Length > 0)
                        sim.ChooseBuff(kv.Key, choices[UnityEngine.Random.Range(0, choices.Length)]);
                }
            }
            if (testFast && sim.Wave != room.FastWave && sim.Enemies.Count > 0 && !sim.BuffPending)
            {
                room.FastWave = sim.Wave;
                sim.Enemies.Clear();
                sim.GainXp(sim.XpNeed);
            }
            // Loading the world can take longer on a four-player deployment. Keep publishing the initial
            // snapshot, but do not start combat until every remaining actor has sent their first input.
            if (MembersOf(room).Where(kv => sim.Actors.ContainsKey(kv.Key)).All(kv => kv.Value.ReceivedMissionInput))
            {
                sim.Tick(Time.fixedDeltaTime);
                // The tick has seen this frame's taps: clear them on the players and on the stored inputs.
                foreach (var kv in MembersOf(room))
                {
                    var m = kv.Value;
                    if (!(m.TapSkill || m.TapDash || m.TapUlt)) continue;
                    m.TapSkill = m.TapDash = m.TapUlt = false;
                    if (sim.Inputs.TryGetValue(kv.Key, out var used)) { used.Skill = used.Dash = used.Ult = false; sim.Inputs[kv.Key] = used; }
                }
            }
            if (sim.Shots.Count + sim.Explosions.Count + sim.Announcements.Count + sim.Damages.Count > 0)
            {
                var events = Protocol.Frame(Msg.Events, w => Protocol.WriteEvents(w, sim));
                Broadcast(room, events);
                sim.Shots.Clear(); sim.Explosions.Clear(); sim.Announcements.Clear(); sim.Damages.Clear();
            }
            foreach (var kv in MembersOf(room))
            {
                if (!sim.BuffPending || !sim.BuffOffers.TryGetValue(kv.Key, out var offer) || kv.Value.OfferSent == offer) continue;
                kv.Value.OfferSent = offer;
                kv.Value.Conn.Send(Protocol.Frame(Msg.BuffOffer, w => { w.Write(sim.Level); w.Write((byte)sim.SuperPick); w.Write(offer.Length); foreach (var c in offer) w.Write(c);
                    // How many of each offered upgrade this player already has (the client's mirror does not track stacks).
                    var mine = sim.Actors.TryGetValue(kv.Key, out var pa) ? pa : null;
                    foreach (var c in offer) { int bi = Defs.BuffIndex(c); w.Write((byte)(mine != null && bi >= 0 && bi < mine.BuffStacks.Length ? Mathf.Min(255, mine.BuffStacks[bi]) : 0)); }
                }));
            }
            foreach (var kv in MembersOf(room))
            {
                if (!sim.Actors.TryGetValue(kv.Key, out var who)) continue;
                string sig = string.Join(",", who.BuffStacks);
                if (sig == kv.Value.StacksSent) continue;
                kv.Value.StacksSent = sig;
                var stacks = who.BuffStacks;
                kv.Value.Conn.Send(Protocol.Frame(Msg.BuffStacks, w => { w.Write((byte)stacks.Length); foreach (int n in stacks) w.Write((byte)Mathf.Min(255, n)); }));
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
            var room = p.Room;
            if (p.Profile != null && room != null && room.Mode == "playing" && !room.Finishing && room.Sim != null && room.Sim.Actors.ContainsKey(id))
            {
                // Mid-mission: hold the seat so they can reconnect into the same run; the soldier stops in place.
                players.Remove(id);
                var actor = room.Sim.Actors[id];
                room.Sim.Inputs[id] = new PlayerInput { Seq = room.Sim.Inputs.TryGetValue(id, out var old) ? old.Seq : 0, Yaw = actor.Yaw, Slot = actor.Slot };
                ghosts[p.AccountId] = (p, clock);
                RoomNotice(room, $"{p.ChatName} 連線中斷，正在嘗試重新連線…");
                Debug.Log("PEER_GHOST " + id);
                return;
            }
            LeaveRoom(p, false);
            players.Remove(id);
            // Record when this account was last online (shown as "離線 · 3 小時前" in friends' lists).
            if (p.Profile != null) api.Post("/api/server/seen", new Dictionary<string, object> { ["account_id"] = p.AccountId }, _ => { });
            Debug.Log("PEER_LEFT " + id);
        }

        /// The reconnect window closed (or the mission ended): the held seat is released for good.
        void ForgetGhost(Player p)
        {
            var room = p.Room;
            if (room != null && (!rooms.TryGetValue(room.Code, out var live) || live != room)) { room.Members.Remove(p.Conn.Id); p.Room = null; }
            LeaveRoom(p, false);
            api?.Post("/api/server/seen", new Dictionary<string, object> { ["account_id"] = p.AccountId }, _ => { });
            Debug.Log("PEER_GHOST_EXPIRED " + p.Conn.Id);
        }

        /// A dropped player logged back in: their new connection takes over the held seat (same id, same soldier).
        /// Returns false if there was nothing to resume.
        bool Resume(Player fresh, Profile profile)
        {
            if (!ghosts.TryGetValue(profile.Id, out var g)) return false;
            ghosts.Remove(profile.Id);
            var p = g.player;
            int oldId = p.Conn.Id;
            var room = p.Room;
            bool seated = room != null && rooms.TryGetValue(room.Code, out var live) && live == room && room.Mode == "playing" && room.Sim != null && room.Sim.Actors.ContainsKey(oldId);
            if (!seated) { ForgetGhost(p); return false; }
            players.Remove(fresh.Conn.Id);
            fresh.Conn.Id = oldId;
            p.Conn = fresh.Conn;
            p.Profile = profile;
            players[oldId] = p;
            p.Conn.Send(Protocol.Frame(Msg.Welcome, w => { w.Write(oldId); w.Write(true); }));
            p.OfferSent = null;
            p.StacksSent = "";
            p.LastInput = p.LastActive = clock;
            p.ReceivedMissionInput = false;
            p.Bot = false;
            room.ForceFull = true;
            p.Conn.Send(Protocol.Frame(Msg.Deploy, w => { w.Write(room.Sim.Region); w.Write(Defs.PackTier(room.Sim.Difficulty, room.Sim.Endless)); }));
            SendLobby(room);
            RoomNotice(room, $"{p.ChatName} 已重新連線");
            Debug.Log("PEER_RESUMED " + oldId);
            if (api != null) FetchFriends(p);
            return true;
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
                // Down to one player mid-mission: it is a solo run now, so the pause menu freezes it again.
                if (room.Mode == "playing" && room.Members.Count == 1 && players.TryGetValue(room.Members[0], out var last))
                    last.Conn.Send(Protocol.Frame(Msg.ChatLine, w => WriteLine(w, ChatChannel.System, 0, "", "", "隊友已離線，任務轉為單人模式 · 按 ESC 可暫停", false)));
                if (room.Leader == id) room.Leader = room.Members[0];
                if (room.Mode == "playing" && room.Sim != null && room.Sim.Actors.Count == 0) EndToLobby(room);
                else if (room.Mode == "result" && room.Members.All(room.Home.Contains)) EndToLobby(room);
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
            Broadcast(room, Protocol.Frame(Msg.Lobby, w => { w.Write(room.Selected); w.Write(unlocked); w.Write(room.Leader); w.Write(count); w.Write(room.Mode); w.Write(code); w.Write(room.Difficulty); WriteMembers(w, room); }));
        }

        /// Room roster for the lobby: [int count] × ([int id][string name][string weapon][int rarity]), so every member
        /// sees who is in the party, each with the soldier and gun they will deploy with.
        void WriteMembers(BinaryWriter w, Room room)
        {
            var members = MembersOf(room).Where(kv => kv.Value.Profile != null).ToList();
            w.Write(members.Count);
            foreach (var kv in members)
            {
                var lo = kv.Value.Profile.Loadout;
                string weapon = lo.Equipped.Count > 0 ? lo.Equipped[0] : "rifle";
                w.Write(kv.Key); w.Write(kv.Value.ChatName); w.Write(weapon); w.Write(lo.RarityOf(weapon));
            }
        }

        /// A player outside any room is the leader of a party of one.
        void SendSoloLobby(Player p)
        {
            p.Conn.Send(Protocol.Frame(Msg.Lobby, w => { w.Write(p.SoloRegion); w.Write(p.Profile.Unlocked); w.Write(p.Conn.Id); w.Write(1); w.Write("lobby"); w.Write(""); w.Write(p.SoloDifficulty); w.Write(0); }));
        }

        /// The requested tier, lowered until it is open for this player in the region.
        /// Keeps the 無盡 flag only where this player has cleared the region.
        static int OpenTier(Profile profile, int region, int packed)
        {
            int tier = Mathf.Clamp(Defs.TierOf(packed), 0, Defs.Difficulties.Length - 1);
            while (tier > 0 && !profile.DifficultyOpen(region, tier)) tier--;
            return Defs.PackTier(tier, Defs.IsEndless(packed) && Defs.EndlessOpen(profile.Cleared, region));
        }

        // ------------------------------------------------------------ messages

        void Handle(Player p, Msg type, byte[] payload)
        {
            using var r = Connection.Reader(payload);
            p.LastSeen = clock;
            try
            {
                if (p.Profile == null)
                {
                    if (type == Msg.Hello && !p.Pending) Authenticate(p, r.ReadString(), r.ReadString());
                    return;
                }
                if (type == Msg.NetPing) { long stamp = r.ReadInt64(); p.Conn.Send(Protocol.Frame(Msg.NetPong, w => w.Write(stamp))); return; }
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
                            // While the computer plays, the idle client's view still kicks with the recoil of the bot's own
                            // shots and its weapon slot follows the bot's switches, so only a real flick of the mouse
                            // (or any key) counts as the player coming back; small view drift and slot changes do not.
                            float yawStep = p.Bot ? 15f : 1f, pitchStep = p.Bot ? 0.25f : 0.02f;
                            bool acted = input.Move.sqrMagnitude > 0.01f || input.Fire || input.Dash || input.Jump || input.Reload || input.Skill || input.Interact || input.Block
                                || Mathf.Abs(Mathf.DeltaAngle(input.Yaw * Mathf.Rad2Deg, p.LastYaw * Mathf.Rad2Deg)) > yawStep || Mathf.Abs(input.Pitch - p.LastPitch) > pitchStep
                                || (!p.Bot && input.Slot != p.LastSlot);
                            p.LastYaw = input.Yaw; p.LastPitch = input.Pitch; p.LastSlot = input.Slot;
                            if (acted)
                            {
                                p.LastActive = clock;
                                if (p.Bot && room != null) { p.Bot = false; RoomNotice(room, $"{p.ChatName} 回來了，結束電腦代打"); }
                            }
                            p.ClientSeq = input.Seq;
                            if (p.Bot) { p.LastInput = clock; break; }   // the computer keeps playing until they act
                            p.TapSkill |= input.Skill; p.TapDash |= input.Dash; p.TapUlt |= input.Ult;
                            input.Skill |= p.TapSkill; input.Dash |= p.TapDash; input.Ult |= p.TapUlt;
                            sim.Inputs[id] = input;
                            p.LastInput = clock;
                            p.ReceivedMissionInput = true;
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
                    case Msg.PvpQueue:
                        bool join = r.ReadBoolean();
                        pvpQueue.Remove(id);
                        p.QueuedAt = -1f;
                        if (join)
                        {
                            if (room != null && room.Mode == "playing") { Notice(p, "請先結束目前的任務再開始配對"); break; }
                            LeaveRoom(p, false);
                            pvpQueue.Add(id);
                            p.QueuedAt = clock;
                            SendSoloLobby(p);
                        }
                        SendPvpStatus(p);
                        break;
                    case Msg.RequestDeploy:
                        int target = r.ReadInt32(), targetTier = r.ReadInt32();
                        if (Defs.IsPvp(targetTier))
                        {
                            // Custom-room PvP: the leader starts a Free For All with everyone in the room.
                            if (room == null || id != room.Leader || room.Mode != "lobby") { if (room == null) Notice(p, $"PvP 自訂房至少需要 {Defs.PvpMinPlayers} 人：先建立房間並邀請朋友，或使用自動配對"); break; }
                            if (room.Members.Count < Defs.PvpMinPlayers) { Notice(p, $"PvP 至少需要 {Defs.PvpMinPlayers} 人"); break; }
                            StartPvp(room);
                            break;
                        }
                        if (room == null) room = CreateRoom(p, true);
                        if (id != room.Leader) break;
                        if (room.Mode == "lobby") StartMission(room, target, targetTier);
                        else if (room.Mode == "result")
                        {
                            // 再次挑戰 from the result sheet: the leader is done with it; the party redeploys together
                            // (teammates still reading theirs come along). If the start is refused, the room settles normally.
                            room.Home.Add(id);
                            StartMission(room, target, targetTier);
                            if (room.Mode == "result" && room.Members.All(room.Home.Contains)) EndToLobby(room);
                        }
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
                        if (mode == "result")
                        {
                            // Only this player leaves the result sheet; the others keep theirs until they choose.
                            room.Home.Add(id);
                            p.Conn.Send(Protocol.Frame(Msg.BackHome));
                            if (room.Members.All(room.Home.Contains)) EndToLobby(room);
                        }
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
                        int pingTarget = r.ReadInt32();
                        // 0.12 s apart at most (a double tap is two pings), and no more than 6 in any 3 seconds.
                        if (mode != "playing" || sim == null || pingKind > PingMarkers.MaxKind || clock - p.LastPing < 0.12f) break;
                        if (float.IsNaN(pingAt.x + pingAt.y + pingAt.z) || pingAt.sqrMagnitude > 1e6f) break;
                        if (clock - p.PingWindow > 3f) { p.PingWindow = clock; p.PingCount = 0; }
                        if (++p.PingCount > 6) break;
                        if (pingKind != PingMarkers.Enemy) pingTarget = 0;
                        p.LastPing = clock;
                        Broadcast(room, Protocol.Frame(Msg.PingMark, w => { w.Write(id); w.Write(p.ChatName); w.Write(pingAt); w.Write(pingKind); w.Write(pingTarget); }));
                        break;
                    case Msg.InviteFriend:
                        int invitee = r.ReadInt32();
                        if (mode != "result") Invite(p, invitee);
                        break;
                    case Msg.FriendRequest:
                        string friendName = r.ReadString();
                        if (p.Profile == null || clock - p.LastFriendNotice < 1f) break;
                        p.LastFriendNotice = clock;
                        var to = players.Values.FirstOrDefault(o => o != p && o.Profile != null && string.Equals(o.Profile.Username, friendName, StringComparison.OrdinalIgnoreCase));
                        to?.Conn.Send(Protocol.Frame(Msg.FriendRequestIn, w => w.Write(p.ChatName)));
                        break;
                    case Msg.TransferLeader:
                        int newLeader = r.ReadInt32();
                        if (room != null && id == room.Leader && newLeader != id && room.Members.Contains(newLeader) && players.TryGetValue(newLeader, out var heir))
                        {
                            room.Leader = newLeader;
                            SendLobby(room);
                            RoomNotice(room, $"{p.ChatName} 將隊長轉移給 {heir.ChatName}");
                        }
                        break;
                    case Msg.JoinRequest:
                        int joinHost = r.ReadInt32();
                        if (p.Profile == null || clock - p.LastFriendNotice < 1f) break;
                        p.LastFriendNotice = clock;
                        AskToJoin(p, joinHost);
                        break;
                    case Msg.JoinAnswer:
                        int asker = r.ReadInt32();
                        bool letIn = r.ReadBoolean();
                        AnswerJoin(p, asker, letIn);
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
                var twin = players.Values.FirstOrDefault(o => o != p && o.Profile != null && o.Profile.Id == profile.Id);
                // A silent twin is this player's own dead connection that the server has not noticed yet (a reconnect
                // after the network dropped): it is replaced. A live one is really another window.
                if (twin != null && clock - twin.LastSeen < 3f) { Reject(p, "這個帳號已在其他視窗登入遊戲"); return; }
                if (twin != null) { Debug.Log("PEER_REPLACED " + twin.Conn.Id); twin.Conn.Close("replaced"); Drop(twin.Conn.Id); }
                if (Resume(p, profile)) return;
                p.Profile = profile;
                p.Conn.Send(Protocol.Frame(Msg.Welcome, w => { w.Write(id); w.Write(false); }));
                Debug.Log("ACCOUNT_PEER_VERIFIED id=" + id);
                SendSoloLobby(p);
                // No backlog on login: chat shows only what is said while you are online.
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
                    w.Write(f?.ChatName ?? "");
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

        /// 申請入隊: a friend in a room is asked (a pop-up on their side); they decide.
        void AskToJoin(Player p, int accountId)
        {
            if (!p.Friends.Contains(accountId)) { Notice(p, "只能向好友申請入隊"); return; }
            var friend = Online(accountId);
            if (friend == null) { Notice(p, "好友目前不在線上"); return; }
            if (friend.Room == null) { Notice(p, $"{friend.ChatName} 目前不在房間中"); return; }
            if (friend.Room == p.Room) { Notice(p, $"你已經在 {friend.ChatName} 的房間"); return; }
            if (friend.Room.Members.Count >= RoomSize) { Notice(p, "對方房間已滿（最多 4 人）"); return; }
            friend.Conn.Send(Protocol.Frame(Msg.JoinRequestIn, w => { w.Write(p.AccountId); w.Write(p.ChatName); }));
            Notice(p, $"已向 {friend.ChatName} 申請入隊");
        }

        void AnswerJoin(Player host, int accountId, bool accept)
        {
            var asker = Online(accountId);
            if (asker == null || !host.Friends.Contains(accountId)) return;
            if (!accept) { Notice(asker, $"{host.ChatName} 婉拒了你的入隊申請"); SystemLine(asker, $"{host.ChatName} 婉拒了你的入隊申請"); return; }
            var room = host.Room;
            if (room == null) { Notice(host, "你已不在房間中"); return; }
            if (asker.Room != null && asker.Room.Mode == "playing" && asker.Room.Sim != null && asker.Room.Sim.Actors.ContainsKey(asker.Conn.Id))
            {
                Notice(host, $"{asker.ChatName} 正在任務中，無法加入");
                return;
            }
            room.Invited.Add(accountId);   // lets them into a private room too
            JoinRoom(asker, room.Code);
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
        string CleanChat(string text, int max = ChatChannel.MaxLength)
        {
            string clean = new string((text ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (clean.Length > max) clean = clean.Substring(0, max);
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
            // A whisper carries the recipient's name in front, so it gets that much more room.
            text = CleanChat(text, channel == ChatChannel.Whisper ? ChatChannel.MaxLength + 24 : ChatChannel.MaxLength);
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
            if (channel == ChatChannel.Whisper)
            {
                // "收件人 訊息": delivered only to that player (online), with a copy back to the sender.
                int space = text.IndexOf(' ');
                string to = space > 0 ? text.Substring(0, space) : text, body = space > 0 ? text.Substring(space + 1).Trim() : "";
                if (body.Length == 0) { SystemLine(p, "私訊格式：/w 帳號 訊息"); return; }
                if (body.Length > ChatChannel.MaxLength) body = body.Substring(0, ChatChannel.MaxLength);
                var target = players.Values.FirstOrDefault(o => o.Profile != null && string.Equals(o.ChatName, to, StringComparison.OrdinalIgnoreCase));
                if (target == null) { SystemLine(p, $"{to} 目前不在線上，私訊未送出"); return; }
                if (target == p) { SystemLine(p, "不能私訊自己"); return; }
                target.Conn.Send(Protocol.Frame(Msg.ChatLine, w => WriteLine(w, ChatChannel.Whisper, p.AccountId, p.ChatName, title, body, p.Profile.Admin)));
                p.Conn.Send(Protocol.Frame(Msg.ChatLine, w => WriteLine(w, ChatChannel.WhisperSent, target.AccountId, target.ChatName, "", body, false)));
                return;
            }
            if (channel == ChatChannel.Room)
            {
                if (p.Room == null) { SystemLine(p, "你不在房間中，訊息未送出（可改用全頻）"); return; }
                Broadcast(p.Room, Protocol.Frame(Msg.ChatLine, w => WriteLine(w, ChatChannel.Room, p.AccountId, p.ChatName, title, text, p.Profile.Admin)));
                return;
            }
            var frame = Protocol.Frame(Msg.ChatLine, w => WriteLine(w, ChatChannel.World, p.AccountId, p.ChatName, title, text, p.Profile.Admin));
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
            bool endless = Defs.IsEndless(difficulty);
            difficulty = Mathf.Clamp(Defs.TierOf(difficulty), 0, Defs.Difficulties.Length - 1);
            if (endless && ready.Any(kv => !Defs.EndlessOpen(kv.Value.Profile.Cleared, region)))
            {
                Notice(players[room.Leader], ready.Count > 1 ? "有隊員尚未通關此區域，無法進行無盡模式" : "通關此區域後開放無盡模式");
                return;
            }
            if (ready.Any(kv => !kv.Value.Profile.DifficultyOpen(region, difficulty)))
            {
                Notice(players[room.Leader], ready.Count > 1 ? $"有隊員尚未解鎖{Defs.Difficulty(difficulty).Name}難度，請先在此區域通關前一難度" : $"{Defs.Difficulty(difficulty).Name}難度尚未解鎖");
                return;
            }
            room.Selected = region;
            room.Difficulty = Defs.PackTier(difficulty, endless);
            room.Pvp = false;
            var sim = room.Sim = new GameSim();
            foreach (var kv in ready) AddToSim(sim, kv.Key, kv.Value);
            sim.StartMission(region, difficulty);
            sim.Endless = endless;
            foreach (var kv in ready) sim.SetAdmin(kv.Key, kv.Value.Admin);
            room.Mode = "playing";
            room.Finishing = false;
            room.ForceFull = true;
            room.SnapshotCount = 0;
            room.FastWave = -1;
            Broadcast(room, Protocol.Frame(Msg.Deploy, w => { w.Write(region); w.Write(Defs.PackTier(difficulty, endless)); }));
            SendLobby(room);
            Debug.Log($"MISSION_START room={room.Code} region={region} difficulty={difficulty} players={ready.Count}");
        }

        // ------------------------------------------------------------ PvP

        void SendPvpStatus(Player p)
        {
            float waited = p.QueuedAt < 0 ? 0f : clock - p.QueuedAt;
            p.Conn.Send(Protocol.Frame(Msg.PvpStatus, w => { w.Write(p.QueuedAt >= 0); w.Write(pvpQueue.Count); w.Write(waited); }));
        }

        /// Matchmaking: drop players who left or went into a mission; start a match once PvpMaxPlayers wait, or
        /// PvpQueueWait seconds after the second player joined.
        void TickPvpQueue()
        {
            pvpQueue.RemoveAll(id => !players.TryGetValue(id, out var q) || q.Profile == null || q.Room != null && q.Room.Mode == "playing");
            if (pvpQueue.Count >= Defs.PvpMinPlayers)
            {
                float secondJoined = players[pvpQueue[1]].QueuedAt;
                if (pvpQueue.Count >= Defs.PvpMaxPlayers || clock - secondJoined >= Defs.PvpQueueWait)
                {
                    var picked = pvpQueue.Take(Defs.PvpMaxPlayers).ToList();
                    pvpQueue.RemoveRange(0, picked.Count);
                    var first = players[picked[0]];
                    var room = CreateRoom(first, true);
                    foreach (var pid in picked.Skip(1))
                    {
                        var q = players[pid];
                        LeaveRoom(q, false);
                        room.Members.Add(pid);
                        q.Room = room;
                    }
                    foreach (var pid in picked) { players[pid].QueuedAt = -1f; SendPvpStatus(players[pid]); }
                    Debug.Log($"PVP_MATCH room={room.Code} players={picked.Count}");
                    StartPvp(room);
                }
            }
            pvpStatusTimer += Time.fixedDeltaTime;
            if (pvpStatusTimer >= 1f)
            {
                pvpStatusTimer = 0;
                foreach (var id in pvpQueue) if (players.TryGetValue(id, out var q)) SendPvpStatus(q);
            }
        }

        /// A Free For All with everyone in the room, stats normalised (see Defs.PvpDuration).
        void StartPvp(Room room)
        {
            var ready = MembersOf(room).Where(kv => kv.Value.Profile != null).ToList();
            if (ready.Count < Defs.PvpMinPlayers) return;
            var sim = room.Sim = new GameSim();
            foreach (var kv in ready)
            {
                var p = kv.Value;
                // Normalised: the player's two equipped guns, 普通 and +0; no gear, talents or admin powers.
                var lo = new Loadout { Owned = new List<string>(p.Profile.Loadout.Owned), Equipped = new List<string>(p.Profile.Loadout.Equipped) };
                sim.AddActor(kv.Key, lo, false).Name = p.Profile.Username;
                p.OfferSent = null; p.LastInput = clock; p.LastActive = clock; p.Bot = false; p.ReceivedMissionInput = false;
            }
            sim.StartPvp();
            if (testFast) sim.PvpTimeLeft = 6f;   // integration test: a short match
            room.Pvp = true;
            room.Selected = 0;
            room.Difficulty = 0;
            room.Mode = "playing";
            room.Finishing = false;
            room.ForceFull = true;
            room.SnapshotCount = 0;
            room.FastWave = -1;
            Broadcast(room, Protocol.Frame(Msg.Deploy, w => { w.Write(0); w.Write(Defs.PvpFlag); }));
            SendLobby(room);
            Debug.Log($"PVP_START room={room.Code} players={ready.Count}");
        }

        System.Collections.IEnumerator FinishPvp(Room room)
        {
            room.Finishing = true;
            var sim = room.Sim;
            var standings = sim.PvpStandings();
            var team = standings.Select(a => new Menus.Contribution
            {
                Id = a.Id, Name = a.Name ?? "", Kills = a.StatKills, Damage = Mathf.RoundToInt(a.StatDamage), Shots = a.StatShots, Hits = a.StatHits,
                Headshots = a.StatHeadshots, Downs = a.StatDowns,
            }).ToList();
            Broadcast(room, Protocol.Frame(Msg.Snapshot, w => Protocol.WriteSnapshot(w, sim)));
            var gold = new Dictionary<int, int>();
            foreach (var kv in MembersOf(room).Where(kv => kv.Value.Profile != null).ToList())
            {
                var p = kv.Value;
                int place = standings.FindIndex(a => a.Id == kv.Key);
                if (place < 0) continue;
                var me = standings[place];
                bool done = false;
                api.Post("/api/server/pvp", new Dictionary<string, object>
                {
                    ["account_id"] = p.Profile.Id, ["place"] = place, ["players"] = standings.Count, ["kills"] = me.StatKills, ["deaths"] = me.StatDowns,
                }, result =>
                {
                    if (result.Bool("ok")) { p.Profile = Profile.From(result.Obj("profile")) ?? p.Profile; gold[kv.Key] = result.Int("gold"); }
                    done = true;
                });
                while (!done) yield return null;
                p.Conn.Send(Protocol.Frame(Msg.ProfileSync));
            }
            room.Mode = "result";
            room.Home.Clear();
            foreach (var kv in MembersOf(room).Where(kv => kv.Value.Profile != null))
            {
                int place = standings.FindIndex(a => a.Id == kv.Key);
                if (place < 0) continue;
                var me = standings[place];
                int g = gold.TryGetValue(kv.Key, out var v) ? v : 0;
                kv.Value.Conn.Send(Protocol.Frame(Msg.Finish, w =>
                {
                    // PvP: "grade" carries the placement (1-based); kills and the record are this player's own.
                    w.Write(true); w.Write(0); w.Write(me.StatKills); w.Write(sim.Elapsed); w.Write(g); w.Write((place + 1).ToString());
                    w.Write(me.StatKills); w.Write(me.StatHeadshots); w.Write(me.StatShots); w.Write(me.StatHits);
                    w.Write(Defs.PvpFlag);
                    Menus.Contribution.Write(w, team);
                }));
            }
            SendLobby(room);
            Debug.Log($"PVP_RESULT room={room.Code} winner={standings.FirstOrDefault()?.Name}");
        }

        void AddToSim(GameSim sim, int id, Player p)
        {
            var lo = new Loadout { Owned = new List<string>(p.Profile.Loadout.Owned), Equipped = new List<string>(p.Profile.Loadout.Equipped) };
            foreach (var kv in p.Profile.Loadout.Levels) lo.Levels[kv.Key] = new Dictionary<string, int>(kv.Value);
            lo.Rarity = new Dictionary<string, int>(p.Profile.Loadout.Rarity);
            lo.Constellation = new Dictionary<string, int>(p.Profile.Loadout.Constellation);
            sim.AddActor(id, lo, p.Profile.Admin).Name = string.IsNullOrEmpty(p.Profile.Title) ? p.Profile.Username : $"〔{p.Profile.Title}〕{p.Profile.Username}";
            var b = p.Profile;
            sim.ApplyProfileBonus(id, b.BonusOf("hp"), b.BonusOf("damage"), b.BonusOf("speed"), b.BonusOf("reload"), b.BonusOf("crit"), b.BonusOf("hp_pct"), b.BonusOf("def"), b.BonusOf("gadget"));
            sim.ApplySkillBonus(id, b.BonusOf("skill_dmg"), b.BonusOf("skill_cd"), b.BonusOf("skill_radius"));
            p.OfferSent = null;
            p.LastInput = clock;
            p.LastActive = clock;
            p.Bot = false;
            p.ReceivedMissionInput = false;
        }

        void JoinMission(Room room, int id)
        {
            var sim = room.Sim;
            AddToSim(sim, id, players[id]);
            sim.SetAdmin(id, players[id].Admin);
            room.ForceFull = true;   // the newcomer needs everyone's names and weapons now
            players[id].Conn.Send(Protocol.Frame(Msg.Deploy, w => { w.Write(sim.Region); w.Write(Defs.PackTier(sim.Difficulty, sim.Endless)); }));
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
            if (room.Pvp) { yield return FinishPvp(room); yield break; }
            room.Finishing = true;
            var sim = room.Sim;
            bool success = sim.State == MissionState.Victory;
            int gold = sim.MissionGold();
            int region = sim.Region, kills = sim.Kills, wave = sim.Wave, difficulty = sim.Difficulty;
            float elapsed = sim.Elapsed;
            string grade = sim.MissionGrade();
            var team = Menus.Contribution.FromSim(sim);
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
                        ["endless"] = sim.Endless,
                        ["downs"] = feat[0], ["revives"] = feat[1], ["boss_kills"] = Mathf.Min(sim.Endless ? 99 : 2, feat[2]), ["elite_kills"] = Mathf.Min(20, feat[3]), ["grade"] = grade,
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
            room.Home.Clear();
            foreach (var kv in MembersOf(room).Where(kv => kv.Value.Profile != null))
            {
                var rec = records.TryGetValue(kv.Key, out var r) ? r : new int[4];
                kv.Value.Conn.Send(Protocol.Frame(Msg.Finish, w =>
                {
                    w.Write(success); w.Write(region); w.Write(kills); w.Write(elapsed); w.Write(gold); w.Write(grade);
                    foreach (var v in rec) w.Write(v);
                    w.Write(difficulty);
                    Menus.Contribution.Write(w, team);
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
