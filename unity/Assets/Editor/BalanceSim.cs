using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RiftWard.Sim;
using UnityEditor;
using UnityEngine;

/// Headless balance check: the 掛機代打 AI plays whole missions (no rendering) for a few gear profiles on every
/// difficulty and logs the clear time, wins and the damage taken. Run with
///   Unity -batchmode -projectPath unity -executeMethod BalanceSim.Run -quit
/// The AI never dodges, dashes, throws grenades or aims for the head, so a real player clears faster than this.
public static class BalanceSim
{
    struct Profile
    {
        public string Name, Weapon;
        public int Rarity, Level, Over;
        public float Hp, Damage, Speed, Reload, Crit, HpPct, Def;
    }

    static readonly Profile[] Profiles =
    {
        new Profile { Name = "新手（普通步槍 +0，無裝備）", Weapon = "rifle", Rarity = 1 },
        new Profile { Name = "一般（稀世步槍 +5，稀世裝備 +3）", Weapon = "rifle", Rarity = 2, Level = 5, Hp = 90, Damage = 12, Speed = 6, Reload = 4, Crit = 6, HpPct = 9, Def = 8 },
        new Profile { Name = "中前期（傳說步槍 +7，傳說裝備 +5）", Weapon = "rifle", Rarity = 3, Level = 7, Hp = 200, Damage = 28, Speed = 13, Reload = 12, Crit = 14, HpPct = 18, Def = 18 },
        new Profile { Name = "後期（神話步槍 +10 超載3，神話裝備 +5 突破3）", Weapon = "rifle", Rarity = 4, Level = 10, Over = 3, Hp = 380, Damage = 50, Speed = 20, Reload = 20, Crit = 22, HpPct = 30, Def = 30 },
    };

    const int Runs = 3, Region = 1;
    const float Dt = 0.02f, Cap = 1800f;

