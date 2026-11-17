"""Start the account/update service and the dedicated game together."""
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


def main():
    base = Path(__file__).resolve().parent.parent
    engine = base / "RiftWard.exe"
    pack = base / "game" / "RiftWardGame.pck"
    if not engine.is_file() or not pack.is_file():
        raise SystemExit("Missing executable/game pack. Run the packaged StartServer.cmd.")
    with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as probe:
        if hasattr(socket,"SO_EXCLUSIVEADDRUSE"):
            probe.setsockopt(socket.SOL_SOCKET,socket.SO_EXCLUSIVEADDRUSE,1)
        try:
            probe.bind(("0.0.0.0",24770))
        except OSError as exc:
            raise SystemExit(f"UDP 0.0.0.0:24770 is unavailable. Close the existing game server first: {exc}") from exc
    store = Store(base / "server-data")
    releases = base / "releases"
    releases.mkdir(parents=True,exist_ok=True)
    with pack.open("rb") as handle:
        digest = hashlib.file_digest(handle,"sha256").hexdigest()
    published = releases / (digest + ".pck")
    if not published.exists():
        shutil.copy2(pack,published)
    if not (releases / "manifest.json").exists():
        (releases / "manifest.json").write_text(json.dumps({"version":VERSION,"sha256":digest,"size":pack.stat().st_size}),encoding="utf-8")
    try:
        api = BetaServer((HOST, PORT), store, base / "releases")
    except OSError as exc:
        raise SystemExit(f"Cannot bind {HOST}:{PORT}: {exc}") from exc
    thread = threading.Thread(target=api.serve_forever, daemon=True)
    thread.start()
    print(f"Account/update service: http://{HOST}:{PORT}", flush=True)
    environment = dict(os.environ, RIFTWARD_API_HOST=HOST)
    process = subprocess.Popen([str(engine), "--headless", "--main-pack", str(pack),
                                "--log-file", str(base / "server-data" / "game-server.log"), "--", "--server"], env=environment)
    try:
        while process.poll() is None:
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
