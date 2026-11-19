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
                case "shotgun": return ("近身汲取", $"擊殺敵人回復 {(myth ? 12 : 6)} 點生命");
                case "sniper": return ("弱點洞察", $"爆頭傷害 +{(myth ? 80 : 40)}%");
                case "pistol": return ("連擊快槍", $"擊殺後 2 秒內射速 +{(myth ? 70 : 40)}%");
                case "saber": return ("嗜能之刃", $"每砍中一名敵人回復 {(myth ? 4 : 2)} 點生命");
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
            new DifficultyDef("地獄", "INFERNO", "敵人血量 ×4.5、傷害 ×2.4，每波多 6 名。金幣 ×3.5、經驗 ×1.75。給頂尖裝備的挑戰。", 4.5f, 2.4f, 6, 3.5f, 1.75f, new Color32(0xff, 0x3b, 0x9a, 255)),
        };

        /// Gear keys and weapon keys (each) for the first clear of a region on a tier: 困難 5, 噩夢 10, 地獄 15.
        /// Mirrors FIRST_CLEAR_KEYS in the account service, which pays them.
        public static int FirstClearKeys(int tier) => tier >= 1 && tier <= 3 ? tier * 5 : 0;

        public static DifficultyDef Difficulty(int tier) => Difficulties[Mathf.Clamp(tier, 0, Difficulties.Length - 1)];

        /// cleared[region] is the highest tier cleared there (-1 = none).
        /// 無盡模式 rides on the difficulty value sent through lobby / deploy messages as a flag bit.
        public const int EndlessFlag = 0x100;
        public static int PackTier(int tier, bool endless) => tier | (endless ? EndlessFlag : 0);
        public static int TierOf(int packed) => packed & 0xff;
        public static bool IsEndless(int packed) => (packed & EndlessFlag) != 0;
        /// Endless opens in a region once it has been cleared on any difficulty.
        public static bool EndlessOpen(IReadOnlyList<int> cleared, int region) => region >= 0 && region < cleared.Count && cleared[region] >= 0;
        /// Endless scaling past the last regular wave: enemy health and damage keep climbing.
        public static float EndlessHp(int wave) => 1f + 0.07f * Mathf.Max(0, wave - WavesPerMission);
        public static float EndlessDamage(int wave) => 1f + 0.035f * Mathf.Max(0, wave - WavesPerMission);

        public static bool DifficultyOpen(IReadOnlyList<int> cleared, int region, int tier) =>
            tier <= 0 || (cleared != null && region >= 0 && region < cleared.Count && cleared[region] >= tier - 1);
        /// XP needed to go from this account level to the next; mirrors level_for() in server/service.py.
        public static int AccountLevelNeed(int level) => 100 + 50 * level;

        public const string Version = "0.7.28-unity.1";

        // Player movement
        public const float EyeHeight = 1.65f;
        public const float BaseSpeed = 6.5f;
        /// Crit chance cap from talents, gear and buffs combined.
        public const float MaxCritical = 0.5f;
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
        /// Weapon enhancement per stat; mirrors MAX_UPGRADE / SOFT_UPGRADE / OVERLOAD_* in server/service.py.
        /// Levels past SoftUpgradeLevel cost x1.5 per level more and count as half a step.
        public const int MaxUpgradeLevel = 10, SoftUpgradeLevel = 5;
        /// 超載: three extra tiers for 傳奇+ copies with every stat maxed, each +5% damage, fire rate and reload speed.
        public const int OverloadMax = 3, OverloadMinRarity = 3;
        /// 穿透 (mirrors PIERCE_* in the account service): 神話 guns only, a one-time unlock per copy. A piercing
        /// round carries on through up to PierceExtra more enemies, losing PierceFalloff of its damage each time.
        public const int PierceMinRarity = 4, PierceShards = 5, PierceGold = 20000;
        /// Extra enemies a round passes through and the damage it loses at each one. The SG-8 fires eight pellets
        /// that each pierce, so it goes through one enemy only and loses half.
        public static int PierceExtra(string weapon) => weapon == "shotgun" ? 1 : 2;
        public static float PierceFalloff(string weapon) => weapon == "shotgun" ? 0.5f : 0.3f;

        /// Shards for the first clear of a region on a tier (on top of the keys): 噩夢 2, 地獄 3.
        /// Mirrors FIRST_CLEAR_SHARDS in the account service.
        public static int FirstClearShards(int tier) => tier == 2 ? 2 : tier >= 3 ? 3 : 0;
        public const float OverloadStep = 0.05f;
        public static readonly int[] OverloadShards = { 1, 2, 4 };
        public static readonly int[] OverloadGold = { 5000, 10000, 20000 };
        public static float EffectiveLevel(int level) => Mathf.Min(level, SoftUpgradeLevel) + 0.5f * Mathf.Max(0, level - SoftUpgradeLevel);

        public static int XpForLevel(int level) => Mathf.RoundToInt(XpBase * Mathf.Pow(XpGrowth, level - 1));
        /// 無盡 stops the XP curve climbing past this level (the exponential curve starved later waves of picks).
        public const int EndlessXpLevelCap = 14;
        public static int XpNeed(int level, bool endless) => XpForLevel(endless ? Mathf.Min(level, EndlessXpLevelCap) : level);
        /// 無盡 damage picks beyond the normal cap multiply damage by 1 + this × rarity instead of adding 20%.
        public const float EndlessDamagePick = 0.12f;
        /// 無盡 life picks beyond the normal cap multiply max life by 1 + this × rarity instead of adding 30.
        public const float EndlessLifePick = 0.10f;

        /// Cover walls: center x, center z, half x, half z, height. Block movement and enemy bullets.
        public static readonly float[][] Covers =
        {
            new[] { -6f, 4f, 2f, 0.35f, 1.8f }, new[] { 6f, 4f, 2f, 0.35f, 1.8f },
            new[] { -6f, -6f, 0.35f, 2f, 1.8f }, new[] { 6f, -6f, 0.35f, 2f, 1.8f },
            new[] { 0f, 10f, 2.5f, 0.35f, 1.8f }, new[] { -13f, -1f, 0.35f, 2.5f, 1.8f },
            new[] { 13f, -1f, 0.35f, 2.5f, 1.8f },
        };

        /// Story chapters; each holds RegionsPerChapter consecutive regions.
        public static readonly (string number, string title)[] Chapters = { ("第一章", "墜落星球"), ("第二章", "帝國母星") };
        public const int RegionsPerChapter = 3;
        public static int ChapterOf(int region) => Mathf.Clamp(region / RegionsPerChapter, 0, Chapters.Length - 1);

        public static readonly RegionDef[] Regions =
        {
            new RegionDef("翠風草原", "你的運輸艦墜落在這顆陌生星球的草原上。\n帝國追擊部隊已經降落，先守住墜落點。", 4, new Color32(0x9b, 0xd3, 0x6a, 255)),
            new RegionDef("熔核工廠", "帝國在星球地底建造了熔核工廠，\n摧毀它，切斷入侵艦隊的能源。", 5, new Color32(0xff, 0xad, 0x66, 255)),
            new RegionDef("帝國裂隙", "裂隙的另一端就是帝國母星。\n擊敗守衛，穿越裂隙發動反擊。", 6, new Color32(0xb4, 0x92, 0xff, 255)),
            new RegionDef("霜冕要塞", "穿過裂隙，你落在帝國母星的北境冰原。\n攻破霜冕要塞的城門，打開南下的道路。", 6, new Color32(0x9f, 0xd8, 0xff, 255)),
            new RegionDef("天穹船塢", "帝國艦隊正在高空船塢集結，準備進攻地球。\n在艦隊起飛前，摧毀船塢的防衛部隊。", 7, new Color32(0xff, 0x9a, 0x6a, 255)),
            new RegionDef("赫利奧王座", "皇帝親自坐鎮王座大廳。\n突破近衛軍，在王座前結束這場戰爭。", 7, new Color32(0xff, 0xd3, 0x6b, 255)),
        };

        public static readonly string[] WeaponOrder = { "pistol", "rifle", "shotgun", "sniper", "saber" };

        // Melee (光劍): left click swings through a frontal arc, right click blocks bullets from the front.
        /// Aiming down sights: spread multiplier (shotguns use AimSpreadPellets) and walk speed multiplier.
        public const float AimSpread = 0.3f, AimSpreadPellets = 0.65f, AimSpeed = 0.62f;
        public const float MeleeHalfArc = 55f, BlockFactor = 0.3f, BlockSpeed = 0.55f, BlockHalfArc = 70f;
        public const int MeleeCleave = 3;

        public static readonly Dictionary<string, WeaponDef> Weapons = new Dictionary<string, WeaponDef>
        {
            ["pistol"] = new WeaponDef("pistol", "P-9 制式手槍", "手槍", "初始武器。彈匣小、換彈快，穩定可靠。", 28f, 0.24f, 12, 1.1f, 1, 0f, 2f, 0.85f, 0, 0.022f, 0.006f, 1.0f),
            ["rifle"] = new WeaponDef("rifle", "AR-27 突擊步槍", "步槍", "全自動高射速，適合壓制大群敵人。", 17f, 0.1f, 30, 1.8f, 1, 0.012f, 2f, 0.75f, 400, 0.019f, 0.007f, 1.3f),
            ["shotgun"] = new WeaponDef("shotgun", "SG-8 霰彈槍", "霰彈", "一次射出 7 顆彈丸，近距離威力驚人。", 13f, 0.8f, 6, 2.2f, 7, 0.07f, 1.5f, 0.85f, 600, 0.065f, 0.015f, 1.4f),
            ["sniper"] = new WeaponDef("sniper", "LR-50 狙擊槍", "狙擊", "單發高傷害，爆頭 3 倍。右鍵開鏡。", 95f, 1.0f, 5, 2.4f, 1, 0f, 3f, 0.35f, 800, 0.09f, 0.01f, 1.6f),
            ["saber"] = new WeaponDef("saber", "EX-1 能量光劍", "光劍", "高能電漿刃。左鍵揮砍前方扇形，一次最多砍中 3 名敵人；右鍵格擋正面子彈。", 55f, 0.42f, 1, 0f, 1, 0f, 1f, 1f, 0, 0.006f, 0f, 1.5f, melee: true, range: 2.8f),
        };

        public static readonly string[] UpgradeKeys = { "damage", "mag", "reload" };

        /// Upgrade price multiplier per rarity (劣質 .. 神話); mirrors RARITY_UPGRADE_MULT in server/service.py.
        public static readonly float[] RarityUpgradeMult = { 0.6f, 1.0f, 1.4f, 2.0f, 3.0f };

        public static int UpgradeCost(string weapon, int level, int rarity = 1) =>
            (int)System.Math.Round(120.0 * (level + 1) * Weapons[weapon].UpgradeFactor * RarityUpgradeMult[Mathf.Clamp(rarity, 0, RarityUpgradeMult.Length - 1)]
                * System.Math.Pow(1.5, System.Math.Max(0, level - SoftUpgradeLevel + 1)), System.MidpointRounding.ToEven);

        public static readonly KindDef[] Kinds =
        {
            new KindDef("突擊兵", new Color32(0xff, 0x88, 0x55, 255), 1.0f),
            new KindDef("衝鋒兵", new Color32(0xff, 0x4d, 0x6d, 255), 0.9f),
            new KindDef("狙擊兵", new Color32(0xb4, 0x7c, 0xff, 255), 1.0f),
            new KindDef("自爆蟲", new Color32(0xff, 0xd2, 0x3f, 255), 0.75f),
            new KindDef("護盾兵", new Color32(0x5a, 0xa9, 0xff, 255), 1.0f),
            new KindDef("帝國重裝兵", new Color32(0xff, 0x55, 0x33, 255), 2.2f),
            new KindDef("帝國指揮官", new Color32(0xff, 0xb3, 0x47, 255), 2.6f),
            new KindDef("帝國近衛兵", new Color32(0xff, 0xcf, 0x5a, 255), 1.15f),
            new KindDef("赫利奧皇帝", new Color32(0xff, 0xd3, 0x6b, 255), 3.0f),
            new KindDef("分裂體", new Color32(0x7d, 0xff, 0x6a, 255), 1.15f),
            new KindDef("分裂幼體", new Color32(0xb8, 0xff, 0x6a, 255), 0.6f),
            new KindDef("醫療兵", new Color32(0x5c, 0xf0, 0xd8, 255), 1.0f),
        };

        public const int KindGrunt = 0, KindCharger = 1, KindSniper = 2, KindBomber = 3, KindShield = 4, KindHeavyBoss = 5, KindCommander = 6,
            KindGuard = 7, KindEmperor = 8, KindSplitter = 9, KindSplitling = 10, KindMedic = 11;

        /// Boss ground slam: a marked circle under a player that bursts after the wind-up.
        public const float SlamWindup = 1.4f, SlamRadius = 2.4f;
        /// 醫療兵: heals wounded allies around it every few seconds (bosses get a smaller share).
        public const float MedicRange = 6.5f, MedicEvery = 3.2f, MedicHeal = 0.12f;
        /// Boss loot: the super chest gives the whole team this many upgrade picks, all 稀有 or better.
        public const int SuperChestPicks = 3;

        public static bool IsBossKind(int kind) => kind == KindHeavyBoss || kind == KindCommander || kind == KindEmperor;

        /// Where a boss takes critical (headshot-multiplied) damage: 0 head, 1 chest core, 2 leg joints.
        public const int WeakHead = 0, WeakChest = 1, WeakLegs = 2;
        public static int WeakPoint(int kind) => kind == KindHeavyBoss ? WeakChest : kind == KindCommander ? WeakLegs : WeakHead;
        public static string WeakPointName(int kind) => WeakPoint(kind) == WeakChest ? "胸口核心" : WeakPoint(kind) == WeakLegs ? "腿部關節" : "頭部";

        /// Imperial Guard charge: a glowing wind-up, then a fast rush that hits once on contact.
        public const float GuardWindup = 0.55f, GuardCharge = 0.7f, GuardChargeSpeed = 3.4f;
        /// The Emperor's phases begin below these fractions of his health.
        public const float EmperorPhase2 = 0.66f, EmperorPhase3 = 0.33f;

        public static readonly BuffDef[] Buffs =
        {
            new BuffDef("超載彈芯", "傷害 +{0}%", 20),
            new BuffDef("急速扳機", "射速 +{0}%", 15),
            new BuffDef("動能靴", "移動速度 +{0}%", 12),
            new BuffDef("生命強化", "生命上限 +{0}，並立即恢復", 30),
            new BuffDef("修復協議", "每波開始恢復 {0} 生命", 15),
            new BuffDef("弱點分析", "{0}% 機率造成雙倍傷害", 20),
            new BuffDef("擴充彈匣", "彈匣 +{0} 發，換彈加快 15%", 4),
            new BuffDef("戰術冷卻", "手榴彈冷卻 -{0}%", 20),
            new BuffDef("吸能模組", "每次擊殺恢復 {0} 生命（回復速度有上限）", 4),
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
        /// Kill / hit healing (吸能模組, shotgun and saber perks) shares a budget: at most HealPerSecond of max HP per
        /// second, banked up to HealBank, so stacked lifesteal can never outheal a late wave. 吸能模組 itself caps too.
        public const float HealPerSecond = 0.08f, HealBank = 0.15f, MaxLifesteal = 20f;
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

        /// How many times each upgrade can be taken in one mission (gadgets also stop at their own level cap).
        /// In 無盡模式 the weapon damage (超載彈芯) and shield (強化護盾) upgrades keep stacking.
        public static readonly int[] BuffMaxStacks = { 6, 5, 4, 6, 4, 3, 4, 4, 4, 4, 99, 5, 4, 5, 4, 99, 99, 99, 99, 3, 5 };
        public static int BuffStackLimit(int index, bool endless) =>
            endless && (index == 0 || index == 3) ? int.MaxValue : index >= 0 && index < BuffMaxStacks.Length ? BuffMaxStacks[index] : 99;

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
        public readonly float Damage, Interval, Reload, Spread, HeadMultiplier, Zoom, Recoil, Sway, UpgradeFactor, Range;
        public readonly int Mag, Pellets, Price;
        /// Melee weapons swing instead of firing: no ammo, no reload; reach is Range.
        public readonly bool Melee;

        public WeaponDef(string id, string name, string shortName, string description, float damage, float interval, int mag, float reload,
            int pellets, float spread, float head, float zoom, int price, float recoil, float sway, float upgradeFactor, bool melee = false, float range = 0f)
        {
            Id = id; Name = name; Short = shortName; Description = description; Damage = damage; Interval = interval; Mag = mag; Reload = reload;
            Pellets = pellets; Spread = spread; HeadMultiplier = head; Zoom = zoom; Price = price; Recoil = recoil; Sway = sway; UpgradeFactor = upgradeFactor;
            Melee = melee; Range = range;
        }

        /// Stats after armory upgrades (before in-mission buffs). On a melee weapon the 彈匣 upgrade lengthens the
        /// reach (+8% per level) and the 換彈 upgrade quickens the swing (-8% per level).
        public WeaponStats StatsAt(int damageLevel, int magLevel, int reloadLevel, int overload = 0)
        {
            float d = Defs.EffectiveLevel(damageLevel), m = Defs.EffectiveLevel(magLevel), r = Defs.EffectiveLevel(reloadLevel);
            float over = Mathf.Clamp(overload, 0, Defs.OverloadMax) * Defs.OverloadStep;
            return new WeaponStats
            {
                Damage = Damage * (1f + 0.12f * d) * (1f + over),
                // A blade's 換彈 upgrade speeds up the swing at half the rate (it used to reach 40% of the interval: far too fast).
                Interval = (Melee ? Interval * (1f - 0.04f * r) : Interval) * (1f - over),
                Mag = Melee ? 1 : Mathf.CeilToInt(Mag * (1f + 0.15f * m)),
                Reload = Melee ? 0f : Reload * (1f - 0.08f * r) * (1f - over),
                Range = Melee ? Range * (1f + 0.08f * m) : 0f,
                Pellets = Pellets, Spread = Spread, HeadMultiplier = HeadMultiplier, Zoom = Zoom,
            };
        }
    }

    public struct WeaponStats
    {
        public float Damage, Interval, Reload, Spread, HeadMultiplier, Zoom, Range;
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
