"""RiftWard beta account, progress and update service (Python standard library)."""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import hashlib
import hmac
import json
import os
from pathlib import Path
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
VERSION = "0.3.0-beta.1"


class Store:
    def __init__(self, directory: Path):
        directory.mkdir(parents=True, exist_ok=True)
        self.path = directory / "players.sqlite3"
        keyfile = directory / "server.key"
        if not keyfile.exists():
            with keyfile.open("x", encoding="ascii") as handle:
                handle.write(secrets.token_hex(32))
        self.server_key = keyfile.read_text(encoding="ascii").strip()
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
                "SELECT a.id,a.username,p.unlocked,p.tutorial_complete FROM accounts a "
                "JOIN progress p ON p.account_id=a.id WHERE a.id=?", (account_id,)
            ).fetchone()
        if row is None:
            return None
        result = dict(row)
        result["tutorial_complete"] = bool(result["tutorial_complete"])
        return result

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
        self.store = store
        self.releases = releases.resolve()
        self.limits = defaultdict(deque)
        self.rate_lock = threading.Lock()
        super().__init__(address, Handler)

    def manifest(self):
        try:
            data = json.loads((self.releases / "manifest.json").read_text(encoding="utf-8-sig"))
            digest = data["sha256"]
            path = self.releases / (digest + ".pck")
            if not re.fullmatch(r"[0-9a-f]{64}", digest) or not path.is_file():
                return None
            if path.stat().st_size != data["size"]:
                return None
            return {"ok": True, "version": data["version"], "sha256": digest,
                    "size": data["size"], "download": "/updates/" + digest + ".pck", "protocol": 1}
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
        match = re.fullmatch(r"/updates/([0-9a-f]{64})\.pck", self.path)
        if match:
            path = self.server.releases / (match[1] + ".pck")
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
        token = self.headers.get("Authorization", "").removeprefix("Bearer ")
        profile = store.authenticate(token)
        if profile is None:
            self._json(401, {"ok": False, "error": "登入已失效，請重新登入"})
            return
        if self.path == "/api/me":
            self._json(200, {"ok": True, "profile": profile})
        elif self.path == "/api/tutorial":
            self._json(200, {"ok": True, "profile": store.tutorial(profile["id"])})
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
