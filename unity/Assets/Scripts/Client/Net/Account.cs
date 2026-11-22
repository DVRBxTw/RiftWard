using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RiftWard.Sim;
using UnityEngine;
using UnityEngine.Networking;

namespace RiftWard.Client
{
    /// Account profile as returned by the Python account service.
    public sealed class Profile
    {
        public int Id;
        public string Username = "";
        public int Unlocked, Gold;
        public bool TutorialComplete, Admin;
        public Loadout Loadout = new Loadout();
        public int Kills, Headshots, Shots, Hits, Missions, Victories, BestWave, EndlessBest;

        public float Accuracy => Shots > 0 ? (float)Hits / Shots : 0f;
        public int Level = 1, LevelXp, LevelNeed = 150, Keys, WeaponKeys, TalentPoints, Shards;
        /// Draws left until 傳奇+ is guaranteed, per pool.
        public int GearPity = 50, WeaponPity = 50, LimitedPity = 80;
        /// 神話 chance of the next draw per pool (base 0.5%, soft pity after 60 draws without one).
        public float GearMyth = 0.5f, WeaponMyth = 0.5f, LimitedMyth = 0.5f;
        /// 限定補給 now running ("" when closed), when it ends (unix seconds), and whether this account's next 神話
        /// is guaranteed to be the limited weapon.
        public string LimitedWeapon = "";
        public long LimitedEndsAt;
        public bool LimitedGuarantee;
        /// The running 登入獎勵 event, or null.
        public LoginEvent Login;
        public bool LimitedOpen => LimitedWeapon != "" && LimitedEndsAt > System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        public Dictionary<string, int> Talents = new Dictionary<string, int>();
        public List<GearItem> Inventory = new List<GearItem>();
        public Dictionary<string, string> Equipment = new Dictionary<string, string>();
        public Dictionary<string, float> Bonus = new Dictionary<string, float>();

        /// Highest difficulty cleared per region (-1 = never).
        public List<int> Cleared = Enumerable.Repeat(-1, Defs.Regions.Length).ToList();
        public DailyMissions Daily = new DailyMissions();
        public DailyMissions Weekly = new DailyMissions();
        /// Best grade per "region:difficulty" (S/A/B/C).
        public Dictionary<string, string> Grades = new Dictionary<string, string>();
        public string BestGrade(int region, int tier) => Grades.TryGetValue($"{region}:{tier}", out var g) ? g : "";
        public string BestGrade(int region)
        {
            string best = "";
            foreach (var kv in Grades)
                if (kv.Key.StartsWith(region + ":") && "SABC".IndexOf(kv.Value) >= 0 && (best == "" || "SABC".IndexOf(kv.Value) < "SABC".IndexOf(best))) best = kv.Value;
            return best;
        }
        public readonly List<Achievement> Achievements = new List<Achievement>();
        /// Worn title: achievement id and its text (both empty when none).
        public string TitleId = "", Title = "";
        public int MailUnread, MailClaimable, FriendRequests;

        /// Reached tiers not yet claimed, across all achievements.
        public int AchievementsClaimable => Achievements.Sum(a => Mathf.Max(0, a.Reached - a.Claimed));

        public float BonusOf(string key) => Bonus.TryGetValue(key, out var v) ? v : 0f;
        public bool DifficultyOpen(int region, int tier) => Defs.DifficultyOpen(Cleared, region, tier);

