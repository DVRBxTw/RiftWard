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
        }

        const int MaxPlayers = 4;
        TcpListener listener;
        readonly ConcurrentQueue<TcpClient> accepted = new ConcurrentQueue<TcpClient>();
        readonly Dictionary<int, Player> players = new Dictionary<int, Player>();
        int nextId = 1;
        AccountApi api;
        GameSim sim;
        int selected, leader = -1;
        string mode = "lobby";
        float snapshotTimer;
        bool finishing;
        bool testFast;
        int fastWave = -1;
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
            if (sim == null || mode != "playing") return;
            foreach (var kv in players)
                if (sim.Actors.TryGetValue(kv.Key, out var actor) && clock - kv.Value.LastInput > 0.25f)
                    sim.Inputs[kv.Key] = new PlayerInput { Seq = sim.Inputs.TryGetValue(kv.Key, out var old) ? old.Seq : 0, Yaw = actor.Yaw, Slot = actor.Slot };
            if (testFast && sim.Wave != fastWave && sim.Enemies.Count > 0 && !sim.BuffPending)
            {
                fastWave = sim.Wave;
                sim.Enemies.Clear();
                sim.GainXp(sim.XpNeed);
            }
            sim.Tick(Time.fixedDeltaTime);
            if (sim.Shots.Count + sim.Explosions.Count + sim.Announcements.Count > 0)
            {
                var events = Protocol.Frame(Msg.Events, w => Protocol.WriteEvents(w, sim));
                Broadcast(events);
                sim.Shots.Clear(); sim.Explosions.Clear(); sim.Announcements.Clear();
            }
            foreach (var kv in players)
            {
                if (!sim.BuffPending || !sim.BuffOffers.TryGetValue(kv.Key, out var offer) || kv.Value.OfferSent == offer) continue;
                kv.Value.OfferSent = offer;
                kv.Value.Conn.Send(Protocol.Frame(Msg.BuffOffer, w => { w.Write(sim.Level); w.Write(offer.Length); foreach (var c in offer) w.Write(c); }));
            }
            snapshotTimer += Time.fixedDeltaTime;
            if (snapshotTimer >= 0.05f)
            {
                snapshotTimer = 0;
                Broadcast(Protocol.Frame(Msg.Snapshot, w => Protocol.WriteSnapshot(w, sim)));
            }
            if ((sim.State == MissionState.Victory || sim.State == MissionState.Defeat) && !finishing) StartCoroutine(Finish());
        }

        System.Collections.IEnumerator AuthTimeout(int id)
        {
            yield return new WaitForSecondsRealtime(12f);
            if (players.TryGetValue(id, out var p) && p.Profile == null) { p.Conn.Close("auth timeout"); Drop(id); }
        }

        void Broadcast(byte[] frame)
        {
            foreach (var p in players.Values) if (p.Profile != null) p.Conn.Send(frame);
        }

        void Drop(int id)
        {
            if (!players.Remove(id)) return;
            Debug.Log("PEER_LEFT " + id);
            if (sim != null && sim.Actors.ContainsKey(id))
            {
                sim.RemoveActor(id);
                if (mode == "playing" && sim.Actors.Count == 0) EndToLobby();
            }
            if (id == leader) leader = players.Keys.Where(k => players[k].Profile != null).DefaultIfEmpty(-1).First();
            BroadcastLobby();
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
                switch (type)
                {
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
                        if (id == leader && mode != "playing") { selected = Mathf.Clamp(region, 0, p.Profile.Unlocked); BroadcastLobby(); }
                        break;
                    case Msg.RequestDeploy:
                        int target = r.ReadInt32();
                        if (id == leader && mode != "playing") StartMission(target);
                        break;
                    case Msg.LeaveMission:
                        if (mode == "playing" && sim != null && sim.Actors.ContainsKey(id))
                        {
                            sim.RemoveActor(id);
                            p.Conn.Send(Protocol.Frame(Msg.BackHome));
                            if (sim.Actors.Count == 0) EndToLobby();
                        }
                        break;
                    case Msg.RequestHome:
                        if (mode == "result") EndToLobby();
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
                if (leader < 0 || !players.ContainsKey(leader) || players[leader].Profile == null) { leader = id; selected = Mathf.Min(selected, profile.Unlocked); }
                p.Conn.Send(Protocol.Frame(Msg.Welcome, w => w.Write(id)));
                Debug.Log("ACCOUNT_PEER_VERIFIED id=" + id);
                BroadcastLobby();
                if (mode == "playing") JoinMission(id);
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

        void BroadcastLobby()
        {
            int unlocked = leader >= 0 && players.TryGetValue(leader, out var l) && l.Profile != null ? l.Profile.Unlocked : 0;
            int count = players.Values.Count(p => p.Profile != null);
            Broadcast(Protocol.Frame(Msg.Lobby, w => { w.Write(selected); w.Write(unlocked); w.Write(leader); w.Write(count); w.Write(mode); }));
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

        void StartMission(int region)
        {
            var ready = players.Where(kv => kv.Value.Profile != null).ToList();
            if (ready.Count == 0) return;
            if (ready.Any(kv => region > kv.Value.Profile.Unlocked))
            {
                players[leader].Conn.Send(Protocol.Frame(Msg.Notice, w => w.Write("隊友尚未解鎖此區域，請先完成前一區域")));
                return;
            }
            selected = region;
            sim = new GameSim();
            foreach (var kv in ready) AddToSim(kv.Key, kv.Value);
            sim.StartMission(region);
            foreach (var kv in ready) sim.SetAdmin(kv.Key, kv.Value.Admin);
            mode = "playing";
            finishing = false;
            fastWave = -1;
            Broadcast(Protocol.Frame(Msg.Deploy, w => w.Write(region)));
            BroadcastLobby();
            Debug.Log("MISSION_START region=" + region + " players=" + ready.Count);
        }

        void AddToSim(int id, Player p)
        {
            var lo = new Loadout { Owned = new List<string>(p.Profile.Loadout.Owned), Equipped = new List<string>(p.Profile.Loadout.Equipped) };
            foreach (var kv in p.Profile.Loadout.Levels) lo.Levels[kv.Key] = new Dictionary<string, int>(kv.Value);
            sim.AddActor(id, lo, p.Profile.Admin);
            p.OfferSent = null;
            p.LastInput = clock;
        }

        void JoinMission(int id)
        {
            AddToSim(id, players[id]);
            sim.SetAdmin(id, players[id].Admin);
            players[id].Conn.Send(Protocol.Frame(Msg.Deploy, w => w.Write(sim.Region)));
        }

        void EndToLobby()
        {
            mode = "lobby";
            sim = null;
            Broadcast(Protocol.Frame(Msg.BackHome));
            BroadcastLobby();
            Debug.Log("RETURNED_HOME");
        }

        System.Collections.IEnumerator Finish()
        {
            finishing = true;
            bool success = sim.State == MissionState.Victory;
            int gold = sim.MissionGold();
            int region = sim.Region, kills = sim.Kills, wave = sim.Wave;
            float elapsed = sim.Elapsed;
            string grade = sim.MissionGrade();
            var records = new Dictionary<int, int[]>();
            foreach (var a in sim.Actors.Values) records[a.Id] = new[] { a.StatKills, a.StatHeadshots, a.StatShots, a.StatHits };
            Broadcast(Protocol.Frame(Msg.Snapshot, w => Protocol.WriteSnapshot(w, sim)));
            foreach (var kv in players.Where(kv => kv.Value.Profile != null).ToList())
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
            mode = "result";
            foreach (var kv in players.Where(kv => kv.Value.Profile != null))
            {
                var rec = records.TryGetValue(kv.Key, out var r) ? r : new int[4];
                kv.Value.Conn.Send(Protocol.Frame(Msg.Finish, w =>
                {
                    w.Write(success); w.Write(region); w.Write(kills); w.Write(elapsed); w.Write(gold); w.Write(grade);
                    foreach (var v in rec) w.Write(v);
                }));
            }
            if (leader >= 0 && players.TryGetValue(leader, out var l) && l.Profile != null) selected = Mathf.Min(selected, l.Profile.Unlocked);
            BroadcastLobby();
            Debug.Log($"MISSION_RESULT success={success} region={region} kills={kills} gold={gold}");
        }
    }
}
