"""Anti-cheat test: an aimbot client against a throwaway local server.

Starts its own account service (in-process, temporary data) and a headless Unity game server bound to
127.0.0.1, registers a throwaway account and plays one mission through the real game protocol while
cheating in two ways:

  lock    the view snaps onto the nearest enemy, then fires. Aim point and view agree.
  silent  the view stays fixed; only the reported aim point is moved onto the enemy (what a memory /
          packet aimbot does). The server should correct these shots and count them.

After the timed phases the bot stands still with the silent aimbot until the mission ends, so the
mission report reaches the account service and the anti-cheat audit log can be checked.

It never connects anywhere but 127.0.0.1.

    python tests/cheat_sim.py build/unity/RiftWard.exe [--phase 25]
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import io
import json
import math
import os
import queue
import re
import secrets
import socket
import struct
import subprocess
import tempfile
import threading
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("service", ROOT / "server/service.py")
service = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(service)
VERSION = re.search(r'Version = "([^"]+)"', (ROOT / "unity/Assets/Scripts/Sim/Defs.cs").read_text(encoding="utf-8"))[1]

# Message ids (unity/Assets/Scripts/Sim/Protocol.cs)
HELLO, INPUT, CHOOSE_BUFF, REQUEST_DEPLOY, LEAVE_MISSION = 1, 2, 3, 5, 7
WELCOME, ERROR, LOBBY, DEPLOY, SNAPSHOT, EVENTS, BUFF_OFFER, FINISH = 50, 51, 52, 53, 54, 55, 56, 57
EYE_HEIGHT, SHOT_RANGE = 1.65, 65.0
KIND_SCALE = [1.0, 0.9, 1.0, 0.75, 1.0, 2.2, 2.6]   # Defs.Kinds scale, for aiming at the chest


# ------------------------------------------------------------------ wire format (C# BinaryWriter)

class Reader:
    def __init__(self, data: bytes):
        self.s = io.BytesIO(data)

    def take(self, fmt):
        size = struct.calcsize(fmt)
        return struct.unpack("<" + fmt, self.s.read(size))

    def i32(self): return self.take("i")[0]
    def f32(self): return self.take("f")[0]
    def u8(self): return self.take("B")[0]
    def bool(self): return self.take("?")[0]
    def i16(self): return self.take("h")[0]
    def vec(self): return self.take("fff")

    def string(self):
        length, shift = 0, 0
        while True:
            b = self.u8()
            length |= (b & 0x7F) << shift
            shift += 7
            if b < 0x80:
                break
        return self.s.read(length).decode("utf-8")


def w_string(text: str) -> bytes:
    data = text.encode("utf-8")
    n, out = len(data), bytearray()
    while True:
        b = n & 0x7F
        n >>= 7
        out.append(b | (0x80 if n else 0))
        if not n:
            break
    return bytes(out) + data


def frame(msg: int, body: bytes = b"") -> bytes:
    return struct.pack("<iB", len(body) + 1, msg) + body


class Connection:
    def __init__(self, port):
        self.sock = socket.create_connection(("127.0.0.1", port), timeout=10)
        self.sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        self.inbox: queue.Queue = queue.Queue()
        threading.Thread(target=self._read, daemon=True).start()

    def _exact(self, n):
        buf = b""
        while len(buf) < n:
            chunk = self.sock.recv(n - len(buf))
            if not chunk:
                raise ConnectionError("closed")
            buf += chunk
        return buf

    def _read(self):
        try:
            while True:
                length = struct.unpack("<i", self._exact(4))[0]
                body = self._exact(length)
                self.inbox.put((body[0], body[1:]))
        except (OSError, ConnectionError):
            self.inbox.put((None, b""))

    def send(self, msg, body=b""):
        self.sock.sendall(frame(msg, body))


# ------------------------------------------------------------------ game state from snapshots

def read_snapshot(r: Reader, me: int):
    state = r.u8()
    r.i32()  # region
    wave, kills = r.i32(), r.i32()
    r.f32(); r.i32(); r.i32(); r.i32(); r.bool()   # elapsed, level, xp, xp need, buff pending
    mine = None
    for _ in range(r.i32()):
        aid = r.i32(); pos = r.vec(); hp = r.f32(); r.f32(); r.f32(); r.f32()
        r.f32(); r.f32(); r.vec(); r.i32()
        r.i32(); r.i32(); r.f32(); r.f32(); r.u8()
        r.f32(); r.f32(); r.f32(); r.f32()
        r.bool(); r.f32()
        for _ in range(r.u8()):
            r.string()
        r.string(); r.u8(); r.bool(); r.u8(); r.bool(); r.bool(); r.u8(); r.i16()
        if aid == me:
            mine = (pos, hp)
    enemies = []
    for _ in range(r.i32()):
        r.i32(); pos = r.vec(); hp = r.f32(); r.f32(); r.f32()
        r.bool(); kind = r.u8(); r.u8(); elite = r.bool()
        if hp > 0:
            enemies.append((pos, KIND_SCALE[min(kind, len(KIND_SCALE) - 1)] * (1.35 if elite else 1.0)))
    return state, wave, kills, mine, enemies


def read_shots(r: Reader, me: int):
    shots = hits = kills = 0
    for _ in range(r.i32()):
        shooter = r.i32(); r.vec(); r.vec(); hit = r.bool(); r.bool(); killed = r.bool(); r.f32()
        if shooter == me:
            shots += 1
            hits += hit
            kills += killed
    return shots, hits, kills


def aim_angles(eye, target):
    dx, dy, dz = target[0] - eye[0], target[1] - eye[1], target[2] - eye[2]
    return math.atan2(dx, dz), math.atan2(dy, math.hypot(dx, dz))


def look(yaw, pitch):
    return (math.sin(yaw) * math.cos(pitch), math.sin(pitch), math.cos(yaw) * math.cos(pitch))


def input_body(seq, yaw, pitch, aim, fire):
    # PlayerInput: seq, move x/y (standing still), yaw, pitch, aim point, flags (1 = fire), weapon slot
    return struct.pack("<iffff", seq, 0.0, 0.0, yaw, pitch) + struct.pack("<fff", *aim) + struct.pack("<BB", 1 if fire else 0, 0)


# ------------------------------------------------------------------ the bot

def play(port, token, phase_seconds, max_seconds):
    conn = Connection(port)
    conn.send(HELLO, w_string(token) + w_string(VERSION))
    me, deployed = None, False
    stats = {mode: {"shots": 0, "hits": 0, "kills": 0} for mode in ("lock", "silent", "standing")}
    mine, enemies = None, []
    seq, start, last_input, finish = 0, None, 0.0, None
    mode = "lock"
    while True:
        try:
            msg, payload = conn.inbox.get(timeout=0.005)
        except queue.Empty:
            msg = 0
        if msg is None:
            raise SystemExit("game server closed the connection")
        if msg == ERROR:
            raise SystemExit("server refused: " + Reader(payload).string())
        if msg == WELCOME:
            me = Reader(payload).i32()
        elif msg == LOBBY and me is not None and not deployed:
            conn.send(REQUEST_DEPLOY, struct.pack("<ii", 0, 0))
            deployed = True
        elif msg == DEPLOY:
            start = time.time()
            print(f"DEPLOYED as actor {me}; phases: lock {phase_seconds}s, silent {phase_seconds}s, then silent standing until the mission ends")
        elif msg == SNAPSHOT and me is not None:
            _, wave, _, found, enemies = read_snapshot(Reader(payload), me)
            mine = found or mine
        elif msg == EVENTS and me is not None:
            shots, hits, kills = read_shots(Reader(payload), me)
            s = stats[mode]
            s["shots"] += shots; s["hits"] += hits; s["kills"] += kills
        elif msg == BUFF_OFFER:
            r = Reader(payload)
            r.i32()
            offer = [r.i32() for _ in range(r.i32())]
            conn.send(CHOOSE_BUFF, struct.pack("<i", offer[0]))
        elif msg == FINISH:
            r = Reader(payload)
            success, region, kills, elapsed, gold = r.bool(), r.i32(), r.i32(), r.f32(), r.i32()
            finish = {"success": success, "team_kills": kills, "elapsed": round(elapsed, 1), "gold": gold}
            break
        if start is None or mine is None:
            continue
        t = time.time() - start
        mode = "lock" if t < phase_seconds else "silent" if t < 2 * phase_seconds else "standing"
        if t > max_seconds:
            conn.send(LEAVE_MISSION)
            print("time limit reached, leaving the mission (no report is sent for an abandoned mission)")
            break
        now = time.time()
        if now - last_input < 1 / 60:
            continue
        last_input = now
        pos, hp = mine
        eye = (pos[0], pos[1] + EYE_HEIGHT, pos[2])
        target = None
        if enemies:
            epos, scale = min(enemies, key=lambda e: (e[0][0] - pos[0]) ** 2 + (e[0][2] - pos[2]) ** 2)
            target = (epos[0], epos[1] + 0.9 * scale, epos[2])
        seq += 1
        if target is None:
            conn.send(INPUT, input_body(seq, 0.0, 0.0, tuple(e + d * SHOT_RANGE for e, d in zip(eye, look(0, 0))), False))
        elif mode == "lock":
            yaw, pitch = aim_angles(eye, target)          # turn the view onto the enemy
            conn.send(INPUT, input_body(seq, yaw, pitch, tuple(e + d * SHOT_RANGE for e, d in zip(eye, look(yaw, pitch))), True))
        else:
            conn.send(INPUT, input_body(seq, 0.0, 0.0, target, True))   # view stays put, aim point jumps to the enemy
    conn.sock.close()
    return stats, finish


def free_port():
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def main():
    import sys
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("exe", type=Path, help="Unity build (RiftWard.exe) used as the headless game server")
    parser.add_argument("--phase", type=float, default=25, help="seconds for each aimbot phase")
    parser.add_argument("--max", type=float, default=420, help="give up after this many seconds")
    args = parser.parse_args()
    with tempfile.TemporaryDirectory() as temp:
        temp = Path(temp)
        releases = temp / "releases"
        releases.mkdir()
        payload = b"cheat-sim"
        digest = hashlib.sha256(payload).hexdigest()
        (releases / (digest + ".pck")).write_bytes(payload)
        (releases / "manifest.json").write_text(json.dumps({"version": VERSION, "sha256": digest, "size": len(payload)}))
        store = service.Store(temp / "data")
        api = service.BetaServer(("127.0.0.1", 0), store, releases)
        threading.Thread(target=api.serve_forever, daemon=True).start()
        game_port = free_port()
        env = dict(os.environ, RIFTWARD_SERVER_DATA=str(temp / "data"), RIFTWARD_API_HOST="127.0.0.1", RIFTWARD_API_PORT=str(api.server_port),
                   RIFTWARD_GAME_PORT=str(game_port), RIFTWARD_BIND="127.0.0.1")
        env.pop("RIFTWARD_TEST_FAST", None)
        log = temp / "server.log"
        server = subprocess.Popen([str(args.exe.resolve()), "-batchmode", "-nographics", "-server", "-logFile", str(log)], env=env)
        try:
            deadline = time.time() + 40
            while time.time() < deadline and not (log.exists() and "SERVER_READY" in log.read_text(errors="replace")):
                time.sleep(0.3)
            account = store.register("Cheat_" + secrets.token_hex(3), secrets.token_urlsafe(16))
            aid = account["profile"]["id"]
            store.tutorial(aid)
            stats, finish = play(game_port, account["token"], args.phase, args.max)
            time.sleep(1.5)   # let the report reach the account service
        finally:
            server.kill()
            api.shutdown()
            api.server_close()

        print("\n==== aimbot results ====")
        names = {"lock": "鎖定型（轉視角）", "silent": "靜默型（只改瞄準點）", "standing": "靜默型 站樁到結算"}
        for mode, s in stats.items():
            rate = f"{100 * s['hits'] / s['shots']:.0f}%" if s["shots"] else "-"
            print(f"  {names[mode]:<14} 開槍 {s['shots']:>4}  命中 {s['hits']:>4} ({rate:>4})  擊殺 {s['kills']:>3}")
        if finish:
            print(f"  結算：{'勝利' if finish['success'] else '失敗'}，全隊擊殺 {finish['team_kills']}，{finish['elapsed']} 秒，金幣 {finish['gold']}")
        entries = [e for e in store.audit_log() if e["username"] == account["profile"]["username"]]
        profile = store.profile(aid)
        print(f"  帳號金幣 {profile['gold']}，總擊殺 {profile['stats']['kills']}")
        print("  稽核記錄：" + ("；".join(f"[{'拒絕' if e['rejected'] else '可疑'}] {e['reason']}" for e in entries) if entries else "（無）"))
        lock, silent = stats["lock"], stats["silent"]
        verdict = []
        verdict.append("PASS 靜默自瞄被校正" if silent["shots"] and silent["hits"] <= silent["shots"] * 0.2 else "FAIL 靜默自瞄仍然命中")
        if finish:
            verdict.append("PASS 結算被標記" if any("瞄準" in e["reason"] for e in entries) else "FAIL 結算沒有被標記")
        verdict.append(("NOTE 鎖定型自瞄命中率 " + (f"{100 * lock['hits'] / lock['shots']:.0f}%" if lock["shots"] else "-")) + "（視角與瞄準點一致，這項檢查本來就抓不到）")
        print("\n".join("  " + v for v in verdict))


if __name__ == "__main__":
    main()