        public static Profile From(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var p = new Profile
            {
                Id = d.Int("id"), Username = d.Str("username"), Unlocked = d.Int("unlocked"), Gold = d.Int("gold"),
                TutorialComplete = d.Bool("tutorial_complete"), Admin = d.Bool("admin"),
            };
            var lo = d.Obj("loadout");
            if (lo != null)
            {
                p.Loadout.Owned = new List<string>();
                foreach (var w in lo.Arr("owned") ?? new List<object>()) p.Loadout.Owned.Add(w.ToString());
                p.Loadout.Equipped = new List<string>();
                foreach (var w in lo.Arr("equipped") ?? new List<object>()) p.Loadout.Equipped.Add(w.ToString());
                var levels = lo.Obj("levels");
                if (levels != null)
                    foreach (var kv in levels)
                    {
                        var stats = new Dictionary<string, int>();
                        if (kv.Value is Dictionary<string, object> s) foreach (var st in s) stats[st.Key] = st.Value is double n ? (int)n : 0;
                        p.Loadout.Levels[kv.Key] = stats;
                    }
            }
            var rarity = lo?.Obj("rarity");
            if (rarity != null) foreach (var kv in rarity) p.Loadout.Rarity[kv.Key] = kv.Value is double rv ? (int)rv : 1;
            var copies = lo?.Obj("copies");
            if (copies != null)
                foreach (var kv in copies)
                {
                    var list = new List<int>();
                    if (kv.Value is List<object> items) foreach (var o in items) list.Add(o is double cv ? (int)cv : 1);
                    p.Loadout.Copies[kv.Key] = list;
                }
            var constellation = lo?.Obj("const");
            if (constellation != null) foreach (var kv in constellation) p.Loadout.Constellation[kv.Key] = kv.Value is double cn ? (int)cn : 0;
            var copyLevels = lo?.Obj("copy_levels");
            if (copyLevels != null)
                foreach (var kv in copyLevels)
                {
                    var rows = new List<Dictionary<string, int>>();
                    if (kv.Value is List<object> items)
                        foreach (var o in items)
                        {
                            var stats = new Dictionary<string, int>();
                            if (o is Dictionary<string, object> s) foreach (var st in s) stats[st.Key] = st.Value is double n ? (int)n : 0;
                            rows.Add(stats);
                        }
                    p.Loadout.CopyLevels[kv.Key] = rows;
                }
            var active = lo?.Obj("active");
            if (active != null) foreach (var kv in active) p.Loadout.Active[kv.Key] = kv.Value is double av ? (int)av : 0;
            p.Loadout.Normalize();
            var record = d.Obj("stats");
            if (record != null)
            {
                p.Kills = record.Int("kills"); p.Headshots = record.Int("headshots"); p.Shots = record.Int("shots"); p.Hits = record.Int("hits");
                p.Missions = record.Int("missions"); p.Victories = record.Int("victories"); p.BestWave = record.Int("best_wave"); p.EndlessBest = record.Int("endless_best");
            }
            p.Level = Mathf.Max(1, d.Int("level", 1)); p.LevelXp = d.Int("level_xp"); p.LevelNeed = Mathf.Max(1, d.Int("level_need", 150));
            p.Keys = d.Int("keys"); p.WeaponKeys = d.Int("weapon_keys"); p.Shards = d.Int("shards"); p.TalentPoints = d.Int("talent_points");
            var pity = d.Obj("pity");
            if (pity != null) { p.GearPity = pity.Int("gear", 50); p.WeaponPity = pity.Int("weapon", 50); p.LimitedPity = pity.Int("limited", 80); }
            var myth = d.Obj("myth_rate");
            if (myth != null) { p.GearMyth = myth.Float("gear", 0.5f); p.WeaponMyth = myth.Float("weapon", 0.5f); p.LimitedMyth = myth.Float("limited", 0.5f); }
            var limited = d.Obj("limited");
            if (limited != null)
            {
                p.LimitedWeapon = RiftWard.Sim.Defs.Weapons.ContainsKey(limited.Str("weapon")) ? limited.Str("weapon") : "";
                p.LimitedEndsAt = (long)(limited.TryGetValue("ends_at", out var end) && end is double e ? e : 0);
                p.LimitedGuarantee = limited.Bool("guarantee");
            }
            p.Login = LoginEvent.From(d.Obj("login_event"));
            var talents = d.Obj("talents");
            if (talents != null) foreach (var kv in talents) p.Talents[kv.Key] = kv.Value is double n ? (int)n : 0;
            foreach (var o in d.Arr("inventory") ?? new List<object>()) if (o is Dictionary<string, object> item) p.Inventory.Add(GearItem.From(item));
            var equipment = d.Obj("equipment");
            if (equipment != null) foreach (var kv in equipment) p.Equipment[kv.Key] = kv.Value?.ToString();
            var bonus = d.Obj("bonus");
            if (bonus != null) foreach (var kv in bonus) p.Bonus[kv.Key] = kv.Value is double n ? (float)n : 0f;
            var cleared = d.Arr("cleared");
            if (cleared != null)
                for (int i = 0; i < p.Cleared.Count && i < cleared.Count; i++) p.Cleared[i] = cleared[i] is double c ? (int)c : -1;
            p.Daily = DailyMissions.From(d.Obj("daily"));
            p.Weekly = DailyMissions.From(d.Obj("weekly"));
            var grades = d.Obj("grades");
            if (grades != null) foreach (var kv in grades) p.Grades[kv.Key] = kv.Value?.ToString() ?? "";
            foreach (var o in d.Arr("achievements") ?? new List<object>()) if (o is Dictionary<string, object> a) p.Achievements.Add(Achievement.From(a));
            var mail = d.Obj("mail");
            if (mail != null) { p.MailUnread = mail.Int("unread"); p.MailClaimable = mail.Int("claimable"); }
            p.FriendRequests = d.Int("friend_requests");
            var title = d.Obj("title");
            if (title != null) { p.TitleId = title.Str("id"); p.Title = title.Str("text"); }
            return p;
        }
    }

