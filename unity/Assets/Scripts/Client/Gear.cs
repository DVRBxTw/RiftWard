using System.Collections.Generic;
using UnityEngine;

namespace RiftWard.Client
{
    /// One piece of equipment from a supply draw.
    public sealed class GearItem
    {
        public string Id, Slot;
        public int Rarity;
        public Dictionary<string, int> Stats = new Dictionary<string, int>();
        /// Overrides used when a reveal card shows something other than gear (weapon draws).
        public string Title, Detail;

        public static GearItem From(Dictionary<string, object> d)
        {
            var item = new GearItem { Id = d.Str("id"), Slot = d.Str("slot"), Rarity = Mathf.Clamp(d.Int("rarity"), 0, Gear.RarityNames.Length - 1) };
            var stats = d.Obj("stats");
            if (stats != null) foreach (var kv in stats) item.Stats[kv.Key] = kv.Value is double n ? (int)n : 0;
            return item;
        }

        public string Name => $"{Gear.RarityNames[Rarity]}{Gear.SlotNames.GetValueOrDefault(Slot, Slot)}";
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
        public static readonly string[] RarityNames = { "劣質", "普通", "稀世", "傳奇", "神話" };
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
