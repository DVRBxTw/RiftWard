"""RiftWard beta account, progress and update service (Python standard library)."""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import hashlib
import hmac
import json
import os
from pathlib import Path
import random
import zipfile
import re
import secrets
import socket
import sqlite3
import ssl
import threading
import time
from collections import defaultdict, deque
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

# 測試服 and 正式服 run side by side on one machine: each StartServer.cmd sets its own ports (正式服 24780/24781).
HOST = os.environ.get("RIFTWARD_BIND", "192.168.213.100")
PORT = int(os.environ.get("RIFTWARD_API_PORT", "24771"))
CHANNEL = os.environ.get("RIFTWARD_CHANNEL", "測試服")
ITERATIONS = 310_000
USERNAME = re.compile(r"[A-Za-z0-9_]{3,24}\Z")
VERSION = "0.4.2-beta.1"

# Armory catalog. The game client mirrors these numbers for display only;
# every purchase is priced and validated here.
WEAPON_PRICES = {"pistol": 0, "rifle": 400, "shotgun": 600, "sniper": 800, "saber": 0, "minigun": 0}
LIMITED_WEAPONS = {"minigun"}   # only from a limited supply drop: never in WEAPON_WEIGHTS
# 限定補給: same rarity odds and 50-draw pity as the weapon supply (its own counter). A 神話 is the limited weapon
# half the time; when it is not (歪), the next 神話 is guaranteed to be the limited weapon.
LIMITED_MYTH_SHARE = 0.5
LIMITED_PITY = 80   # the 80th draw without a 神話 is a 神話 (replaces the weapon supply's 50-draw 傳奇+ pity here)
# 意見回饋: players write to the admins; a reply goes back as system mail.
FEEDBACK_CATEGORIES = {"suggest": "建議", "bug": "錯誤回報", "other": "其他"}
FEEDBACK_MAX = 500
LIMITED_MAX_DAYS = 90
UPGRADE_STATS = ("damage", "mag", "reload")
MAX_UPGRADE = 10      # weapon enhancement cap per stat; levels past SOFT_UPGRADE cost more and give half a step
SOFT_UPGRADE = 5
OVERLOAD_MAX = 3      # 超載: extra tiers for 傳奇+ copies with every stat at MAX_UPGRADE, paid in shards + gold
OVERLOAD_SHARDS = [1, 2, 4]
OVERLOAD_GOLD = [5000, 10000, 20000]
OVERLOAD_MIN_RARITY = 3
PIERCE_MIN_RARITY = 4  # 穿透: bullets pass through enemies; 神話 guns only, one-time unlock per copy
PIERCE_SHARDS = 5
PIERCE_GOLD = 20000
MAX_REWARD = 12000   # mirrors GameSim.MaxMissionGold (地獄 multiplies the gold of a cleared mission)

# Difficulty tiers (普通, 困難, 噩夢, 地獄); mirrors Defs.Difficulties in the game client.
DIFFICULTY_NAMES = ["普通", "困難", "噩夢", "地獄"]
DIFFICULTY_XP = [1.0, 1.25, 1.5, 1.75]
REGION_COUNT = 6   # two chapters of three regions; mirrors Defs.Regions


DIFFICULTY_GOLD = [1.0, 1.6, 2.4, 3.5]


ENDLESS_MAX_WAVE = 500


