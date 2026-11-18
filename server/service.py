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
import re
import secrets
import socket
import sqlite3
import ssl
import threading
import time
from collections import defaultdict, deque
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

HOST = "192.168.213.100"
PORT = 24771
ITERATIONS = 310_000
USERNAME = re.compile(r"[A-Za-z0-9_]{3,24}\Z")
VERSION = "0.4.2-beta.1"

# Armory catalog. The game client mirrors these numbers for display only;
# every purchase is priced and validated here.
WEAPON_PRICES = {"pistol": 0, "rifle": 400, "shotgun": 600, "sniper": 800, "saber": 0}
UPGRADE_STATS = ("damage", "mag", "reload")
MAX_UPGRADE = 5
MAX_REWARD = 12000   # mirrors GameSim.MaxMissionGold (地獄 multiplies the gold of a cleared mission)

# Difficulty tiers (普通, 困難, 噩夢, 地獄); mirrors Defs.Difficulties in the game client.
DIFFICULTY_NAMES = ["普通", "困難", "噩夢", "地獄"]
DIFFICULTY_XP = [1.0, 1.25, 1.5, 1.75]
REGION_COUNT = 6   # two chapters of three regions; mirrors Defs.Regions


DIFFICULTY_GOLD = [1.0, 1.6, 2.4, 3.5]


def mission_gold(team_kills, wave, level, victory, region, tier) -> int:
    """Gold a mission pays; mirrors GameSim.MissionGold (the anti-cheat check compares the report against it)."""
    base = team_kills * 3 + wave * 12 + (level - 1) * 15 + (200 + 150 * region if victory else 0)
    return max(0, min(MAX_REWARD, int(round(base * DIFFICULTY_GOLD[tier]))))


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


# ---- achievements: lifetime goals in three tiers (銅 / 銀 / 金). Progress is derived from the account's records,
# each reached tier is claimed once for its reward, and the 金 tier of some achievements grants a title.
ACHIEVEMENT_TIER_NAMES = ["銅", "銀", "金"]
ACHIEVEMENT_REWARDS = [{"gold": 300}, {"gold": 600, "keys": 2}, {"keys": 3, "weapon_keys": 2}]
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


def roll_daily(account_id, day, veteran):
    """The day's tasks, the same for one account all day. 困難 tasks are only offered once 普通 has been cleared."""
    pool = [k for k in DAILY_TASKS if k != "hard" or veteran]
    return random.Random(f"{account_id}:{day}").sample(pool, DAILY_COUNT)


# Upgrade prices scale with rarity (劣質 .. 神話): better items cost more to improve.
RARITY_UPGRADE_MULT = [0.6, 1.0, 1.4, 2.0, 3.0]
GEAR_UPGRADE_BASE = [40, 70, 120, 200, 320]   # gold for gear +1; level n -> n+1 costs base * (n + 1)
GEAR_MAX_LEVEL = 5
GEAR_LEVEL_BONUS = 0.12                        # every enhancement level adds 12% to all of an item's stats (+60% at +5)


def upgrade_cost(weapon: str, level: int, rarity: int = 1) -> int:
    factor = {"pistol": 1.0, "rifle": 1.3, "shotgun": 1.4, "sniper": 1.6, "saber": 1.5}[weapon]
    return int(round(120 * (level + 1) * factor * RARITY_UPGRADE_MULT[max(0, min(len(RARITY_UPGRADE_MULT) - 1, rarity))]))


def gear_upgrade_cost(rarity: int, level: int) -> int:
    return GEAR_UPGRADE_BASE[rarity] * (level + 1)


def gear_invested(item) -> int:
    return sum(gear_upgrade_cost(item["rarity"], n) for n in range(item.get("level", 0)))


def gear_stats(item) -> dict:
    """Effective stats of an item at its enhancement level (stored stats are the +0 values)."""
    mult = 1 + GEAR_LEVEL_BONUS * item.get("level", 0)
    return {k: int(round(v * mult)) for k, v in item["stats"].items()}


# ---- account level, talents, gear and supply draws
TALENTS = {"hp": 5, "damage": 2, "speed": 1.5, "reload": 3, "crit": 1}   # bonus per point
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


def roll_rarity(rng, pity):
    """Returns (rarity, new pity count). Pity counts draws since the last 傳奇+; at the limit the draw is forced
    up to 傳奇 or 神話 (kept at their 5:1 ratio)."""
    if pity + 1 >= PITY_LIMIT:
        rarity = rng.choices([3, 4], weights=RARITY_WEIGHTS[3:])[0]
    else:
        rarity = rng.choices(range(len(RARITIES)), weights=RARITY_WEIGHTS)[0]
    return rarity, 0 if rarity >= 3 else pity + 1


