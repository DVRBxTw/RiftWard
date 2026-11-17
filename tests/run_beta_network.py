"""Real HTTP + database + ENet integration on the designated LAN host."""
import importlib.util
import json
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
import threading

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("service",ROOT / "server/service.py")
service = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(service)


def main():
    engine = ROOT / "tools/Godot_v4.5.1-stable_win64_console.exe"
    with tempfile.TemporaryDirectory() as temp:
        store = service.Store(Path(temp))
        api = service.BetaServer((service.HOST,service.PORT),store,ROOT / "build/releases")
        thread = threading.Thread(target=api.serve_forever,daemon=True)
        thread.start()
        environment = os.environ.copy()
        environment["RIFTWARD_SERVER_DATA"] = temp
        processes = []
        logs = []
        try:
            logs.append(tempfile.TemporaryFile())
            server = subprocess.Popen([str(engine),"--headless","--path",str(ROOT),"--script","res://tests/beta_network_server.gd","--","--server"],env=environment,stdout=logs[-1],stderr=subprocess.STDOUT)
            processes.append(server)
            for number in range(2):
                account = store.register("QA_"+secrets.token_hex(5),secrets.token_urlsafe(18))
                store.tutorial(account["profile"]["id"])
                client_env = environment.copy()
                client_env["RIFTWARD_TEST_TOKEN"] = account["token"]
                logs.append(tempfile.TemporaryFile())
                client = subprocess.Popen([str(engine),"--headless","--path",str(ROOT),"--script","res://tests/beta_network_client.gd"],env=client_env,stdout=logs[-1],stderr=subprocess.STDOUT)
                processes.append(client)
            failed = []
            for number, process in enumerate(processes):
                try:
                    process.wait(timeout=40)
                except subprocess.TimeoutExpired:
                    process.terminate()
                    process.wait(timeout=5)
                logs[number].seek(0)
                output = logs[number].read().decode("utf-8",errors="replace")
                print(f"PROCESS {number}:\n{output[:10000]}",flush=True)
                if process.returncode or "PASS" not in output or "SCRIPT ERROR" in output:
                    failed.append(number)
            if failed:
                raise AssertionError(f"Integration processes failed: {failed}")
        finally:
            for process in processes:
                if process.poll() is None:
                    process.terminate()
                    process.wait(timeout=5)
            api.shutdown()
            api.server_close()
            thread.join()
            for log in logs: log.close()
    print("BETA_NETWORK_INTEGRATION_PASS")


if __name__ == "__main__":
    main()
