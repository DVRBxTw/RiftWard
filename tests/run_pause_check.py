"""Online solo pause: a player alone in a mission opens the pause menu and the server freezes the mission.

Runs a throwaway local account service and Unity game server (127.0.0.1 only), deploys a solo mission
through the real protocol, then checks that while paused no snapshots arrive and the mission clock does
not advance, and that after resuming the clock continues from where it stopped.

    python tests/run_pause_check.py build/unity/RiftWard.exe
"""
from __future__ import annotations

import os
import queue
import secrets
import struct
import subprocess
import sys
import tempfile
import threading
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cheat_sim as cs   # reuses the protocol helpers and local server setup

SET_PAUSE = 17


def mission_clock(payload):
    r = cs.Reader(payload)
    r.u8(); r.i32(); r.i32(); r.i32()
    return r.f32()


def main():
    exe = Path(sys.argv[1]).resolve()
    with tempfile.TemporaryDirectory() as temp:
        temp = Path(temp)
        releases = temp / "releases"
        releases.mkdir()
        payload = b"pause-check"
        digest = cs.hashlib.sha256(payload).hexdigest()
        (releases / (digest + ".pck")).write_bytes(payload)
        (releases / "manifest.json").write_text(cs.json.dumps({"version": cs.VERSION, "sha256": digest, "size": len(payload)}))
        store = cs.service.Store(temp / "data")
        api = cs.service.BetaServer(("127.0.0.1", 0), store, releases)
        threading.Thread(target=api.serve_forever, daemon=True).start()
        port = cs.free_port()
        env = dict(os.environ, RIFTWARD_SERVER_DATA=str(temp / "data"), RIFTWARD_API_HOST="127.0.0.1", RIFTWARD_API_PORT=str(api.server_port),
                   RIFTWARD_GAME_PORT=str(port), RIFTWARD_BIND="127.0.0.1")
        env.pop("RIFTWARD_TEST_FAST", None)
        log = temp / "server.log"
        server = subprocess.Popen([str(exe), "-batchmode", "-nographics", "-server", "-logFile", str(log)], env=env)
        try:
            deadline = time.time() + 40
            while time.time() < deadline and not (log.exists() and "SERVER_READY" in log.read_text(errors="replace")):
                time.sleep(0.3)
            account = store.register("Pause_" + secrets.token_hex(3), secrets.token_urlsafe(16))
            store.tutorial(account["profile"]["id"])
            conn = cs.Connection(port)
            conn.send(cs.HELLO, cs.w_string(account["token"]) + cs.w_string(cs.VERSION))
            clock, deployed, me = None, False, None

            def pump(seconds):
                """Drains messages for a while; returns (snapshots received, last mission clock)."""
                nonlocal clock, deployed, me
                snapshots, end = 0, time.time() + seconds
                while time.time() < end:
                    try:
                        msg, data = conn.inbox.get(timeout=0.05)
                    except queue.Empty:
                        continue
                    if msg == cs.WELCOME:
                        me = cs.Reader(data).i32()
                    elif msg == cs.LOBBY and me is not None and not deployed:
                        conn.send(cs.REQUEST_DEPLOY, struct.pack("<ii", 0, 0))
                        deployed = True
                    elif msg == cs.SNAPSHOT:
                        snapshots += 1
                        clock = mission_clock(data)
                    elif msg == cs.BUFF_OFFER:
                        r = cs.Reader(data); r.i32()
                        offer = [r.i32() for _ in range(r.i32())]
                        conn.send(cs.CHOOSE_BUFF, struct.pack("<i", offer[0]))
                return snapshots, clock

            pump(6)
            before = clock
            conn.send(SET_PAUSE, b"\x01")
            pump(0.4)                       # let in-flight snapshots arrive
            paused_at = clock
            frozen_snaps, frozen_clock = pump(3)
            conn.send(SET_PAUSE, b"\x00")
            resumed_snaps, resumed_clock = pump(2)
        finally:
            server.kill()
            api.shutdown()
            api.server_close()
        print(f"running before pause: clock {before:.2f}")
        print(f"paused 3 s: snapshots {frozen_snaps}, clock {paused_at:.2f} -> {frozen_clock:.2f}")
        print(f"resumed 2 s: snapshots {resumed_snaps}, clock {frozen_clock:.2f} -> {resumed_clock:.2f}")
        ok = before is not None and before > 1 and frozen_snaps == 0 and abs(frozen_clock - paused_at) < 0.01 \
            and resumed_snaps > 10 and 1.0 < resumed_clock - paused_at < 3.0
        print("PAUSE_CHECK_PASS" if ok else "PAUSE_CHECK_FAIL")
        if not ok:
            raise SystemExit(1)


if __name__ == "__main__":
    main()
