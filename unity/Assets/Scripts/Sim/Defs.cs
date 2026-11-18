using System.Collections.Generic;
using UnityEngine;

namespace RiftWard.Sim
{
    /// Static game data shared by server and client. Values mirror the Godot 0.4.2 build.
    public static class Defs
    {
        /// 傳奇+ weapon perk: name and description for a weapon at a rarity (null below 傳奇). 神話 doubles the effect.
        public static (string name, string text)? Perk(string weapon, int rarity)
        {
            if (rarity < 3) return null;
            bool myth = rarity >= 4;
            switch (weapon)
            {
                case "rifle": return ("致命節奏", $"爆頭後，接下來 {(myth ? 2 : 1)} 發必定暴擊");
                case "shotgun": return ("近身汲取", $"擊殺敵人回復 {(myth ? 12 : 6)} 點護盾");
                case "sniper": return ("弱點洞察", $"爆頭傷害 +{(myth ? 80 : 40)}%");
                case "pistol": return ("連擊快槍", $"擊殺後 2 秒內射速 +{(myth ? 70 : 40)}%");
                default: return null;
            }
        }

        /// Account XP for a mission; mirrors mission_xp() in server/service.py (higher difficulty multiplies it).
        public static int MissionXp(int kills, int wave, bool victory, int difficulty = 0) =>
            (int)System.Math.Round((Mathf.Max(0, kills) + Mathf.Max(0, wave) * 5 + (victory ? 100 : 0)) * (double)Difficulty(difficulty).Xp, System.MidpointRounding.ToEven);

        /// Mission difficulty tiers; mirrors DIFFICULTY_* in server/service.py. A tier opens for a region once the
        /// tier below it has been cleared there.
        public static readonly DifficultyDef[] Difficulties =
        {
            new DifficultyDef("普通", "NORMAL", "標準的帝國部隊。", 1f, 1f, 0, 1f, 1f, new Color32(0x9b, 0xd3, 0x6a, 255)),
            new DifficultyDef("困難", "HARD", "敵人血量 ×1.6、傷害 ×1.3，每波多 2 名。金幣 ×1.6、經驗 ×1.25。", 1.6f, 1.3f, 2, 1.6f, 1.25f, new Color32(0xf0, 0xb2, 0x4a, 255)),
            new DifficultyDef("噩夢", "NIGHTMARE", "敵人血量 ×2.4、傷害 ×1.7，每波多 4 名。金幣 ×2.4、經驗 ×1.5。", 2.4f, 1.7f, 4, 2.4f, 1.5f, new Color32(0xe5, 0x53, 0x4b, 255)),
        };

        public static DifficultyDef Difficulty(int tier) => Difficulties[Mathf.Clamp(tier, 0, Difficulties.Length - 1)];

        /// cleared[region] is the highest tier cleared there (-1 = none).
        public static bool DifficultyOpen(IReadOnlyList<int> cleared, int region, int tier) =>
            tier <= 0 || (cleared != null && region >= 0 && region < cleared.Count && cleared[region] >= tier - 1);
        /// XP needed to go from this account level to the next; mirrors level_for() in server/service.py.
        public static int AccountLevelNeed(int level) => 100 + 50 * level;

        public const string Version = "0.7.8-unity.1";

        // Player movement
        public const float EyeHeight = 1.65f;
        public const float BaseSpeed = 6.5f;
        public const float JumpVelocity = 7f;
        public const float Gravity = 20f;
        public const float DashDuration = 0.18f;
        public const float DashDistance = 4f;
        public const float DashCooldown = 4.5f;
        public const float FlySpeed = 7f;
        public const float FlyCeiling = 28f;
        public const float ArenaHalf = 21f;
        public const float LookLimit = Mathf.PI * 85f / 180f;

        // Combat
        public const float ShotRange = 65f;
        public const float HeadHeight = 1.65f;
        public const float HeadRadius = 0.3f;
        public const float SkillCooldown = 12f;
        public const float GrenadeRadius = 4.5f;
        public const float GrenadeDamage = 130f;
        public const float ReviveTime = 3f;
        public const float PickupChance = 0.16f;
        public const int MaxEnemies = 34;
        public const int MaxBullets = 140;

        // Enemy fire
        public const float BulletSpeed = 7.5f;
        public const float BossBulletSpeed = 6f;
        public const float SniperBulletSpeed = 14f;
        public const float SniperWarning = 0.9f;
        public const float BulletRange = 24f;
        public const float BulletLife = 4.5f;
        public const float BulletHitRadius = 0.55f;