def mission_gold(team_kills, wave, level, victory, region, tier, endless=False) -> int:
    """Gold a mission pays; mirrors GameSim.MissionGold (the anti-cheat check compares the report against it).
    無盡模式 never ends in victory; every 10 waves survived pays a milestone instead."""
    base = team_kills * 3 + wave * 12 + (level - 1) * 15 + (200 + 150 * region if victory else 0) + ((wave // 10) * (150 + 100 * region) if endless else 0)
    return max(0, min(MAX_REWARD, int(round(base * DIFFICULTY_GOLD[tier]))))


# First clear of a region on each tier pays both key kinds once (mirrors Defs.FirstClearKeys):
# 困難 5 + 5, 噩夢 10 + 10, 地獄 15 + 15.
FIRST_CLEAR_KEYS = {1: 5, 2: 10, 3: 15}
# ...and shards on the two top tiers (mirrors Defs.FirstClearShards): 噩夢 2, 地獄 3.
FIRST_CLEAR_SHARDS = {2: 2, 3: 3}


WELCOME_MAIL = ("歡迎加入裂隙守衛",
                "士兵，歡迎報到！\n\n這是指揮部準備的見面禮：3 把裝備鑰匙和 1 把武器鑰匙，領取後到「商城」的補給站開啟吧。\n"
                "每天記得完成「任務」分頁的每日任務，可以持續獲得鑰匙。\n\n祝武運昌隆。\n—— 遠征軍指揮部",
                {"keys": 3, "weapon_keys": 1})


def difficulty_open(cleared, region, tier) -> bool:
    """A tier opens in a region once the tier below it has been cleared there (普通 is always open)."""
    return tier == 0 or cleared[region] >= tier - 1


# ---- daily missions: three tasks a day (Taiwan time), progress counted from the combat records the game
# server reports, rewards claimed by the player. Finishing all three unlocks a bonus.
DAILY_UTC_OFFSET = 8 * 3600
DAILY_COUNT = 3
DAILY_TASKS = {
    "kills":     {"text": "擊殺 {goal} 名敵人",            "goal": 150, "reward": {"gold": 300}},
    "headshots": {"text": "爆頭命中 {goal} 次",            "goal": 40,  "reward": {"gold": 250, "keys": 1}},
    "missions":  {"text": "出擊 {goal} 場任務",            "goal": 3,   "reward": {"keys": 2}},
    "waves":     {"text": "累計推進 {goal} 波",            "goal": 30,  "reward": {"gold": 350}},
    "victories": {"text": "成功完成 {goal} 場任務",        "goal": 1,   "reward": {"gold": 200, "keys": 1}},
    "deep":      {"text": "單場任務到達第 {goal} 波",      "goal": 10,  "reward": {"gold": 250, "keys": 1}},
    "hard":      {"text": "在困難以上難度完成 {goal} 場任務", "goal": 1, "reward": {"weapon_keys": 1}},
}
DAILY_BONUS = {"keys": 2, "weapon_keys": 1}
# ---- weekly missions: five bigger tasks a week (Monday 00:00 Taiwan time), same progress source as the dailies.
WEEKLY_COUNT = 5
WEEKLY_TASKS = {
    "kills":     {"text": "擊殺 {goal} 名敵人",              "goal": 1000, "reward": {"gold": 1500}},
    "headshots": {"text": "爆頭命中 {goal} 次",              "goal": 300,  "reward": {"gold": 1000, "keys": 3}},
    "missions":  {"text": "出擊 {goal} 場任務",              "goal": 15,   "reward": {"keys": 5}},
    "victories": {"text": "成功完成 {goal} 場任務",          "goal": 8,    "reward": {"weapon_keys": 2}},
    "waves":     {"text": "累計推進 {goal} 波",              "goal": 250,  "reward": {"gold": 1500, "keys": 2}},
    "bosses":    {"text": "擊敗 {goal} 隻 Boss",             "goal": 5,    "reward": {"weapon_keys": 2}},
    "hard":      {"text": "在困難以上難度完成 {goal} 場任務", "goal": 4,    "reward": {"weapon_keys": 3}},
    "s_grades":  {"text": "以 S 評分完成 {goal} 場任務",     "goal": 3,    "reward": {"weapon_keys": 2, "shards": 1}},
}
WEEKLY_BONUS = {"weapon_keys": 3, "shards": 3}
GRADE_ORDER = ("S", "A", "B", "C")


# ---- achievements: lifetime goals in three tiers (銅 / 銀 / 金). Progress is derived from the account's records,
# each reached tier is claimed once for its reward, and the 金 tier of some achievements grants a title.
ACHIEVEMENT_TIER_NAMES = ["銅", "銀", "金"]
ACHIEVEMENT_REWARDS = [{"gold": 300}, {"gold": 600, "keys": 2}, {"keys": 3, "weapon_keys": 2, "shards": 1}]
ACHIEVEMENTS = [
    # id, category, name, description ({goal} = tier goal), progress source, tier goals, title at 金 (or None)
    ("kills",       "combat",  "殲滅行動",   "累計擊殺 {goal} 名敵人",            "kills",               [500, 2500, 10000], "殲滅者"),
    ("headshots",   "combat",  "一槍入魂",   "累計爆頭命中 {goal} 次",            "headshots",           [200, 1000, 4000],  "神射手"),
    ("bosses",      "combat",  "巨人殺手",   "擊倒 {goal} 名 Boss",               "boss_kills",          [5, 25, 100],       "巨人殺手"),
    ("elites",      "combat",  "精英獵人",   "擊敗 {goal} 名精英敵人",            "elite_kills",         [10, 50, 200],      None),
    ("victories",   "clear",   "百戰老兵",   "完成 {goal} 場任務",                "victories",           [5, 25, 100],       "老兵"),
    ("hard",        "clear",   "迎難而上",   "在困難以上難度完成 {goal} 場任務",  "hard_victories",      [1, 10, 50],        None),
    ("nightmare",   "clear",   "裂隙征服者", "在 {goal} 個區域通關噩夢",          "nightmare_regions",   [2, 4, 6],          "裂隙征服者"),
    ("flawless",    "clear",   "不死之身",   "零倒地完成 {goal} 場任務",          "flawless",            [1, 5, 20],         "不死之身"),
    ("grades",      "clear",   "完美評價",   "以 S 評分完成 {goal} 場任務",       "s_grades",            [1, 10, 30],        None),
    ("revives",     "support", "戰地天使",   "救援隊友 {goal} 次",                "revives",             [5, 30, 100],       "戰地天使"),
    ("daily",       "support", "勤務兵",     "領取 {goal} 項每日任務獎勵",        "daily_claimed",       [10, 50, 200],      "勤務兵"),
    ("level",       "support", "步步高升",   "帳號等級達到 {goal}",               "level",               [10, 25, 50],       "指揮官"),
    ("legends",     "collect", "軍火大亨",   "擁有 {goal} 種傳奇以上武器",        "legend_weapons",      [1, 2, 4],          "軍火大亨"),
    ("mythic",      "collect", "神話收藏家", "持有 {goal} 件神話武器或裝備",      "mythic_items",        [1, 3, 8],          None),
    ("forged",      "collect", "千錘百鍊",   "將 {goal} 件裝備強化到 +5",         "maxed_gear",          [1, 3, 8],          None),
    ("draws",       "collect", "補給常客",   "開啟補給 {goal} 次",                "draws",               [10, 100, 500],     None),
]
ACHIEVEMENT_IDS = {a[0]: a for a in ACHIEVEMENTS}
TITLES = {a[0]: a[6] for a in ACHIEVEMENTS if a[6]}


def daily_day(now=None) -> str:
    return time.strftime("%Y-%m-%d", time.gmtime((time.time() if now is None else now) + DAILY_UTC_OFFSET))


def daily_resets_in(now=None) -> int:
    local = (time.time() if now is None else now) + DAILY_UTC_OFFSET
    return int(86400 - local % 86400)


def weekly_week(now=None) -> str:
    """The Monday (Taiwan time) that starts this week, as YYYY-MM-DD."""
    local = (time.time() if now is None else now) + DAILY_UTC_OFFSET
    monday = local - (time.gmtime(local).tm_wday * 86400) - local % 86400
    return time.strftime("%Y-%m-%d", time.gmtime(monday))


def weekly_resets_in(now=None) -> int:
    local = (time.time() if now is None else now) + DAILY_UTC_OFFSET
    return int((7 - time.gmtime(local).tm_wday) * 86400 - local % 86400)


def roll_weekly(account_id, week, veteran):
    pool = [k for k in WEEKLY_TASKS if k != "hard" or veteran]
    return random.Random(f"w:{account_id}:{week}").sample(pool, WEEKLY_COUNT)


def roll_daily(account_id, day, veteran):
    """The day's tasks, the same for one account all day. 困難 tasks are only offered once 普通 has been cleared."""
    pool = [k for k in DAILY_TASKS if k != "hard" or veteran]
    return random.Random(f"{account_id}:{day}").sample(pool, DAILY_COUNT)


# Upgrade prices scale with rarity (劣質 .. 神話): better items cost more to improve.
RARITY_UPGRADE_MULT = [0.6, 1.0, 1.4, 2.0, 3.0]
GEAR_UPGRADE_BASE = [100, 175, 300, 500, 800]   # gold for gear +1; level n -> n+1 costs base * (n + 1)
GEAR_MAX_LEVEL = 5
GEAR_LEVEL_BONUS = 0.12                        # every enhancement level adds 12% to all of an item's stats (+60% at +5)


def upgrade_cost(weapon: str, level: int, rarity: int = 1) -> int:
    factor = {"pistol": 1.0, "rifle": 1.3, "shotgun": 1.4, "sniper": 1.6, "saber": 1.5, "minigun": 1.5}[weapon]
    steep = 1.5 ** max(0, level - SOFT_UPGRADE + 1)   # levels 6-10: x1.5 per level on top of the linear curve
    return int(round(120 * (level + 1) * factor * RARITY_UPGRADE_MULT[max(0, min(len(RARITY_UPGRADE_MULT) - 1, rarity))] * steep))


def weapon_invested(weapon: str, rarity: int, levels: dict):
    """(gold, shards) put into one weapon copy: its enhancement levels and its overload tiers."""
    gold = sum(upgrade_cost(weapon, n, rarity) for stat in UPGRADE_STATS for n in range(levels.get(stat, 0)))
    over = levels.get("over", 0)
    pierce = 1 if levels.get("pierce", 0) else 0
    return gold + sum(OVERLOAD_GOLD[:over]) + PIERCE_GOLD * pierce, sum(OVERLOAD_SHARDS[:over]) + PIERCE_SHARDS * pierce


def gear_upgrade_cost(rarity: int, level: int) -> int:
    return GEAR_UPGRADE_BASE[rarity] * (level + 1)


# Defense (% damage taken removed) grows with each item's enhancement and 突破, on top of its stats.
GEAR_DEF_PER_LEVEL = 0.8
GEAR_DEF_PER_LIMIT = 1.5


def gear_defense(item) -> float:
    return GEAR_DEF_PER_LEVEL * item.get("level", 0) + GEAR_DEF_PER_LIMIT * item.get("limit", 0)


def gear_invested(item) -> int:
    """Gold put into an item: enhancement levels plus 突破 tiers (salvage refunds 80%)."""
    return sum(gear_upgrade_cost(item["rarity"], n) for n in range(item.get("level", 0))) + sum(LIMIT_GOLD[:item.get("limit", 0)])


def gear_invested_shards(item) -> int:
    return sum(LIMIT_SHARDS[:item.get("limit", 0)])


# Every piece of gear carries base life by rarity, on top of its own stats, so building gear makes you tougher
# as well as stronger (it used to come only from the armour slot).
GEAR_BASE_LIFE = [5, 10, 20, 35, 60]


def gear_stats(item) -> dict:
    """Effective stats of an item at its enhancement level and 突破 tier (stored stats are the +0 values), with the
    rarity's base life added to its life."""
    mult = (1 + GEAR_LEVEL_BONUS * item.get("level", 0)) * (1 + GEAR_LIMIT_BONUS * item.get("limit", 0))
    stats = {k: int(round(v * mult)) for k, v in item["stats"].items()}
    stats["hp"] = stats.get("hp", 0) + int(round(GEAR_BASE_LIFE[item["rarity"]] * mult))
    return stats


# ---- gear crafting (mirrored in Gear.cs): 重鑄, 突破, 升階 and sets
# 重鑄 rerolls the secondary stat (and the set) of a 稀世+ item; locking keeps the stat and the set, rerolling the value.
REFORGE_SHARDS = [0, 0, 1, 2, 3]
REFORGE_LOCK_SHARDS = 2
REFORGE_GOLD = [0, 0, 500, 1000, 2000]
SECONDARY_RANGE = (0.35, 0.75)   # secondary value as a share of the stat's full value at that rarity
# 突破: past +5, up to three tiers, each adding 8% to every stat of the item.
GEAR_LIMIT_MAX = 3
GEAR_LIMIT_BONUS = 0.08
LIMIT_SHARDS = [2, 4, 6]
LIMIT_GOLD = [3000, 6000, 12000]
# 升階: one more item of the same slot and rarity is consumed to raise the rarity by one (index = current rarity).
ASCEND_SHARDS = [1, 2, 4, 8]
ASCEND_GOLD = [500, 1500, 4000, 10000]
# Sets (稀世+ items carry one): bonuses for 2 and 4 equipped pieces of the same set, as plain stat bonuses.
GEAR_SETS = {
    "assault": {2: {"damage": 5}, 4: {"crit": 8}},
    "bulwark": {2: {"hp": 60}, 4: {"def": 10}},
    "gale": {2: {"speed": 5}, 4: {"reload": 15}},
}
GEAR_SET_MIN_RARITY = 2


def roll_secondary(rng, primary, rarity, keep=None):
    stat = keep if keep else rng.choice([k for k in GEAR_VALUES if k != primary])
    lo, hi = SECONDARY_RANGE
    return stat, max(1, int(round(GEAR_VALUES[stat][rarity] * rng.uniform(lo, hi))))


def set_bonus(items) -> dict:
    """Stat bonuses from the sets of the equipped items."""
    counts = {}
    for item in items:
        if item.get("set") in GEAR_SETS:
            counts[item["set"]] = counts.get(item["set"], 0) + 1
    bonus = {}
    for name, count in counts.items():
        for need, stats in GEAR_SETS[name].items():
            if count >= need:
                for k, v in stats.items():
                    bonus[k] = bonus.get(k, 0) + v
    return bonus


# ---- account level, talents, gear and supply draws
TALENTS = {"hp": 3, "damage": 2, "speed": 1.5, "reload": 3, "crit": 1}   # bonus per point (hp: % of max shield)
# The shield talent multiplies max shield (start and in-mission upgrades alike), so it is reported as "hp_pct",
# apart from the flat shield gear gives under "hp".
TALENT_BONUS_KEY = {"hp": "hp_pct"}
TALENT_MAX = 10
GEAR_SLOTS = {"armor": "hp", "module": "damage", "boots": "speed", "chip": "crit"}
RARITIES = ["劣質", "普通", "稀世", "傳奇", "神話"]
RARITY_WEIGHTS = [50, 33, 14, 2.5, 0.5]   # percent per draw; mirrored on the supply screen (Screens.cs)
# Primary stat value per rarity for each stat (hp is flat, the rest are percentages).
GEAR_VALUES = {"hp": [6, 12, 20, 32, 50], "damage": [2, 4, 7, 11, 16], "speed": [2, 3, 5, 8, 12],
               "crit": [2, 3, 5, 8, 12], "reload": [3, 5, 8, 12, 18]}
GEAR_NAMES = {"armor": "護甲", "module": "槍械模組", "boots": "戰術靴", "chip": "戰術晶片"}
INVENTORY_LIMIT = 200


def mission_xp(stats):
    """Account XP for one mission. Tuned down from kills*3 + wave*15 + 200, which let a single cleared region
    jump several levels; the level curve itself is unchanged so existing accounts keep their level and talents.
    Higher difficulty multiplies it (mirrors Defs.MissionXp)."""
    kills = stats.get("kills", 0) if isinstance(stats.get("kills"), int) else 0
    wave = stats.get("wave", 0) if isinstance(stats.get("wave"), int) else 0
    tier = stats.get("difficulty", 0)
    tier = tier if isinstance(tier, int) and not isinstance(tier, bool) and 0 <= tier < len(DIFFICULTY_XP) else 0
    return int(round((max(0, kills) * 1 + max(0, wave) * 5 + (100 if stats.get("victory") is True else 0)) * DIFFICULTY_XP[tier]))


def level_for(xp: int):
    """Returns (level, xp into level, xp needed for next level). Each level needs 100 + 50 * level."""
    level = 1
    while xp >= 100 + 50 * level and level < 99:
        xp -= 100 + 50 * level
        level += 1
    return level, xp, 100 + 50 * level


PITY_LIMIT = 50   # the 50th draw without 傳奇 or better is guaranteed to be 傳奇+


# Soft pity: from the 61st draw since the last 神話, each draw adds 0.02% to the 神話 chance (taken from 劣質).
SOFT_PITY_START = 60
SOFT_PITY_STEP = 0.02


def soft_weights(myth_streak):
    """Rarity weights for the next draw after `myth_streak` draws without a 神話."""
    bonus = max(0, myth_streak + 1 - SOFT_PITY_START) * SOFT_PITY_STEP
    weights = list(RARITY_WEIGHTS)
    weights[4] += bonus
    weights[0] = max(0.0, weights[0] - bonus)
    return weights


def myth_rate(myth_streak):
    """神話 chance of the next draw, in percent (shown on the supply screen)."""
    return round(soft_weights(myth_streak)[4], 2)


def roll_rarity(rng, pity, myth=0):
    """Returns (rarity, new pity count, new 神話 streak). Pity counts draws since the last 傳奇+; at the limit the
    draw is forced up to 傳奇 or 神話. The 神話 streak drives the soft pity."""
    weights = soft_weights(myth)
    if pity + 1 >= PITY_LIMIT:
        rarity = rng.choices([3, 4], weights=weights[3:])[0]
    else:
        rarity = rng.choices(range(len(RARITIES)), weights=weights)[0]
    return rarity, 0 if rarity >= 3 else pity + 1, 0 if rarity == 4 else myth + 1


def roll_limited_rarity(rng, pity):
    """限定補給: returns (rarity, new pity count). The count runs until a 神話 (and drives the soft pity); the
    LIMITED_PITY-th draw without one is a 神話."""
    if pity + 1 >= LIMITED_PITY:
        rarity = len(RARITIES) - 1
    else:
        rarity = rng.choices(range(len(RARITIES)), weights=soft_weights(pity))[0]
    return rarity, 0 if rarity == len(RARITIES) - 1 else pity + 1


def roll_item(rng, rarity=None):
    slot = rng.choice(list(GEAR_SLOTS))
    if rarity is None:
        rarity = rng.choices(range(len(RARITIES)), weights=RARITY_WEIGHTS)[0]
    primary = GEAR_SLOTS[slot]
    stats = {primary: GEAR_VALUES[primary][rarity]}
    item = {"id": secrets.token_hex(6), "slot": slot, "rarity": rarity, "stats": stats}
    if rarity >= 2:  # rare and better carry a random secondary stat at half strength, and a set
        secondary = rng.choice([k for k in GEAR_VALUES if k != primary])
        stats[secondary] = max(1, GEAR_VALUES[secondary][rarity] // 2)
        item["set"] = rng.choice(list(GEAR_SETS))
    return item


WEAPON_WEIGHTS = {"pistol": 30, "rifle": 30, "shotgun": 20, "sniper": 20, "saber": 5}   # the 光劍 is a rare find: 5 / 105 ≈ 4.8% of draws
DUPLICATE_GOLD = [40, 100, 240, 0, 0]       # 傳奇+ salvage into shards instead
GEAR_SALVAGE_GOLD = [20, 50, 120, 0, 0]
SALVAGE_SHARDS = [0, 0, 0, 1, 4]


def default_loadout():
    # New recruits start with a worn (劣質) AR-27; better guns come from weapon supply draws.
    return {"owned": ["rifle"], "levels": {}, "equipped": ["rifle"], "rarity": {"rifle": 0}, "copies": {"rifle": [0]}, "active": {"rifle": 0},
            "copy_levels": {"rifle": [{}]}}


WEAPON_COPY_LIMIT = 50


def sync_rarity(loadout):
    """The active copy decides each weapon's rarity."""
    loadout["rarity"] = {w: loadout["copies"][w][loadout["active"][w]] for w in loadout["owned"]}
    # Every copy keeps its own enhancement; "levels" is the active copy's, which is what the game reads.
    rows = loadout.setdefault("copy_levels", {})
    for w in loadout["owned"]:
        n = len(loadout["copies"][w])
        rows[w] = (rows.get(w) or [])[:n]
        rows[w] += [{} for _ in range(n - len(rows[w]))]
    for w in [w for w in rows if w not in loadout["owned"]]:
        del rows[w]
    loadout["levels"] = {w: dict(loadout["copy_levels"][w][loadout["active"][w]]) for w in loadout["owned"]
                         if loadout["copy_levels"][w][loadout["active"][w]]}


def add_copy(loadout, weapon, rarity):
    loadout["copies"][weapon].append(rarity)
    loadout["copy_levels"][weapon].append({})


def copy_refund(weapon, rarity, levels):
    """What salvaging a copy pays: its base value plus 80% of the gold and all the shards invested in it."""
    gold, shards = weapon_invested(weapon, rarity, levels)
    return DUPLICATE_GOLD[rarity] + refund_gold(gold), SALVAGE_SHARDS[rarity] + shards


def refund_gold(invested: int) -> int:
    """Salvage gives back 80% of the gold spent improving an item (shards come back in full)."""
    return invested * 4 // 5


def _locked(method):
    """Serialises a Store mutation: the HTTP server handles requests on parallel threads, and two overlapping
    read-modify-write calls (e.g. a double-clicked salvage) could otherwise overwrite each other."""
    def wrapper(self, *args, **kwargs):
        with self.lock:
            return method(self, *args, **kwargs)
    wrapper.__name__ = method.__name__
    wrapper.__doc__ = method.__doc__
    return wrapper


class Store:
    # Anti-cheat: the shortest believable time per wave, and the shortest gap between two rewards of one account.
    # Tests and the fast integration run lower these.
    min_wave_seconds = 4.0

    def __init__(self, directory: Path):
        self.lock = threading.RLock()
        self.last_reward = {}
        self._local = threading.local()   # per-thread connection shared inside a db_session()
        directory.mkdir(parents=True, exist_ok=True)
        self.path = directory / "players.sqlite3"
        keyfile = directory / "server.key"
        if not keyfile.exists():
            with keyfile.open("x", encoding="ascii") as handle:
                handle.write(secrets.token_hex(32))
        self.server_key = keyfile.read_text(encoding="ascii").strip()
        # Operators list admin usernames here, one per line; re-read on every check.
        self.admin_file = directory / "admins.txt"
        if not self.admin_file.exists():
            self.admin_file.write_text("# 每行一個管理員帳號名稱（不分大小寫）\n", encoding="utf-8")
        with self.connect() as db:
            db.executescript("""
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS accounts (
                    id INTEGER PRIMARY KEY, username TEXT UNIQUE COLLATE NOCASE NOT NULL,
                    password_hash BLOB NOT NULL, salt BLOB NOT NULL, created INTEGER NOT NULL
                );
                CREATE TABLE IF NOT EXISTS progress (
                    account_id INTEGER PRIMARY KEY REFERENCES accounts(id),
                    unlocked INTEGER NOT NULL DEFAULT 0,
                    tutorial_complete INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS sessions (
                    token_hash TEXT PRIMARY KEY, account_id INTEGER NOT NULL REFERENCES accounts(id),
                    expires INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS session_account ON sessions(account_id);
                CREATE TABLE IF NOT EXISTS mail (
                    id INTEGER PRIMARY KEY, account_id INTEGER NOT NULL REFERENCES accounts(id),
                    title TEXT NOT NULL, body TEXT NOT NULL, gold INTEGER NOT NULL DEFAULT 0, keys INTEGER NOT NULL DEFAULT 0,
                    weapon_keys INTEGER NOT NULL DEFAULT 0, created INTEGER NOT NULL, expires INTEGER NOT NULL,
                    read INTEGER NOT NULL DEFAULT 0, claimed INTEGER NOT NULL DEFAULT 0, deleted INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS mail_account ON mail(account_id, deleted);
                CREATE TABLE IF NOT EXISTS friendships (
                    requester INTEGER NOT NULL REFERENCES accounts(id), addressee INTEGER NOT NULL REFERENCES accounts(id),
                    status TEXT NOT NULL, created INTEGER NOT NULL, PRIMARY KEY (requester, addressee)
                );
                CREATE INDEX IF NOT EXISTS friendship_addressee ON friendships(addressee);
                CREATE TABLE IF NOT EXISTS feedback (
                    id INTEGER PRIMARY KEY, account_id INTEGER NOT NULL, created INTEGER NOT NULL,
                    category TEXT NOT NULL, body TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'open',
                    reply TEXT NOT NULL DEFAULT '', replied INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS feedback_account ON feedback(account_id, id);
                CREATE TABLE IF NOT EXISTS login_claims (
                    account_id INTEGER NOT NULL, event_id INTEGER NOT NULL, claimed INTEGER NOT NULL DEFAULT 0,
                    last_day TEXT NOT NULL DEFAULT '', PRIMARY KEY(account_id, event_id)
                );
                CREATE TABLE IF NOT EXISTS settings (
                    key TEXT PRIMARY KEY, value TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS gacha_log (
                    id INTEGER PRIMARY KEY, account_id INTEGER NOT NULL, created INTEGER NOT NULL,
                    pool TEXT NOT NULL, item TEXT NOT NULL, rarity INTEGER NOT NULL, outcome TEXT NOT NULL DEFAULT ''
                );
                CREATE INDEX IF NOT EXISTS gacha_log_account ON gacha_log(account_id, id);
                CREATE TABLE IF NOT EXISTS audit (
                    id INTEGER PRIMARY KEY, account_id INTEGER NOT NULL, created INTEGER NOT NULL,
                    reason TEXT NOT NULL, detail TEXT NOT NULL, rejected INTEGER NOT NULL DEFAULT 0
                );
            """)
            columns = {row[1] for row in db.execute("PRAGMA table_info(progress)")}
            if "gold" not in columns:
                db.execute("ALTER TABLE progress ADD COLUMN gold INTEGER NOT NULL DEFAULT 0")
            if "loadout" not in columns:
                db.execute("ALTER TABLE progress ADD COLUMN loadout TEXT NOT NULL DEFAULT '{}'")
            if "stats" not in columns:
                db.execute("ALTER TABLE progress ADD COLUMN stats TEXT NOT NULL DEFAULT '{}'")
            for column, ddl in (("xp", "INTEGER NOT NULL DEFAULT 0"), ("keys", "INTEGER NOT NULL DEFAULT 0"), ("weapon_keys", "INTEGER NOT NULL DEFAULT 0"),
                                ("talents", "TEXT NOT NULL DEFAULT '{}'"), ("gear", "TEXT NOT NULL DEFAULT '{}'"), ("pity", "TEXT NOT NULL DEFAULT '{}'"),
                                ("cleared", "TEXT NOT NULL DEFAULT '[]'"), ("daily", "TEXT NOT NULL DEFAULT '{}'"), ("weekly", "TEXT NOT NULL DEFAULT '{}'"), ("grades", "TEXT NOT NULL DEFAULT '{}'"), ("codex", "TEXT NOT NULL DEFAULT '{}'"),
                                ("achievements", "TEXT NOT NULL DEFAULT '{}'"), ("last_seen", "INTEGER NOT NULL DEFAULT 0"),
                                ("shards", "INTEGER NOT NULL DEFAULT 0"), ("limited_guarantee", "INTEGER NOT NULL DEFAULT 0")):
                if column not in columns:
                    db.execute(f"ALTER TABLE progress ADD COLUMN {column} {ddl}")

    def _open(self):
        db = sqlite3.connect(self.path, timeout=5)
        db.row_factory = sqlite3.Row
        db.execute("PRAGMA foreign_keys=ON")
        return db

    @contextmanager
    def db_session(self):
        """Shares one connection across everything this thread does until the block ends (one request, one
        profile build). Without a session every connect() opens and closes its own connection."""
        if getattr(self._local, "db", None) is not None:
            yield
            return
        db = self._open()
        self._local.db, self._local.depth = db, 0
        try:
            yield
        finally:
            self._local.db = None
            db.close()

    @contextmanager
    def connect(self):
        """A transaction. Inside a session the shared connection is used; only the outermost block commits."""
        shared = getattr(self._local, "db", None)
        if shared is None:
            db = self._open()
            try:
                with db:
                    yield db
            finally:
                db.close()
            return
        if self._local.depth:
            self._local.depth += 1
            try:
                yield shared
            finally:
                self._local.depth -= 1
            return
        self._local.depth = 1
        try:
            with shared:
                yield shared
        finally:
            self._local.depth = 0

    @staticmethod
    def _hash(password: str, salt: bytes):
        return hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), salt, ITERATIONS)

    def profile(self, account_id: int):
        with self.db_session():
            return self._profile(account_id)

    def _profile(self, account_id: int):
        with self.connect() as db:
            row = db.execute(
                "SELECT a.id,a.username,p.unlocked,p.tutorial_complete,p.gold,p.shards,p.loadout,p.stats,p.xp,p.keys,p.weapon_keys,p.talents,p.gear,p.cleared FROM accounts a "
                "JOIN progress p ON p.account_id=a.id WHERE a.id=?", (account_id,)
            ).fetchone()
        if row is None:
            return None
        result = dict(row)
        result["tutorial_complete"] = bool(result["tutorial_complete"])
        result["loadout"] = self._parse_loadout(result.get("loadout"))
        result["admin"] = self.is_admin(result["username"])
        result["stats"] = self._parse_stats(result.get("stats"))
        level, into, need = level_for(result["xp"])
        result.update(level=level, level_xp=into, level_need=need)
        talents = self._parse_talents(result.pop("talents"))
        gear = self._parse_gear(result.pop("gear"))
        result["talents"] = talents
        result["talent_points"] = max(0, level - 1 - sum(talents.values()))
        pity = self._pity(account_id)
        # Draws left until the pity hits: 傳奇+ for the regular supplies, 神話 for the limited one.
        result["pity"] = {pool: (LIMITED_PITY if pool == "limited" else PITY_LIMIT) - pity[pool] for pool in ("gear", "weapon", "limited")}
        result["myth_rate"] = {"gear": myth_rate(pity["gear_myth"]), "weapon": myth_rate(pity["weapon_myth"]), "limited": myth_rate(pity["limited"])}
        banner = self.limited_banner()
        if banner:
            with self.connect() as db:
                row = db.execute("SELECT limited_guarantee FROM progress WHERE account_id=?", (account_id,)).fetchone()
            result["limited"] = dict(banner, guarantee=bool(row and row[0]))
        event = self._login_view(account_id)
        if event:
            result["login_event"] = event
        result["inventory"] = [dict(item, stats=gear_stats(item), base=item["stats"], level=item.get("level", 0), defense=gear_defense(item)) for item in gear["inventory"]]
        result["equipment"] = gear["equipped"]
        bonus = {TALENT_BONUS_KEY.get(k, k): talents[k] * per for k, per in TALENTS.items()}
        worn = [item for item in gear["inventory"] if gear["equipped"].get(item["slot"]) == item["id"]]
        for item in worn:
            for k, v in gear_stats(item).items():
                bonus[k] = bonus.get(k, 0) + v
            if gear_defense(item) > 0:
                bonus["def"] = bonus.get("def", 0) + gear_defense(item)
        for k, v in set_bonus(worn).items():
            bonus[k] = bonus.get(k, 0) + v
        result["bonus"] = {k: round(v, 2) for k, v in bonus.items()}
        cleared = self._parse_cleared(result.pop("cleared"), result["unlocked"])
        result["cleared"] = cleared
        # A cleared region opens the next one. Before chapter 2 the last region capped progress, so accounts that
        # cleared 帝國裂隙 then still read unlocked = 2; their clear record opens 霜冕要塞 now.
        passed = [i for i, c in enumerate(cleared) if c >= 0]
        if passed:
            result["unlocked"] = max(result["unlocked"], min(REGION_COUNT - 1, passed[-1] + 1))
        result["daily"] = self._daily_view(self._daily(account_id, cleared))
        result["weekly"] = self._weekly_view(self._weekly(account_id, cleared))
        result["grades"] = self.grades(account_id)
        state = self._achievement_state(account_id)
        progress = self._achievement_progress(result, gear)
        result["achievements"] = self._achievement_view(state, progress)
        title = state["title"] if state["title"] in TITLES and self._title_earned(state, state["title"], progress) else ""
        result["title"] = {"id": title, "text": TITLES.get(title, "")}
        result["mail"] = self.mail_counts(account_id)
        with self.connect() as db:
            result["friend_requests"] = db.execute("SELECT COUNT(*) FROM friendships WHERE addressee=? AND status='pending'", (account_id,)).fetchone()[0]
        return result

    # ---- achievements

    def _achievement_state(self, account_id):
        with self.connect() as db:
            row = db.execute("SELECT achievements FROM progress WHERE account_id=?", (account_id,)).fetchone()
        try:
            data = json.loads(row[0]) if row else {}
        except (TypeError, ValueError):
            data = {}
        data = data if isinstance(data, dict) else {}
        claimed = data.get("claimed") if isinstance(data.get("claimed"), dict) else {}
        claimed = {k: max(0, min(3, v)) for k, v in claimed.items() if k in ACHIEVEMENT_IDS and isinstance(v, int) and not isinstance(v, bool)}
        title = data.get("title") if isinstance(data.get("title"), str) else ""
        return {"claimed": claimed, "title": title}

    def _save_achievements(self, account_id, state):
        with self.connect() as db:
            db.execute("UPDATE progress SET achievements=? WHERE account_id=?", (json.dumps(state), account_id))

    @staticmethod
    def _achievement_progress(profile, gear):
        """Current value of every progress source, from the profile's records, collection and level."""
        stats = profile["stats"]
        loadout = profile["loadout"]
        values = {k: stats.get(k, 0) for k in Store.STAT_KEYS}
        values["level"] = profile["level"]
        values["nightmare_regions"] = sum(1 for c in profile["cleared"] if c >= 2)
        values["legend_weapons"] = sum(1 for w in loadout["owned"] if max(loadout["copies"].get(w, [0])) >= 3)
        values["mythic_items"] = sum(1 for w in loadout["owned"] for r in loadout["copies"].get(w, []) if r >= 4) + \
            sum(1 for i in gear["inventory"] if i["rarity"] >= 4)
        values["maxed_gear"] = sum(1 for i in gear["inventory"] if i.get("level", 0) >= GEAR_MAX_LEVEL)
        return values

    @staticmethod
    def _reached(spec, progress):
        return sum(1 for goal in spec[5] if progress.get(spec[4], 0) >= goal)

    @staticmethod
    def _title_earned(state, title, progress):
        # A title stays earned once its 金 tier is claimed, even if the collection behind it later shrinks.
        return state["claimed"].get(title, 0) >= 3

    @classmethod
    def _achievement_view(cls, state, progress):
        items = []
        for spec in ACHIEVEMENTS:
            aid, category, name, text, source, goals, title = spec
            claimed = state["claimed"].get(aid, 0)
            reached = cls._reached(spec, progress)
            shown = min(claimed, 2)   # the tier being worked on (or the last one once all are claimed)
            items.append({"id": aid, "category": category, "name": name, "text": text.format(goal=goals[shown]),
                          "progress": progress.get(source, 0), "goals": goals, "reached": reached, "claimed": claimed,
                          "title": title or "", "reward": ACHIEVEMENT_REWARDS[claimed] if claimed < 3 else {}})
        return items

    @_locked
    def claim_achievement(self, account_id: int, achievement):
        """Pays out the next reached, unclaimed tier of one achievement."""
        if achievement not in ACHIEVEMENT_IDS:
            raise ValueError("找不到此成就")
        profile = self.profile(account_id)
        if profile is None:
            raise ValueError("找不到帳號")
        state = self._achievement_state(account_id)
        progress = self._achievement_progress(profile, self._raw_gear(account_id))
        spec = ACHIEVEMENT_IDS[achievement]
        claimed = state["claimed"].get(achievement, 0)
        if claimed >= 3:
            raise ValueError("此成就已全部完成")
        if self._reached(spec, progress) <= claimed:
            raise ValueError("尚未達成下一階段")
        reward = ACHIEVEMENT_REWARDS[claimed]
        state["claimed"][achievement] = claimed + 1
        title = spec[6] if claimed + 1 == 3 else None
        if title and not state["title"]:
            state["title"] = achievement   # the first title earned is worn automatically
        self._save_achievements(account_id, state)
        with self.connect() as db:
            db.execute("UPDATE progress SET gold=gold+?, keys=keys+?, weapon_keys=weapon_keys+?, shards=shards+? WHERE account_id=?",
                       (reward.get("gold", 0), reward.get("keys", 0), reward.get("weapon_keys", 0), reward.get("shards", 0), account_id))
        return {"tier": claimed, "reward": reward, "title": title or ""}, self.profile(account_id)

    @_locked
    def set_title(self, account_id: int, title):
        """Wears an earned title, or none with an empty string."""
        if not isinstance(title, str) or (title and title not in TITLES):
            raise ValueError("找不到此稱號")
        state = self._achievement_state(account_id)
        if title and state["claimed"].get(title, 0) < 3:
            raise ValueError("尚未獲得此稱號")
        state["title"] = title
        self._save_achievements(account_id, state)
        return self.profile(account_id)

    def _bump_stats(self, account_id, **amounts):
        """Adds to lifetime counters kept outside missions (supply draws, daily rewards)."""
        with self.connect() as db:
            row = db.execute("SELECT stats FROM progress WHERE account_id=?", (account_id,)).fetchone()
            total = self._parse_stats(row[0] if row else "{}")
            for key, value in amounts.items():
                total[key] += value
            db.execute("UPDATE progress SET stats=? WHERE account_id=?", (json.dumps(total), account_id))

    @_locked
    def admin_reset(self, admin_id: int, username, confirm):
        """清除玩家資料: puts one account back to a fresh start (progress, weapons, gear, keys, mail, draw history,
        friends) while keeping the login. Dangerous: the admin must type the exact account name as confirm, and can
        never wipe every account at once. An admin may wipe their own account: admin status comes from admins.txt
        by name, so it survives, and their current session is kept so they stay signed in."""
        if not isinstance(username, str) or not USERNAME.fullmatch(username):
            raise ValueError("請輸入正確的玩家帳號")
        if not isinstance(confirm, str) or confirm != username:
            raise ValueError("確認帳號不一致，未清除任何資料")
        with self.connect() as db:
            row = db.execute("SELECT id, username FROM accounts WHERE username=?", (username,)).fetchone()
            if row is None:
                raise ValueError("找不到此玩家")
            account_id = row[0]
            db.execute("DELETE FROM progress WHERE account_id=?", (account_id,))
            db.execute("INSERT INTO progress(account_id) VALUES(?)", (account_id,))
            db.execute("DELETE FROM mail WHERE account_id=?", (account_id,))
            db.execute("DELETE FROM gacha_log WHERE account_id=?", (account_id,))
            db.execute("DELETE FROM friendships WHERE requester=? OR addressee=?", (account_id, account_id))
            if account_id != admin_id:
                db.execute("DELETE FROM sessions WHERE account_id=?", (account_id,))   # signed out everywhere
        self.audit(account_id, "admin_reset", f"cleared by admin #{admin_id}")
        self.send_mail([account_id], *WELCOME_MAIL)   # a fresh start includes the welcome gift again
        return row[1]

    @staticmethod
    def _parse_cleared(raw, unlocked=0):
        """Highest difficulty cleared per region (-1 = never). Accounts from before difficulties count every region
        they have moved past as cleared on 普通."""
        try:
            data = json.loads(raw or "[]")
        except ValueError:
            data = []
        data = data if isinstance(data, list) else []
        cleared = [max(-1, min(len(DIFFICULTY_NAMES) - 1, v)) if isinstance(v, int) and not isinstance(v, bool) else -1 for v in data[:REGION_COUNT]]
        cleared += [-1] * (REGION_COUNT - len(cleared))
        for region in range(min(REGION_COUNT, unlocked)):
            cleared[region] = max(cleared[region], 0)
        return cleared

    # ---- daily missions

    def _daily(self, account_id, cleared):
        """Today's daily state, rolled over (and saved) when the day has changed."""
        with self.lock:
            with self.connect() as db:
                row = db.execute("SELECT daily FROM progress WHERE account_id=?", (account_id,)).fetchone()
            try:
                data = json.loads(row[0]) if row else {}
            except (TypeError, ValueError):
                data = {}
            data = data if isinstance(data, dict) else {}
            day = daily_day()
            tasks = data.get("tasks")
            if data.get("day") != day or not isinstance(tasks, list) or len(tasks) != DAILY_COUNT or not all(t in DAILY_TASKS for t in tasks):
                data = {"day": day, "tasks": roll_daily(account_id, day, max(cleared) >= 0), "progress": {}, "claimed": [], "bonus": False}
                self._save_daily(account_id, data)
            progress = data.get("progress") if isinstance(data.get("progress"), dict) else {}
            data["progress"] = {t: min(DAILY_TASKS[t]["goal"], progress[t]) if isinstance(progress.get(t), int) and progress[t] > 0 else 0 for t in data["tasks"]}
            claimed = data.get("claimed") if isinstance(data.get("claimed"), list) else []
            data["claimed"] = [t for t in data["tasks"] if t in claimed]
            data["bonus"] = data.get("bonus") is True
            return data

    def _save_daily(self, account_id, data):
        with self.connect() as db:
            db.execute("UPDATE progress SET daily=? WHERE account_id=?", (json.dumps(data), account_id))

    @staticmethod
    def _daily_view(data):
        tasks = []
        for t in data["tasks"]:
            spec = DAILY_TASKS[t]
            tasks.append({"id": t, "text": spec["text"].format(goal=spec["goal"]), "goal": spec["goal"], "progress": data["progress"][t],
                          "reward": spec["reward"], "done": data["progress"][t] >= spec["goal"], "claimed": t in data["claimed"]})
        return {"day": data["day"], "resets_in": daily_resets_in(), "tasks": tasks,
                "bonus": {"reward": DAILY_BONUS, "ready": len(data["claimed"]) == DAILY_COUNT, "claimed": data["bonus"]}}

    def _advance_daily(self, account_id, cleared, record):
        """Adds one mission's record to today's task progress."""
        data = self._daily(account_id, cleared)
        for t in data["tasks"]:
            if t == "deep":
                data["progress"][t] = max(data["progress"][t], record["wave"])
            else:
                data["progress"][t] += record[t]
            data["progress"][t] = min(DAILY_TASKS[t]["goal"], data["progress"][t])
        self._save_daily(account_id, data)

    # ---- weekly missions

    def _weekly(self, account_id, cleared):
        """This week's state, rolled over (and saved) when the week has changed."""
        with self.lock:
            with self.connect() as db:
                row = db.execute("SELECT weekly FROM progress WHERE account_id=?", (account_id,)).fetchone()
            try:
                data = json.loads(row[0]) if row else {}
            except (TypeError, ValueError):
                data = {}
            data = data if isinstance(data, dict) else {}
            week = weekly_week()
            tasks = data.get("tasks")
            if data.get("week") != week or not isinstance(tasks, list) or len(tasks) != WEEKLY_COUNT or not all(t in WEEKLY_TASKS for t in tasks):
                data = {"week": week, "tasks": roll_weekly(account_id, week, max(cleared) >= 0), "progress": {}, "claimed": [], "bonus": False}
                self._save_weekly(account_id, data)
            progress = data.get("progress") if isinstance(data.get("progress"), dict) else {}
            data["progress"] = {t: min(WEEKLY_TASKS[t]["goal"], progress[t]) if isinstance(progress.get(t), int) and progress[t] > 0 else 0 for t in data["tasks"]}
            claimed = data.get("claimed") if isinstance(data.get("claimed"), list) else []
            data["claimed"] = [t for t in data["tasks"] if t in claimed]
            data["bonus"] = data.get("bonus") is True
            return data

    def _save_weekly(self, account_id, data):
        with self.connect() as db:
            db.execute("UPDATE progress SET weekly=? WHERE account_id=?", (json.dumps(data), account_id))

    @staticmethod
    def _weekly_view(data):
        tasks = []
        for t in data["tasks"]:
            spec = WEEKLY_TASKS[t]
            tasks.append({"id": t, "text": spec["text"].format(goal=spec["goal"]), "goal": spec["goal"], "progress": data["progress"][t],
                          "reward": spec["reward"], "done": data["progress"][t] >= spec["goal"], "claimed": t in data["claimed"]})
        return {"day": data["week"], "resets_in": weekly_resets_in(), "tasks": tasks,
                "bonus": {"reward": WEEKLY_BONUS, "ready": len(data["claimed"]) == WEEKLY_COUNT, "claimed": data["bonus"]}}

    def _advance_weekly(self, account_id, cleared, record):
        data = self._weekly(account_id, cleared)
        for t in data["tasks"]:
            data["progress"][t] = min(WEEKLY_TASKS[t]["goal"], data["progress"][t] + record.get(t, 0))
        self._save_weekly(account_id, data)

    @_locked
    def claim_weekly(self, account_id: int, task):
        """Pays out one finished weekly task, or the all-tasks bonus (task == "bonus")."""
        profile = self.profile(account_id)
        if profile is None:
            raise ValueError("找不到帳號")
        data = self._weekly(account_id, profile["cleared"])
        if task == "bonus":
            if len(data["claimed"]) < WEEKLY_COUNT:
                raise ValueError("請先領取本週全部任務的獎勵")
            if data["bonus"]:
                raise ValueError("本週的額外獎勵已領取")
            reward, data["bonus"] = WEEKLY_BONUS, True
        else:
            if task not in data["tasks"]:
                raise ValueError("找不到此任務，可能已換週")
            if task in data["claimed"]:
                raise ValueError("此任務獎勵已領取")
            if data["progress"][task] < WEEKLY_TASKS[task]["goal"]:
                raise ValueError("任務尚未完成")
            reward = WEEKLY_TASKS[task]["reward"]
            data["claimed"].append(task)
        self._save_weekly(account_id, data)
        with self.connect() as db:
            db.execute("UPDATE progress SET gold=gold+?, keys=keys+?, weapon_keys=weapon_keys+?, shards=shards+? WHERE account_id=?",
                       (reward.get("gold", 0), reward.get("keys", 0), reward.get("weapon_keys", 0), reward.get("shards", 0), account_id))
        return reward, self.profile(account_id)

    @_locked
    def claim_daily(self, account_id: int, task):
        """Pays out one finished task, or the all-tasks bonus (task == "bonus")."""
        profile = self.profile(account_id)
        if profile is None:
            raise ValueError("找不到帳號")
        data = self._daily(account_id, profile["cleared"])
        if task == "bonus":
            if len(data["claimed"]) < DAILY_COUNT:
                raise ValueError("請先領取今天全部任務的獎勵")
            if data["bonus"]:
                raise ValueError("今天的額外獎勵已領取")
            reward, data["bonus"] = DAILY_BONUS, True
        else:
            if task not in data["tasks"]:
                raise ValueError("找不到此任務，可能已換日")
            if task in data["claimed"]:
                raise ValueError("此任務獎勵已領取")
            if data["progress"][task] < DAILY_TASKS[task]["goal"]:
                raise ValueError("任務尚未完成")
            reward = DAILY_TASKS[task]["reward"]
            data["claimed"].append(task)
            self._bump_stats(account_id, daily_claimed=1)
        self._save_daily(account_id, data)
        with self.connect() as db:
            db.execute("UPDATE progress SET gold=gold+?, keys=keys+?, weapon_keys=weapon_keys+?, shards=shards+? WHERE account_id=?",
                       (reward.get("gold", 0), reward.get("keys", 0), reward.get("weapon_keys", 0), reward.get("shards", 0), account_id))
        return reward, self.profile(account_id)

    @staticmethod
    def _parse_talents(raw):
        try:
            data = json.loads(raw or "{}")
        except ValueError:
            data = {}
        data = data if isinstance(data, dict) else {}
        return {k: min(TALENT_MAX, max(0, int(data.get(k, 0)))) if isinstance(data.get(k, 0), int) else 0 for k in TALENTS}

    @staticmethod
    def _parse_gear(raw):
        try:
            data = json.loads(raw or "{}")
        except ValueError:
            data = {}
        data = data if isinstance(data, dict) else {}
        inventory = [i for i in data.get("inventory", []) if isinstance(i, dict) and i.get("slot") in GEAR_SLOTS
                     and isinstance(i.get("rarity"), int) and 0 <= i["rarity"] < len(RARITIES) and isinstance(i.get("stats"), dict)]
        for i in inventory:
            level = i.get("level", 0)
            i["level"] = max(0, min(GEAR_MAX_LEVEL, level)) if isinstance(level, int) else 0
            limit = i.get("limit", 0)
            i["limit"] = max(0, min(GEAR_LIMIT_MAX, limit)) if isinstance(limit, int) else 0
            if i.get("set") not in GEAR_SETS:
                i.pop("set", None)
        ids = {i["id"] for i in inventory}
        equipped = {k: v for k, v in (data.get("equipped") or {}).items() if k in GEAR_SLOTS and v in ids}
        return {"inventory": inventory[:INVENTORY_LIMIT], "equipped": equipped}

    def _pity(self, account_id):
        with self.connect() as db:
            row = db.execute("SELECT pity FROM progress WHERE account_id=?", (account_id,)).fetchone()
        try:
            data = json.loads(row[0]) if row else {}
        except (TypeError, ValueError):
            data = {}
        cap = {"gear": PITY_LIMIT, "weapon": PITY_LIMIT, "limited": LIMITED_PITY, "gear_myth": 100000, "weapon_myth": 100000}
        return {pool: max(0, min(cap[pool] - 1, data.get(pool, 0))) if isinstance(data.get(pool), int) else 0 for pool in cap}

    def _save_pity(self, account_id, pity):
        with self.connect() as db:
            db.execute("UPDATE progress SET pity=? WHERE account_id=?", (json.dumps(pity), account_id))

    def _save_gear(self, account_id, gear):
        with self.connect() as db:
            db.execute("UPDATE progress SET gear=? WHERE account_id=?", (json.dumps(gear, ensure_ascii=False), account_id))

    def _raw_gear(self, account_id):
        with self.connect() as db:
            row = db.execute("SELECT gear FROM progress WHERE account_id=?", (account_id,)).fetchone()
        return self._parse_gear(row[0] if row else "{}")

    @_locked
    def assign_talent(self, account_id: int, talent, points):
        if talent not in TALENTS:
            raise ValueError("找不到此天賦")
        if not isinstance(points, int) or not 1 <= points <= TALENT_MAX:
            raise ValueError("點數不正確")
        profile = self.profile(account_id)
        if profile["talent_points"] < points:
            raise ValueError("天賦點數不足")
        talents = profile["talents"]
        if talents[talent] + points > TALENT_MAX:
            raise ValueError("此天賦已達上限")
        talents[talent] += points
        with self.connect() as db:
            db.execute("UPDATE progress SET talents=? WHERE account_id=?", (json.dumps(talents), account_id))
        return self.profile(account_id)

    @_locked
    def reset_talents(self, account_id: int):
        with self.connect() as db:
            db.execute("UPDATE progress SET talents='{}' WHERE account_id=?", (account_id,))
        return self.profile(account_id)

    GACHA_LOG_KEEP = 500

    def _log_draws(self, account_id, pool, entries):
        """Appends draws to this account's own history (oldest pruned past GACHA_LOG_KEEP)."""
        now = int(time.time())
        with self.connect() as db:
            db.executemany("INSERT INTO gacha_log(account_id, created, pool, item, rarity, outcome) VALUES(?,?,?,?,?,?)",
                           [(account_id, now, pool, item, rarity, outcome) for item, rarity, outcome in entries])
            pruning = db.execute("SELECT COUNT(*) FROM gacha_log WHERE account_id=?", (account_id,)).fetchone()[0] > self.GACHA_LOG_KEEP
        if pruning:
            self.codex(account_id)   # fold the oldest draws into the 圖鑑 before they are dropped
        with self.connect() as db:
            db.execute("DELETE FROM gacha_log WHERE account_id=? AND id NOT IN (SELECT id FROM gacha_log WHERE account_id=? ORDER BY id DESC LIMIT ?)",
                       (account_id, account_id, self.GACHA_LOG_KEEP))

    # ---- 圖鑑: every weapon and gear slot at every rarity this account has ever held. Kept as its own record
    # (the draw log is pruned), merged with the draw log and whatever is owned right now.
    def codex(self, account_id):
        with self.lock:
            with self.connect() as db:
                row = db.execute("SELECT codex FROM progress WHERE account_id=?", (account_id,)).fetchone()
                logged = db.execute("SELECT pool, item, rarity FROM gacha_log WHERE account_id=?", (account_id,)).fetchall()
            try:
                data = json.loads(row[0]) if row else {}
            except (TypeError, ValueError):
                data = {}
            data = data if isinstance(data, dict) else {}
            seen = {"weapons": {w: set() for w in WEAPON_PRICES}, "gear": {g: set() for g in GEAR_SLOTS}}
            for kind in seen:
                stored = data.get(kind) if isinstance(data.get(kind), dict) else {}
                for key, rarities in stored.items():
                    if key in seen[kind] and isinstance(rarities, list):
                        seen[kind][key].update(r for r in rarities if isinstance(r, int) and 0 <= r < len(RARITIES))
            for pool, item, rarity in logged:
                kind = "gear" if pool == "gear" else "weapons"
                if item in seen[kind] and 0 <= rarity < len(RARITIES):
                    seen[kind][item].add(rarity)
            profile = self.profile(account_id)
            for weapon, copies in profile["loadout"]["copies"].items():
                if weapon in seen["weapons"]:
                    seen["weapons"][weapon].update(copies)
            for item in profile["inventory"]:
                if item["slot"] in seen["gear"]:
                    seen["gear"][item["slot"]].add(item["rarity"])
            out = {kind: {key: sorted(v) for key, v in table.items()} for kind, table in seen.items()}
            if out != data:
                with self.connect() as db:
                    db.execute("UPDATE progress SET codex=? WHERE account_id=?", (json.dumps(out), account_id))
            total = len(RARITIES) * (len(WEAPON_PRICES) + len(GEAR_SLOTS))
            return dict(out, found=sum(len(v) for table in out.values() for v in table.values()), total=total)

    def gacha_history(self, account_id, pool=None, limit=200):
        """This account's draws, newest first. Only ever the caller's own rows."""
        if pool not in (None, "", "gear", "weapon", "limited"):
            raise ValueError("未知的補給類型")
        limit = limit if isinstance(limit, int) and not isinstance(limit, bool) and 1 <= limit <= self.GACHA_LOG_KEEP else 200
        with self.connect() as db:
            if pool:
                rows = db.execute("SELECT created, pool, item, rarity, outcome FROM gacha_log WHERE account_id=? AND pool=? ORDER BY id DESC LIMIT ?",
                                  (account_id, pool, limit)).fetchall()
            else:
                rows = db.execute("SELECT created, pool, item, rarity, outcome FROM gacha_log WHERE account_id=? ORDER BY id DESC LIMIT ?",
                                  (account_id, limit)).fetchall()
            totals = {r[0]: r[1] for r in db.execute("SELECT pool, COUNT(*) FROM gacha_log WHERE account_id=? GROUP BY pool", (account_id,))}
        return {"history": [{"time": r["created"], "pool": r["pool"], "item": r["item"], "rarity": r["rarity"], "outcome": r["outcome"]} for r in rows],
                "totals": {"gear": totals.get("gear", 0), "weapon": totals.get("weapon", 0), "limited": totals.get("limited", 0)}}

    @_locked
    def seen(self, account_id):
        if isinstance(account_id, bool) or not isinstance(account_id, int):
            raise ValueError("找不到帳號")
        with self.connect() as db:
            db.execute("UPDATE progress SET last_seen=? WHERE account_id=?", (int(time.time()), account_id))

    @_locked
    def draw(self, account_id: int, count):
        if count not in (1, 10):
            raise ValueError("一次只能抽 1 或 10 次")
        gear = self._raw_gear(account_id)
        if len(gear["inventory"]) + count > INVENTORY_LIMIT:
            raise ValueError(f"背包已滿（上限 {INVENTORY_LIMIT} 件），請先分解裝備")
        with self.connect() as db:
            changed = db.execute("UPDATE progress SET keys=keys-? WHERE account_id=? AND keys>=?", (count, account_id, count)).rowcount
        if not changed:
            raise ValueError("鑰匙不足")
        rng = random.SystemRandom()
        pity = self._pity(account_id)
        items = []
        for _ in range(count):
            rarity, pity["gear"], pity["gear_myth"] = roll_rarity(rng, pity["gear"], pity["gear_myth"])
            items.append(roll_item(rng, rarity))
        gear["inventory"].extend(items)
        self._save_gear(account_id, gear)
        self._save_pity(account_id, pity)
        self._bump_stats(account_id, draws=count)
        self._log_draws(account_id, "gear", [(item["slot"], item["rarity"], "") for item in items])
        return items, self.profile(account_id)

    @_locked
    def draw_weapons(self, account_id: int, count):
        if count not in (1, 10):
            raise ValueError("一次只能抽 1 或 10 次")
        with self.connect() as db:
            changed = db.execute("UPDATE progress SET weapon_keys=weapon_keys-? WHERE account_id=? AND weapon_keys>=?",
                                 (count, account_id, count)).rowcount
        if not changed:
            raise ValueError("武器鑰匙不足")
        rng = random.SystemRandom()
        loadout = self.profile(account_id)["loadout"]
        pity = self._pity(account_id)
        results, gold, shards = [], 0, 0
        for _ in range(count):
            weapon = rng.choices(list(WEAPON_WEIGHTS), weights=list(WEAPON_WEIGHTS.values()))[0]
            rarity, pity["weapon"], pity["weapon_myth"] = roll_rarity(rng, pity["weapon"], pity["weapon_myth"])
            result = self._receive_weapon(loadout, weapon, rarity)
            gold += result["gold"]; shards += result["shards"]
            results.append(result)
        sync_rarity(loadout)
        with self.connect() as db:
            db.execute("UPDATE progress SET loadout=?, gold=gold+?, shards=shards+?, pity=? WHERE account_id=?",
                       (json.dumps(loadout), gold, shards, json.dumps(pity), account_id))
        self._bump_stats(account_id, draws=count)
        self._log_draws(account_id, "weapon", [(w["weapon"], w["rarity"], w["outcome"]) for w in results])
        return results, self.profile(account_id)

    @staticmethod
    def _receive_weapon(loadout, weapon, rarity):
        """Puts one drawn weapon into the loadout: a new weapon, another copy, or (copies full) gold and shards."""
        gold = shards = 0
        if weapon not in loadout["owned"]:
            loadout["owned"].append(weapon)
            loadout["copies"][weapon] = [rarity]
            loadout["copy_levels"][weapon] = [{}]
            loadout["active"][weapon] = 0
            if len(loadout["equipped"]) < 2:
                loadout["equipped"].append(weapon)
            outcome = "new"
        elif len(loadout["copies"][weapon]) < WEAPON_COPY_LIMIT:
            add_copy(loadout, weapon, rarity)
            outcome = "copy"
        else:
            outcome = "full"
            gold, shards = DUPLICATE_GOLD[rarity], SALVAGE_SHARDS[rarity]
        return {"weapon": weapon, "rarity": rarity, "outcome": outcome, "gold": gold, "shards": shards}

    # ---- 限定補給
    def limited_banner(self):
        """The running limited supply drop, or None: {"weapon", "ends_at"}."""
        with self.connect() as db:
            row = db.execute("SELECT value FROM settings WHERE key='limited_banner'").fetchone()
        try:
            data = json.loads(row[0]) if row else None
        except (TypeError, ValueError):
            data = None
        if not isinstance(data, dict) or data.get("weapon") not in LIMITED_WEAPONS or not isinstance(data.get("ends_at"), int):
            return None
        return data if data["ends_at"] > time.time() else None

    def set_limited_banner(self, admin_id, weapon, days):
        """Admin: opens the limited supply for `days` days from now (0 closes it)."""
        if isinstance(days, bool) or not isinstance(days, int) or not 0 <= days <= LIMITED_MAX_DAYS:
            raise ValueError(f"天數需為 0 到 {LIMITED_MAX_DAYS} 的整數")
        weapon = weapon or "minigun"
        if weapon not in LIMITED_WEAPONS:
            raise ValueError("找不到此限定武器")
        with self.connect() as db:
            if days == 0:
                db.execute("DELETE FROM settings WHERE key='limited_banner'")
            else:
                value = json.dumps({"weapon": weapon, "ends_at": int(time.time()) + days * 86400})
                db.execute("INSERT INTO settings(key, value) VALUES('limited_banner', ?) ON CONFLICT(key) DO UPDATE SET value=excluded.value", (value,))
        return self.limited_banner()

    @_locked
    def draw_limited(self, account_id: int, count):
        """限定補給 draws (weapon keys): weapon-supply odds and pity on a separate counter; a 神話 is the limited
        weapon on a coin flip, and always after one that was not."""
        banner = self.limited_banner()
        if banner is None:
            raise ValueError("限定補給目前未開放")
        if count not in (1, 10):
            raise ValueError("一次只能抽 1 或 10 次")
        with self.connect() as db:
            changed = db.execute("UPDATE progress SET weapon_keys=weapon_keys-? WHERE account_id=? AND weapon_keys>=?",
                                 (count, account_id, count)).rowcount
            guarantee = bool(db.execute("SELECT limited_guarantee FROM progress WHERE account_id=?", (account_id,)).fetchone()[0])
        if not changed:
            raise ValueError("武器鑰匙不足")
        rng = random.SystemRandom()
        loadout = self.profile(account_id)["loadout"]
        pity = self._pity(account_id)
        results, gold, shards = [], 0, 0
        for _ in range(count):
            rarity, pity["limited"] = roll_limited_rarity(rng, pity["limited"])
            limited = False
            if rarity == len(RARITIES) - 1:
                if guarantee or rng.random() < LIMITED_MYTH_SHARE:
                    limited, guarantee = True, False
                else:
                    guarantee = True   # 歪: the next 神話 is the limited weapon
            weapon = banner["weapon"] if limited else rng.choices(list(WEAPON_WEIGHTS), weights=list(WEAPON_WEIGHTS.values()))[0]
            result = self._receive_weapon(loadout, weapon, rarity)
            result["limited"] = limited
            gold += result["gold"]; shards += result["shards"]
            results.append(result)
        sync_rarity(loadout)
        with self.connect() as db:
            db.execute("UPDATE progress SET loadout=?, gold=gold+?, shards=shards+?, pity=?, limited_guarantee=? WHERE account_id=?",
                       (json.dumps(loadout), gold, shards, json.dumps(pity), int(guarantee), account_id))
        self._bump_stats(account_id, draws=count)
        self._log_draws(account_id, "limited", [(w["weapon"], w["rarity"], w["outcome"]) for w in results])
        return results, self.profile(account_id)

    def _weapon_copy(self, account_id, weapon, index, rarity=None):
        loadout = self.profile(account_id)["loadout"]
        if weapon not in loadout["owned"]:
            raise ValueError("尚未擁有此武器")
        if isinstance(index, bool) or not isinstance(index, int) or not 0 <= index < len(loadout["copies"][weapon]):
            raise ValueError("找不到此武器副本")
        # The client sends the rarity it saw; a mismatch means the list changed underneath it (stale index).
        if rarity is not None and loadout["copies"][weapon][index] != rarity:
            raise ValueError("武器庫已變動，請重新選擇")
        return loadout

    def _save_loadout(self, account_id, loadout, gold=0, shards=0):
        sync_rarity(loadout)
        with self.connect() as db:
            db.execute("UPDATE progress SET loadout=?, gold=gold+?, shards=shards+? WHERE account_id=?", (json.dumps(loadout), gold, shards, account_id))
        return self.profile(account_id)

    @_locked
    def select_weapon_copy(self, account_id: int, weapon, index, rarity=None):
        loadout = self._weapon_copy(account_id, weapon, index, rarity)
        loadout["active"][weapon] = index
        return self._save_loadout(account_id, loadout)

    @_locked
    def salvage_weapon_copy(self, account_id: int, weapon, index, rarity=None):
        loadout = self._weapon_copy(account_id, weapon, index, rarity)
        if index == loadout["active"][weapon]:
            raise ValueError("使用中的武器無法分解")
        rarity = loadout["copies"][weapon].pop(index)
        levels = loadout["copy_levels"][weapon].pop(index)
        if loadout["active"][weapon] > index:
            loadout["active"][weapon] -= 1
        gold, shards = copy_refund(weapon, rarity, levels)
        return self._save_loadout(account_id, loadout, gold, shards)

    @_locked
    def upgrade_gear(self, account_id: int, item_id):
        """Enhances one gear item by a level; the price depends on its rarity and current level."""
        gear = self._raw_gear(account_id)
        item = next((i for i in gear["inventory"] if i["id"] == item_id), None)
        if item is None:
            raise ValueError("找不到此裝備")
        level = item.get("level", 0)
        if level >= GEAR_MAX_LEVEL:
            raise ValueError("已達最高強化等級")
        cost = gear_upgrade_cost(item["rarity"], level)
        with self.connect() as db:
            if not db.execute("UPDATE progress SET gold=gold-? WHERE account_id=? AND gold>=?", (cost, account_id, cost)).rowcount:
                raise ValueError("金幣不足")
        item["level"] = level + 1
        self._save_gear(account_id, gear)
        return self.profile(account_id)

    def _pay(self, account_id, gold, shards):
        """Takes gold and shards together, or neither."""
        with self.connect() as db:
            if not db.execute("UPDATE progress SET gold=gold-?, shards=shards-? WHERE account_id=? AND gold>=? AND shards>=?",
                              (gold, shards, account_id, gold, shards)).rowcount:
                row = db.execute("SELECT gold, shards FROM progress WHERE account_id=?", (account_id,)).fetchone()
                raise ValueError("碎片不足" if row and row[1] < shards else "金幣不足")

    def _gear_item(self, gear, item_id):
        item = next((i for i in gear["inventory"] if i["id"] == item_id), None)
        if item is None:
            raise ValueError("找不到此裝備")
        return item

    @_locked
    def reforge_gear(self, account_id: int, item_id, lock=False):
        """重鑄: rerolls the secondary stat and the set of a 稀世+ item; with lock, only the value."""
        gear = self._raw_gear(account_id)
        item = self._gear_item(gear, item_id)
        if item["rarity"] < 2:
            raise ValueError("稀世以上的裝備才能重鑄")
        lock = lock is True
        primary = GEAR_SLOTS[item["slot"]]
        current = next((k for k in item["stats"] if k != primary), None)
        self._pay(account_id, REFORGE_GOLD[item["rarity"]], REFORGE_SHARDS[item["rarity"]] + (REFORGE_LOCK_SHARDS if lock else 0))
        rng = random.SystemRandom()
        stat, value = roll_secondary(rng, primary, item["rarity"], current if lock else None)
        item["stats"] = {primary: item["stats"][primary], stat: value}
        if not lock or "set" not in item:
            item["set"] = rng.choice(list(GEAR_SETS))
        self._save_gear(account_id, gear)
        return self.profile(account_id)

    @_locked
    def limit_break_gear(self, account_id: int, item_id):
        """突破: a +5 item gains a tier (up to three), each +8% to all its stats."""
        gear = self._raw_gear(account_id)
        item = self._gear_item(gear, item_id)
        if item.get("level", 0) < GEAR_MAX_LEVEL:
            raise ValueError(f"強化到 +{GEAR_MAX_LEVEL} 後才能突破")
        tier = item.get("limit", 0)
        if tier >= GEAR_LIMIT_MAX:
            raise ValueError("已達最高突破")
        self._pay(account_id, LIMIT_GOLD[tier], LIMIT_SHARDS[tier])
        item["limit"] = tier + 1
        self._save_gear(account_id, gear)
        return self.profile(account_id)

    @_locked
    def ascend_gear(self, account_id: int, item_id, fodder_id):
        """升階: consumes another item of the same slot and rarity to raise this one's rarity by one.
        Enhancement and 突破 stay; the stats move up to the new rarity's values."""
        gear = self._raw_gear(account_id)
        item = self._gear_item(gear, item_id)
        fodder = self._gear_item(gear, fodder_id)
        if fodder is item:
            raise ValueError("請選擇另一件裝備作為素材")
        rarity = item["rarity"]
        if rarity >= len(RARITIES) - 1:
            raise ValueError("已是最高稀有度")
        if fodder["slot"] != item["slot"] or fodder["rarity"] != rarity:
            raise ValueError("素材必須是同部位、同稀有度的裝備")
        if fodder["id"] in gear["equipped"].values():
            raise ValueError("素材裝備使用中，請先卸下")
        self._pay(account_id, ASCEND_GOLD[rarity], ASCEND_SHARDS[rarity])
        gear["inventory"].remove(fodder)
        rng = random.SystemRandom()
        primary = GEAR_SLOTS[item["slot"]]
        stats = {primary: GEAR_VALUES[primary][rarity + 1]}
        for k, v in item["stats"].items():
            if k != primary:
                stats[k] = max(1, int(round(v * GEAR_VALUES[k][rarity + 1] / max(1, GEAR_VALUES[k][rarity]))))
        if rarity + 1 >= 2 and len(stats) == 1:
            stat, value = roll_secondary(rng, primary, rarity + 1)
            stats[stat] = value
        item["stats"] = stats
        item["rarity"] = rarity + 1
        if item["rarity"] >= GEAR_SET_MIN_RARITY and item.get("set") not in GEAR_SETS:
            item["set"] = rng.choice(list(GEAR_SETS))
        self._save_gear(account_id, gear)
        return self.profile(account_id)

    @_locked
    def equip_gear(self, account_id: int, item_id):
        gear = self._raw_gear(account_id)
        item = next((i for i in gear["inventory"] if i["id"] == item_id), None)
        if item is None:
            raise ValueError("找不到此裝備")
        if gear["equipped"].get(item["slot"]) == item_id:
            del gear["equipped"][item["slot"]]
        else:
            gear["equipped"][item["slot"]] = item_id
        self._save_gear(account_id, gear)
        return self.profile(account_id)

    @_locked
    def salvage(self, account_id: int, item_id):
        """Breaks an unequipped item down for gold."""
        gear = self._raw_gear(account_id)
        item = next((i for i in gear["inventory"] if i["id"] == item_id), None)
        if item is None:
            raise ValueError("找不到此裝備")
        if item_id in gear["equipped"].values():
            raise ValueError("請先卸下此裝備")
        gear["inventory"].remove(item)
        self._save_gear(account_id, gear)
        with self.connect() as db:
            db.execute("UPDATE progress SET gold=gold+?, shards=shards+? WHERE account_id=?",
                       (GEAR_SALVAGE_GOLD[item["rarity"]] + refund_gold(gear_invested(item)), SALVAGE_SHARDS[item["rarity"]] + gear_invested_shards(item), account_id))
        return self.profile(account_id)

    @_locked
    def salvage_bulk(self, account_id: int, kind, rarities, confirm=False, slot=None):
        """Salvages every unequipped item (gear) or every inactive copy (weapons) of the chosen rarities; for gear,
        optionally only one slot (the tab the bag is on). Anything 傳奇 or above needs confirm=True, so a stray
        request can never wipe the best loot."""
        if kind not in ("gear", "weapon"):
            raise ValueError("分解類型不正確")
        if slot in ("", None):
            slot = None
        elif kind != "gear" or slot not in GEAR_SLOTS:
            raise ValueError("裝備部位不正確")
        if not isinstance(rarities, list) or not rarities or not all(isinstance(r, int) and not isinstance(r, bool) and 0 <= r < len(RARITIES) for r in rarities):
            raise ValueError("請選擇要分解的稀有度")
        chosen = set(rarities)
        if max(chosen) >= 3 and confirm is not True:
            raise ValueError("包含傳奇以上，請先確認")
        count, gold, shards = 0, 0, 0
        if kind == "gear":
            gear = self._raw_gear(account_id)
            worn = set(gear["equipped"].values())
            kept = []
            for item in gear["inventory"]:
                if item["rarity"] in chosen and item["id"] not in worn and (slot is None or item["slot"] == slot):
                    count += 1
                    gold += GEAR_SALVAGE_GOLD[item["rarity"]] + refund_gold(gear_invested(item))
                    shards += SALVAGE_SHARDS[item["rarity"]] + gear_invested_shards(item)
                else:
                    kept.append(item)
            gear["inventory"] = kept
            self._save_gear(account_id, gear)
            with self.connect() as db:
                db.execute("UPDATE progress SET gold=gold+?, shards=shards+? WHERE account_id=?", (gold, shards, account_id))
            return count, gold, shards, self.profile(account_id)
        loadout = self.profile(account_id)["loadout"]
        for weapon in loadout["owned"]:
            copies, active = loadout["copies"][weapon], loadout["active"][weapon]
            kept, kept_levels = [], []
            for i, rarity in enumerate(copies):
                levels = loadout["copy_levels"][weapon][i]
                if rarity in chosen and i != active:
                    count += 1
                    g, sh = copy_refund(weapon, rarity, levels)
                    gold += g
                    shards += sh
                else:
                    if i == active:
                        loadout["active"][weapon] = len(kept)
                    kept.append(rarity)
                    kept_levels.append(levels)
            loadout["copies"][weapon], loadout["copy_levels"][weapon] = kept, kept_levels
        return count, gold, shards, self._save_loadout(account_id, loadout, gold, shards)

    @_locked
    def add_xp(self, account_id: int, xp: int):
        with self.connect() as db:
            db.execute("UPDATE progress SET xp=xp+? WHERE account_id=?", (max(0, min(5000, xp)), account_id))

    STAT_KEYS = ("kills", "headshots", "shots", "hits", "missions", "victories", "best_wave", "endless_best", "endless_runs",
                 "boss_kills", "elite_kills", "revives", "flawless", "s_grades", "hard_victories", "draws", "daily_claimed")

    @classmethod
    def _parse_stats(cls, raw):
        try:
            data = json.loads(raw or "{}")
        except ValueError:
            data = {}
        if not isinstance(data, dict):
            data = {}
        return {k: int(data.get(k, 0)) if isinstance(data.get(k, 0), int) else 0 for k in cls.STAT_KEYS}

    @_locked
    def grades(self, account_id: int):
        with self.connect() as db:
            row = db.execute("SELECT grades FROM progress WHERE account_id=?", (account_id,)).fetchone()
        try:
            data = json.loads(row[0]) if row else {}
        except (TypeError, ValueError):
            data = {}
        return {k: v for k, v in data.items() if v in GRADE_ORDER} if isinstance(data, dict) else {}

    def record_stats(self, account_id: int, stats):
        """Adds one mission's combat record (reported by the game server) to lifetime stats."""
        if not isinstance(stats, dict):
            raise ValueError("戰績格式錯誤")
        values = {}
        endless = stats.get("endless") is True
        for key, limit in (("kills", 50000 if endless else 5000), ("headshots", 50000 if endless else 5000), ("shots", 1000000 if endless else 100000),
                           ("hits", 1000000 if endless else 100000), ("wave", ENDLESS_MAX_WAVE if endless else 20)):
            value = stats.get(key, 0)
            if not isinstance(value, int) or not 0 <= value <= limit:
                raise ValueError("戰績數值不合法")
            values[key] = value
        if values["hits"] > values["shots"] or values["headshots"] > values["hits"] * 7:
            raise ValueError("戰績數值不合法")
        # Achievement records (optional: older game servers do not send them).
        for key, limit in (("downs", 1000), ("revives", 1000), ("boss_kills", ENDLESS_MAX_WAVE // 10 if endless else 2), ("elite_kills", 200 if endless else 20)):
            value = stats.get(key, 0)
            if isinstance(value, bool) or not isinstance(value, int) or not 0 <= value <= limit:
                raise ValueError("戰績數值不合法")
            values[key] = value
        grade = stats.get("grade", "")
        if grade not in ("", "S", "A", "B", "C"):
            raise ValueError("戰績數值不合法")
        victory = stats.get("victory") is True and not endless
        tier = stats.get("difficulty", 0)
        if isinstance(tier, bool) or not isinstance(tier, int) or not 0 <= tier < len(DIFFICULTY_NAMES):
            raise ValueError("難度不合法")
        profile = self.profile(account_id)
        if profile is None:
            raise ValueError("找不到帳號")
        total = profile["stats"]
        for key in ("kills", "headshots", "shots", "hits"):
            total[key] += values[key]
        total["missions"] += 1
        total["victories"] += 1 if victory else 0
        if endless:
            total["endless_best"] = max(total["endless_best"], values["wave"])
            total["endless_runs"] += 1
        else:
            total["best_wave"] = max(total["best_wave"], values["wave"])
        for key in ("boss_kills", "elite_kills", "revives"):
            total[key] += values[key]
        total["flawless"] += 1 if victory and values["downs"] == 0 and "downs" in stats else 0
        total["s_grades"] += 1 if victory and grade == "S" else 0
        total["hard_victories"] += 1 if victory and tier >= 1 else 0
        with self.connect() as db:
            db.execute("UPDATE progress SET stats=? WHERE account_id=?", (json.dumps(total), account_id))
        # Best grade per stage and difficulty ("region:tier"), shown on the deploy screen.
        region = stats.get("region")
        if victory and grade and isinstance(region, int) and not isinstance(region, bool) and 0 <= region < REGION_COUNT:
            grades = self.grades(account_id)
            slot = f"{region}:{tier}"
            if GRADE_ORDER.index(grade) < GRADE_ORDER.index(grades.get(slot, "C")) or slot not in grades:
                grades[slot] = grade
                with self.connect() as db:
                    db.execute("UPDATE progress SET grades=? WHERE account_id=?", (json.dumps(grades), account_id))
        self._advance_daily(account_id, profile["cleared"], {
            "kills": values["kills"], "headshots": values["headshots"], "missions": 1, "waves": values["wave"], "wave": values["wave"],
            "victories": 1 if victory else 0, "hard": 1 if victory and tier >= 1 else 0})
        self._advance_weekly(account_id, profile["cleared"], {
            "kills": values["kills"], "headshots": values["headshots"], "missions": 1, "waves": values["wave"], "victories": 1 if victory else 0,
            "bosses": values["boss_kills"], "hard": 1 if victory and tier >= 1 else 0, "s_grades": 1 if victory and grade == "S" else 0})

    def endless_leaderboard(self, account_id=None, limit=20):
        """Best endless wave per player, highest first; banned accounts are left out."""
        with self.connect() as db:
            rows = db.execute("SELECT a.id,a.username,p.stats FROM accounts a JOIN progress p ON p.account_id=a.id").fetchall()
        banned = self._banned_ids() if hasattr(self, "_banned_ids") else set()
        board = []
        for row in rows:
            best = self._parse_stats(row["stats"])["endless_best"]
            if best > 0 and row["id"] not in banned:
                board.append({"id": row["id"], "username": row["username"], "wave": best})
        board.sort(key=lambda x: (-x["wave"], x["username"]))
        for i, entry in enumerate(board):
            entry["rank"] = i + 1
        mine = next((e for e in board if e["id"] == account_id), None)
        return {"top": [{k: e[k] for k in ("rank", "username", "wave")} for e in board[:limit]],
                "me": {k: mine[k] for k in ("rank", "username", "wave")} if mine else None}

    # ---- anti-cheat

    def audit(self, account_id, reason, detail, rejected=False):
        with self.connect() as db:
            db.execute("INSERT INTO audit(account_id,created,reason,detail,rejected) VALUES(?,?,?,?,?)",
                       (account_id, int(time.time()), reason, json.dumps(detail, ensure_ascii=False)[:2000], 1 if rejected else 0))
            db.execute("DELETE FROM audit WHERE id <= (SELECT MAX(id) FROM audit) - 5000")   # keep the log bounded

    @_locked
    def verify_reward(self, account_id: int, gold, stats):
        """Cross-checks a mission report from the game server before anything is paid out. Impossible reports are
        rejected and logged; merely unusual ones (inhuman accuracy, aim corrections) are logged for the admins."""
        if not isinstance(stats, dict) or not isinstance(gold, int) or isinstance(gold, bool):
            raise ValueError("戰績格式錯誤")
        def number(key, low, high, default=None, kind=int):
            value = stats.get(key, default)
            if value is None:
                return None
            if isinstance(value, bool) or not isinstance(value, (int, float) if kind is float else int) or not low <= value <= high:
                raise ValueError("戰績數值不合法")
            return value
        endless = stats.get("endless") is True
        wave = number("wave", 0, ENDLESS_MAX_WAVE if endless else 20, 0)
        kills = number("kills", 0, 50000 if endless else 5000, 0)
        team = number("team_kills", 0, 50000 if endless else 5000)
        level = number("level", 1, 300)
        elapsed = number("elapsed", 0, 86400, None, float)
        region = number("region", 0, REGION_COUNT - 1)
        tier = number("difficulty", 0, len(DIFFICULTY_NAMES) - 1, 0)
        aim = number("aim_flags", 0, 1_000_000, 0)
        victory = stats.get("victory") is True
        problems = []
        if victory and endless:
            problems.append("無盡模式不會勝利結算")
        if victory and wave != 20:
            problems.append("勝利但未完成 20 波")
        if team is not None and kills > team:
            problems.append("個人擊殺多於全隊擊殺")
        if team is not None and team > 90 * max(1, wave):
            problems.append("全隊擊殺數超出波次上限")
        if elapsed is not None and elapsed < max(0, wave - 1) * self.min_wave_seconds:
            problems.append(f"作戰時間過短（{elapsed:.0f} 秒到達第 {wave} 波）")
        if None not in (team, level, region):
            expected = mission_gold(team, wave, level, victory, region, tier, endless)
            if gold > expected + 1:
                problems.append(f"金幣 {gold} 超過可得上限 {expected}")
        now = time.monotonic()
        last = self.last_reward.get(account_id)
        if last is not None and self.min_wave_seconds > 0 and now - last < max(1.0, max(0, wave - 1) * self.min_wave_seconds * 0.5):
            problems.append("結算過於頻繁")
        detail = {k: stats.get(k) for k in ("kills", "team_kills", "wave", "elapsed", "level", "region", "difficulty", "victory", "endless", "shots", "hits", "headshots", "aim_flags")}
        detail["gold"] = gold
        if problems:
            self.audit(account_id, "；".join(problems), detail, rejected=True)
            raise ValueError("結算資料異常，已記錄並暫停發放")
        shots, hits, heads = stats.get("shots", 0), stats.get("hits", 0), stats.get("headshots", 0)
        flags = []
        if aim > 30:
            flags.append(f"瞄準方向與視角不符 {aim} 次")
        if isinstance(shots, int) and isinstance(hits, int) and shots >= 300 and hits > shots * 0.97:
            flags.append(f"命中率異常（{hits}/{shots}）")
        if isinstance(hits, int) and isinstance(heads, int) and hits >= 200 and heads > hits * 0.9:
            flags.append(f"爆頭率異常（{heads}/{hits}）")
        if flags:
            self.audit(account_id, "；".join(flags), detail)
        self.last_reward[account_id] = now

    def audit_log(self, limit=100):
        with self.connect() as db:
            rows = db.execute("SELECT a.username, l.created, l.reason, l.rejected FROM audit l JOIN accounts a ON a.id=l.account_id "
                              "ORDER BY l.id DESC LIMIT ?", (limit,)).fetchall()
        return [dict(row) for row in rows]

    # ---- 意見回饋
    @_locked
    def send_feedback(self, account_id: int, category, body):
        if category not in FEEDBACK_CATEGORIES:
            raise ValueError("請選擇回饋類別")
        body = body.strip() if isinstance(body, str) else ""
        if len(body) < 5:
            raise ValueError("內容至少 5 個字")
        if len(body) > FEEDBACK_MAX:
            raise ValueError(f"內容最多 {FEEDBACK_MAX} 字")
        with self.connect() as db:
            db.execute("INSERT INTO feedback(account_id, created, category, body) VALUES(?,?,?,?)",
                       (account_id, int(time.time()), category, body))
        return self.my_feedback(account_id)

    def my_feedback(self, account_id: int):
        """This account's own feedback, newest first."""
        with self.connect() as db:
            rows = db.execute("SELECT id, created, category, body, status, reply, replied FROM feedback WHERE account_id=? ORDER BY id DESC LIMIT 50",
                              (account_id,)).fetchall()
        return {"feedback": [dict(r) for r in rows]}

    def list_feedback(self, status=None, limit=200):
        """Admin: every account's feedback, newest first, optionally only open or done."""
        if status not in (None, "", "open", "done"):
            raise ValueError("未知的狀態")
        query = ("SELECT f.id, a.username, f.created, f.category, f.body, f.status, f.reply, f.replied FROM feedback f "
                 "JOIN accounts a ON a.id=f.account_id")
        args = []
        if status:
            query += " WHERE f.status=?"
            args.append(status)
        query += " ORDER BY f.id DESC LIMIT ?"
        args.append(limit)
        with self.connect() as db:
            rows = db.execute(query, args).fetchall()
            open_count = db.execute("SELECT COUNT(*) FROM feedback WHERE status='open'").fetchone()[0]
        return {"feedback": [dict(r) for r in rows], "open": open_count}

    @_locked
    def resolve_feedback(self, feedback_id, reply="", reopen=False):
        """Admin: marks a feedback handled (or open again); a non-empty reply is mailed to its author."""
        if isinstance(feedback_id, bool) or not isinstance(feedback_id, int):
            raise ValueError("找不到此回饋")
        reply = reply.strip() if isinstance(reply, str) else ""
        if len(reply) > FEEDBACK_MAX:
            raise ValueError(f"回覆最多 {FEEDBACK_MAX} 字")
        with self.connect() as db:
            row = db.execute("SELECT account_id, category, body FROM feedback WHERE id=?", (feedback_id,)).fetchone()
            if row is None:
                raise ValueError("找不到此回饋")
            if reopen:
                db.execute("UPDATE feedback SET status='open' WHERE id=?", (feedback_id,))
            else:
                db.execute("UPDATE feedback SET status='done', reply=CASE WHEN ?<>'' THEN ? ELSE reply END, replied=CASE WHEN ?<>'' THEN ? ELSE replied END WHERE id=?",
                           (reply, reply, reply, int(time.time()), feedback_id))
        if reply and not reopen:
            quoted = row["body"] if len(row["body"]) <= 80 else row["body"][:80] + "…"
            self.send_mail([row["account_id"]], f"回覆你的{FEEDBACK_CATEGORIES.get(row['category'], '回饋')}",
                           f"{reply}\n\n——你寫的內容——\n{quoted}")
        return self.list_feedback()

    # ---- system mail

    MAIL_LIMIT = 100

    def send_mail(self, account_ids, title, body, reward=None, days=30):
        reward = reward or {}
        now = int(time.time())
        with self.connect() as db:
            db.executemany("INSERT INTO mail(account_id,title,body,gold,keys,weapon_keys,created,expires) VALUES(?,?,?,?,?,?,?,?)",
                           [(aid, title, body, reward.get("gold", 0), reward.get("keys", 0), reward.get("weapon_keys", 0), now, now + days * 86400)
                            for aid in account_ids])

    def _mail_rows(self, account_id, mail_id=None):
        query = "SELECT * FROM mail WHERE account_id=? AND deleted=0 AND expires>?"
        args = [account_id, int(time.time())]
        if mail_id is not None:
            query += " AND id=?"
            args.append(mail_id)
        with self.connect() as db:
            return [dict(r) for r in db.execute(query + " ORDER BY id DESC LIMIT ?", (*args, self.MAIL_LIMIT)).fetchall()]

    @staticmethod
    def _mail_view(row):
        reward = {k: row[k] for k in ("gold", "keys", "weapon_keys") if row[k]}
        return {"id": row["id"], "title": row["title"], "body": row["body"], "reward": reward, "created": row["created"],
                "expires": row["expires"], "read": bool(row["read"]), "claimed": bool(row["claimed"]) or not reward}

    def mail_list(self, account_id):
        return [self._mail_view(r) for r in self._mail_rows(account_id)]

    def mail_counts(self, account_id):
        rows = self._mail_rows(account_id)
        views = [self._mail_view(r) for r in rows]
        return {"unread": sum(1 for v in views if not v["read"]), "claimable": sum(1 for v in views if not v["claimed"])}

    def _one_mail(self, account_id, mail_id):
        if isinstance(mail_id, bool) or not isinstance(mail_id, int):
            raise ValueError("找不到此郵件")
        rows = self._mail_rows(account_id, mail_id)
        if not rows:
            raise ValueError("找不到此郵件，可能已過期")
        return rows[0]

    @_locked
    def read_mail(self, account_id, mail_id):
        self._one_mail(account_id, mail_id)
        with self.connect() as db:
            db.execute("UPDATE mail SET read=1 WHERE id=?", (mail_id,))

    @_locked
    def claim_mail(self, account_id, mail_id=None):
        """Collects one mail's attachment, or every unclaimed attachment when mail_id is None."""
        rows = [self._one_mail(account_id, mail_id)] if mail_id is not None else self._mail_rows(account_id)
        rows = [r for r in rows if not r["claimed"] and (r["gold"] or r["keys"] or r["weapon_keys"])]
        if not rows:
            raise ValueError("沒有可領取的附件")
        total = {k: sum(r[k] for r in rows) for k in ("gold", "keys", "weapon_keys")}
        with self.connect() as db:
            for r in rows:
                if db.execute("UPDATE mail SET claimed=1, read=1 WHERE id=? AND claimed=0", (r["id"],)).rowcount != 1:
                    raise ValueError("附件已領取")
            db.execute("UPDATE progress SET gold=gold+?, keys=keys+?, weapon_keys=weapon_keys+? WHERE account_id=?",
                       (total["gold"], total["keys"], total["weapon_keys"], account_id))
        return {k: v for k, v in total.items() if v}, len(rows)

    @_locked
    def delete_mail(self, account_id, mail_id):
        row = self._one_mail(account_id, mail_id)
        if not self._mail_view(row)["claimed"]:
            raise ValueError("請先領取附件再刪除")
        with self.connect() as db:
            db.execute("UPDATE mail SET deleted=1 WHERE id=?", (mail_id,))

    def admin_mail(self, username, title, body, gold=0, keys=0, weapon_keys=0, days=30):
        if not isinstance(title, str) or not 1 <= len(title.strip()) <= 40:
            raise ValueError("標題需為 1–40 個字")
        if not isinstance(body, str) or len(body) > 500:
            raise ValueError("內容最多 500 個字")
        for value, high in ((gold, 100000), (keys, 1000), (weapon_keys, 1000)):
            if isinstance(value, bool) or not isinstance(value, int) or not 0 <= value <= high:
                raise ValueError("附件數量不正確")
        if isinstance(days, bool) or not isinstance(days, int) or not 1 <= days <= 90:
            raise ValueError("保存天數需為 1–90")
        ids = self._target_ids(username)
        self.send_mail(ids, title.strip(), body.strip(), {"gold": gold, "keys": keys, "weapon_keys": weapon_keys}, days)
        return len(ids)

    def _target_ids(self, username):
        if not isinstance(username, str) or not (username == "*" or USERNAME.fullmatch(username)):
            raise ValueError("請輸入正確的玩家帳號")
        with self.connect() as db:
            if username == "*":
                return [row[0] for row in db.execute("SELECT id FROM accounts")]
            row = db.execute("SELECT id FROM accounts WHERE username=?", (username,)).fetchone()
        if row is None:
            raise ValueError("找不到此玩家")
        return [row[0]]

    # ---- friends

    FRIEND_LIMIT = 100
    PENDING_LIMIT = 50

    def _account_by_name(self, username):
        if not isinstance(username, str) or not USERNAME.fullmatch(username):
            raise ValueError("請輸入正確的玩家帳號")
        with self.connect() as db:
            row = db.execute("SELECT id, username FROM accounts WHERE username=?", (username,)).fetchone()
        if row is None:
            raise ValueError("找不到此玩家")
        return row["id"], row["username"]

    def friend_ids(self, account_id):
        with self.connect() as db:
            rows = db.execute("SELECT requester, addressee FROM friendships WHERE status='accepted' AND (requester=? OR addressee=?)",
                              (account_id, account_id)).fetchall()
        return [r[1] if r[0] == account_id else r[0] for r in rows]

    def _briefs(self, ids):
        """Name, level and worn title for many accounts in one query (no full profile per friend)."""
        if not ids:
            return {}
        with self.connect() as db:
            rows = db.execute("SELECT a.id, a.username, p.xp, p.achievements, p.last_seen FROM accounts a JOIN progress p ON p.account_id=a.id "
                              "WHERE a.id IN (%s)" % ",".join("?" * len(ids)), list(ids)).fetchall()
        out = {}
        for row in rows:
            try:
                ach = json.loads(row["achievements"] or "{}")
            except ValueError:
                ach = {}
            ach = ach if isinstance(ach, dict) else {}
            title = ach.get("title") if isinstance(ach.get("title"), str) else ""
            claimed = ach.get("claimed") if isinstance(ach.get("claimed"), dict) else {}
            earned = title in TITLES and isinstance(claimed.get(title), int) and claimed[title] >= 3
            out[row["id"]] = {"id": row["id"], "username": row["username"], "level": level_for(row["xp"])[0], "title": TITLES[title] if earned else "",
                              "last_seen": row["last_seen"] or 0}
        return out

    def friends(self, account_id):
        with self.db_session():
            with self.connect() as db:
                incoming = [r[0] for r in db.execute("SELECT requester FROM friendships WHERE addressee=? AND status='pending' ORDER BY created", (account_id,))]
                outgoing = [r[0] for r in db.execute("SELECT addressee FROM friendships WHERE requester=? AND status='pending' ORDER BY created", (account_id,))]
            friend_ids = self.friend_ids(account_id)
            briefs = self._briefs(set(friend_ids) | set(incoming) | set(outgoing))
        friends = sorted((briefs[i] for i in friend_ids if i in briefs), key=lambda f: f["username"].lower())
        return {"friends": friends, "incoming": [briefs[i] for i in incoming if i in briefs], "outgoing": [briefs[i] for i in outgoing if i in briefs]}

    @_locked
    def player_card(self, viewer_id, username):
        """Public card of another player: level, title, equipped weapons and gear, mission record and achievement
        tiers, plus how the viewer stands with them (self / friend / sent / received / none). Never gold, keys or
        anything private."""
        target, name = self._account_by_name(username)
        p = self.profile(target)
        stats = p.get("stats") or {}
        shots = stats.get("shots", 0)
        lo = p["loadout"]
        weapons = [{"weapon": w, "rarity": lo.get("rarity", {}).get(w, 1)} for w in lo.get("equipped", [])]
        worn = {i["id"]: i for i in p["inventory"]}
        gear = [{"slot": slot, "rarity": worn[item]["rarity"], "level": worn[item].get("level", 0), "limit": worn[item].get("limit", 0),
                 "set": worn[item].get("set", "")} for slot, item in p["equipment"].items() if item in worn]
        tiers = sum(a["claimed"] for a in p.get("achievements", []))
        with self.connect() as db:
            mine = db.execute("SELECT status FROM friendships WHERE requester=? AND addressee=?", (viewer_id, target)).fetchone()
            theirs = db.execute("SELECT status FROM friendships WHERE requester=? AND addressee=?", (target, viewer_id)).fetchone()
        relation = "self" if target == viewer_id else "friend" if (mine and mine[0] == "accepted") or (theirs and theirs[0] == "accepted")             else "sent" if mine else "received" if theirs else "none"
        return {"id": target, "username": name, "level": p["level"], "title": p.get("title", {}).get("text", ""),
                "weapons": weapons, "gear": gear, "achievement_tiers": tiers, "achievement_total": 3 * len(ACHIEVEMENTS),
                "missions": stats.get("missions", 0), "victories": stats.get("victories", 0), "kills": stats.get("kills", 0),
                "accuracy": round(stats.get("hits", 0) / shots, 3) if shots else 0, "best_wave": stats.get("best_wave", 0),
                "endless_best": stats.get("endless_best", 0), "relation": relation}

    def request_friend(self, account_id, username):
        target, name = self._account_by_name(username)
        if target == account_id:
            raise ValueError("不能加自己為好友")
        with self.connect() as db:
            mine = db.execute("SELECT status FROM friendships WHERE requester=? AND addressee=?", (account_id, target)).fetchone()
            theirs = db.execute("SELECT status FROM friendships WHERE requester=? AND addressee=?", (target, account_id)).fetchone()
            if (mine and mine[0] == "accepted") or (theirs and theirs[0] == "accepted"):
                raise ValueError(f"{name} 已經是你的好友")
            if mine:
                raise ValueError("已送出邀請，等待對方回應")
            if theirs:   # they asked first: asking back accepts
                db.execute("UPDATE friendships SET status='accepted' WHERE requester=? AND addressee=?", (target, account_id))
                return "accepted"
        if len(self.friend_ids(account_id)) >= self.FRIEND_LIMIT:
            raise ValueError(f"好友已達上限（{self.FRIEND_LIMIT} 人）")
        with self.connect() as db:
            if db.execute("SELECT COUNT(*) FROM friendships WHERE requester=? AND status='pending'", (account_id,)).fetchone()[0] >= self.PENDING_LIMIT:
                raise ValueError("送出的邀請太多，請等待對方回應")
            db.execute("INSERT INTO friendships VALUES(?,?,'pending',?)", (account_id, target, int(time.time())))
        return "pending"

    @_locked
    def respond_friend(self, account_id, username, accept):
        requester, _ = self._account_by_name(username)
        if accept is True and len(self.friend_ids(account_id)) >= self.FRIEND_LIMIT:
            raise ValueError(f"好友已達上限（{self.FRIEND_LIMIT} 人）")
        with self.connect() as db:
            if accept is True:
                changed = db.execute("UPDATE friendships SET status='accepted' WHERE requester=? AND addressee=? AND status='pending'",
                                     (requester, account_id)).rowcount
            else:
                changed = db.execute("DELETE FROM friendships WHERE requester=? AND addressee=? AND status='pending'", (requester, account_id)).rowcount
        if not changed:
            raise ValueError("找不到此好友邀請")

    @_locked
    def remove_friend(self, account_id, username):
        """Removes a friend, or cancels a request this account sent."""
        other, _ = self._account_by_name(username)
        with self.connect() as db:
            changed = db.execute("DELETE FROM friendships WHERE (requester=? AND addressee=?) OR (requester=? AND addressee=? AND status='accepted')",
                                 (account_id, other, other, account_id)).rowcount
        if not changed:
            raise ValueError("你們不是好友")

    def is_admin(self, username: str) -> bool:
        try:
            lines = self.admin_file.read_text(encoding="utf-8-sig").splitlines()
        except OSError:
            return False
        names = {line.strip().lower() for line in lines if line.strip() and not line.strip().startswith("#")}
        return username.lower() in names

    def list_players(self):
        with self.connect() as db:
            rows = db.execute("SELECT a.username,p.gold,p.unlocked,(SELECT COUNT(*) FROM audit l WHERE l.account_id=a.id) AS flags "
                              "FROM accounts a JOIN progress p ON p.account_id=a.id ORDER BY a.username COLLATE NOCASE LIMIT 500").fetchall()
        return [dict(row) for row in rows]

    @_locked
    def admin_grant(self, username, gold, weapon, keys=0, weapon_keys=0, rarity=1, shards=0, gear=None, gear_rarity=1, gear_count=1):
        """Gives gold, keys, shards, optionally a weapon at a chosen rarity and optionally gear pieces at a chosen
        rarity (a slot, or "random" for a random slot per piece). A player without that weapon gets it (equipped if
        a slot is free); one who has it gets the copy added to their armory. Gear goes into the bag."""
        self._check_reward(gold, weapon, keys, weapon_keys, rarity, shards, gear, gear_rarity, gear_count)
        if not isinstance(username, str) or not (username == "*" or USERNAME.fullmatch(username)):
            raise ValueError("請輸入正確的玩家帳號")
        with self.connect() as db:
            if username == "*":
                ids = [row[0] for row in db.execute("SELECT id FROM accounts")]
            else:
                row = db.execute("SELECT id FROM accounts WHERE username=?", (username,)).fetchone()
                if row is None:
                    raise ValueError("找不到此玩家")
                ids = [row[0]]
        self._grant(ids, gold, weapon, keys, weapon_keys, rarity, shards, gear, gear_rarity, gear_count)
        return len(ids)

    @staticmethod
    def _check_reward(gold, weapon, keys=0, weapon_keys=0, rarity=1, shards=0, gear=None, gear_rarity=1, gear_count=1):
        def count_ok(v, high):
            return isinstance(v, int) and not isinstance(v, bool) and 0 <= v <= high
        if not count_ok(gold, 100000):
            raise ValueError("金幣數量需為 0–100000")
        if not count_ok(keys, 1000) or not count_ok(weapon_keys, 1000):
            raise ValueError("鑰匙數量需為 0–1000")
        if not count_ok(shards, 1000):
            raise ValueError("碎片數量需為 0–1000")
        if weapon not in (None, "") and weapon not in WEAPON_PRICES:
            raise ValueError("找不到此武器")
        if isinstance(rarity, bool) or not isinstance(rarity, int) or not 0 <= rarity < len(RARITIES):
            raise ValueError("稀有度不正確")
        if gear not in (None, "", "random") and gear not in GEAR_SLOTS:
            raise ValueError("找不到此裝備部位")
        if gear and (isinstance(gear_rarity, bool) or not isinstance(gear_rarity, int) or not 0 <= gear_rarity < len(RARITIES)):
            raise ValueError("裝備稀有度不正確")
        if gear and not (count_ok(gear_count, 10) and gear_count >= 1):
            raise ValueError("裝備數量需為 1–10")
        if gold == 0 and not weapon and keys == 0 and weapon_keys == 0 and shards == 0 and not gear:
            raise ValueError("請選擇要發放的金幣、鑰匙、碎片、武器或裝備")

    def _grant(self, ids, gold, weapon, keys=0, weapon_keys=0, rarity=1, shards=0, gear=None, gear_rarity=1, gear_count=1):
        with self.connect() as db:
            db.execute("UPDATE progress SET gold=gold+?, keys=keys+?, weapon_keys=weapon_keys+?, shards=shards+? WHERE account_id IN (%s)" % ",".join("?" * len(ids)),
                       (gold, keys, weapon_keys, shards, *ids)) if ids else None
        if gear:
            rng = random.SystemRandom()
            for account_id in ids:
                bag = self._raw_gear(account_id)
                room = max(0, INVENTORY_LIMIT - len(bag["inventory"]))   # a full bag takes what fits
                for _ in range(min(gear_count, room)):
                    item = roll_item(rng, gear_rarity)
                    if gear != "random" and item["slot"] != gear:
                        # Same rarity, the chosen slot: re-roll the stats for that slot.
                        primary = GEAR_SLOTS[gear]
                        stats = {primary: GEAR_VALUES[primary][gear_rarity]}
                        if gear_rarity >= 2:
                            secondary = rng.choice([k for k in GEAR_VALUES if k != primary])
                            stats[secondary] = max(1, GEAR_VALUES[secondary][gear_rarity] // 2)
                        item = {"id": item["id"], "slot": gear, "rarity": gear_rarity, "stats": stats}
                    bag["inventory"].append(item)
                self._save_gear(account_id, bag)
        if weapon:
            for account_id in ids:
                profile = self.profile(account_id)
                loadout = profile["loadout"]
                if weapon not in loadout["owned"]:
                    loadout["owned"].append(weapon)
                    loadout["copies"][weapon] = [rarity]
                    loadout["copy_levels"][weapon] = [{}]
                    loadout["active"][weapon] = 0
                    if len(loadout["equipped"]) < 2:
                        loadout["equipped"].append(weapon)
                elif len(loadout["copies"][weapon]) < WEAPON_COPY_LIMIT:
                    add_copy(loadout, weapon, rarity)   # kept in the armory; the player picks which to use
                else:
                    continue
                sync_rarity(loadout)
                with self.connect() as db:
                    db.execute("UPDATE progress SET loadout=? WHERE account_id=?", (json.dumps(loadout), account_id))

    # ---- 登入獎勵: an admin-made event of up to 7 daily rewards; one claim per calendar day, in order.
    REWARD_KEYS = ("gold", "keys", "weapon_keys", "shards", "weapon", "rarity", "gear", "gear_rarity", "gear_count")

    @classmethod
    def _clean_reward(cls, raw):
        if not isinstance(raw, dict):
            raise ValueError("獎勵格式不正確")
        r = {"gold": raw.get("gold", 0), "keys": raw.get("keys", 0), "weapon_keys": raw.get("weapon_keys", 0), "shards": raw.get("shards", 0),
             "weapon": raw.get("weapon") or "", "rarity": raw.get("rarity", 1), "gear": raw.get("gear") or "",
             "gear_rarity": raw.get("gear_rarity", 1), "gear_count": raw.get("gear_count", 1)}
        cls._check_reward(r["gold"], r["weapon"], r["keys"], r["weapon_keys"], r["rarity"], r["shards"], r["gear"], r["gear_rarity"], r["gear_count"])
        return r

    def login_event(self):
        with self.connect() as db:
            row = db.execute("SELECT value FROM settings WHERE key='login_event'").fetchone()
        try:
            data = json.loads(row[0]) if row else None
        except (TypeError, ValueError):
            return None
        if not isinstance(data, dict) or not isinstance(data.get("ends_at"), int) or data["ends_at"] <= time.time():
            return None
        return data

    def set_login_event(self, title, rewards, days):
        """Admin: starts a login event (replacing any running one, which restarts everyone's progress), or ends it
        with days=0."""
        if isinstance(days, bool) or not isinstance(days, int) or not 0 <= days <= 90:
            raise ValueError("活動天數需為 0 到 90 的整數")
        with self.connect() as db:
            if days == 0:
                db.execute("DELETE FROM settings WHERE key='login_event'")
                return None
        title = title.strip() if isinstance(title, str) else ""
        if not 1 <= len(title) <= 20:
            raise ValueError("活動標題需為 1–20 字")
        if not isinstance(rewards, list) or not 1 <= len(rewards) <= 7:
            raise ValueError("獎勵需為 1 到 7 天")
        if days < len(rewards):
            raise ValueError("活動天數不能少於獎勵天數")
        clean = [self._clean_reward(r) for r in rewards]
        event = {"id": int(time.time() * 1000), "title": title, "rewards": clean, "ends_at": int(time.time()) + days * 86400}
        with self.connect() as db:
            db.execute("INSERT INTO settings(key, value) VALUES('login_event', ?) ON CONFLICT(key) DO UPDATE SET value=excluded.value", (json.dumps(event),))
        return event

    def _login_view(self, account_id):
        event = self.login_event()
        if event is None:
            return None
        with self.connect() as db:
            row = db.execute("SELECT claimed, last_day FROM login_claims WHERE account_id=? AND event_id=?", (account_id, event["id"])).fetchone()
        claimed, last = (row[0], row[1]) if row else (0, "")
        return {"title": event["title"], "rewards": event["rewards"], "ends_at": event["ends_at"], "claimed": claimed,
                "can_claim": claimed < len(event["rewards"]) and last != daily_day(), "resets_in": daily_resets_in()}

    @_locked
    def claim_login(self, account_id):
        event = self.login_event()
        if event is None:
            raise ValueError("目前沒有登入活動")
        view = self._login_view(account_id)
        if not view["can_claim"]:
            raise ValueError("今天已經領過了，明天再來" if view["claimed"] < len(event["rewards"]) else "活動獎勵已全部領取")
        reward = event["rewards"][view["claimed"]]
        with self.connect() as db:
            db.execute("INSERT INTO login_claims(account_id, event_id, claimed, last_day) VALUES(?,?,1,?) "
                       "ON CONFLICT(account_id, event_id) DO UPDATE SET claimed=claimed+1, last_day=excluded.last_day",
                       (account_id, event["id"], daily_day()))
        self._grant([account_id], reward["gold"], reward["weapon"], reward["keys"], reward["weapon_keys"], reward["rarity"], reward["shards"],
                    reward["gear"], reward["gear_rarity"], reward["gear_count"])
        return reward, self.profile(account_id)

    @staticmethod
    def _parse_loadout(raw):
        loadout = default_loadout()
        try:
            data = json.loads(raw or "{}")
        except ValueError:
            data = {}
        if isinstance(data, dict) and data.get("owned"):
            owned = [w for w in dict.fromkeys(data.get("owned", [])) if w in WEAPON_PRICES]
            loadout["owned"] = owned or ["rifle"]
            rarity = data.get("rarity") if isinstance(data.get("rarity"), dict) else {}
            # Weapons owned before rarities existed count as 普通 (1).
            loadout["rarity"] = {w: max(0, min(len(RARITIES) - 1, rarity[w])) if isinstance(rarity.get(w), int) else 1 for w in loadout["owned"]}
            copies = data.get("copies") if isinstance(data.get("copies"), dict) else {}
            active = data.get("active") if isinstance(data.get("active"), dict) else {}
            loadout["copies"], loadout["active"] = {}, {}
            for w in loadout["owned"]:
                kept = [max(0, min(len(RARITIES) - 1, r)) for r in copies.get(w, []) if isinstance(r, int)][:WEAPON_COPY_LIMIT]
                loadout["copies"][w] = kept or [loadout["rarity"][w]]
                index = active.get(w)
                loadout["active"][w] = index if isinstance(index, int) and 0 <= index < len(loadout["copies"][w]) else 0
            loadout["equipped"] = []

            def clean(stats):
                if not isinstance(stats, dict):
                    return {}
                out = {k: max(0, min(MAX_UPGRADE, v)) for k, v in stats.items() if k in UPGRADE_STATS and isinstance(v, int) and v > 0}
                over = stats.get("over")
                if isinstance(over, int) and over > 0:
                    out["over"] = min(OVERLOAD_MAX, over)
                if stats.get("pierce") == 1:
                    out["pierce"] = 1
                return out

            stored = data.get("copy_levels") if isinstance(data.get("copy_levels"), dict) else None
            legacy = data.get("levels") if isinstance(data.get("levels"), dict) else {}
            loadout["copy_levels"] = {}
            for w in loadout["owned"]:
                count = len(loadout["copies"][w])
                if stored is not None and isinstance(stored.get(w), list):
                    rows = [clean(x) for x in stored[w]][:count]
                else:
                    # Before per-copy enhancement every copy shared one set of levels: the copy in use keeps them.
                    rows = [clean(legacy.get(w)) if i == loadout["active"][w] else {} for i in range(count)]
                loadout["copy_levels"][w] = rows + [{} for _ in range(count - len(rows))]
            sync_rarity(loadout)
            equipped = data.get("equipped")
            if isinstance(equipped, list):
                loadout["equipped"] = [w for w in dict.fromkeys(equipped) if w in loadout["owned"]][:2]
        if not loadout["equipped"]:
            # Default kit: pistol plus the first other weapon owned.
            loadout["equipped"] = loadout["owned"][:2]
        return loadout

    @_locked
    def equip(self, account_id: int, weapons):
        profile = self.profile(account_id)
        if profile is None or not isinstance(weapons, list) or not 1 <= len(weapons) <= 2:
            raise ValueError("請選擇一到兩把武器")
        if len(set(weapons)) != len(weapons) or any(w not in profile["loadout"]["owned"] for w in weapons):
            raise ValueError("只能裝備已擁有的武器")
        loadout = profile["loadout"]
        loadout["equipped"] = list(weapons)
        with self.connect() as db:
            db.execute("UPDATE progress SET loadout=? WHERE account_id=?", (json.dumps(loadout), account_id))
        return self.profile(account_id)

    def _spend(self, account_id: int, cost: int, loadout: dict):
        with self.connect() as db:
            cursor = db.execute("UPDATE progress SET gold=gold-?, loadout=? WHERE account_id=? AND gold>=?",
                                (cost, json.dumps(loadout), account_id, cost))
            if cursor.rowcount != 1:
                raise ValueError("金幣不足")
        return self.profile(account_id)

    @_locked
    def buy_weapon(self, account_id: int, weapon):
        profile = self.profile(account_id)
        if profile is None or weapon not in WEAPON_PRICES:
            raise ValueError("找不到此武器")
        raise ValueError("武器改由武器補給取得")
        loadout = profile["loadout"]
        if weapon in loadout["owned"]:
            raise ValueError("已擁有此武器")
        loadout["owned"].append(weapon)
        if len(loadout["equipped"]) < 2:
            loadout["equipped"].append(weapon)
        return self._spend(account_id, WEAPON_PRICES[weapon], loadout)

    @_locked
    def upgrade_weapon(self, account_id: int, weapon, stat):
        profile = self.profile(account_id)
        if profile is None or weapon not in WEAPON_PRICES or stat not in UPGRADE_STATS:
            raise ValueError("升級項目不合法")
        loadout = profile["loadout"]
        if weapon not in loadout["owned"]:
            raise ValueError("尚未擁有此武器")
        levels = loadout["copy_levels"][weapon][loadout["active"][weapon]]   # the copy in use
        level = levels.get(stat, 0)
        if level >= MAX_UPGRADE:
            raise ValueError("已達最高等級")
        levels[stat] = level + 1
        sync_rarity(loadout)
        return self._spend(account_id, upgrade_cost(weapon, level, loadout["rarity"][weapon]), loadout)

    @_locked
    def overload_weapon(self, account_id: int, weapon):
        """超載: one more tier on the copy in use, for 傳奇+ copies whose three stats are all maxed."""
        profile = self.profile(account_id)
        if profile is None or weapon not in WEAPON_PRICES:
            raise ValueError("找不到此武器")
        loadout = profile["loadout"]
        if weapon not in loadout["owned"]:
            raise ValueError("尚未擁有此武器")
        levels = loadout["copy_levels"][weapon][loadout["active"][weapon]]
        if loadout["rarity"][weapon] < OVERLOAD_MIN_RARITY:
            raise ValueError("只有傳奇以上的武器可以超載")
        if any(levels.get(s, 0) < MAX_UPGRADE for s in UPGRADE_STATS):
            raise ValueError("三項強化都滿級後才能超載")
        over = levels.get("over", 0)
        if over >= OVERLOAD_MAX:
            raise ValueError("已達最高超載")
        gold, shards = OVERLOAD_GOLD[over], OVERLOAD_SHARDS[over]
        levels["over"] = over + 1
        sync_rarity(loadout)
        with self.connect() as db:
            cursor = db.execute("UPDATE progress SET gold=gold-?, shards=shards-?, loadout=? WHERE account_id=? AND gold>=? AND shards>=?",
                                (gold, shards, json.dumps(loadout), account_id, gold, shards))
            if cursor.rowcount != 1:
                raise ValueError("碎片或金幣不足")
        return self.profile(account_id)

    @_locked
    def pierce_weapon(self, account_id: int, weapon):
        """穿透: unlocks piercing rounds on the copy in use. 神話 guns only (not the blade); 5 shards + 20000 gold."""
        profile = self.profile(account_id)
        if profile is None or weapon not in WEAPON_PRICES:
            raise ValueError("找不到此武器")
        loadout = profile["loadout"]
        if weapon not in loadout["owned"]:
            raise ValueError("尚未擁有此武器")
        if weapon == "saber":
            raise ValueError("光劍無法加裝穿透")
        if loadout["rarity"][weapon] < PIERCE_MIN_RARITY:
            raise ValueError("只有神話武器可以加裝穿透")
        levels = loadout["copy_levels"][weapon][loadout["active"][weapon]]
        if levels.get("pierce"):
            raise ValueError("已加裝穿透")
        levels["pierce"] = 1
        sync_rarity(loadout)
        with self.connect() as db:
            cursor = db.execute("UPDATE progress SET gold=gold-?, shards=shards-?, loadout=? WHERE account_id=? AND gold>=? AND shards>=?",
                                (PIERCE_GOLD, PIERCE_SHARDS, json.dumps(loadout), account_id, PIERCE_GOLD, PIERCE_SHARDS))
            if cursor.rowcount != 1:
                raise ValueError("碎片或金幣不足")
        return self.profile(account_id)

    @_locked
    def reward(self, account_id: int, gold):
        if self.profile(account_id) is None or not isinstance(gold, int) or not 0 <= gold <= MAX_REWARD:
            raise ValueError("獎勵不合法")
        with self.connect() as db:
            db.execute("UPDATE progress SET gold=gold+? WHERE account_id=?", (gold, account_id))
        return self.profile(account_id)

    def session(self, account_id: int):
        token = secrets.token_urlsafe(32)
        with self.connect() as db:
            db.execute("DELETE FROM sessions WHERE expires<?", (int(time.time()),))
            db.execute("INSERT INTO sessions VALUES(?,?,?)", (
                hashlib.sha256(token.encode()).hexdigest(), account_id, int(time.time()) + 86400
            ))
        return {"ok": True, "token": token, "profile": self.profile(account_id)}

    def register(self, username: str, password: str):
        if not isinstance(username, str) or not USERNAME.fullmatch(username):
            raise ValueError("帳號需為 3–24 個英文字母、數字或底線")
        if not isinstance(password, str) or not 8 <= len(password) <= 72:
            raise ValueError("密碼需為 8–72 個字元")
        salt = secrets.token_bytes(16)
        digest = self._hash(password, salt)
        try:
            with self.connect() as db:
                cursor = db.execute("INSERT INTO accounts(username,password_hash,salt,created) VALUES(?,?,?,?)",
                                    (username, digest, salt, int(time.time())))
                account_id = cursor.lastrowid
                db.execute("INSERT INTO progress(account_id) VALUES(?)", (account_id,))
        except sqlite3.IntegrityError as exc:
            raise ValueError("此帳號已被註冊") from exc
        self.send_mail([account_id], *WELCOME_MAIL)
        return self.session(account_id)

    def login(self, username: str, password: str):
        if not isinstance(username, str) or not isinstance(password, str) or len(password) > 72:
            raise ValueError("帳號或密碼錯誤")
        with self.connect() as db:
            row = db.execute("SELECT * FROM accounts WHERE username=?", (username,)).fetchone()
        salt = bytes(row["salt"]) if row else b"riftward-dummy123"
        candidate = self._hash(password, salt)
        if row is None or not hmac.compare_digest(candidate, bytes(row["password_hash"])):
            raise ValueError("帳號或密碼錯誤")
        return self.session(row["id"])

    def authenticate(self, token: str):
        if not isinstance(token, str) or not 20 <= len(token) <= 128:
            return None
        with self.connect() as db:
            row = db.execute("SELECT account_id FROM sessions WHERE token_hash=? AND expires>?",
                             (hashlib.sha256(token.encode()).hexdigest(), int(time.time()))).fetchone()
        return self.profile(row["account_id"]) if row else None

    @_locked
    def tutorial(self, account_id: int):
        with self.connect() as db:
            db.execute("UPDATE progress SET tutorial_complete=1 WHERE account_id=?", (account_id,))
        return self.profile(account_id)

    @_locked
    def complete_mission(self, account_id: int, mission: int, difficulty=0):
        profile = self.profile(account_id)
        if profile is None or not isinstance(mission, int) or not 0 <= mission <= profile["unlocked"]:
            raise ValueError("關卡進度不合法")
        if isinstance(difficulty, bool) or not isinstance(difficulty, int) or not 0 <= difficulty < len(DIFFICULTY_NAMES) \
                or not difficulty_open(profile["cleared"], mission, difficulty):
            raise ValueError("難度尚未解鎖")
        cleared = profile["cleared"]
        before = cleared[mission]
        cleared[mission] = max(before, difficulty)
        # Every tier cleared here for the first time pays its first-clear keys (normally just this one).
        tiers = range(before + 1, difficulty + 1)
        bonus = sum(FIRST_CLEAR_KEYS.get(t, 0) for t in tiers)
        shards = sum(FIRST_CLEAR_SHARDS.get(t, 0) for t in tiers)
        with self.connect() as db:
            db.execute("UPDATE progress SET unlocked=MAX(unlocked,?), cleared=?, keys=keys+?, weapon_keys=weapon_keys+?, shards=shards+? WHERE account_id=?",
                       (min(REGION_COUNT - 1, mission + 1), json.dumps(cleared), bonus, bonus, shards, account_id))
        return self.profile(account_id)


class BetaServer(ThreadingHTTPServer):
    daemon_threads = True

    def __init__(self, address, store: Store, releases: Path, tls: ssl.SSLContext | None = None):
        self.game_online = lambda: False
        # Optional TLS on the same port: connections that open with a TLS handshake are encrypted, plain HTTP
        # (older clients, the local game server) keeps working. Networks that filter plain HTTP see only TLS.
        self.tls = tls
        self.store = store
        self.releases = releases.resolve()
        self.limits = defaultdict(deque)
        self.rate_lock = threading.Lock()
        super().__init__(address, Handler)

    def manifest(self):
        try:
            data = json.loads((self.releases / "manifest.json").read_text(encoding="utf-8-sig"))
            digest = data["sha256"]
            # Godot builds ship a single .pck; Unity builds ship a zipped player folder.
            ext = "zip" if data.get("package") == "zip" else "pck"
            path = self.releases / (digest + "." + ext)
            if not re.fullmatch(r"[0-9a-f]{64}", digest) or not path.is_file():
                return None
            if path.stat().st_size != data["size"]:
                return None
            result = {"ok": True, "version": data["version"], "sha256": digest,
                      "size": data["size"], "download": "/updates/" + digest + "." + ext, "package": ext, "protocol": 1}
            # Delta updates: launchers that know "files" fetch the list and only the changed files; older
            # launchers ignore the field and keep downloading the whole zip.
            if (self.releases / f"files-{digest}.json").is_file():
                result["files"] = f"/updates/files-{digest}.json"
            return result
        except (OSError, ValueError, KeyError):
            return None


def publish_files(pack: Path, releases: Path, digest: str) -> Path:
    """Delta updates: splits the game zip into content-addressed files (releases/blobs/<sha256>) and writes the
    file list (releases/files-<zip sha256>.json). A launcher that has an earlier version copies the files whose
    hash it already has and downloads only the rest. Blobs are shared across versions, so only changed files
    take new space. Idempotent: an existing list is kept."""
    listing = releases / f"files-{digest}.json"
    if listing.is_file():
        return listing
    blobs = releases / "blobs"
    blobs.mkdir(parents=True, exist_ok=True)
    entries = []
    with zipfile.ZipFile(pack) as archive:
        for info in archive.infolist():
            if info.is_dir():
                continue
            name = info.filename.replace("\\", "/")
            if name.startswith("/") or ".." in name.split("/") or ":" in name:
                raise ValueError(f"unsafe entry {name}")
            data = archive.read(info)
            sha = hashlib.sha256(data).hexdigest()
            blob = blobs / sha
            if not blob.is_file():
                temp = blobs / (sha + ".tmp")
                temp.write_bytes(data)
                temp.replace(blob)
            entries.append({"path": name, "sha256": sha, "size": len(data)})
    temp = listing.with_suffix(".tmp")
    temp.write_text(json.dumps({"version": 1, "files": entries}), encoding="utf-8")
    temp.replace(listing)
    return listing


def tls_context(cert: Path, key: Path) -> ssl.SSLContext | None:
    """Server-side TLS context from PEM files, or None when they are missing."""
    if not cert.is_file() or not key.is_file():
        return None
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.load_cert_chain(cert, key)
    return context


def cert_fingerprint(cert: Path) -> str:
    """SHA-256 of the certificate (DER), lower-case hex: what clients pin."""
    der = ssl.PEM_cert_to_DER_cert(cert.read_text(encoding="ascii"))
    return hashlib.sha256(der).hexdigest()


class Handler(BaseHTTPRequestHandler):
    server: BetaServer
    server_version = "RiftWardBeta/0.3"

    def setup(self):
        tls = self.server.tls
        if tls is not None:
            self.request.settimeout(10)
            try:
                first = self.request.recv(1, socket.MSG_PEEK)
            except OSError:
                first = b""
            if first == b"\x16":   # TLS handshake record
                self.request = tls.wrap_socket(self.request, server_side=True)
        super().setup()
        self.connection.settimeout(15)

    def finish(self):
        try:
            super().finish()
        finally:
            # The server only shuts down the original socket; a TLS wrapper must be closed here.
            if isinstance(self.request, ssl.SSLSocket):
                try:
                    self.request.close()
                except OSError:
                    pass

    def log_message(self, fmt, *args):
        # No credentials, tokens, request bodies or query strings in logs.
        pass

    def _json(self, code: int, data: dict):
        payload = json.dumps(data, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(payload)

    def do_GET(self):
        if self.path == "/health":
            self._json(200, {"ok": True, "service": "RiftWard Beta"})
            return
        if self.path == "/api/version":
            manifest = self.server.manifest()
            self._json(200 if manifest else 503, manifest or {"ok": False, "error": "更新尚未發布"})
            return
        match = re.fullmatch(r"/updates/([0-9a-f]{64})\.(pck|zip)", self.path)
        path = self.server.releases / (match[1] + "." + match[2]) if match else None
        if not match:
            # Delta updates: the file list of a version, and one content-addressed file.
            listed = re.fullmatch(r"/updates/files-([0-9a-f]{64})\.json", self.path)
            blob = re.fullmatch(r"/updates/blobs/([0-9a-f]{64})", self.path)
            if listed:
                path = self.server.releases / f"files-{listed[1]}.json"
            elif blob:
                path = self.server.releases / "blobs" / blob[1]
        if path is not None:
            if path.is_file():
                total = path.stat().st_size
                # Resume support: "Range: bytes=N-" continues an interrupted download from byte N.
                start = 0
                ranged = re.fullmatch(r"bytes=(\d+)-", self.headers.get("Range", "").strip())
                if ranged and 0 < int(ranged[1]) < total:
                    start = int(ranged[1])
                    self.send_response(206)
                    self.send_header("Content-Range", f"bytes {start}-{total - 1}/{total}")
                else:
                    self.send_response(200)
                self.send_header("Content-Type", "application/octet-stream")
                self.send_header("Content-Length", str(total - start))
                self.send_header("Accept-Ranges", "bytes")
                self.end_headers()
                self.connection.settimeout(60)   # slow links: allow longer stalls during the big transfer
                with path.open("rb") as handle:
                    handle.seek(start)
                    while block := handle.read(65536):
                        self.wfile.write(block)
                return
        self._json(404, {"ok": False, "error": "找不到此服務"})

    def _rate_ok(self):
        if self.path not in ("/api/register", "/api/login"):
            return True
        key = (self.client_address[0], self.path)
        now = time.monotonic()
        with self.server.rate_lock:
            attempts = self.server.limits[key]
            while attempts and attempts[0] < now - 60:
                attempts.popleft()
            if len(attempts) >= 20:
                return False
            attempts.append(now)
        return True

    def do_POST(self):
        try:
            with self.server.store.db_session():
                self._post()
        except (BrokenPipeError, ConnectionResetError, TimeoutError):
            return
        except (ValueError, TypeError, UnicodeError):
            self._json(400, {"ok": False, "error": "請求格式錯誤"})
        except sqlite3.Error:
            self._json(503, {"ok": False, "error": "資料庫忙碌，請稍後再試"})

    def _post(self):
        if not self._rate_ok():
            self._json(429, {"ok": False, "error": "操作過於頻繁，請稍後再試"})
            return
        size = int(self.headers.get("Content-Length", "0"))
        if not 0 < size <= 8192:
            self._json(413, {"ok": False, "error": "請求過大或缺少內容"})
            return
        data = json.loads(self.rfile.read(size))
        if not isinstance(data, dict):
            raise ValueError("object required")
        store = self.server.store
        if self.path in ("/api/register", "/api/login"):
            try:
                action = store.register if self.path.endswith("register") else store.login
                result = action(data.get("username"), data.get("password"))
            except ValueError as exc:
                self._json(400 if self.path.endswith("register") else 401, {"ok": False, "error": str(exc)})
                return
            self._json(201 if self.path.endswith("register") else 200, result)
            return
        if self.path.startswith("/api/server/"):
            provided = self.headers.get("X-Server-Key", "")
            if not hmac.compare_digest(provided, store.server_key):
                self._json(403, {"ok": False, "error": "僅限遊戲伺服器操作"})
                return
            if self.path == "/api/server/validate":
                manifest = self.server.manifest()
                required_version = manifest["version"] if manifest else VERSION
                if data.get("version") != required_version:
                    self._json(409, {"ok": False, "error": "請重新開啟更新器以取得最新版本"})
                    return
                profile = store.authenticate(data.get("token"))
                if profile:
                    store.seen(profile["id"])
                self._json(200 if profile else 401, {"ok": profile is not None, "profile": profile})
                return
            if self.path == "/api/server/seen":
                try:
                    store.seen(data.get("account_id"))
                except ValueError as exc:
                    self._json(400, {"ok": False, "error": str(exc)})
                    return
                self._json(200, {"ok": True})
                return
            if self.path == "/api/server/progress":
                try:
                    profile = store.complete_mission(data.get("account_id"), data.get("mission"), data.get("difficulty", 0))
                except ValueError as exc:
                    self._json(400, {"ok": False, "error": str(exc)})
                    return
                self._json(200, {"ok": True, "profile": profile})
                return
            if self.path == "/api/server/friends":
                account_id = data.get("account_id")
                if isinstance(account_id, bool) or not isinstance(account_id, int):
                    self._json(400, {"ok": False, "error": "找不到帳號"})
                    return
                self._json(200, {"ok": True, "friends": store.friend_ids(account_id)})
                return
            if self.path in ("/api/server/reward", "/api/server/profile"):
                try:
                    if self.path.endswith("reward"):
                        if "stats" in data:
                            store.verify_reward(data.get("account_id"), data.get("gold"), data.get("stats"))
                            store.record_stats(data.get("account_id"), data.get("stats"))
                            st = data["stats"]
                            store.add_xp(data.get("account_id"), mission_xp(st))
                        profile = store.reward(data.get("account_id"), data.get("gold"))
                    else:
                        account_id = data.get("account_id")
                        if isinstance(account_id, float) and account_id.is_integer():
                            account_id = int(account_id)
                        profile = store.profile(account_id) if isinstance(account_id, int) else None
                        if profile is None:
                            raise ValueError("找不到帳號")
                except ValueError as exc:
                    self._json(400, {"ok": False, "error": str(exc)})
                    return
                self._json(200, {"ok": True, "profile": profile})
                return
        token = self.headers.get("Authorization", "").removeprefix("Bearer ")
        profile = store.authenticate(token)
        if profile is None:
            self._json(401, {"ok": False, "error": "登入已失效，請重新登入"})
            return
        if self.path == "/api/me":
            self._json(200, {"ok": True, "profile": profile, "game_online": bool(self.server.game_online())})
        elif self.path == "/api/tutorial":
            self._json(200, {"ok": True, "profile": store.tutorial(profile["id"])})
        elif self.path.startswith("/api/mail/") or self.path.startswith("/api/friends/"):
            pid = profile["id"]
            try:
                extra = {}
                if self.path == "/api/mail/list":
                    extra["mail"] = store.mail_list(pid)
                elif self.path == "/api/mail/read":
                    store.read_mail(pid, data.get("id"))
                elif self.path == "/api/mail/claim":
                    extra["reward"], extra["count"] = store.claim_mail(pid, data.get("id"))
                elif self.path == "/api/mail/claim_all":
                    extra["reward"], extra["count"] = store.claim_mail(pid, None)
                elif self.path == "/api/mail/delete":
                    store.delete_mail(pid, data.get("id"))
                elif self.path == "/api/friends/list":
                    pass
                elif self.path == "/api/friends/request":
                    extra["status"] = store.request_friend(pid, data.get("username"))
                elif self.path == "/api/friends/respond":
                    store.respond_friend(pid, data.get("username"), data.get("accept"))
                elif self.path == "/api/friends/remove":
                    store.remove_friend(pid, data.get("username"))
                else:
                    self._json(404, {"ok": False, "error": "找不到此服務"})
                    return
                if self.path.startswith("/api/mail/") and "mail" not in extra:
                    extra["mail"] = store.mail_list(pid)
                if self.path.startswith("/api/friends/"):
                    extra.update(store.friends(pid))
            except ValueError as exc:
                self._json(400, {"ok": False, "error": str(exc)})
                return
            self._json(200, {"ok": True, "profile": store.profile(pid), **extra})
        elif self.path == "/api/endless/leaderboard":
            self._json(200, {"ok": True, **store.endless_leaderboard(profile["id"])})
        elif self.path == "/api/gacha/history":
            try:
                self._json(200, {"ok": True, **store.gacha_history(profile["id"], data.get("pool"), data.get("limit", 200))})
            except ValueError as exc:
                self._json(400, {"ok": False, "error": str(exc)})
        elif self.path in ("/api/achievement/claim", "/api/achievement/title"):
            try:
                if self.path.endswith("claim"):
                    outcome, result = store.claim_achievement(profile["id"], data.get("achievement"))
                    extra = {"claim": outcome}
                else:
                    result, extra = store.set_title(profile["id"], data.get("title")), {}
            except ValueError as exc:
                self._json(400, {"ok": False, "error": str(exc)})
                return
            self._json(200, {"ok": True, "profile": result, **extra})
        elif self.path == "/api/daily/claim":
            try:
                reward, result = store.claim_daily(profile["id"], data.get("task"))
            except ValueError as exc:
                self._json(400, {"ok": False, "error": str(exc)})
                return
            self._json(200, {"ok": True, "reward": reward, "profile": result})
        elif self.path == "/api/codex":
            self._json(200, {"ok": True, "codex": store.codex(profile["id"])})
        elif self.path == "/api/weekly/claim":
            try:
                reward, result = store.claim_weekly(profile["id"], data.get("task"))
            except ValueError as exc:
                self._json(400, {"ok": False, "error": str(exc)})
                return
            self._json(200, {"ok": True, "reward": reward, "profile": result})
        elif self.path == "/api/login/claim":
            try:
                reward, result = store.claim_login(profile["id"])
            except ValueError as exc:
                self._json(400, {"ok": False, "error": str(exc)})
                return
            self._json(200, {"ok": True, "reward": reward, "profile": result})
        elif self.path == "/api/player/card":
            try:
                card = store.player_card(profile["id"], data.get("username"))
            except ValueError as exc:
                self._json(400, {"ok": False, "error": str(exc)})
                return
            self._json(200, {"ok": True, "card": card})
        elif self.path in ("/api/feedback/send", "/api/feedback/mine"):
            try:
                result = store.send_feedback(profile["id"], data.get("category"), data.get("body")) if self.path.endswith("send") else store.my_feedback(profile["id"])
            except ValueError as exc:
                self._json(400, {"ok": False, "error": str(exc)})
                return
            self._json(200, {"ok": True, **result})
        elif self.path.startswith("/api/admin/"):
            if not profile.get("admin"):
                self._json(403, {"ok": False, "error": "需要管理員權限"})
                return
            if self.path == "/api/admin/players":
                self._json(200, {"ok": True, "players": store.list_players()})
            elif self.path == "/api/admin/audit":
                self._json(200, {"ok": True, "entries": store.audit_log()})
            elif self.path == "/api/admin/mail":
                try:
                    count = store.admin_mail(data.get("username"), data.get("title"), data.get("body", ""), data.get("gold", 0),
                                             data.get("keys", 0), data.get("weapon_keys", 0), data.get("days", 30))
                except ValueError as exc:
                    self._json(400, {"ok": False, "error": str(exc)})
                    return
                self._json(200, {"ok": True, "count": count, "profile": store.profile(profile["id"])})
            elif self.path == "/api/admin/grant":
                try:
                    count = store.admin_grant(data.get("username"), data.get("gold", 0), data.get("weapon"), data.get("keys", 0), data.get("weapon_keys", 0), data.get("rarity", 1),
                                              data.get("shards", 0), data.get("gear"), data.get("gear_rarity", 1), data.get("gear_count", 1))
                except ValueError as exc:
                    self._json(400, {"ok": False, "error": str(exc)})
                    return
                self._json(200, {"ok": True, "count": count, "profile": store.profile(profile["id"])})
            elif self.path == "/api/admin/feedback":
                try:
                    self._json(200, {"ok": True, **store.list_feedback(data.get("status"))})
                except ValueError as exc:
                    self._json(400, {"ok": False, "error": str(exc)})
            elif self.path == "/api/admin/feedback/resolve":
                try:
                    self._json(200, {"ok": True, **store.resolve_feedback(data.get("id"), data.get("reply", ""), data.get("reopen") is True)})
                except ValueError as exc:
                    self._json(400, {"ok": False, "error": str(exc)})
            elif self.path == "/api/admin/login":
                try:
                    event = store.set_login_event(data.get("title"), data.get("rewards"), data.get("days"))
                except ValueError as exc:
                    self._json(400, {"ok": False, "error": str(exc)})
                    return
                self._json(200, {"ok": True, "event": event, "profile": store.profile(profile["id"])})
            elif self.path == "/api/admin/limited":
                try:
                    banner = store.set_limited_banner(profile["id"], data.get("weapon"), data.get("days"))
                except ValueError as exc:
                    self._json(400, {"ok": False, "error": str(exc)})
                    return
                self._json(200, {"ok": True, "banner": banner, "profile": store.profile(profile["id"])})
            elif self.path == "/api/admin/reset":
                try:
                    name = store.admin_reset(profile["id"], data.get("username"), data.get("confirm"))
                except ValueError as exc:
                    self._json(400, {"ok": False, "error": str(exc)})
                    return
                self._json(200, {"ok": True, "username": name})
            else:
                self._json(404, {"ok": False, "error": "找不到此服務"})
        elif self.path in ("/api/talent/assign", "/api/talent/reset", "/api/gacha/draw", "/api/gacha/weapon", "/api/gacha/limited", "/api/gear/equip", "/api/gear/salvage", "/api/gear/upgrade", "/api/gear/reforge", "/api/gear/limit", "/api/gear/ascend", "/api/weapon/select", "/api/weapon/salvage", "/api/salvage/bulk"):
            try:
                extra = {}
                if self.path == "/api/talent/assign":
                    result = store.assign_talent(profile["id"], data.get("talent"), data.get("points", 1))
                elif self.path == "/api/talent/reset":
                    result = store.reset_talents(profile["id"])
                elif self.path == "/api/gacha/draw":
                    items, result = store.draw(profile["id"], data.get("count", 1))
                    extra["items"] = items
                elif self.path == "/api/gacha/limited":
                    weapons, result = store.draw_limited(profile["id"], data.get("count", 1))
                    extra["weapons"] = weapons
                elif self.path == "/api/gacha/weapon":
                    weapons, result = store.draw_weapons(profile["id"], data.get("count", 1))
                    extra["weapons"] = weapons
                elif self.path == "/api/weapon/select":
                    result = store.select_weapon_copy(profile["id"], data.get("weapon"), data.get("index"), data.get("rarity"))
                elif self.path == "/api/salvage/bulk":
                    count, gold, shards, result = store.salvage_bulk(profile["id"], data.get("kind"), data.get("rarities"), data.get("confirm", False), data.get("slot"))
                    extra.update(count=count, gold=gold, shards=shards)
                elif self.path == "/api/weapon/salvage":
                    result = store.salvage_weapon_copy(profile["id"], data.get("weapon"), data.get("index"), data.get("rarity"))
                elif self.path == "/api/gear/equip":
                    result = store.equip_gear(profile["id"], data.get("item"))
                elif self.path == "/api/gear/upgrade":
                    result = store.upgrade_gear(profile["id"], data.get("item"))
                elif self.path == "/api/gear/reforge":
                    result = store.reforge_gear(profile["id"], data.get("item"), data.get("lock", False))
                elif self.path == "/api/gear/limit":
                    result = store.limit_break_gear(profile["id"], data.get("item"))
                elif self.path == "/api/gear/ascend":
                    result = store.ascend_gear(profile["id"], data.get("item"), data.get("fodder"))
                else:
                    result = store.salvage(profile["id"], data.get("item"))
            except ValueError as exc:
                self._json(400, {"ok": False, "error": str(exc)})
                return
            self._json(200, {"ok": True, "profile": result, **extra})
        elif self.path == "/api/shop/equip":
            try:
                result = store.equip(profile["id"], data.get("weapons"))
            except ValueError as exc:
                self._json(400, {"ok": False, "error": str(exc)})
                return
            self._json(200, {"ok": True, "profile": result})
        elif self.path in ("/api/shop/buy", "/api/shop/upgrade", "/api/shop/overload", "/api/shop/pierce"):
            try:
                if self.path.endswith("pierce"):
                    result = store.pierce_weapon(profile["id"], data.get("weapon"))
                elif self.path.endswith("buy"):
                    result = store.buy_weapon(profile["id"], data.get("weapon"))
                elif self.path.endswith("overload"):
                    result = store.overload_weapon(profile["id"], data.get("weapon"))
                else:
                    result = store.upgrade_weapon(profile["id"], data.get("weapon"), data.get("stat"))
            except ValueError as exc:
                self._json(400, {"ok": False, "error": str(exc)})
                return
            self._json(200, {"ok": True, "profile": result})
        elif self.path == "/api/logout":
            with store.connect() as db:
                db.execute("DELETE FROM sessions WHERE token_hash=?", (hashlib.sha256(token.encode()).hexdigest(),))
            self._json(200, {"ok": True})
        else:
            self._json(404, {"ok": False, "error": "找不到此服務"})


def main():
    base = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default=HOST)
    parser.add_argument("--port", type=int, default=PORT)
    parser.add_argument("--data-dir", type=Path, default=base / "server-data")
    parser.add_argument("--release-dir", type=Path, default=base / "releases")
    args = parser.parse_args()
    server = BetaServer((args.host, args.port), Store(args.data_dir), args.release_dir)
    print(f"ACCOUNT_UPDATE_SERVER_READY {args.host}:{server.server_port}", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
