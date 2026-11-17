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
            public float LastInput;
            public AdminSettings Admin = new AdminSettings();
            public Room Room;
            /// Region picked while playing alone (no room).
            public int SoloRegion;
        }

        /// A party with its own leader, mission and snapshots. Players outside a room are alone.
        sealed class Room
        {
            public string Code;
            public readonly List<int> Members = new List<int>();
            public bool Private;
            public GameSim Sim;
            public int Selected, Leader = -1;
            public string Mode = "lobby";
            public float SnapshotTimer;
            public bool Finishing;
            public int FastWave = -1;
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
        float clock;

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
            string dataDir = Env("RIFTWARD_SERVER_DATA", Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "server-data"));
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
        }

        IEnumerable<KeyValuePair<int, Player>> MembersOf(Room room) =>
            room.Members.Where(players.ContainsKey).Select(id => new KeyValuePair<int, Player>(id, players[id])).ToList();

        void TickRoom(Room room)
        {
            var sim = room.Sim;
            if (sim == null || room.Mode != "playing") return;
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
                Broadcast(room, Protocol.Frame(Msg.Snapshot, w => Protocol.WriteSnapshot(w, sim)));
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
            if (!rooms.TryGetValue(code, out var room) || room.Private) { Notice(p, "找不到房號 " + code); return; }
            if (room == p.Room) return;
            if (room.Members.Count >= RoomSize) { Notice(p, "房間已滿（最多 4 人）"); return; }
            if (room.Mode == "result") { Notice(p, "房間正在結算，請稍後再試"); return; }
            LeaveRoom(p, false);
            room.Members.Add(p.Conn.Id);
            p.Room = room;
            Debug.Log($"ROOM_JOINED {code} id={p.Conn.Id}");
            SendLobby(room);
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
            Broadcast(room, Protocol.Frame(Msg.Lobby, w => { w.Write(room.Selected); w.Write(unlocked); w.Write(room.Leader); w.Write(count); w.Write(room.Mode); w.Write(code); }));
        }

        /// A player outside any room is the leader of a party of one.
        void SendSoloLobby(Player p)
        {
            p.Conn.Send(Protocol.Frame(Msg.Lobby, w => { w.Write(p.SoloRegion); w.Write(p.Profile.Unlocked); w.Write(p.Conn.Id); w.Write(1); w.Write("lobby"); w.Write(""); }));
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
                        int region = r.ReadInt32();
                        if (room == null) { p.SoloRegion = Mathf.Clamp(region, 0, p.Profile.Unlocked); SendSoloLobby(p); }
                        else if (id == room.Leader && mode != "playing") { room.Selected = Mathf.Clamp(region, 0, p.Profile.Unlocked); SendLobby(room); }
                        break;
                    case Msg.RequestDeploy:
                        int target = r.ReadInt32();
                        if (room == null) room = CreateRoom(p, true);
                        if (id == room.Leader && room.Mode == "lobby") StartMission(room, target);
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

        // ------------------------------------------------------------ missions

        void StartMission(Room room, int region)
        {
            var ready = MembersOf(room).Where(kv => kv.Value.Profile != null).ToList();
            if (ready.Count == 0) return;
            if (ready.Any(kv => region > kv.Value.Profile.Unlocked))
            {
                Notice(players[room.Leader], "隊友尚未解鎖此區域，請先完成前一區域");
                return;
            }
            room.Selected = region;
            var sim = room.Sim = new GameSim();
            foreach (var kv in ready) AddToSim(sim, kv.Key, kv.Value);
            sim.StartMission(region);
            foreach (var kv in ready) sim.SetAdmin(kv.Key, kv.Value.Admin);
            room.Mode = "playing";
            room.Finishing = false;
            room.FastWave = -1;
            Broadcast(room, Protocol.Frame(Msg.Deploy, w => w.Write(region)));
            SendLobby(room);
            Debug.Log($"MISSION_START room={room.Code} region={region} players={ready.Count}");
        }

        void AddToSim(GameSim sim, int id, Player p)
        {
            var lo = new Loadout { Owned = new List<string>(p.Profile.Loadout.Owned), Equipped = new List<string>(p.Profile.Loadout.Equipped) };
            foreach (var kv in p.Profile.Loadout.Levels) lo.Levels[kv.Key] = new Dictionary<string, int>(kv.Value);
            lo.Rarity = new Dictionary<string, int>(p.Profile.Loadout.Rarity);
            sim.AddActor(id, lo, p.Profile.Admin).Name = p.Profile.Username;
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
            players[id].Conn.Send(Protocol.Frame(Msg.Deploy, w => w.Write(sim.Region)));
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
            int region = sim.Region, kills = sim.Kills, wave = sim.Wave;
            float elapsed = sim.Elapsed;
            string grade = sim.MissionGrade();
            var records = new Dictionary<int, int[]>();
            foreach (var a in sim.Actors.Values) records[a.Id] = new[] { a.StatKills, a.StatHeadshots, a.StatShots, a.StatHits };
            Broadcast(room, Protocol.Frame(Msg.Snapshot, w => Protocol.WriteSnapshot(w, sim)));
            foreach (var kv in MembersOf(room).Where(kv => kv.Value.Profile != null).ToList())
            {
                var p = kv.Value;
                bool done = false;
                var reward = new Dictionary<string, object> { ["account_id"] = p.Profile.Id, ["gold"] = gold };
                if (records.TryGetValue(kv.Key, out var rec))
                    reward["stats"] = new Dictionary<string, object> { ["kills"] = rec[0], ["headshots"] = rec[1], ["shots"] = rec[2], ["hits"] = rec[3], ["wave"] = wave, ["victory"] = success };
                api.Post("/api/server/reward", reward, result =>
                {
                    if (result.Bool("ok")) p.Profile = Profile.From(result.Obj("profile")) ?? p.Profile;
                    done = true;
                });
                while (!done) yield return null;
                if (success)
                {
                    done = false;
                    api.Post("/api/server/progress", new Dictionary<string, object> { ["account_id"] = p.Profile.Id, ["mission"] = region }, result =>
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
                }));
            }
            if (players.TryGetValue(room.Leader, out var l) && l.Profile != null) room.Selected = Mathf.Min(room.Selected, l.Profile.Unlocked);
            SendLobby(room);
            Debug.Log($"MISSION_RESULT success={success} region={region} kills={kills} gold={gold}");
        }
    }
}
