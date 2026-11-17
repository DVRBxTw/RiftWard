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

    def test_gold_and_armory(self):
        account = self.register()
        auth = {"Authorization":"Bearer "+account["token"]}
        key = {"X-Server-Key":self.store.server_key}
        aid = account["profile"]["id"]
        self.assertEqual(account["profile"]["gold"],0)
        lo = account["profile"]["loadout"]
        self.assertEqual((lo["owned"], lo["equipped"], lo["rarity"]), (["rifle"], ["rifle"], {"rifle": 0}))
        # Players cannot grant themselves gold; only the game server can.
        self.assertEqual(self.call("/api/server/reward",{"account_id":aid,"gold":500})[0],403)
        self.assertEqual(self.call("/api/server/reward",{"account_id":aid,"gold":99999},key)[0],400)
        code, reward = self.call("/api/server/reward",{"account_id":aid,"gold":1000},key)
        self.assertEqual((code,reward["profile"]["gold"]),(200,1000))
        # Weapons are no longer sold; gold still buys upgrades.
        self.assertEqual(self.call("/api/shop/buy",{"weapon":"sniper"},auth)[0],400)
        code, upgraded = self.call("/api/shop/upgrade",{"weapon":"rifle","stat":"damage"},auth)
        self.assertEqual(code,200)
        self.assertEqual(upgraded["profile"]["loadout"]["levels"]["rifle"]["damage"],1)
        self.assertEqual(self.call("/api/shop/upgrade",{"weapon":"sniper","stat":"damage"},auth)[0],400)
        self.assertEqual(self.call("/api/shop/upgrade",{"weapon":"rifle","stat":"speed"},auth)[0],400)
        # Weapon supply draws use their own keys.
        self.assertEqual(self.call("/api/gacha/weapon",{"count":1},auth)[0],400)
        with self.store.connect() as db:
            db.execute("UPDATE progress SET weapon_keys=30 WHERE account_id=?", (aid,))
        code, drawn = self.call("/api/gacha/weapon",{"count":10},auth)
        self.assertEqual((code, len(drawn["weapons"]), drawn["profile"]["weapon_keys"], drawn["profile"]["keys"]), (200, 10, 20, 0))
        lo = drawn["profile"]["loadout"]
        # Every drawn weapon is kept as a copy; nothing is replaced or auto-salvaged.
        self.assertEqual(sum(len(c) for c in lo["copies"].values()), 11)
        self.assertEqual(lo["copies"]["rifle"][0], 0)
        self.assertEqual(lo["rarity"]["rifle"], 0)
        self.assertLessEqual(len(lo["equipped"]), 2)
        gun = next(w for w, c in lo["copies"].items() if len(c) > 1)
        code, r = self.call("/api/weapon/select", {"weapon": gun, "index": 1}, auth)
        self.assertEqual((code, r["profile"]["loadout"]["active"][gun], r["profile"]["loadout"]["rarity"][gun]), (200, 1, lo["copies"][gun][1]))
        self.assertEqual(self.call("/api/weapon/salvage", {"weapon": gun, "index": 1}, auth)[0], 400)
        gold = r["profile"]["gold"]
        code, r = self.call("/api/weapon/salvage", {"weapon": gun, "index": 0}, auth)
        lo2 = r["profile"]["loadout"]
        self.assertEqual((code, len(lo2["copies"][gun]), lo2["active"][gun]), (200, len(lo["copies"][gun]) - 1, 0))
        self.assertGreater(r["profile"]["gold"], gold)
        # A stale rarity (the list changed since the client drew it) is refused instead of hitting another copy.
        wrong = (lo2["copies"][gun][0] + 1) % 5
        self.assertEqual(self.call("/api/weapon/salvage", {"weapon": gun, "index": 0, "rarity": wrong}, auth)[0], 400)
        lo = lo2
        # Bulk weapon salvage keeps every active copy and re-points the active index.
        active_rarity = {w: lo["copies"][w][lo["active"][w]] for w in lo["owned"]}
        code, r = self.call("/api/salvage/bulk", {"kind": "weapon", "rarities": [0, 1, 2, 3, 4], "confirm": True}, auth)
        lo = r["profile"]["loadout"]
        self.assertEqual(code, 200)
        for w in lo["owned"]:
            self.assertEqual((lo["copies"][w], lo["active"][w]), ([active_rarity[w]], 0))
        code, equipped = self.call("/api/shop/equip",{"weapons":[lo["owned"][0]]},auth)
        self.assertEqual((code,equipped["profile"]["loadout"]["equipped"]),(200,[lo["owned"][0]]))
        # Legacy loadouts keep their guns, counted as 普通.
        with self.store.connect() as db:
            db.execute("UPDATE progress SET loadout=? WHERE account_id=?", ('{"owned":["pistol","sniper"],"equipped":["pistol"]}', aid))
        self.assertEqual(self.store.profile(aid)["loadout"]["rarity"], {"pistol": 1, "sniper": 1})

    def test_admin_grant(self):
        account = self.register()
        auth = {"Authorization":"Bearer "+account["token"]}
        self.assertFalse(account["profile"]["admin"])
        self.assertEqual(self.call("/api/admin/grant",{"username":"Beta_Test","gold":100},auth)[0],403)
        self.store.admin_file.write_text("# admins\nbeta_test\n",encoding="utf-8")
        code, players = self.call("/api/admin/players",{},auth)
        self.assertEqual((code,players["players"][0]["username"]),(200,"Beta_Test"))
        code, granted = self.call("/api/admin/grant",{"username":"Beta_Test","gold":250,"weapon":"sniper"},auth)
        self.assertEqual((code,granted["count"],granted["profile"]["gold"]),(200,1,250))
        self.assertIn("sniper",granted["profile"]["loadout"]["owned"])
        self.assertEqual(self.call("/api/admin/grant",{"username":"*","gold":10},auth)[1]["profile"]["gold"],260)
        self.assertEqual(self.call("/api/admin/grant",{"username":"nobody_here","gold":10},auth)[0],400)
        self.assertEqual(self.call("/api/admin/grant",{"username":"Beta_Test","gold":-5},auth)[0],400)

    def test_levels_talents_keys_and_gear(self):
        account = self.register()
        aid = account["profile"]["id"]
        auth = {"Authorization": "Bearer " + account["token"]}
        key = {"X-Server-Key": self.store.server_key}
        p = account["profile"]
        self.assertEqual((p["level"], p["talent_points"], p["keys"]), (1, 0, 0))
        # Missions grant account XP: 40 kills * 1 + 20 waves * 5 + 100 victory = 240 -> level 2 (needs 150).
        record = {"kills": 40, "headshots": 12, "shots": 300, "hits": 180, "wave": 20, "victory": True}
        self.call("/api/server/reward", {"account_id": aid, "gold": 0, "stats": record}, key)
        p = self.store.profile(aid)
        self.assertEqual((p["level"], p["talent_points"]), (2, 1))
        # Two more wins: 720 total -> level 4 (150 + 200 + 250 = 600).
        for _ in range(2):
            self.call("/api/server/reward", {"account_id": aid, "gold": 0, "stats": record}, key)
        p = self.store.profile(aid)
        self.assertEqual((p["level"], p["talent_points"]), (4, 3))
        code, r = self.call("/api/talent/assign", {"talent": "damage", "points": 2}, auth)
        self.assertEqual((code, r["profile"]["talents"]["damage"], r["profile"]["talent_points"], r["profile"]["bonus"]["damage"]), (200, 2, 1, 4))
        self.assertEqual(self.call("/api/talent/assign", {"talent": "damage", "points": 2}, auth)[0], 400)
        # No keys -> no draws; an admin grants keys.
        self.assertEqual(self.call("/api/gacha/draw", {"count": 1}, auth)[0], 400)
        self.store.admin_file.write_text("beta_test\n", encoding="utf-8")
        self.assertEqual(self.call("/api/admin/grant", {"username": "Beta_Test", "keys": 11}, auth)[1]["profile"]["keys"], 11)
        code, r = self.call("/api/gacha/draw", {"count": 10}, auth)
        self.assertEqual((code, len(r["items"]), r["profile"]["keys"], len(r["profile"]["inventory"])), (200, 10, 1, 10))
        item = r["items"][0]
        self.assertIn(item["rarity"], range(5))
        code, r = self.call("/api/gear/equip", {"item": item["id"]}, auth)
        self.assertEqual(r["profile"]["equipment"][item["slot"]], item["id"])
        primary = service.GEAR_SLOTS[item["slot"]]
        self.assertGreaterEqual(r["profile"]["bonus"][primary], item["stats"][primary])
        self.assertEqual(self.call("/api/gear/salvage", {"item": item["id"]}, auth)[0], 400)
        other = r["profile"]["inventory"][1]
        gold = r["profile"]["gold"]
        code, r = self.call("/api/gear/salvage", {"item": other["id"]}, auth)
        self.assertEqual((code, len(r["profile"]["inventory"])), (200, 9))
        self.assertGreater(r["profile"]["gold"], gold)
        # Bulk salvage: 傳奇+ needs confirm; worn gear always survives.
        self.assertEqual(self.call("/api/salvage/bulk", {"kind": "gear", "rarities": [3]}, auth)[0], 400)
        self.assertEqual(self.call("/api/salvage/bulk", {"kind": "gear", "rarities": []}, auth)[0], 400)
        gold = r["profile"]["gold"]
        code, r = self.call("/api/salvage/bulk", {"kind": "gear", "rarities": [0, 1, 2, 3, 4], "confirm": True}, auth)
        self.assertEqual((code, r["count"], [i["id"] for i in r["profile"]["inventory"]]), (200, 8, [item["id"]]))
        self.assertEqual(r["profile"]["gold"], gold + r["gold"])

    def test_lifetime_stats(self):
        account = self.register()
        aid = account["profile"]["id"]
        key = {"X-Server-Key": self.store.server_key}
        self.assertEqual(account["profile"]["stats"]["kills"], 0)
        record = {"kills": 40, "headshots": 12, "shots": 300, "hits": 180, "wave": 20, "victory": True}
        code, result = self.call("/api/server/reward", {"account_id": aid, "gold": 100, "stats": record}, key)
        self.assertEqual(code, 200)
        self.call("/api/server/reward", {"account_id": aid, "gold": 10, "stats": dict(record, wave=7, victory=False)}, key)
        stats = self.store.profile(aid)["stats"]
        self.assertEqual((stats["kills"], stats["missions"], stats["victories"], stats["best_wave"]), (80, 2, 1, 20))
        self.assertEqual(self.call("/api/server/reward", {"account_id": aid, "gold": 0, "stats": dict(record, hits=999)}, key)[0], 400)
        self.assertEqual(self.call("/api/server/reward", {"account_id": aid, "gold": 0, "stats": dict(record, kills=-1)}, key)[0], 400)

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