        // Progression
        public const int WavesPerMission = 20;
        public const float XpBase = 100f;
        public const float XpGrowth = 1.35f;
        public const float BuffTimeout = 30f;
        public const int MaxUpgradeLevel = 5;

        public static int XpForLevel(int level) => Mathf.RoundToInt(XpBase * Mathf.Pow(XpGrowth, level - 1));

        /// Cover walls: center x, center z, half x, half z, height. Block movement and enemy bullets.
        public static readonly float[][] Covers =
        {
            new[] { -6f, 4f, 2f, 0.35f, 1.8f }, new[] { 6f, 4f, 2f, 0.35f, 1.8f },
            new[] { -6f, -6f, 0.35f, 2f, 1.8f }, new[] { 6f, -6f, 0.35f, 2f, 1.8f },
            new[] { 0f, 10f, 2.5f, 0.35f, 1.8f }, new[] { -13f, -1f, 0.35f, 2.5f, 1.8f },
            new[] { 13f, -1f, 0.35f, 2.5f, 1.8f },
        };

        public static readonly RegionDef[] Regions =
        {
            new RegionDef("翠風草原", "你的運輸艦墜落在這顆陌生星球的草原上。\n帝國追擊部隊已經降落，先守住墜落點。", 4, new Color32(0x9b, 0xd3, 0x6a, 255)),
            new RegionDef("熔核工廠", "帝國在星球地底建造了熔核工廠，\n摧毀它，切斷入侵艦隊的能源。", 5, new Color32(0xff, 0xad, 0x66, 255)),
            new RegionDef("帝國裂隙", "裂隙的另一端就是帝國母星。\n擊敗守衛，穿越裂隙發動反擊。", 6, new Color32(0xb4, 0x92, 0xff, 255)),
        };

        public static readonly string[] WeaponOrder = { "pistol", "rifle", "shotgun", "sniper" };

        public static readonly Dictionary<string, WeaponDef> Weapons = new Dictionary<string, WeaponDef>
        {
            ["pistol"] = new WeaponDef("pistol", "P-9 制式手槍", "手槍", "初始武器。彈匣小、換彈快，穩定可靠。", 28f, 0.24f, 12, 1.1f, 1, 0f, 2f, 0.85f, 0, 0.022f, 0.006f, 1.0f),
            ["rifle"] = new WeaponDef("rifle", "AR-27 突擊步槍", "步槍", "全自動高射速，適合壓制大群敵人。", 17f, 0.1f, 30, 1.8f, 1, 0.012f, 2f, 0.75f, 400, 0.011f, 0.007f, 1.3f),
            ["shotgun"] = new WeaponDef("shotgun", "SG-8 霰彈槍", "霰彈", "一次射出 7 顆彈丸，近距離威力驚人。", 11f, 0.8f, 6, 2.2f, 7, 0.07f, 1.5f, 0.85f, 600, 0.065f, 0.015f, 1.4f),
            ["sniper"] = new WeaponDef("sniper", "LR-50 狙擊槍", "狙擊", "單發高傷害，爆頭 3 倍。右鍵開鏡。", 95f, 1.0f, 5, 2.4f, 1, 0f, 3f, 0.35f, 800, 0.09f, 0.01f, 1.6f),
        };

        public static readonly string[] UpgradeKeys = { "damage", "mag", "reload" };

        /// Upgrade price multiplier per rarity (劣質 .. 神話); mirrors RARITY_UPGRADE_MULT in server/service.py.
        public static readonly float[] RarityUpgradeMult = { 0.6f, 1.0f, 1.4f, 2.0f, 3.0f };

        public static int UpgradeCost(string weapon, int level, int rarity = 1) =>
            (int)System.Math.Round(120.0 * (level + 1) * Weapons[weapon].UpgradeFactor * RarityUpgradeMult[Mathf.Clamp(rarity, 0, RarityUpgradeMult.Length - 1)], System.MidpointRounding.ToEven);

