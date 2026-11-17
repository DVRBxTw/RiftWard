import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import threading
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen

SPEC = importlib.util.spec_from_file_location("riftward_service", Path(__file__).resolve().parents[1] / "server/service.py")
service = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(service)


class ServiceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.base = Path(self.temp.name)
        releases = self.base / "releases"
        releases.mkdir()
        self.payload = b"test-pack\x00contents"
        self.digest = hashlib.sha256(self.payload).hexdigest()
        (releases / (self.digest + ".pck")).write_bytes(self.payload)
        (releases / "manifest.json").write_text(json.dumps({"version":service.VERSION,"sha256":self.digest,"size":len(self.payload)}))
        self.store = service.Store(self.base / "data")
        self.server = service.BetaServer(("127.0.0.1",0),self.store,releases)
        self.thread = threading.Thread(target=self.server.serve_forever,daemon=True)
        self.thread.start()
        self.url = f"http://127.0.0.1:{self.server.server_port}"

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()
        self.temp.cleanup()

    def call(self, path, data=None, headers=None):
        request = Request(self.url+path, data=json.dumps(data).encode() if data is not None else None,
                          headers={"Content-Type":"application/json",**(headers or {})})
        try:
            response = urlopen(request,timeout=5)
        except HTTPError as error:
            response = error
        with response:
            return response.status,json.loads(response.read())

    def register(self):
        code, response = self.call("/api/register",{"username":"Beta_Test","password":"test-password-8921"})
        self.assertEqual(code,201)
        return response

    def test_auth_and_persistence(self):
        account = self.register()
        self.assertFalse(account["profile"]["tutorial_complete"])
        self.assertEqual(self.call("/api/register",{"username":"beta_test","password":"test-password-8921"})[0],400)
        self.assertEqual(self.call("/api/login",{"username":"Beta_Test","password":"wrong-password"})[0],401)
        _, login = self.call("/api/login",{"username":"beta_test","password":"test-password-8921"})
        self.assertTrue(login["ok"])
        self.assertNotEqual(account["token"],login["token"])
        headers = {"Authorization":"Bearer "+account["token"]}
        self.assertEqual(self.call("/api/me",{},headers)[0],200)
        self.assertTrue(self.call("/api/tutorial",{},headers)[1]["profile"]["tutorial_complete"])
        self.assertEqual(self.call("/api/me",{}, {"Authorization":"Bearer fake"})[0],401)
        reopened = service.Store(self.base / "data")
        self.assertTrue(reopened.authenticate(account["token"])["tutorial_complete"])
        with self.store.connect() as db:
            row = db.execute("SELECT password_hash,salt FROM accounts").fetchone()
            self.assertNotIn(b"test-password",bytes(row["password_hash"]))
            self.assertEqual(len(row["salt"]),16)
        self.assertEqual(self.call("/api/logout",{},headers)[0],200)
        self.assertEqual(self.call("/api/me",{},headers)[0],401)

    def test_server_only_progress_and_version(self):
        account = self.register()
        data = {"token":account["token"],"version":service.VERSION}
        self.assertEqual(self.call("/api/server/validate",data)[0],403)
        server_headers = {"X-Server-Key":self.store.server_key}
        self.assertEqual(self.call("/api/server/validate",{**data,"version":"0.0.1"},server_headers)[0],409)
        self.assertTrue(self.call("/api/server/validate",data,server_headers)[1]["ok"])
        progress = {"account_id":account["profile"]["id"],"mission":2}
        self.assertEqual(self.call("/api/server/progress",progress,server_headers)[0],400)
        progress["mission"] = 0
        self.assertEqual(self.call("/api/server/progress",progress)[0],403)
        self.assertEqual(self.call("/api/server/progress",progress,server_headers)[1]["profile"]["unlocked"],1)

    def test_manifest_and_path_restriction(self):
        code, manifest = self.call("/api/version")
        self.assertEqual(code,200)
        self.assertEqual(manifest["sha256"],self.digest)
        with urlopen(self.url+manifest["download"]) as response:
            self.assertEqual(response.read(),self.payload)
        self.assertEqual(self.call("/updates/../data/server.key")[0],404)
        self.assertEqual(self.call("/updates/"+"0"*64+".pck")[0],404)

    def test_input_validation(self):
        for username, password in [("a", "12345678"),("user';--","12345678"),("okay_user","short"),(123,"12345678")]:
            self.assertEqual(self.call("/api/register",{"username":username,"password":password})[0],400)

    def test_session_resume_availability_and_expiry(self):
        account = self.register()
        headers = {"Authorization": "Bearer " + account["token"]}
        self.assertFalse(self.call("/api/me", {}, headers)[1]["game_online"])
        self.server.game_online = lambda: True
        self.assertTrue(self.call("/api/me", {}, headers)[1]["game_online"])
        with self.store.connect() as db:
            db.execute("UPDATE sessions SET expires=0")
        self.assertEqual(self.call("/api/me", {}, headers)[0], 401)


if __name__ == "__main__":
    unittest.main()
