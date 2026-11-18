using System.Collections.Generic;
using RiftWard.Sim;
using UnityEngine;

namespace RiftWard.Client
{
    /// One piece of equipment from a supply draw.
    public sealed class GearItem
    {
        public string Id, Slot;
        public int Rarity, Level;
        /// Effective stats (enhancement included); Base holds the +0 values.
        public Dictionary<string, int> Stats = new Dictionary<string, int>(), Base = new Dictionary<string, int>();
        /// Overrides used when a reveal card shows something other than gear (weapon draws).
        public string Title, Detail;

        public static GearItem From(Dictionary<string, object> d)
        {
            var item = new GearItem { Id = d.Str("id"), Slot = d.Str("slot"), Rarity = Mathf.Clamp(d.Int("rarity"), 0, Gear.RarityNames.Length - 1) };
            var stats = d.Obj("stats");
            if (stats != null) foreach (var kv in stats) item.Stats[kv.Key] = kv.Value is double n ? (int)n : 0;
            var baseStats = d.Obj("base");
            if (baseStats != null) foreach (var kv in baseStats) item.Base[kv.Key] = kv.Value is double n ? (int)n : 0;
            item.Level = Mathf.Clamp(d.Int("level"), 0, Gear.MaxLevel);
            return item;
        }

        public string Name => Gear.ItemName(Slot, Rarity) + (Level > 0 ? $" +{Level}" : "");
        public bool Maxed => Level >= Gear.MaxLevel;
        public int UpgradeCost => Gear.UpgradeCost(Rarity, Level);
        public Color Color => Gear.RarityColors[Rarity];

        public string StatText()
        {
            var parts = new List<string>();
            foreach (var kv in Stats) parts.Add(Gear.StatText(kv.Key, kv.Value));
            return string.Join("  ·  ", parts);
        }
    }

    /// Display data for account progression: talents, gear slots and rarities (rules live in the account service).
    public static class Gear
    {
        /// Currency colours shared by the nav chips, shop and icons.
        public static readonly Color GearKeyColor = Palette.Hex("b07cff"), WeaponKeyColor = Palette.Hex("e5534b"), ShardColor = Palette.Hex("4fd6e8");
        public static readonly string[] RarityNames = { "劣質", "普通", "稀世", "傳奇", "神話" };
        /// Gear enhancement; mirrors GEAR_UPGRADE_BASE / GEAR_MAX_LEVEL in server/service.py.
        public const int MaxLevel = 5;
        public static readonly int[] UpgradeBase = { 100, 175, 300, 500, 800 };
        public static int UpgradeCost(int rarity, int level) => UpgradeBase[Mathf.Clamp(rarity, 0, 4)] * (level + 1);

        /// Salvage value per piece; mirrors GEAR_SALVAGE_GOLD / DUPLICATE_GOLD / SALVAGE_SHARDS in server/service.py.
        /// 傳奇 and 神話 break down into shards instead of gold.
        public static readonly int[] GearSalvageGold = { 20, 50, 120, 0, 0 };
        public static readonly int[] WeaponSalvageGold = { 40, 100, 240, 0, 0 };
        public static readonly int[] SalvageShards = { 0, 0, 0, 1, 4 };

        /// (gold, shards) invested in one weapon copy: enhancement levels plus overload tiers.
        public static (int gold, int shards) WeaponInvested(string weapon, int rarity, Dictionary<string, int> levels)
        {
            int gold = 0, shards = 0;
            foreach (var stat in Defs.UpgradeKeys)
                for (int n = 0; n < (levels.TryGetValue(stat, out var lv) ? lv : 0); n++) gold += Defs.UpgradeCost(weapon, n, rarity);
            int over = levels.TryGetValue("over", out var o) ? Mathf.Clamp(o, 0, Defs.OverloadMax) : 0;
            for (int n = 0; n < over; n++) { gold += Defs.OverloadGold[n]; shards += Defs.OverloadShards[n]; }
            return (gold, shards);
        }
        public static readonly Color[] RarityColors =
        {
            Palette.Hex("e8e6e1"), Palette.Hex("5fd17a"), Palette.Hex("b07cff"), Palette.Hex("ff9a3c"), Palette.Hex("ff4b4b"),
        };
        public static readonly string[] Slots = { "armor", "module", "boots", "chip" };
        public static readonly Dictionary<string, string> SlotNames = new Dictionary<string, string>
        {
            ["armor"] = "護甲", ["module"] = "槍械模組", ["boots"] = "戰術靴", ["chip"] = "戰術晶片",
        };
        public static readonly string[] Talents = { "hp", "damage", "speed", "reload", "crit" };
        public static readonly Dictionary<string, (string name, string per)> TalentInfo = new Dictionary<string, (string, string)>
        {
            ["hp"] = ("強韌體魄", "護盾上限 +5"), ["damage"] = ("火力專精", "傷害 +2%"), ["speed"] = ("機動訓練", "移動速度 +1.5%"),
            ["reload"] = ("快速裝填", "換彈速度 +3%"), ["crit"] = ("致命直覺", "爆擊率 +1%"),
        };
        public const int TalentMax = 10;

        /// Every slot has its own item line, one name per rarity (劣質 → 神話).
        static readonly System.Collections.Generic.Dictionary<string, string[]> ItemNames = new System.Collections.Generic.Dictionary<string, string[]>
        {
            ["armor"] = new[] { "破損防彈背心", "制式戰術護甲", "幽影奈米甲", "熾陽動力裝甲", "裂隙神諭戰甲" },
            ["module"] = new[] { "生鏽準星模組", "制式光學模組", "幽能聚焦模組", "熾金火控核心", "裂隙湮滅核心" },
            ["boots"] = new[] { "磨損行軍靴", "制式戰術靴", "幽影疾行靴", "熾陽推進靴", "裂隙相位靴" },
            ["chip"] = new[] { "老舊運算晶片", "制式戰術晶片", "幽能神經晶片", "熾金預判晶片", "裂隙先知晶片" },
        };

        /// Weapons keep their model name; the rarity adds a finish prefix.
        static readonly string[] WeaponPrefix = { "破舊", "制式", "幽能", "熾金", "裂隙神話" };

        public static string ItemName(string slot, int rarity) =>
            ItemNames.TryGetValue(slot, out var names) ? names[Mathf.Clamp(rarity, 0, names.Length - 1)] : slot;

        public static string WeaponName(string weapon, int rarity) =>
            $"{WeaponPrefix[Mathf.Clamp(rarity, 0, WeaponPrefix.Length - 1)]} {(Defs.Weapons.TryGetValue(weapon, out var w) ? w.Name : weapon)}";

        public static string StatText(string key, float value) => key switch
        {
            "hp" => $"護盾 +{value:0}",
            "damage" => $"傷害 +{value:0.#}%",
            "speed" => $"移速 +{value:0.#}%",
            "reload" => $"換彈 +{value:0.#}%",
            "crit" => $"爆擊 +{value:0.#}%",
            _ => $"{key} +{value}",
        };
    }
}