        public static readonly KindDef[] Kinds =
        {
            new KindDef("突擊兵", new Color32(0xff, 0x88, 0x55, 255), 1.0f),
            new KindDef("衝鋒兵", new Color32(0xff, 0x4d, 0x6d, 255), 0.9f),
            new KindDef("狙擊兵", new Color32(0xb4, 0x7c, 0xff, 255), 1.0f),
            new KindDef("自爆蟲", new Color32(0xff, 0xd2, 0x3f, 255), 0.75f),
            new KindDef("護盾兵", new Color32(0x5a, 0xa9, 0xff, 255), 1.0f),
            new KindDef("帝國重裝兵", new Color32(0xff, 0x55, 0x33, 255), 2.2f),
            new KindDef("帝國指揮官", new Color32(0xff, 0xb3, 0x47, 255), 2.6f),
        };

        public const int KindGrunt = 0, KindCharger = 1, KindSniper = 2, KindBomber = 3, KindShield = 4, KindHeavyBoss = 5, KindCommander = 6;

        public static readonly BuffDef[] Buffs =
        {
            new BuffDef("超載彈芯", "傷害 +{0}%", 20),
            new BuffDef("急速扳機", "射速 +{0}%", 15),
            new BuffDef("動能靴", "移動速度 +{0}%", 12),
            new BuffDef("強化護盾", "護盾上限 +{0}，並立即恢復", 30),
            new BuffDef("修復協議", "每波開始恢復 {0} 護盾", 15),
            new BuffDef("弱點分析", "{0}% 機率造成雙倍傷害", 20),
            new BuffDef("擴充彈匣", "彈匣 +{0} 發，換彈加快 15%", 4),
            new BuffDef("戰術冷卻", "手榴彈冷卻 -{0}%", 20),
            new BuffDef("吸能模組", "每次擊殺恢復 {0} 護盾", 4),
            new BuffDef("爆裂彈頭", "擊殺時爆炸，造成 {0} 範圍傷害", 35),
            new BuffDef("僚機無人機", "無人機 +{0} 架，自動射擊附近敵人", 1),
            new BuffDef("無人機火控", "無人機傷害 +{0}%", 35),
            new BuffDef("無人機超頻", "無人機攻擊冷卻 -{0}%", 15),
            new BuffDef("肩載電漿砲", "電漿砲定時轟擊敵群；已裝備則傷害 +{0}%", 40),
            new BuffDef("電漿冷卻", "電漿砲冷卻 -{0}%", 15),
            new BuffDef("旋轉刃盾", "環繞刃片 Lv +{0}：刃片數與傷害提升", 1),
            new BuffDef("感應地雷", "定時在腳下佈雷 Lv +{0}", 1),
            new BuffDef("追蹤飛彈", "定時發射追蹤飛彈 Lv +{0}", 1),
            new BuffDef("閃電鏈", "定時釋放連鎖閃電 Lv +{0}", 1),
            new BuffDef("磁力場", "經驗晶體吸收範圍 +{0}%", 40),
            new BuffDef("複合裝甲", "受到的傷害 -{0}%", 8),
        };

        public const int BuffDrone = 10, BuffDroneDamage = 11, BuffDroneRate = 12, BuffPlasma = 13, BuffPlasmaRate = 14;
        /// Weapon rarity (劣質, 普通, 稀世, 傳奇, 神話) scales its damage.
        public static readonly float[] WeaponRarityMult = { 0.9f, 1f, 1.15f, 1.35f, 1.6f };

        public const int BuffOrbit = 15, BuffMine = 16, BuffMissile = 17, BuffLightning = 18, BuffMagnet = 19, BuffArmor = 20;
        public const float MaxArmor = 0.6f;
        public const int MaxGadgetLevel = 5;
        public const float BaseMagnet = 2.5f, EliteEvery = 3;

        /// Buffs whose value is a count/level: rare tiers grant +2 instead of scaling a percentage.
        public static bool IsLevelBuff(int index) => index == BuffDrone || index == BuffOrbit || index == BuffMine || index == BuffMissile || index == BuffLightning;

        /// Orbiting blade i of n around its owner (shared by sim and renderer).
        public static Vector3 BladeOffset(int i, int n, float time)
        {
            float angle = time * 3.2f + i * Mathf.PI * 2f / Mathf.Max(1, n);
            return new Vector3(Mathf.Cos(angle) * 2.4f, 1.0f, Mathf.Sin(angle) * 2.4f);
        }

        public static int BladeCount(int level) => level <= 0 ? 0 : Mathf.Min(5, 1 + level);
        public const int MaxDrones = 4;
        public const float DroneInterval = 1.1f, DroneDamage = 14f, DroneRange = 24f;
        public const float PlasmaInterval = 6f, PlasmaDamage = 70f, PlasmaRadius = 4.2f, PlasmaRange = 30f;