    /// One three-tier achievement as reported by the account service.
    public sealed class Achievement
    {
        public string Id = "", Category = "", Name = "", Text = "", Title = "";
        public int Progress, Reached, Claimed;
        public int[] Goals = { 1, 1, 1 };
        public DailyReward Reward;

        public bool Complete => Claimed >= 3;
        /// Goal of the tier being worked on (the last goal once complete).
        public int Goal => Goals[Mathf.Clamp(Claimed, 0, Goals.Length - 1)];
        public bool CanClaim => Reached > Claimed && !Complete;

        public static Achievement From(Dictionary<string, object> d)
        {
            var a = new Achievement
            {
                Id = d.Str("id"), Category = d.Str("category"), Name = d.Str("name"), Text = d.Str("text"), Title = d.Str("title"),
                Progress = d.Int("progress"), Reached = d.Int("reached"), Claimed = d.Int("claimed"), Reward = DailyReward.From(d.Obj("reward")),
            };
            var goals = d.Arr("goals");
            if (goals != null && goals.Count == 3) a.Goals = goals.Select(g => g is double v ? Mathf.Max(1, (int)v) : 1).ToArray();
            return a;
        }
    }

    /// Reward bundle of a daily task or the all-tasks bonus.
    /// One day of a 登入獎勵 event: currencies plus an optional weapon and gear pieces.
    public sealed class LoginReward
    {
        public DailyReward Base;
        public string Weapon = "", Gear = "";
        public int Rarity = 1, GearRarity = 1, GearCount = 1;

        public static LoginReward From(Dictionary<string, object> d) => new LoginReward
        {
            Base = DailyReward.From(d), Weapon = d.Str("weapon"), Rarity = Mathf.Clamp(d.Int("rarity", 1), 0, 4),
            Gear = d.Str("gear"), GearRarity = Mathf.Clamp(d.Int("gear_rarity", 1), 0, 4), GearCount = Mathf.Max(1, d.Int("gear_count", 1)),
        };

        public string WeaponText => RiftWard.Sim.Defs.Weapons.ContainsKey(Weapon) ? RiftWard.Client.Gear.WeaponName(Weapon, Rarity) : "";
        public string GearText => Gear == "" ? "" :
            $"{RiftWard.Client.Gear.RarityNames[GearRarity]}{(RiftWard.Client.Gear.SlotNames.TryGetValue(Gear, out var slot) ? slot : "裝備")} ×{GearCount}";
    }

