using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RiftWard.Sim;
using UnityEditor;
using UnityEngine;

/// Skill (gadget) strength per level on every difficulty: the 中前期 AI plays a whole mission (kept alive, so every
/// level is measured over all 20 waves) owning one gadget at a fixed level from the start, and the damage that gadget
/// landed (overkill excluded) per second is recorded. Level-up picks during the run only take neutral buffs, so the
/// gadget is the only thing being measured. Run with
///   Unity -batchmode -projectPath unity -executeMethod SkillRanking.Run -quit
public static class SkillRanking
{
    const float Dt = 0.02f, Cap = 900f;
    const int Region = 1;
    static readonly int[] Neutral = { 2, 3, 4, 19, 20 };   // 動能靴, 生命強化, 修復協議, 磁力場, 複合裝甲

    /// Each step is one level-up pick (普通 rarity) put into the gadget, in the order a player would usually take them.
    static readonly (string name, int source, int[] ladder)[] Skills =
    {
        ("僚機無人機", Defs.SrcDrone, Ladder(new[] { Defs.BuffDrone }, (Defs.BuffDrone, Defs.MaxDrones - 1), (Defs.BuffDroneDamage, 5), (Defs.BuffDroneRate, 4))),
        ("肩載電漿砲", Defs.SrcPlasma, Ladder(new[] { Defs.BuffPlasma }, (Defs.BuffPlasma, 4), (Defs.BuffPlasmaRate, 4))),
        ("旋轉刃盾", Defs.SrcOrbit, Enumerable.Repeat(Defs.BuffOrbit, Defs.MaxGadgetLevel).ToArray()),
        ("感應地雷", Defs.SrcMine, Enumerable.Repeat(Defs.BuffMine, Defs.MaxGadgetLevel).ToArray()),
        ("追蹤飛彈", Defs.SrcMissile, Enumerable.Repeat(Defs.BuffMissile, Defs.MaxGadgetLevel).ToArray()),
        ("閃電鏈", Defs.SrcLightning, Enumerable.Repeat(Defs.BuffLightning, Defs.MaxGadgetLevel).ToArray()),
        ("爆裂彈頭", Defs.SrcBurst, Enumerable.Repeat(9, 4).ToArray()),
    };

    /// The first picks, then the upgrade picks taken in turn (one of each while they last).
    static int[] Ladder(int[] first, params (int code, int count)[] ups)
    {
        var list = new List<int>(first);
        var left = ups.Select(u => u.count).ToArray();
        while (left.Any(n => n > 0))
            for (int i = 0; i < ups.Length; i++)
                if (left[i]-- > 0) list.Add(ups[i].code);
        return list.ToArray();
    }

    /// Same table with the AI holding its ground (it still aims and fires), so enemies close in: the case the
    /// close-range skills (刃盾, 地雷) are made for.
    public static void RunStanding() { standing = true; Run(); standing = false; }
    static bool standing;

    public static void Run()
    {
        var rows = new List<(string skill, int level, float[] dps)>();
        foreach (var (name, source, ladder) in Skills)
            for (int level = 1; level <= ladder.Length; level++)
            {
                var dps = new float[Defs.Difficulties.Length];
                for (int tier = 0; tier < dps.Length; tier++) dps[tier] = Play(ladder.Take(level).ToArray(), source, tier, 900 + level);
                rows.Add((name, level, dps));
            }

        var md = new StringBuilder();
        md.AppendLine($"技能強度（第 {Region + 1} 關，中前期玩家，單人，{(standing ? "站定不動、讓敵人靠近" : "AI 保持 7–18 公尺距離")}；每秒實際造成的傷害，不含溢出）");
        md.AppendLine();
        md.Append("| 技能 | 等級 |");
        foreach (var d in Defs.Difficulties) md.Append($" {d.Name} |");
        md.AppendLine(" 綜合排名 |");
        md.Append("|---|---|");
        foreach (var _ in Defs.Difficulties) md.Append("---|");
        md.AppendLine("---|");
        var order = rows.OrderByDescending(r => r.dps.Average()).ToList();
        foreach (var r in rows)
        {
            md.Append($"| {r.skill} | Lv{r.level} |");
            foreach (var v in r.dps) md.Append($" {v:0} |");
            md.AppendLine($" {order.IndexOf(r) + 1} |");
        }
        md.AppendLine();
        md.AppendLine("滿級比較：");
        foreach (var top in rows.GroupBy(r => r.skill).Select(g => g.Last()).OrderByDescending(r => r.dps.Average()))
            md.AppendLine($"- {top.skill} Lv{top.level}：" + string.Join("、", Defs.Difficulties.Select((d, i) => $"{d.Name} {top.dps[i]:0}")));
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "build", standing ? "skill-ranking-standing.md" : "skill-ranking.md"));
        File.WriteAllText(path, md.ToString(), new UTF8Encoding(false));
        Debug.Log("SKILL RANKING\n" + md);
    }

    static float Play(int[] picks, int source, int tier, int seed)
    {
        var sim = new GameSim(seed);
        var lo = new Loadout { Owned = new List<string> { "pistol", "rifle" }, Equipped = new List<string> { "rifle", "pistol" } };
        lo.Rarity["rifle"] = 3;
        lo.Levels["rifle"] = new Dictionary<string, int> { ["damage"] = 7, ["mag"] = 7, ["reload"] = 7 };
        sim.AddActor(1, lo);
        sim.StartMission(Region, tier);
        sim.ApplyProfileBonus(1, 200, 28, 13, 12, 14, 18, 18);
        foreach (var code in picks) sim.ApplyBuff(1, code);
        var a = sim.Actors[1];
        int seq = 0;
        while (sim.State == MissionState.Playing && sim.Elapsed < Cap)
        {
            if (sim.BuffPending)
            {
                if (sim.BuffOffers.TryGetValue(1, out var offer) && offer.Length > 0)
                {
                    int pick = offer.FirstOrDefault(c => Neutral.Contains(Defs.BuffIndex(c)));
                    if (pick == 0 && !Neutral.Contains(Defs.BuffIndex(offer[0]))) pick = offer[0];
                    // No neutral pick on offer: take one anyway but undo nothing; it is rare and shared by all rows.
                    sim.ChooseBuff(1, pick == 0 ? offer[0] : pick);
                }
                else sim.Tick(Dt);
                continue;
            }
            a.Hp = a.MaxHp;   // measure the gadget over the whole mission, not the AI's survival
            var input = sim.BotInput(a, ++seq);
            if (standing) input.Move = Vector2.zero;
            sim.Inputs[1] = input;
            sim.Tick(Dt);
        }
        return sim.Elapsed > 0 ? a.SkillDamage[source] / sim.Elapsed : 0;
    }
}
