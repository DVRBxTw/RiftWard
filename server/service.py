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
import sqlite3
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
WEAPON_PRICES = {"pistol": 0, "rifle": 400, "shotgun": 600, "sniper": 800}
UPGRADE_STATS = ("damage", "mag", "reload")
MAX_UPGRADE = 5
MAX_REWARD = 3000


def upgrade_cost(weapon: str, level: int) -> int:
    factor = {"pistol": 1.0, "rifle": 1.3, "shotgun": 1.4, "sniper": 1.6}[weapon]
    return int(round(120 * (level + 1) * factor))


# ---- account level, talents, gear and supply draws
TALENTS = {"hp": 5, "damage": 2, "speed": 1.5, "reload": 3, "crit": 1}   # bonus per point
TALENT_MAX = 10
GEAR_SLOTS = {"armor": "hp", "module": "damage", "boots": "speed", "chip": "crit"}
RARITIES = ["劣質", "普通", "稀世", "傳奇", "神話"]
RARITY_WEIGHTS = [50, 30, 14, 5, 1]
# Primary stat value per rarity for each stat (hp is flat, the rest are percentages).
GEAR_VALUES = {"hp": [6, 12, 20, 32, 50], "damage": [2, 4, 7, 11, 16], "speed": [2, 3, 5, 8, 12],
               "crit": [2, 3, 5, 8, 12], "reload": [3, 5, 8, 12, 18]}
GEAR_NAMES = {"armor": "護甲", "module": "槍械模組", "boots": "戰術靴", "chip": "戰術晶片"}
INVENTORY_LIMIT = 200


def level_for(xp: int):
    """Returns (level, xp into level, xp needed for next level). Each level needs 100 + 50 * level."""
    level = 1
    while xp >= 100 + 50 * level and level < 99:
        xp -= 100 + 50 * level
        level += 1
    return level, xp, 100 + 50 * level


