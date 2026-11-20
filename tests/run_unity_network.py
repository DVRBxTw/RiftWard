"""End-to-end test: four Unity clients clear chapter one, return home, then deploy into chapter two."""
import hashlib, importlib.util, json, os, secrets, socket, subprocess, sys, tempfile, threading, time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("service", ROOT / "server/service.py")
service = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(service)
import re
VERSION = re.search(r'Version = "([^"]+)"', (ROOT / "unity/Assets/Scripts/Sim/Defs.cs").read_text(encoding="utf-8"))[1]


def free_port(kind):
    with socket.socket(socket.AF_INET, kind) as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def main():
    exe = Path(sys.argv[1])
    with tempfile.TemporaryDirectory() as temp:
        temp = Path(temp)
        releases = temp / "releases"
        releases.mkdir()
        payload = b"unity-build-placeholder"
        digest = hashlib.sha256(payload).hexdigest()
        (releases / (digest + ".pck")).write_bytes(payload)
        (releases / "manifest.json").write_text(json.dumps({"version": VERSION, "sha256": digest, "size": len(payload)}))
        store = service.Store(temp / "data")
        store.min_wave_seconds = 0   # RIFTWARD_TEST_FAST clears waves instantly
        api = service.BetaServer(("127.0.0.1", 0), store, releases)
        api.game_online = lambda: True
        threading.Thread(target=api.serve_forever, daemon=True).start()
        game_port = free_port(socket.SOCK_STREAM)
        env = dict(os.environ, RIFTWARD_SERVER_DATA=str(temp / "data"), RIFTWARD_API_HOST="127.0.0.1", RIFTWARD_API_PORT=str(api.server_port),
                   RIFTWARD_GAME_PORT=str(game_port), RIFTWARD_BIND="127.0.0.1", RIFTWARD_TEST_FAST="1",
                   RIFTWARD_TEST_API_HOST="127.0.0.1", RIFTWARD_TEST_API_PORT=str(api.server_port), RIFTWARD_TEST_GAME_PORT=str(game_port),
                   RIFTWARD_TEST_PLAYERS="4", RIFTWARD_TEST_ROUNDS="2", RIFTWARD_TEST_REGION="2")
        logs = [temp / "server.log"]
        procs = [subprocess.Popen([str(exe), "-batchmode", "-nographics", "-server", "-logFile", str(logs[0])], env=env)]
        deadline = time.time() + 30
        while time.time() < deadline and not (logs[0].exists() and "SERVER_READY" in logs[0].read_text(errors="replace")):
            time.sleep(0.3)
        for n in range(4):
            account = store.register("QA_" + secrets.token_hex(4), secrets.token_urlsafe(16))
            store.tutorial(account["profile"]["id"])
            for region in range(2):
                store.complete_mission(account["profile"]["id"], region, 0)
            client_env = dict(env, RIFTWARD_TEST_TOKEN=account["token"])
            logs.append(temp / f"client{n}.log")
            procs.append(subprocess.Popen([str(exe), "-batchmode", "-nographics", "-net-test", "-logFile", str(logs[-1])], env=client_env))
            time.sleep(1.0)
        failed = []
        for i, p in enumerate(procs[1:], 1):
            try:
                p.wait(timeout=120)
            except subprocess.TimeoutExpired:
                p.kill()
            text = logs[i].read_text(errors="replace") if logs[i].exists() else ""
            interesting = [l for l in text.splitlines() if "UNITY_CLIENT" in l or "Exception" in l]
            print(f"CLIENT {i}:", *interesting, sep="\n  ")
            if text.count("UNITY_CLIENT_PASS") != 2 or "UNITY_CLIENT_RETURN_PASS" not in text or "UNITY_CLIENT_FAIL" in text:
                failed.append(i)
        procs[0].kill()
        server_text = logs[0].read_text(errors="replace")
        print("SERVER:", *[l for l in server_text.splitlines() if any(k in l for k in ("SERVER_READY", "VERIFIED", "MISSION", "RETURNED", "Exception", "error"))], sep="\n  ")
        api.shutdown()
        api.server_close()
        for acct in store.list_players():
            print("ACCOUNT", acct)
        if failed:
            raise SystemExit(f"FAILED clients {failed}")
        print("UNITY_NETWORK_INTEGRATION_PASS")


if __name__ == "__main__":
    main()