    public sealed class LoginEvent
    {
        public string Title = "";
        public List<LoginReward> Rewards = new List<LoginReward>();
        public int Claimed;
        public bool CanClaim;
        public long EndsAt;
        public bool Done => Claimed >= Rewards.Count;

        public static LoginEvent From(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var e = new LoginEvent { Title = d.Str("title"), Claimed = d.Int("claimed"), CanClaim = d.Bool("can_claim"),
                EndsAt = (long)(d.TryGetValue("ends_at", out var end) && end is double n ? n : 0) };
            foreach (var o in d.Arr("rewards") ?? new List<object>()) if (o is Dictionary<string, object> r) e.Rewards.Add(LoginReward.From(r));
            return e.Rewards.Count > 0 ? e : null;
        }
    }

    public struct DailyReward
    {
        public int Gold, Keys, WeaponKeys, Shards;

        public static DailyReward From(Dictionary<string, object> d) =>
            d == null ? default : new DailyReward { Gold = d.Int("gold"), Keys = d.Int("keys"), WeaponKeys = d.Int("weapon_keys"), Shards = d.Int("shards") };
    }

    public sealed class DailyTask
    {
        public string Id = "", Text = "";
        public int Goal = 1, Progress;
        public bool Done, Claimed;
        public DailyReward Reward;
    }

    /// Today's three daily missions as reported by the account service.
    public sealed class DailyMissions
    {
        public readonly List<DailyTask> Tasks = new List<DailyTask>();
        public DailyReward Bonus;
        public bool BonusReady, BonusClaimed;
        /// Seconds until the next reset when the profile was fetched.
        public int ResetsIn;
        public float FetchedAt;

        /// Rewards waiting to be collected (finished tasks plus a ready bonus).
        public int Claimable => Tasks.Count(t => t.Done && !t.Claimed) + (BonusReady && !BonusClaimed ? 1 : 0);
        public int ClaimedCount => Tasks.Count(t => t.Claimed);

        public static DailyMissions From(Dictionary<string, object> d)
        {
            var m = new DailyMissions { FetchedAt = Time.realtimeSinceStartup };
            if (d == null) return m;
            m.ResetsIn = d.Int("resets_in");
            foreach (var o in d.Arr("tasks") ?? new List<object>())
                if (o is Dictionary<string, object> t)
                    m.Tasks.Add(new DailyTask
                    {
                        Id = t.Str("id"), Text = t.Str("text"), Goal = Mathf.Max(1, t.Int("goal", 1)), Progress = t.Int("progress"),
                        Done = t.Bool("done"), Claimed = t.Bool("claimed"), Reward = DailyReward.From(t.Obj("reward")),
                    });
            var bonus = d.Obj("bonus");
            if (bonus != null) { m.Bonus = DailyReward.From(bonus.Obj("reward")); m.BonusReady = bonus.Bool("ready"); m.BonusClaimed = bonus.Bool("claimed"); }
            return m;
        }
    }

    /// JSON-over-HTTP client for the account service (port 24771).
    public sealed class AccountApi
    {
        public string Host = "123.240.75.135";
        public int Port = 24771;
        public string Token = "";
        /// Set on the dedicated server: requests authenticate with X-Server-Key instead of a player token.
        public string ServerKey = "";
        readonly MonoBehaviour runner;

        public AccountApi(MonoBehaviour runner)
        {
            this.runner = runner;
            LoadServerConfig();
        }

        public string BaseUrl => $"http://{Host}:{Port}";
        /// From server.cfg: the game server's TCP port and the server's display name (測試服 / 正式服).
        public int GamePort = Protocol.DefaultPort;
        public string Channel = "";