    public static void Run()
    {
        var report = new StringBuilder();
        report.AppendLine($"關卡 {Defs.Regions[Region].Name}（第 {Region + 1} 關），單人，每格 {Runs} 場；時間為通關場次平均");
        foreach (var p in Profiles)
        {
            report.AppendLine($"\n{p.Name}");
            for (int tier = 0; tier < Defs.Difficulties.Length; tier++)
            {
                var times = new List<float>(); int wins = 0; float taken = 0; int waveSum = 0;
                for (int run = 0; run < Runs; run++)
                {
                    var (state, time, wave, dmg) = Play(p, tier, 1000 + run * 77 + tier);
                    if (state == MissionState.Victory) { wins++; times.Add(time); }
                    taken += dmg; waveSum += wave;
                }
                string avg = times.Count > 0 ? $"{times.Average():0} 秒（{times.Average() / 60f:0.0} 分）" : "—";
                report.AppendLine($"  {Defs.Difficulties[tier].Name}: 勝 {wins}/{Runs}，通關 {avg}，平均到第 {waveSum / (float)Runs:0.0} 波，每場受傷 {taken / Runs:0}");
            }
        }
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "build", "balance-report.txt"));
        File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
        Debug.Log("BALANCE REPORT\n" + report);
    }

    /// How hard the hits land: the largest single hit and the most damage inside any one second, as a share of the
    /// player's max life at that moment, on 噩夢 and 地獄 with the live numbers and with a proposal.
    public static void RunBurst()
    {
        var live = (Defs.Difficulties[2], Defs.Difficulties[3]);
        var sets = new (string name, DifficultyDef nightmare, DifficultyDef inferno)[]
        {
            ("現行", live.Item1, live.Item2),
            ("A", With(live.Item1, 4f, 2.2f, 2), With(live.Item2, 6f, 3f, 2)),
            ("B", With(live.Item1, 5f, 2.5f, 2), With(live.Item2, 7.5f, 3.2f, 2)),
            ("C", With(live.Item1, 6f, 2.8f, 1), With(live.Item2, 9f, 3.5f, 2)),
        };
        var subjects = new[]
        {
            Profiles[2],
            new Profile { Name = "中後期（神話步槍 +7，傳說～神話裝備 +5）", Weapon = "rifle", Rarity = 4, Level = 7, Hp = 280, Damage = 38, Speed = 16, Reload = 16, Crit = 18, HpPct = 24, Def = 24 },
            Profiles[3],
        };
        var report = new StringBuilder();
        report.AppendLine($"關卡 {Defs.Regions[Region].Name}，單人，每格 5 場。單發 / 1 秒內 = 占當下最大生命的百分比（取 5 場中最高）");
        foreach (var set in sets)
        {
            Defs.Difficulties[2] = set.nightmare; Defs.Difficulties[3] = set.inferno;
            report.AppendLine($"\n== {set.name}：噩夢 血×{set.nightmare.Hp} 傷×{set.nightmare.Damage} +{set.nightmare.ExtraEnemies}，地獄 血×{set.inferno.Hp} 傷×{set.inferno.Damage} +{set.inferno.ExtraEnemies}");
            foreach (var p in subjects)
            {
                report.AppendLine($"  {p.Name}");
                for (int tier = 2; tier <= 3; tier++)
                {
                    float hit = 0, second = 0; int wins = 0, big = 0; var times = new List<float>(); float waves = 0;
                    for (int run = 0; run < 5; run++)
                    {
                        var r = PlayFull(p, tier, 5000 + run * 31 + tier);
                        hit = Mathf.Max(hit, r.maxHit); second = Mathf.Max(second, r.maxSecond); big += r.bigHits; waves += r.wave;
                        if (r.state == MissionState.Victory) { wins++; times.Add(r.time); }
                    }
                    string t = times.Count > 0 ? $"{times.Average() / 60f:0.0} 分" : "—";
                    report.AppendLine($"    {Defs.Difficulties[tier].Name}: 勝 {wins}/5，通關 {t}，平均到第 {waves / 5f:0.0} 波｜最大單發 {hit:P0}，1 秒內最多 {second:P0}，單發 ≥30% 共 {big} 次");
                }
            }
        }
        Defs.Difficulties[2] = live.Item1; Defs.Difficulties[3] = live.Item2;
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "build", "balance-burst.txt"));
        File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
        Debug.Log("BURST REPORT\n" + report);
    }

    /// 電漿砲: plays 困難 with the 中前期 profile and, at waves 5/10/15/20, compares the weakest regular enemy's life
    /// with what a maxed plasma salvo deals at its centre (4 damage picks of each rarity, the profile's damage bonus
    /// plus three rare 超載彈芯), with the live formula and the proposal.
    public static void RunPlasma()
    {
        var p = Profiles[2];
        var sim = new GameSim(4242);
        var lo = new Loadout { Owned = new List<string> { "pistol", p.Weapon }, Equipped = new List<string> { p.Weapon, "pistol" } };
        lo.Rarity[p.Weapon] = p.Rarity;
        lo.Levels[p.Weapon] = new Dictionary<string, int> { ["damage"] = p.Level, ["mag"] = p.Level, ["reload"] = p.Level };
        sim.AddActor(1, lo);
        sim.StartMission(Region, 1);
        sim.ApplyProfileBonus(1, p.Hp, p.Damage, p.Speed, p.Reload, p.Crit, p.HpPct, p.Def);
        var a = sim.Actors[1];
        a.Hp = a.MaxHp = 99999f;   // only the enemy life curve matters here
        var rng = new System.Random(1);
        var report = new StringBuilder("困難，中前期，第 2 關。中心傷害 / 該波最弱一般敵人的血量（≥100% = 一砲秒殺）\n");
        float gearDamage = 1f + p.Damage / 100f, buffDamage = 1f + 3 * 0.2f * 1.6f;
        int seq = 0, logged = 0;
        int[] marks = { 5, 10, 15, 20 };
        while (sim.State == MissionState.Playing && logged < marks.Length && sim.Elapsed < Cap)
        {
            if (sim.BuffPending) { if (sim.BuffOffers.TryGetValue(1, out var picks)) sim.ChooseBuff(1, picks[rng.Next(picks.Length)]); else sim.Tick(Dt); continue; }
            a.Hp = a.MaxHp;
            sim.Inputs[1] = sim.BotInput(a, ++seq);
            sim.Tick(Dt);
            if (sim.Wave == marks[logged] && sim.Enemies.Count > 0)
            {
                float weakest = sim.Enemies.Values.Where(e => !e.Boss && !e.Elite && e.Kind == Defs.KindGrunt).Select(e => e.MaxHp).DefaultIfEmpty(0).Min();
                float scale = 1f + 0.15f * Region, mult = gearDamage * buffDamage;
                report.AppendLine($"  第 {sim.Wave} 波（等級 {sim.Level}）一般敵人血量 {weakest:0}");
                foreach (var (name, m) in new[] { ("普通", 1f), ("稀有", 1.6f), ("傳說", 2.5f) })
                {
                    float live = Defs.PlasmaDamage * scale * (1f + 4 * 0.4f * m) * mult;
                    float proposal = 50f * scale * (1f + 4 * 0.25f * m) * (1f + 0.5f * (mult - 1f));
                    report.AppendLine($"    {name}強化×4：現行 {live:0}（{live / weakest:P0}） → 提案 {proposal:0}（{proposal / weakest:P0}）");
                }
                logged++;
            }
        }
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "build", "balance-plasma.txt"));
        File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
    }

    static DifficultyDef With(DifficultyDef d, float hp, float damage, int extra) =>
        new DifficultyDef(d.Name, d.Code, d.Summary, hp, damage, extra, d.Gold, d.Xp, d.Accent);

    static (MissionState, float, int, float) Play(Profile p, int tier, int seed)
    {
        var r = PlayFull(p, tier, seed);
        return (r.state, r.time, r.wave, r.taken);
    }

    static (MissionState state, float time, int wave, float taken, float maxHit, float maxSecond, int bigHits) PlayFull(Profile p, int tier, int seed)
    {
        var sim = new GameSim(seed);
        var lo = new Loadout { Owned = new List<string> { "pistol", p.Weapon }, Equipped = new List<string> { p.Weapon, "pistol" } };
        lo.Rarity[p.Weapon] = p.Rarity;
        lo.Levels[p.Weapon] = new Dictionary<string, int> { ["damage"] = p.Level, ["mag"] = p.Level, ["reload"] = p.Level, ["over"] = p.Over };
        sim.AddActor(1, lo);
        sim.StartMission(Region, tier);
        sim.ApplyProfileBonus(1, p.Hp, p.Damage, p.Speed, p.Reload, p.Crit, p.HpPct, p.Def);
        var rng = new System.Random(seed);
        float taken = 0, maxHit = 0, maxSecond = 0, windowSum = 0;
        int seq = 0, big = 0;
        var window = new Queue<float>();
        while (sim.State == MissionState.Playing && sim.Elapsed < Cap)
        {
            var a = sim.Actors[1];
            if (sim.BuffPending)
            {
                if (sim.BuffOffers.TryGetValue(1, out var picks) && picks.Length > 0) sim.ChooseBuff(1, picks[rng.Next(picks.Length)]);
                else sim.Tick(Dt);
                continue;
            }
            float before = a.Hp, max = a.MaxHp;
            bool wasAlive = a.Alive;
            sim.Inputs[1] = sim.BotInput(a, ++seq);
            sim.Tick(Dt);
            float lost = wasAlive && a.Hp < before ? before - Mathf.Max(0, a.Hp) : 0;
            taken += lost;
            window.Enqueue(lost); windowSum += lost;
            if (window.Count > (int)(1f / Dt)) windowSum -= window.Dequeue();
            if (lost > 0) { maxHit = Mathf.Max(maxHit, lost / max); if (lost / max >= 0.3f) big++; }
            maxSecond = Mathf.Max(maxSecond, windowSum / max);
        }
        return (sim.State, sim.Elapsed, sim.Wave, taken, maxHit, maxSecond, big);
    }
}