def roll_item(rng):
    slot = rng.choice(list(GEAR_SLOTS))
    rarity = rng.choices(range(len(RARITIES)), weights=RARITY_WEIGHTS)[0]
    primary = GEAR_SLOTS[slot]
    stats = {primary: GEAR_VALUES[primary][rarity]}
    if rarity >= 2:  # rare and better carry a random secondary stat at half strength
        secondary = rng.choice([k for k in GEAR_VALUES if k != primary])
        stats[secondary] = max(1, GEAR_VALUES[secondary][rarity] // 2)
    return {"id": secrets.token_hex(6), "slot": slot, "rarity": rarity, "stats": stats}


WEAPON_WEIGHTS = {"pistol": 30, "rifle": 30, "shotgun": 20, "sniper": 20}
DUPLICATE_GOLD = [20, 50, 120, 300, 800]


def default_loadout():
    # New recruits start with a worn (劣質) AR-27; better guns come from weapon supply draws.
    return {"owned": ["rifle"], "levels": {}, "equipped": ["rifle"], "rarity": {"rifle": 0}}


class Store:
    def __init__(self, directory: Path):
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
            """)
            columns = {row[1] for row in db.execute("PRAGMA table_info(progress)")}
            if "gold" not in columns:
                db.execute("ALTER TABLE progress ADD COLUMN gold INTEGER NOT NULL DEFAULT 0")
            if "loadout" not in columns:
                db.execute("ALTER TABLE progress ADD COLUMN loadout TEXT NOT NULL DEFAULT '{}'")
            if "stats" not in columns:
                db.execute("ALTER TABLE progress ADD COLUMN stats TEXT NOT NULL DEFAULT '{}'")
            for column, ddl in (("xp", "INTEGER NOT NULL DEFAULT 0"), ("keys", "INTEGER NOT NULL DEFAULT 0"), ("weapon_keys", "INTEGER NOT NULL DEFAULT 0"),
                                ("talents", "TEXT NOT NULL DEFAULT '{}'"), ("gear", "TEXT NOT NULL DEFAULT '{}'")):
                if column not in columns:
                    db.execute(f"ALTER TABLE progress ADD COLUMN {column} {ddl}")

    @contextmanager
    def connect(self):
        db = sqlite3.connect(self.path, timeout=5)
        db.row_factory = sqlite3.Row
        db.execute("PRAGMA foreign_keys=ON")
        try:
            with db:
                yield db
        finally:
            db.close()

    @staticmethod
    def _hash(password: str, salt: bytes):
        return hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), salt, ITERATIONS)

    def profile(self, account_id: int):
        with self.connect() as db:
            row = db.execute(
                "SELECT a.id,a.username,p.unlocked,p.tutorial_complete,p.gold,p.loadout,p.stats,p.xp,p.keys,p.weapon_keys,p.talents,p.gear FROM accounts a "
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
        result["inventory"] = gear["inventory"]
        result["equipment"] = gear["equipped"]
        bonus = {k: talents[k] * per for k, per in TALENTS.items()}
        for item in gear["inventory"]:
            if gear["equipped"].get(item["slot"]) == item["id"]:
                for k, v in item["stats"].items():
                    bonus[k] = bonus.get(k, 0) + v
        result["bonus"] = {k: round(v, 2) for k, v in bonus.items()}
        return result

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
        ids = {i["id"] for i in inventory}
        equipped = {k: v for k, v in (data.get("equipped") or {}).items() if k in GEAR_SLOTS and v in ids}
        return {"inventory": inventory[:INVENTORY_LIMIT], "equipped": equipped}

    def _save_gear(self, account_id, gear):
        with self.connect() as db:
            db.execute("UPDATE progress SET gear=? WHERE account_id=?", (json.dumps(gear, ensure_ascii=False), account_id))

    def _raw_gear(self, account_id):
        with self.connect() as db:
            row = db.execute("SELECT gear FROM progress WHERE account_id=?", (account_id,)).fetchone()
        return self._parse_gear(row[0] if row else "{}")

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

    def reset_talents(self, account_id: int):
        with self.connect() as db:
            db.execute("UPDATE progress SET talents='{}' WHERE account_id=?", (account_id,))
        return self.profile(account_id)

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
        items = [roll_item(rng) for _ in range(count)]
        gear["inventory"].extend(items)
        self._save_gear(account_id, gear)
        return items, self.profile(account_id)

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
        results, gold = [], 0
        for _ in range(count):
            weapon = rng.choices(list(WEAPON_WEIGHTS), weights=list(WEAPON_WEIGHTS.values()))[0]
            rarity = rng.choices(range(len(RARITIES)), weights=RARITY_WEIGHTS)[0]
            if weapon not in loadout["owned"]:
                loadout["owned"].append(weapon)
                loadout["rarity"][weapon] = rarity
                if len(loadout["equipped"]) < 2:
                    loadout["equipped"].append(weapon)
                outcome, refund = "new", 0
            elif rarity > loadout["rarity"].get(weapon, 1):
                loadout["rarity"][weapon] = rarity
                outcome, refund = "upgrade", 0
            else:
                outcome, refund = "duplicate", DUPLICATE_GOLD[rarity]
                gold += refund
            results.append({"weapon": weapon, "rarity": rarity, "outcome": outcome, "gold": refund})
        with self.connect() as db:
            db.execute("UPDATE progress SET loadout=?, gold=gold+? WHERE account_id=?", (json.dumps(loadout), gold, account_id))
        return results, self.profile(account_id)

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
            db.execute("UPDATE progress SET gold=gold+? WHERE account_id=?", ([10, 25, 60, 150, 400][item["rarity"]], account_id))
        return self.profile(account_id)

    def add_xp(self, account_id: int, xp: int):
        with self.connect() as db:
            db.execute("UPDATE progress SET xp=xp+? WHERE account_id=?", (max(0, min(5000, xp)), account_id))

    STAT_KEYS = ("kills", "headshots", "shots", "hits", "missions", "victories", "best_wave")

    @classmethod
    def _parse_stats(cls, raw):
        try:
            data = json.loads(raw or "{}")
        except ValueError:
            data = {}
        if not isinstance(data, dict):
            data = {}
        return {k: int(data.get(k, 0)) if isinstance(data.get(k, 0), int) else 0 for k in cls.STAT_KEYS}

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
        victory = stats.get("victory") is True
        profile = self.profile(account_id)
        if profile is None:
            raise ValueError("找不到帳號")
        total = profile["stats"]
        for key in ("kills", "headshots", "shots", "hits"):
            total[key] += values[key]
        total["missions"] += 1
        total["victories"] += 1 if victory else 0
        total["best_wave"] = max(total["best_wave"], values["wave"])
        with self.connect() as db:
            db.execute("UPDATE progress SET stats=? WHERE account_id=?", (json.dumps(total), account_id))

    def is_admin(self, username: str) -> bool:
        try:
            lines = self.admin_file.read_text(encoding="utf-8-sig").splitlines()
        except OSError:
            return False
        names = {line.strip().lower() for line in lines if line.strip() and not line.strip().startswith("#")}
        return username.lower() in names

    def list_players(self):
        with self.connect() as db:
            rows = db.execute("SELECT a.username,p.gold,p.unlocked FROM accounts a JOIN progress p ON p.account_id=a.id "
                              "ORDER BY a.username COLLATE NOCASE LIMIT 500").fetchall()
        return [dict(row) for row in rows]

    def admin_grant(self, username, gold, weapon, keys=0, weapon_keys=0):
        if not isinstance(gold, int) or not 0 <= gold <= 100000:
            raise ValueError("金幣數量需為 0–100000")
        if not isinstance(keys, int) or not 0 <= keys <= 1000 or not isinstance(weapon_keys, int) or not 0 <= weapon_keys <= 1000:
            raise ValueError("鑰匙數量需為 0–1000")
        if weapon not in (None, "") and weapon not in WEAPON_PRICES:
            raise ValueError("找不到此武器")
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
                    loadout["rarity"][weapon] = 1
                    if len(loadout["equipped"]) < 2:
                        loadout["equipped"].append(weapon)
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
        return self._spend(account_id, upgrade_cost(weapon, level), loadout)

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

    def tutorial(self, account_id: int):
        with self.connect() as db:
            db.execute("UPDATE progress SET tutorial_complete=1 WHERE account_id=?", (account_id,))
        return self.profile(account_id)

    def complete_mission(self, account_id: int, mission: int):
        profile = self.profile(account_id)
        if profile is None or not isinstance(mission, int) or not 0 <= mission <= profile["unlocked"]:
            raise ValueError("關卡進度不合法")
        with self.connect() as db:
            db.execute("UPDATE progress SET unlocked=MAX(unlocked,?) WHERE account_id=?",
                       (min(2, mission + 1), account_id))
        return self.profile(account_id)


class BetaServer(ThreadingHTTPServer):
    daemon_threads = True

    def __init__(self, address, store: Store, releases: Path):
        self.game_online = lambda: False
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


class Handler(BaseHTTPRequestHandler):
    server: BetaServer
    server_version = "RiftWardBeta/0.3"

    def setup(self):
        super().setup()
        self.connection.settimeout(15)

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
                self.send_response(200)
                self.send_header("Content-Type", "application/octet-stream")
                self.send_header("Content-Length", str(path.stat().st_size))
                self.end_headers()
                with path.open("rb") as handle:
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
                self._json(200 if profile else 401, {"ok": profile is not None, "profile": profile})
                return
            if self.path == "/api/server/progress":
                try:
                    profile = store.complete_mission(data.get("account_id"), data.get("mission"))
                except ValueError as exc:
                    self._json(400, {"ok": False, "error": str(exc)})
                    return
                self._json(200, {"ok": True, "profile": profile})
                return
            if self.path in ("/api/server/reward", "/api/server/profile"):
                try:
                    if self.path.endswith("reward"):
                        if "stats" in data:
                            store.record_stats(data.get("account_id"), data.get("stats"))
                            st = data["stats"]
                            store.add_xp(data.get("account_id"), st.get("kills", 0) * 3 + st.get("wave", 0) * 15 + (200 if st.get("victory") is True else 0))
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
        elif self.path.startswith("/api/admin/"):
            if not profile.get("admin"):
                self._json(403, {"ok": False, "error": "需要管理員權限"})
                return
            if self.path == "/api/admin/players":
                self._json(200, {"ok": True, "players": store.list_players()})
            elif self.path == "/api/admin/grant":
                try:
                    count = store.admin_grant(data.get("username"), data.get("gold", 0), data.get("weapon"), data.get("keys", 0), data.get("weapon_keys", 0))
                except ValueError as exc:
                    self._json(400, {"ok": False, "error": str(exc)})
                    return
                self._json(200, {"ok": True, "count": count, "profile": store.profile(profile["id"])})
            else:
                self._json(404, {"ok": False, "error": "找不到此服務"})
        elif self.path in ("/api/talent/assign", "/api/talent/reset", "/api/gacha/draw", "/api/gacha/weapon", "/api/gear/equip", "/api/gear/salvage"):
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
                elif self.path == "/api/gear/equip":
                    result = store.equip_gear(profile["id"], data.get("item"))
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