        /// server.cfg next to the executable: [server] address=1.2.3.4 (same file the Godot build used).
        void LoadServerConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(Application.dataPath);
                string path = Path.Combine(dir ?? ".", "server.cfg");
                if (!File.Exists(path)) return;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim().Trim('"');
                    if (key == "address" && System.Net.IPAddress.TryParse(value, out _)) Host = value;
                    else if (key == "port" && int.TryParse(value, out int p) && p > 0 && p < 65536) Port = p;
                    else if (key == "game_port" && int.TryParse(value, out int g) && g > 0 && g < 65536) GamePort = g;
                    else if (key == "name" && value.Length <= 12) Channel = value;
                }
            }
            catch (Exception e) { Debug.LogWarning("server.cfg: " + e.Message); }
        }

        public void Post(string path, Dictionary<string, object> data, Action<Dictionary<string, object>> done) =>
            runner.StartCoroutine(PostRoutine(path, data, done));

        IEnumerator PostRoutine(string path, Dictionary<string, object> data, Action<Dictionary<string, object>> done)
        {
            var body = Encoding.UTF8.GetBytes(Json.Write(data ?? new Dictionary<string, object>()));
            using var req = new UnityWebRequest(BaseUrl + path, "POST")
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 8,
            };
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(ServerKey)) req.SetRequestHeader("X-Server-Key", ServerKey);
            else if (!string.IsNullOrEmpty(Token)) req.SetRequestHeader("Authorization", "Bearer " + Token);
            yield return req.SendWebRequest();
            Dictionary<string, object> result;
            if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.DataProcessingError)
                result = new Dictionary<string, object> { ["ok"] = false, ["error"] = "帳號服務無回應，請確認伺服器已啟動" };
            else
            {
                try { result = Json.Parse(req.downloadHandler.text) as Dictionary<string, object>; }
                catch { result = null; }
                result ??= new Dictionary<string, object> { ["ok"] = false, ["error"] = "伺服器回覆格式錯誤" };
                result["http_status"] = (double)req.responseCode;
            }
            done?.Invoke(result);
        }
    }

    /// Remembers the login token, encrypted with a per-machine key.
    public static class SessionStore
    {
        static string PathFor => System.IO.Path.Combine(Application.persistentDataPath, "login-session.bin");

        static byte[] Key() => SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(SystemInfo.deviceUniqueIdentifier + "RiftWard/session/v2"));

        public static void Save(string token, string host)
        {
            try
            {
                using var aes = Aes.Create();
                aes.Key = Key();
                aes.GenerateIV();
                var plain = Encoding.UTF8.GetBytes(host + "\n" + token);
                var cipher = aes.CreateEncryptor().TransformFinalBlock(plain, 0, plain.Length);
                var blob = new byte[aes.IV.Length + cipher.Length];
                Buffer.BlockCopy(aes.IV, 0, blob, 0, aes.IV.Length);
                Buffer.BlockCopy(cipher, 0, blob, aes.IV.Length, cipher.Length);
                File.WriteAllBytes(PathFor, blob);
            }
            catch (Exception e) { Debug.LogWarning("session save: " + e.Message); }
        }

        public static string Load(string host)
        {
            try
            {
                if (!File.Exists(PathFor)) return null;
                var blob = File.ReadAllBytes(PathFor);
                using var aes = Aes.Create();
                aes.Key = Key();
                var iv = new byte[16];
                Buffer.BlockCopy(blob, 0, iv, 0, 16);
                aes.IV = iv;
                var plain = Encoding.UTF8.GetString(aes.CreateDecryptor().TransformFinalBlock(blob, 16, blob.Length - 16));
                var parts = plain.Split('\n');
                return parts.Length == 2 && parts[0] == host && parts[1].Length >= 20 && parts[1].Length <= 128 ? parts[1] : null;
            }
            catch { return null; }
        }

        public static void Forget()
        {
            try { if (File.Exists(PathFor)) File.Delete(PathFor); } catch { }
        }
    }
}
