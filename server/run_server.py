"""Start the account/update service and the dedicated game server together.

Unity builds live in build/unity/RiftWard.exe (TCP game port, zipped client in game/RiftWard.zip).
Older Godot builds (RiftWard.exe + game/RiftWardGame.pck, UDP game port) are still supported.
"""
from pathlib import Path
import hashlib
import json
import os
import shutil
import socket
import subprocess
import threading
import sys
import time

from service import BetaServer, Store, HOST, PORT, VERSION

GAME_PORT = 24770


def port_free(kind):
    with socket.socket(socket.AF_INET, kind) as probe:
        if hasattr(socket, "SO_EXCLUSIVEADDRUSE"):
            probe.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        try:
            probe.bind(("0.0.0.0", GAME_PORT))
            return None
        except OSError as exc:
            return exc


def main():
    base = Path(__file__).resolve().parent.parent
    unity_engine = base / "unity" / "RiftWard.exe"
    unity = unity_engine.is_file()
    if unity:
        engine, pack, package, kind = unity_engine, base / "game" / "RiftWard.zip", "zip", socket.SOCK_STREAM
    else:
        engine, pack, package, kind = base / "RiftWard.exe", base / "game" / "RiftWardGame.pck", "pck", socket.SOCK_DGRAM
    if not engine.is_file() or not pack.is_file():
        raise SystemExit("Missing executable/game pack. Run the packaged StartServer.cmd.")
    problem = port_free(kind)
    if problem:
        protocol = "TCP" if unity else "UDP"
        raise SystemExit(f"{protocol} 0.0.0.0:{GAME_PORT} is unavailable. Close the existing game server first: {problem}")
    store = Store(base / "server-data")
    releases = base / "releases"
    releases.mkdir(parents=True, exist_ok=True)
    with pack.open("rb") as handle:
        digest = hashlib.file_digest(handle, "sha256").hexdigest()
    published = releases / f"{digest}.{package}"
    if not published.exists():
        shutil.copy2(pack, published)
    bundled = base / "game" / "manifest.json"
    version = VERSION
    if bundled.exists():
        version = json.loads(bundled.read_text(encoding="utf-8-sig")).get("version", VERSION)
    manifest = {"version": version, "sha256": digest, "size": pack.stat().st_size}
    if package == "zip":
        manifest["package"] = "zip"
    (releases / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    try:
        api = BetaServer((HOST, PORT), store, base / "releases")
    except OSError as exc:
        raise SystemExit(f"Cannot bind {HOST}:{PORT}: {exc}") from exc
    thread = threading.Thread(target=api.serve_forever, daemon=True)
    thread.start()
    print(f"Account/update service: http://{HOST}:{PORT}  ·  client version {version}", flush=True)
    environment = dict(os.environ, RIFTWARD_API_HOST=HOST, RIFTWARD_SERVER_DATA=str(base / "server-data"))
    log_path = base / "server-data" / "game-server.log"
    log_path.write_text("", encoding="utf-8")
    if unity:
        command = [str(engine), "-batchmode", "-nographics", "-server", "-logFile", str(log_path)]
    else:
        command = [str(engine), "--headless", "--main-pack", str(pack), "--log-file", str(log_path), "--", "--server"]
    process = subprocess.Popen(command, env=environment)
    ready = False
    api.game_online = lambda: ready and process.poll() is None
    try:
        while process.poll() is None:
            if not ready:
                with log_path.open(encoding="utf-8", errors="replace") as log:
                    ready = "SERVER_READY address=" in log.read()
                if ready:
                    print(f"Game server ready on {'TCP' if unity else 'UDP'} {GAME_PORT}", flush=True)
            time.sleep(0.5)
    except KeyboardInterrupt:
        process.terminate()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
    finally:
        api.shutdown()
        api.server_close()
    return process.returncode or 0


if __name__ == "__main__":
    sys.exit(main())
