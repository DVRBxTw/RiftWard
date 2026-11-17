import importlib.util
import hashlib
import json
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
import threading

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("service",ROOT/"server/service.py")
service = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(service)


def main():
    with tempfile.TemporaryDirectory() as temporary:
        base = Path(temporary)
        payload = secrets.token_bytes(32000)
        digest = hashlib.sha256(payload).hexdigest()
        releases = base/"releases"
        releases.mkdir()
        (releases/(digest+".pck")).write_bytes(payload)
        (releases/"manifest.json").write_text(json.dumps({"version":"0.3.0-qa."+secrets.token_hex(3),"sha256":digest,"size":len(payload)}))
        api = service.BetaServer((service.HOST,service.PORT),service.Store(base/"data"),releases)
        thread = threading.Thread(target=api.serve_forever,daemon=True)
        thread.start()
        try:
            for attempt in range(2):
                environment = os.environ.copy()
                environment["RIFTWARD_EXPECT_DOWNLOAD"] = "1"
                result = subprocess.run([str(ROOT/"tools/Godot_v4.5.1-stable_win64_console.exe"),"--headless","--path",str(ROOT/"launcher"),"--script","res://tests/http_update.gd"],env=environment,capture_output=True,timeout=20)
                output = (result.stdout+result.stderr).decode("utf-8",errors="replace")
                print(output)
                assert result.returncode == 0 and "LAUNCHER_HTTP_E2E_PASS" in output and "SCRIPT ERROR" not in output
        finally:
            api.shutdown()
            api.server_close()
            thread.join()
    print("UPDATE_DOWNLOAD_AND_CACHE_PASS")


if __name__ == "__main__":
    main()