        /// Buff codes pack the buff index and its rarity tier: code = index + 100 * tier.
        public static int BuffIndex(int code) => code % 100;
        public static int BuffTier(int code) => Mathf.Clamp(code / 100, 0, 2);

        /// Where drone i of n orbits its owner at a given time (shared by sim and renderer).
        public static Vector3 DroneOffset(int i, int n, float time)
        {
            float angle = time * 1.6f + i * Mathf.PI * 2f / Mathf.Max(1, n);
            return new Vector3(Mathf.Cos(angle) * 1.1f, 2.3f + Mathf.Sin(time * 3f + i) * 0.12f, Mathf.Sin(angle) * 1.1f);
        }

        public static readonly string[] RarityNames = { "普通", "稀有", "傳說" };
        public static readonly float[] RarityMultiplier = { 1f, 1.6f, 2.5f };
        public static readonly Color32[] RarityColors = { new Color32(0xa7, 0xa9, 0xac, 255), new Color32(0x7f, 0xb2, 0xe5, 255), new Color32(0xf0, 0xb2, 0x4a, 255) };

        public static string BuffDescription(int code)
        {
            var buff = Buffs[BuffIndex(code)];
            int tier = BuffTier(code);
            float value = IsLevelBuff(BuffIndex(code)) ? (tier == 2 ? 2 : 1) : Mathf.RoundToInt(buff.BaseValue * RarityMultiplier[tier]);
            return string.Format(buff.Description, value);
        }
    }

    public readonly struct RegionDef
    {
        public readonly string Name, Story;
        public readonly int BaseEnemies;
        public readonly Color32 Accent;
        public RegionDef(string name, string story, int baseEnemies, Color32 accent) { Name = name; Story = story; BaseEnemies = baseEnemies; Accent = accent; }
    }

    public readonly struct DifficultyDef
    {
        public readonly string Name, Code, Summary;
        public readonly float Hp, Damage, Gold, Xp;
        public readonly int ExtraEnemies;
        public readonly Color32 Accent;
        public DifficultyDef(string name, string code, string summary, float hp, float damage, int extraEnemies, float gold, float xp, Color32 accent)
        {
            Name = name; Code = code; Summary = summary; Hp = hp; Damage = damage; ExtraEnemies = extraEnemies; Gold = gold; Xp = xp; Accent = accent;
        }
    }

    public sealed class WeaponDef
    {
        public readonly string Id, Name, Short, Description;
        public readonly float Damage, Interval, Reload, Spread, HeadMultiplier, Zoom, Recoil, Sway, UpgradeFactor;
        public readonly int Mag, Pellets, Price;

        public WeaponDef(string id, string name, string shortName, string description, float damage, float interval, int mag, float reload,
            int pellets, float spread, float head, float zoom, int price, float recoil, float sway, float upgradeFactor)
        {
            Id = id; Name = name; Short = shortName; Description = description; Damage = damage; Interval = interval; Mag = mag; Reload = reload;
            Pellets = pellets; Spread = spread; HeadMultiplier = head; Zoom = zoom; Price = price; Recoil = recoil; Sway = sway; UpgradeFactor = upgradeFactor;
        }

        /// Stats after armory upgrades (before in-mission buffs).
        public WeaponStats StatsAt(int damageLevel, int magLevel, int reloadLevel) => new WeaponStats
        {
            Damage = Damage * (1f + 0.12f * damageLevel),
            Interval = Interval,
            Mag = Mathf.CeilToInt(Mag * (1f + 0.15f * magLevel)),
            Reload = Reload * (1f - 0.08f * reloadLevel),
            Pellets = Pellets, Spread = Spread, HeadMultiplier = HeadMultiplier, Zoom = Zoom,
        };
    }

    public struct WeaponStats
    {
        public float Damage, Interval, Reload, Spread, HeadMultiplier, Zoom;
        public int Mag, Pellets;
    }

    public readonly struct KindDef
    {
        public readonly string Name;
        public readonly Color32 Color;
        public readonly float Scale;
        public KindDef(string name, Color32 color, float scale) { Name = name; Color = color; Scale = scale; }
    }

    public readonly struct BuffDef
    {
        public readonly string Name, Description;
        public readonly float BaseValue;
        public BuffDef(string name, string description, float baseValue) { Name = name; Description = description; BaseValue = baseValue; }
    }
}