def roll_item(rng, rarity=None):
    slot = rng.choice(list(GEAR_SLOTS))
    if rarity is None:
        rarity = rng.choices(range(len(RARITIES)), weights=RARITY_WEIGHTS)[0]
    primary = GEAR_SLOTS[slot]
    stats = {primary: GEAR_VALUES[primary][rarity]}
    if rarity >= 2:  # rare and better carry a random secondary stat at half strength
        secondary = rng.choice([k for k in GEAR_VALUES if k != primary])
        stats[secondary] = max(1, GEAR_VALUES[secondary][rarity] // 2)
    return {"id": secrets.token_hex(6), "slot": slot, "rarity": rarity, "stats": stats}


WEAPON_WEIGHTS = {"pistol": 30, "rifle": 30, "shotgun": 20, "sniper": 20, "saber": 5}   # the 光劍 is a rare find: 5 / 105 ≈ 4.8% of draws
DUPLICATE_GOLD = [20, 50, 120, 300, 800]
GEAR_SALVAGE_GOLD = [10, 25, 60, 150, 400]


def default_loadout():
    # New recruits start with a worn (劣質) AR-27; better guns come from weapon supply draws.
    return {"owned": ["rifle"], "levels": {}, "equipped": ["rifle"], "rarity": {"rifle": 0}, "copies": {"rifle": [0]}, "active": {"rifle": 0}}


WEAPON_COPY_LIMIT = 50


def sync_rarity(loadout):
    """The active copy decides each weapon's rarity."""
    loadout["rarity"] = {w: loadout["copies"][w][loadout["active"][w]] for w in loadout["owned"]}


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
                                ("cleared", "TEXT NOT NULL DEFAULT '[]'"), ("daily", "TEXT NOT NULL DEFAULT '{}'"),
                                ("achievements", "TEXT NOT NULL DEFAULT '{}'"), ("last_seen", "INTEGER NOT NULL DEFAULT 0")):
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
                "SELECT a.id,a.username,p.unlocked,p.tutorial_complete,p.gold,p.loadout,p.stats,p.xp,p.keys,p.weapon_keys,p.talents,p.gear,p.cleared FROM accounts a "
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
        result["pity"] = {pool: PITY_LIMIT - count for pool, count in pity.items()}   # draws left until 傳奇+ is guaranteed
        result["inventory"] = [dict(item, stats=gear_stats(item), base=item["stats"], level=item.get("level", 0)) for item in gear["inventory"]]
        result["equipment"] = gear["equipped"]
        bonus = {k: talents[k] * per for k, per in TALENTS.items()}
        for item in gear["inventory"]:
            if gear["equipped"].get(item["slot"]) == item["id"]:
                for k, v in gear_stats(item).items():
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
            db.execute("UPDATE progress SET gold=gold+?, keys=keys+?, weapon_keys=weapon_keys+? WHERE account_id=?",
                       (reward.get("gold", 0), reward.get("keys", 0), reward.get("weapon_keys", 0), account_id))
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
            db.execute("UPDATE progress SET gold=gold+?, keys=keys+?, weapon_keys=weapon_keys+? WHERE account_id=?",
                       (reward.get("gold", 0), reward.get("keys", 0), reward.get("weapon_keys", 0), account_id))
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
        return {pool: max(0, min(PITY_LIMIT - 1, data.get(pool, 0))) if isinstance(data.get(pool), int) else 0 for pool in ("gear", "weapon")}

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
            db.execute("DELETE FROM gacha_log WHERE account_id=? AND id NOT IN (SELECT id FROM gacha_log WHERE account_id=? ORDER BY id DESC LIMIT ?)",
                       (account_id, account_id, self.GACHA_LOG_KEEP))

    def gacha_history(self, account_id, pool=None, limit=200):
        """This account's draws, newest first. Only ever the caller's own rows."""
        if pool not in (None, "", "gear", "weapon"):
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
                "totals": {"gear": totals.get("gear", 0), "weapon": totals.get("weapon", 0)}}

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
            rarity, pity["gear"] = roll_rarity(rng, pity["gear"])
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
        results, gold, refund = [], 0, 0
        for _ in range(count):
            weapon = rng.choices(list(WEAPON_WEIGHTS), weights=list(WEAPON_WEIGHTS.values()))[0]
            rarity, pity["weapon"] = roll_rarity(rng, pity["weapon"])
            if weapon not in loadout["owned"]:
                loadout["owned"].append(weapon)
                loadout["copies"][weapon] = [rarity]
                loadout["active"][weapon] = 0
                if len(loadout["equipped"]) < 2:
                    loadout["equipped"].append(weapon)
                outcome = "new"
            elif len(loadout["copies"][weapon]) < WEAPON_COPY_LIMIT:
                loadout["copies"][weapon].append(rarity)
                outcome = "copy"
            else:
                outcome, refund = "full", DUPLICATE_GOLD[rarity]
                gold += refund
            results.append({"weapon": weapon, "rarity": rarity, "outcome": outcome, "gold": DUPLICATE_GOLD[rarity] if outcome == "full" else 0})
        sync_rarity(loadout)
        with self.connect() as db:
            db.execute("UPDATE progress SET loadout=?, gold=gold+?, pity=? WHERE account_id=?", (json.dumps(loadout), gold, json.dumps(pity), account_id))
        self._bump_stats(account_id, draws=count)
        self._log_draws(account_id, "weapon", [(w["weapon"], w["rarity"], w["outcome"]) for w in results])
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

    def _save_loadout(self, account_id, loadout, gold=0):
        sync_rarity(loadout)
        with self.connect() as db:
            db.execute("UPDATE progress SET loadout=?, gold=gold+? WHERE account_id=?", (json.dumps(loadout), gold, account_id))
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
        if loadout["active"][weapon] > index:
            loadout["active"][weapon] -= 1
        return self._save_loadout(account_id, loadout, DUPLICATE_GOLD[rarity])

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
            db.execute("UPDATE progress SET gold=gold+? WHERE account_id=?", (GEAR_SALVAGE_GOLD[item["rarity"]] + gear_invested(item) // 2, account_id))
        return self.profile(account_id)

    @_locked
    def salvage_bulk(self, account_id: int, kind, rarities, confirm=False):
        """Salvages every unequipped item (gear) or every inactive copy (weapons) of the chosen rarities.
        Anything 傳奇 or above needs confirm=True, so a stray request can never wipe the best loot."""
        if kind not in ("gear", "weapon"):
            raise ValueError("分解類型不正確")
        if not isinstance(rarities, list) or not rarities or not all(isinstance(r, int) and not isinstance(r, bool) and 0 <= r < len(RARITIES) for r in rarities):
            raise ValueError("請選擇要分解的稀有度")
        chosen = set(rarities)
        if max(chosen) >= 3 and confirm is not True:
            raise ValueError("包含傳奇以上，請先確認")
        count, gold = 0, 0
        if kind == "gear":
            gear = self._raw_gear(account_id)
            worn = set(gear["equipped"].values())
            kept = []
            for item in gear["inventory"]:
                if item["rarity"] in chosen and item["id"] not in worn:
                    count += 1
                    gold += GEAR_SALVAGE_GOLD[item["rarity"]] + gear_invested(item) // 2
                else:
                    kept.append(item)
            gear["inventory"] = kept
            self._save_gear(account_id, gear)
            with self.connect() as db:
                db.execute("UPDATE progress SET gold=gold+? WHERE account_id=?", (gold, account_id))
            return count, gold, self.profile(account_id)
        loadout = self.profile(account_id)["loadout"]
        for weapon in loadout["owned"]:
            copies, active = loadout["copies"][weapon], loadout["active"][weapon]
            kept = []
            for i, rarity in enumerate(copies):
                if rarity in chosen and i != active:
                    count += 1
                    gold += DUPLICATE_GOLD[rarity]
                else:
                    if i == active:
                        loadout["active"][weapon] = len(kept)
                    kept.append(rarity)
            loadout["copies"][weapon] = kept
        return count, gold, self._save_loadout(account_id, loadout, gold)

    @_locked
    def add_xp(self, account_id: int, xp: int):
        with self.connect() as db:
            db.execute("UPDATE progress SET xp=xp+? WHERE account_id=?", (max(0, min(5000, xp)), account_id))

    STAT_KEYS = ("kills", "headshots", "shots", "hits", "missions", "victories", "best_wave",
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
    def record_stats(self, account_id: int, stats):
        """Adds one mission's combat record (reported by the game server) to lifetime stats."""
        if not isinstance(stats, dict):
            raise ValueError("戰績格式錯誤")
        values = {}
        for key, limit in (("kills", 5000), ("headshots", 5000), ("shots", 100000), ("hits", 100000), ("wave", 20)):
            value = stats.get(key, 0)
            if not isinstance(value, int) or not 0 <= value <= limit:
                raise ValueError("戰績數值不合法")
            values[key] = value
        if values["hits"] > values["shots"] or values["headshots"] > values["hits"] * 7:
            raise ValueError("戰績數值不合法")
        # Achievement records (optional: older game servers do not send them).
        for key, limit in (("downs", 1000), ("revives", 1000), ("boss_kills", 2), ("elite_kills", 20)):
            value = stats.get(key, 0)
            if isinstance(value, bool) or not isinstance(value, int) or not 0 <= value <= limit:
                raise ValueError("戰績數值不合法")
            values[key] = value
        grade = stats.get("grade", "")
        if grade not in ("", "S", "A", "B", "C"):
            raise ValueError("戰績數值不合法")
        victory = stats.get("victory") is True
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
        total["best_wave"] = max(total["best_wave"], values["wave"])
        for key in ("boss_kills", "elite_kills", "revives"):
            total[key] += values[key]
        total["flawless"] += 1 if victory and values["downs"] == 0 and "downs" in stats else 0
        total["s_grades"] += 1 if victory and grade == "S" else 0
        total["hard_victories"] += 1 if victory and tier >= 1 else 0
        with self.connect() as db:
            db.execute("UPDATE progress SET stats=? WHERE account_id=?", (json.dumps(total), account_id))
        self._advance_daily(account_id, profile["cleared"], {
            "kills": values["kills"], "headshots": values["headshots"], "missions": 1, "waves": values["wave"], "wave": values["wave"],
            "victories": 1 if victory else 0, "hard": 1 if victory and tier >= 1 else 0})

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
        wave = number("wave", 0, 20, 0)
        kills = number("kills", 0, 5000, 0)
        team = number("team_kills", 0, 5000)
        level = number("level", 1, 300)
        elapsed = number("elapsed", 0, 86400, None, float)
        region = number("region", 0, REGION_COUNT - 1)
        tier = number("difficulty", 0, len(DIFFICULTY_NAMES) - 1, 0)
        aim = number("aim_flags", 0, 1_000_000, 0)
        victory = stats.get("victory") is True
        problems = []
        if victory and wave != 20:
            problems.append("勝利但未完成 20 波")
        if team is not None and kills > team:
            problems.append("個人擊殺多於全隊擊殺")
        if team is not None and team > 90 * max(1, wave):
            problems.append("全隊擊殺數超出波次上限")
        if elapsed is not None and elapsed < max(0, wave - 1) * self.min_wave_seconds:
            problems.append(f"作戰時間過短（{elapsed:.0f} 秒到達第 {wave} 波）")
        if None not in (team, level, region):
            expected = mission_gold(team, wave, level, victory, region, tier)
            if gold > expected + 1:
                problems.append(f"金幣 {gold} 超過可得上限 {expected}")
        now = time.monotonic()
        last = self.last_reward.get(account_id)
        if last is not None and self.min_wave_seconds > 0 and now - last < max(1.0, max(0, wave - 1) * self.min_wave_seconds * 0.5):
            problems.append("結算過於頻繁")
        detail = {k: stats.get(k) for k in ("kills", "team_kills", "wave", "elapsed", "level", "region", "difficulty", "victory", "shots", "hits", "headshots", "aim_flags")}
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
    def admin_grant(self, username, gold, weapon, keys=0, weapon_keys=0, rarity=1):
        """Gives gold, keys and optionally a weapon at a chosen rarity. A player without that weapon gets it
        (equipped if a slot is free); one who has it gets the copy added to their armory."""
        if not isinstance(gold, int) or not 0 <= gold <= 100000:
            raise ValueError("金幣數量需為 0–100000")
        if not isinstance(keys, int) or not 0 <= keys <= 1000 or not isinstance(weapon_keys, int) or not 0 <= weapon_keys <= 1000:
            raise ValueError("鑰匙數量需為 0–1000")
        if weapon not in (None, "") and weapon not in WEAPON_PRICES:
            raise ValueError("找不到此武器")
        if isinstance(rarity, bool) or not isinstance(rarity, int) or not 0 <= rarity < len(RARITIES):
            raise ValueError("稀有度不正確")
        if gold == 0 and not weapon and keys == 0 and weapon_keys == 0:
            raise ValueError("請選擇要發放的金幣、鑰匙或武器")
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
            db.execute("UPDATE progress SET gold=gold+?, keys=keys+?, weapon_keys=weapon_keys+? WHERE account_id IN (%s)" % ",".join("?" * len(ids)),
                       (gold, keys, weapon_keys, *ids)) if ids else None
        if weapon:
            for account_id in ids:
                profile = self.profile(account_id)
                loadout = profile["loadout"]
                if weapon not in loadout["owned"]:
                    loadout["owned"].append(weapon)
                    loadout["copies"][weapon] = [rarity]
                    loadout["active"][weapon] = 0
                    if len(loadout["equipped"]) < 2:
                        loadout["equipped"].append(weapon)
                elif len(loadout["copies"][weapon]) < WEAPON_COPY_LIMIT:
                    loadout["copies"][weapon].append(rarity)   # kept in the armory; the player picks which to use
                else:
                    continue
                sync_rarity(loadout)
                with self.connect() as db:
                    db.execute("UPDATE progress SET loadout=? WHERE account_id=?", (json.dumps(loadout), account_id))
        return len(ids)

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
            sync_rarity(loadout)
            loadout["equipped"] = []
            levels = data.get("levels", {})
            if isinstance(levels, dict):
                for weapon, stats in levels.items():
                    if weapon in loadout["owned"] and isinstance(stats, dict):
                        loadout["levels"][weapon] = {k: max(0, min(MAX_UPGRADE, int(v))) for k, v in stats.items()
                                                     if k in UPGRADE_STATS and isinstance(v, int)}
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
        levels = loadout["levels"].setdefault(weapon, {})
        level = levels.get(stat, 0)
        if level >= MAX_UPGRADE:
            raise ValueError("已達最高等級")
        levels[stat] = level + 1
        return self._spend(account_id, upgrade_cost(weapon, level, loadout["rarity"].get(weapon, 1)), loadout)

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
        cleared[mission] = max(cleared[mission], difficulty)
        with self.connect() as db:
            db.execute("UPDATE progress SET unlocked=MAX(unlocked,?), cleared=? WHERE account_id=?",
                       (min(REGION_COUNT - 1, mission + 1), json.dumps(cleared), account_id))
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
            return {"ok": True, "version": data["version"], "sha256": digest,
                    "size": data["size"], "download": "/updates/" + digest + "." + ext, "package": ext, "protocol": 1}
        except (OSError, ValueError, KeyError):
            return None


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
        if match:
            path = self.server.releases / (match[1] + "." + match[2])
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
                    count = store.admin_grant(data.get("username"), data.get("gold", 0), data.get("weapon"), data.get("keys", 0), data.get("weapon_keys", 0), data.get("rarity", 1))
                except ValueError as exc:
                    self._json(400, {"ok": False, "error": str(exc)})
                    return
                self._json(200, {"ok": True, "count": count, "profile": store.profile(profile["id"])})
            else:
                self._json(404, {"ok": False, "error": "找不到此服務"})
        elif self.path in ("/api/talent/assign", "/api/talent/reset", "/api/gacha/draw", "/api/gacha/weapon", "/api/gear/equip", "/api/gear/salvage", "/api/gear/upgrade", "/api/weapon/select", "/api/weapon/salvage", "/api/salvage/bulk"):
            try:
                extra = {}
                if self.path == "/api/talent/assign":
                    result = store.assign_talent(profile["id"], data.get("talent"), data.get("points", 1))
                elif self.path == "/api/talent/reset":
                    result = store.reset_talents(profile["id"])
                elif self.path == "/api/gacha/draw":
                    items, result = store.draw(profile["id"], data.get("count", 1))
                    extra["items"] = items
                elif self.path == "/api/gacha/weapon":
                    weapons, result = store.draw_weapons(profile["id"], data.get("count", 1))
                    extra["weapons"] = weapons
                elif self.path == "/api/weapon/select":
                    result = store.select_weapon_copy(profile["id"], data.get("weapon"), data.get("index"), data.get("rarity"))
                elif self.path == "/api/salvage/bulk":
                    count, gold, result = store.salvage_bulk(profile["id"], data.get("kind"), data.get("rarities"), data.get("confirm", False))
                    extra.update(count=count, gold=gold)
                elif self.path == "/api/weapon/salvage":
                    result = store.salvage_weapon_copy(profile["id"], data.get("weapon"), data.get("index"), data.get("rarity"))
                elif self.path == "/api/gear/equip":
                    result = store.equip_gear(profile["id"], data.get("item"))
                elif self.path == "/api/gear/upgrade":
                    result = store.upgrade_gear(profile["id"], data.get("item"))
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
        elif self.path in ("/api/shop/buy", "/api/shop/upgrade"):
            try:
                if self.path.endswith("buy"):
                    result = store.buy_weapon(profile["id"], data.get("weapon"))
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
