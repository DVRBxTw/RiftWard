import random
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
        self.store.min_wave_seconds = 0   # timing checks have their own test
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

    def test_feedback_submit_validation_and_private_history(self):
        account = self.register()
        auth = {"Authorization": "Bearer " + account["token"]}
        body = "四人房間開始遊戲後無法繼續，請協助檢查。"
        code, response = self.call("/api/feedback/send", {"category": "bug", "body": body}, auth)
        self.assertEqual(code, 200)
        self.assertTrue(response["ok"])
        self.assertEqual(response["feedback"][0]["body"], body)
        code, history = self.call("/api/feedback/mine", {}, auth)
        self.assertEqual(code, 200)
        self.assertEqual(len(history["feedback"]), 1)
        code, invalid = self.call("/api/feedback/send", {"category": "bug", "body": "短"}, auth)
        self.assertEqual(code, 400)
        self.assertIn("5", invalid["error"])
        code, other = self.call("/api/register", {"username": "Other_Test", "password": "other-password-8921"})
        self.assertEqual(code, 201)
        code, history = self.call("/api/feedback/mine", {}, {"Authorization": "Bearer " + other["token"]})
        self.assertEqual(code, 200)
        self.assertEqual(history["feedback"], [])

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
        # The starter rifle is 劣質: upgrade price x0.6 (120 * 1.3 * 0.6 = 94).
        self.assertEqual(upgraded["profile"]["gold"], 1000 - 94)
        self.assertEqual(service.upgrade_cost("rifle", 0, 4), 468)
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

    def test_pity_copies_and_concurrency(self):
        account = self.register()
        aid = account["profile"]["id"]
        self.assertEqual(account["profile"]["pity"], {"gear": 50, "weapon": 50, "limited": service.LIMITED_PITY})
        # Pity: a run that never rolled 傳奇+ is forced up on the 50th draw.
        rng = random.Random(1)
        pity, rarity = 0, 0
        for _ in range(49):
            pity = service.roll_rarity(type("R", (), {"choices": lambda self, pop, weights: [min(pop[0], 1)]})(), pity)[1]
        rarity, pity = service.roll_rarity(rng, pity)
        self.assertGreaterEqual(rarity, 3)
        self.assertEqual(pity, 0)
        # The counter persists per pool and is reported as draws left.
        with self.store.connect() as db:
            db.execute("UPDATE progress SET weapon_keys=10, pity=? WHERE account_id=?", (json.dumps({"weapon": 45}), aid))
        code, drawn = self.call("/api/gacha/weapon", {"count": 10}, {"Authorization": "Bearer " + account["token"]})
        self.assertEqual(code, 200)
        self.assertTrue(any(w["rarity"] >= 3 for w in drawn["weapons"][:5]))
        # Copy limit: a full armory turns the draw into gold.
        lo = self.store.profile(aid)["loadout"]
        for w in list(service.WEAPON_WEIGHTS):
            if w not in lo["owned"]:
                lo["owned"].append(w); lo["active"][w] = 0
            lo["copies"][w] = [1] * service.WEAPON_COPY_LIMIT
        self.store._save_loadout(aid, lo)
        with self.store.connect() as db:
            db.execute("UPDATE progress SET weapon_keys=1 WHERE account_id=?", (aid,))
        code, drawn = self.call("/api/gacha/weapon", {"count": 1}, {"Authorization": "Bearer " + account["token"]})
        self.assertEqual((code, drawn["weapons"][0]["outcome"]), (200, "full"))
        self.assertGreater(drawn["weapons"][0]["gold"] + drawn["weapons"][0]["shards"], 0)   # 傳奇+ turn into shards
        # Salvaging a copy in front of the active one shifts the active index back.
        lo = self.store.profile(aid)["loadout"]
        lo["copies"]["rifle"] = [0, 1, 2]; lo["active"]["rifle"] = 2
        self.store._save_loadout(aid, lo)
        p = self.store.salvage_weapon_copy(aid, "rifle", 0)
        self.assertEqual((p["loadout"]["copies"]["rifle"], p["loadout"]["active"]["rifle"], p["loadout"]["rarity"]["rifle"]), ([1, 2], 1, 2))
        # Concurrent salvages of the same stale copy: exactly one succeeds, the other is refused.
        lo = self.store.profile(aid)["loadout"]
        lo["copies"]["pistol"] = [0, 1, 4]; lo["active"]["pistol"] = 2
        self.store._save_loadout(aid, lo)
        results = []
        def hit():
            results.append(self.call("/api/weapon/salvage", {"weapon": "pistol", "index": 0, "rarity": 0}, {"Authorization": "Bearer " + account["token"]})[0])
        threads = [threading.Thread(target=hit) for _ in range(4)]
        for t in threads: t.start()
        for t in threads: t.join()
        self.assertEqual(sorted(results), [200, 400, 400, 400])
        self.assertEqual(self.store.profile(aid)["loadout"]["copies"]["pistol"], [1, 4])

    def test_delta_update_files(self):
        import zipfile, io
        releases = self.base / "releases"
        pack = self.base / "game.zip"
        with zipfile.ZipFile(pack, "w") as z:
            z.writestr("RiftWard.exe", b"exe-v2")
            z.writestr("RiftWard_Data/level0", b"same-asset")
            z.writestr("RiftWard_Data/sub/", b"")
        digest = hashlib.sha256(pack.read_bytes()).hexdigest()
        listing = service.publish_files(pack, releases, digest)
        files = json.loads(listing.read_text())["files"]
        self.assertEqual(sorted(f["path"] for f in files), ["RiftWard.exe", "RiftWard_Data/level0"])
        for f in files:
            body = urlopen(self.url + "/updates/blobs/" + f["sha256"], timeout=5).read()
            self.assertEqual((hashlib.sha256(body).hexdigest(), len(body)), (f["sha256"], f["size"]))
        listed = json.loads(urlopen(self.url + f"/updates/files-{digest}.json", timeout=5).read())
        self.assertEqual(listed["files"], files)
        # Publishing again is a no-op; the manifest only advertises "files" for the zip actually published.
        self.assertEqual(service.publish_files(pack, releases, digest), listing)
        self.assertNotIn("files", self.call("/api/version")[1])
        (releases / f"files-{self.digest}.json").write_text(json.dumps({"version": 1, "files": []}))
        self.assertEqual(self.call("/api/version")[1]["files"], f"/updates/files-{self.digest}.json")
        self.assertEqual(self.call("/updates/blobs/" + "0" * 64)[0], 404)
        self.assertEqual(self.call("/updates/blobs/../manifest.json")[0], 404)
        bad = self.base / "bad.zip"
        with zipfile.ZipFile(bad, "w") as z:
            z.writestr("../evil.exe", b"x")
        with self.assertRaises(ValueError):
            service.publish_files(bad, self.base / "other", "1" * 64)

    def test_update_download_resume(self):
        url = self.url + "/updates/" + self.digest + ".pck"
        with urlopen(Request(url), timeout=5) as r:
            self.assertEqual((r.status, r.read(), r.headers["Accept-Ranges"]), (200, self.payload, "bytes"))
        with urlopen(Request(url, headers={"Range": "bytes=5-"}), timeout=5) as r:
            self.assertEqual((r.status, r.read(), r.headers["Content-Range"]), (206, self.payload[5:], f"bytes 5-{len(self.payload) - 1}/{len(self.payload)}"))
        with urlopen(Request(url, headers={"Range": "bytes=999-"}), timeout=5) as r:   # out of range: whole file
            self.assertEqual((r.status, r.read()), (200, self.payload))

    def test_tls_and_plain_http_share_the_port(self):
        import shutil, ssl, subprocess, http.client
        openssl = shutil.which("openssl") or "C:/Program Files/Git/usr/bin/openssl.exe"
        if not Path(openssl).is_file():
            self.skipTest("openssl not available")
        cert, key = self.base / "tls.crt", self.base / "tls.key"
        subprocess.run([openssl, "req", "-x509", "-newkey", "ec", "-pkeyopt", "ec_paramgen_curve:prime256v1", "-nodes", "-days", "1",
                        "-subj", "/CN=RiftWard", "-keyout", str(key), "-out", str(cert)], check=True, capture_output=True)
        server = service.BetaServer(("127.0.0.1", 0), self.store, self.base / "releases", service.tls_context(cert, key))
        threading.Thread(target=server.serve_forever, daemon=True).start()
        try:
            port = server.server_port
            client = ssl.create_default_context()
            client.check_hostname = False
            client.verify_mode = ssl.CERT_NONE   # clients pin the fingerprint instead of trusting a CA
            tls = http.client.HTTPSConnection("127.0.0.1", port, context=client, timeout=5)
            tls.request("GET", "/health")
            pinned = hashlib.sha256(tls.sock.getpeercert(binary_form=True)).hexdigest()
            body = json.loads(tls.getresponse().read())
            tls.close()
            self.assertEqual((body["ok"], pinned), (True, service.cert_fingerprint(cert)))
            plain = http.client.HTTPConnection("127.0.0.1", port, timeout=5)
            plain.request("GET", "/health")
            self.assertTrue(json.loads(plain.getresponse().read())["ok"])
            plain.close()
        finally:
            server.shutdown()
            server.server_close()

    def test_admin_gear_shards_and_reset(self):
        admin = self.register()
        auth = {"Authorization":"Bearer "+admin["token"]}
        self.store.admin_file.write_text("# admins\nbeta_test\n",encoding="utf-8")
        code, other = self.call("/api/register",{"username":"Target_One","password":"test-password-8921"})
        self.assertEqual(code,201)
        tid = other["profile"]["id"]
        # Gear at a chosen slot and rarity, plus shards.
        code, out = self.call("/api/admin/grant",{"username":"Target_One","shards":7,"gear":"boots","gear_rarity":4,"gear_count":5},auth)
        self.assertEqual((code,out["count"]),(200,1))
        target = self.store.profile(tid)
        self.assertEqual(target["shards"],7)
        bag = self.store._raw_gear(tid)["inventory"]
        self.assertEqual([(i["slot"],i["rarity"]) for i in bag],[("boots",4)]*5)
        self.assertIn("speed",bag[0]["stats"])
        self.call("/api/admin/grant",{"username":"Target_One","gear":"random","gear_rarity":0},auth)
        self.assertEqual(len(self.store._raw_gear(tid)["inventory"]),6)
        for bad in ({"gear":"hat"},{"gear":"boots","gear_rarity":5},{"gear":"boots","gear_count":11},{"shards":1001},{"shards":-1}):
            self.assertEqual(self.call("/api/admin/grant",{"username":"Target_One",**bad},auth)[0],400)
        # Reset: the typed confirmation must match; never yourself; the login survives, progress does not.
        self.store.complete_mission(tid,0,0)
        self.assertEqual(self.call("/api/admin/reset",{"username":"Target_One","confirm":"target_one"},auth)[1]["error"],"確認帳號不一致，未清除任何資料")
        self.assertEqual(self.call("/api/admin/reset",{"username":"Target_One"},auth)[0],400)
        self.assertEqual(self.call("/api/admin/reset",{"username":"*","confirm":"*"},auth)[0],400)
        code, done = self.call("/api/admin/reset",{"username":"Target_One","confirm":"Target_One"},auth)
        self.assertEqual((code,done["username"]),(200,"Target_One"))
        fresh = self.store.profile(tid)
        self.assertEqual((fresh["shards"],fresh["cleared"][0],fresh["unlocked"]),(0,-1,0))
        self.assertEqual(self.store._raw_gear(tid)["inventory"],[])
        self.assertEqual(self.call("/api/login",{"username":"Target_One","password":"test-password-8921"})[0],200)
        # Not an admin: no reset.
        plain = {"Authorization":"Bearer "+self.call("/api/login",{"username":"Target_One","password":"test-password-8921"})[1]["token"]}
        self.assertEqual(self.call("/api/admin/reset",{"username":"Beta_Test","confirm":"Beta_Test"},plain)[0],403)
        # An admin may wipe their own account: progress goes, admin status and the current session stay.
        self.store.reward(admin["profile"]["id"],500)
        code, done = self.call("/api/admin/reset",{"username":"Beta_Test","confirm":"Beta_Test"},auth)
        self.assertEqual(code,200)
        code, me = self.call("/api/me",{},auth)
        self.assertEqual((code,me["profile"]["gold"],me["profile"]["admin"]),(200,0,True))

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

    def test_endless_reports_and_leaderboard(self):
        account = self.register()
        auth = {"Authorization": "Bearer " + account["token"]}
        aid = account["profile"]["id"]
        self.store.min_wave_seconds = 0
        # Endless pays a milestone every 10 waves and may go past wave 20; it never counts as a victory.
        self.assertEqual(service.mission_gold(100, 35, 10, False, 1, 0, True), 100 * 3 + 35 * 12 + 9 * 15 + 3 * 250)
        stats = {"kills": 300, "team_kills": 300, "wave": 35, "level": 10, "region": 1, "difficulty": 0, "endless": True, "elapsed": 2000.0,
                 "shots": 900, "hits": 500, "headshots": 100, "boss_kills": 3}
        gold = service.mission_gold(300, 35, 10, False, 1, 0, True)
        self.store.verify_reward(aid, gold, stats)
        self.store.record_stats(aid, stats)
        with self.assertRaises(ValueError):
            self.store.verify_reward(aid, gold, dict(stats, endless=False))   # wave 35 outside endless
        with self.assertRaises(ValueError):
            self.store.verify_reward(aid, gold + 500, stats)
        p = self.store.profile(aid)
        self.assertEqual((p["stats"]["endless_best"], p["stats"]["endless_runs"], p["stats"]["best_wave"], p["stats"]["victories"]), (35, 1, 0, 0))
        code, board = self.call("/api/endless/leaderboard", {}, auth)
        self.assertEqual(code, 200)
        self.assertEqual(board["top"][0]["wave"], 35)
        self.assertEqual(board["me"]["rank"], 1)

    def test_pierce_needs_myth_shards_and_gold(self):
        account = self.register()
        auth = {"Authorization": "Bearer " + account["token"]}
        aid = account["profile"]["id"]
        lo = {"owned": ["rifle", "saber"], "equipped": ["rifle"], "rarity": {"rifle": 3, "saber": 4}, "copies": {"rifle": [3, 4], "saber": [4]},
              "active": {"rifle": 0, "saber": 0}}
        with self.store.connect() as db:
            db.execute("UPDATE progress SET loadout=?, gold=30000, shards=4 WHERE account_id=?", (json.dumps(lo), aid))
        def buy(weapon):
            return self.call("/api/shop/pierce", {"weapon": weapon}, auth)
        self.assertEqual(buy("rifle")[1]["error"], "只有神話武器可以加裝穿透")   # 傳奇 copy in use
        self.assertEqual(buy("saber")[1]["error"], "光劍無法加裝穿透")
        self.assertEqual(buy("shotgun")[0], 400)                                 # not owned
        self.assertEqual(self.call("/api/weapon/select", {"weapon": "rifle", "index": 1, "rarity": 4}, auth)[0], 200)
        self.assertEqual(buy("rifle")[1]["error"], "碎片或金幣不足")             # 4 shards
        with self.store.connect() as db:
            db.execute("UPDATE progress SET shards=6 WHERE account_id=?", (aid,))
        code, out = buy("rifle")
        self.assertEqual(code, 200)
        self.assertEqual((out["profile"]["gold"], out["profile"]["shards"]), (10000, 1))
        self.assertEqual(out["profile"]["loadout"]["levels"]["rifle"], {"pierce": 1})
        self.assertEqual(buy("rifle")[1]["error"], "已加裝穿透")
        self.assertEqual(service.weapon_invested("rifle", 4, {"pierce": 1}), (20000, 5))

    def test_per_copy_enhancement_overload_and_shards(self):
        account = self.register()
        auth = {"Authorization": "Bearer " + account["token"]}
        aid = account["profile"]["id"]
        # A legacy account: shared levels and no copy_levels; the copy in use inherits them, the others start at 0.
        legacy = {"owned": ["rifle"], "equipped": ["rifle"], "rarity": {"rifle": 0}, "copies": {"rifle": [0, 4]}, "active": {"rifle": 0},
                  "levels": {"rifle": {"damage": 5, "mag": 3}}}
        with self.store.connect() as db:
            db.execute("UPDATE progress SET loadout=?, gold=10000000 WHERE account_id=?", (json.dumps(legacy), aid))
        lo = self.store.profile(aid)["loadout"]
        self.assertEqual(lo["copy_levels"]["rifle"], [{"damage": 5, "mag": 3}, {}])
        self.assertEqual(lo["levels"]["rifle"], {"damage": 5, "mag": 3})
        # Switching to the 神話 copy does not carry the levels over: no cheap enhancement through a 劣質 copy.
        self.assertEqual(self.call("/api/weapon/select", {"weapon": "rifle", "index": 1, "rarity": 4}, auth)[0], 200)
        lo = self.store.profile(aid)["loadout"]
        self.assertNotIn("rifle", lo["levels"])
        gold = self.store.profile(aid)["gold"]
        code, up = self.call("/api/shop/upgrade", {"weapon": "rifle", "stat": "damage"}, auth)
        self.assertEqual((code, up["profile"]["gold"]), (200, gold - service.upgrade_cost("rifle", 0, 4)))
        self.assertEqual(up["profile"]["loadout"]["copy_levels"]["rifle"], [{"damage": 5, "mag": 3}, {"damage": 1}])
        # Levels past 5 get steeper; the cap is 10.
        self.assertEqual(service.upgrade_cost("pistol", 5, 1), round(120 * 6 * 1.5))
        self.assertGreater(service.upgrade_cost("pistol", 9, 1), service.upgrade_cost("pistol", 4, 1) * 7)
        # Overload needs every stat maxed, then shards + gold.
        self.assertEqual(self.call("/api/shop/overload", {"weapon": "rifle"}, auth)[0], 400)
        for stat in service.UPGRADE_STATS:
            while self.store.profile(aid)["loadout"]["levels"]["rifle"].get(stat, 0) < service.MAX_UPGRADE:
                self.assertEqual(self.call("/api/shop/upgrade", {"weapon": "rifle", "stat": stat}, auth)[0], 200)
        self.assertEqual(self.call("/api/shop/upgrade", {"weapon": "rifle", "stat": "damage"}, auth)[0], 400)
        self.assertEqual(self.call("/api/shop/overload", {"weapon": "rifle"}, auth)[0], 400)   # no shards yet
        with self.store.connect() as db:
            db.execute("UPDATE progress SET shards=7 WHERE account_id=?", (aid,))
        for tier in range(service.OVERLOAD_MAX):
            self.assertEqual(self.call("/api/shop/overload", {"weapon": "rifle"}, auth)[0], 200)
        self.assertEqual(self.call("/api/shop/overload", {"weapon": "rifle"}, auth)[0], 400)
        p = self.store.profile(aid)
        self.assertEqual((p["shards"], p["loadout"]["levels"]["rifle"]["over"]), (0, 3))
        # A 劣質 copy cannot be overloaded; salvaging the 神話 copy returns 4 shards plus half of what went into it.
        self.assertEqual(self.call("/api/weapon/select", {"weapon": "rifle", "index": 0, "rarity": 0}, auth)[0], 200)
        gold_spent, shards_spent = service.weapon_invested("rifle", 4, p["loadout"]["copy_levels"]["rifle"][1])
        gold = self.store.profile(aid)["gold"]
        code, out = self.call("/api/weapon/salvage", {"weapon": "rifle", "index": 1, "rarity": 4}, auth)
        self.assertEqual(code, 200)
        self.assertEqual(out["profile"]["shards"], 4 + shards_spent // 2)
        self.assertEqual(out["profile"]["gold"], gold + gold_spent // 2)
        self.assertEqual(out["profile"]["loadout"]["copy_levels"]["rifle"], [{"damage": 5, "mag": 3}])

    def test_gear_crafting_reforge_limit_ascend_sets(self):
        account = self.register()
        auth = {"Authorization": "Bearer " + account["token"]}
        aid = account["profile"]["id"]
        def put(items, equipped=None, gold=100000, shards=100):
            self.store._save_gear(aid, {"inventory": items, "equipped": equipped or {}})
            with self.store.connect() as db:
                db.execute("UPDATE progress SET gold=?, shards=? WHERE account_id=?", (gold, shards, aid))
        def item(i, slot="module", rarity=2, level=0, **extra):
            primary = service.GEAR_SLOTS[slot]
            stats = {primary: service.GEAR_VALUES[primary][rarity]}
            if rarity >= 2:
                stats["hp" if primary != "hp" else "crit"] = 5
            return dict({"id": i, "slot": slot, "rarity": rarity, "level": level, "stats": stats}, **extra)
        def find(profile, i):
            return next(x for x in profile["inventory"] if x["id"] == i)

        # 重鑄: 稀世+ only; costs gold + shards; the secondary stays within its range; lock keeps stat and set.
        put([item("a"), item("b", rarity=1)])
        self.assertEqual(self.call("/api/gear/reforge", {"item": "b"}, auth)[0], 400)
        code, r = self.call("/api/gear/reforge", {"item": "a"}, auth)
        self.assertEqual(code, 200)
        self.assertEqual((r["profile"]["gold"], r["profile"]["shards"]), (100000 - service.REFORGE_GOLD[2], 100 - service.REFORGE_SHARDS[2]))
        got = find(r["profile"], "a")
        self.assertIn(got["set"], service.GEAR_SETS)
        second = [k for k in got["base"] if k != "damage"]
        self.assertEqual(len(second), 1)
        full = service.GEAR_VALUES[second[0]][2]
        self.assertTrue(max(1, round(full * 0.35)) <= got["base"][second[0]] <= round(full * 0.75))
        for _ in range(5):
            code, r = self.call("/api/gear/reforge", {"item": "a", "lock": True}, auth)
            locked = find(r["profile"], "a")
            self.assertEqual((list(locked["base"])[1:], locked["set"]), (second, got["set"]))
        put([item("a")], shards=0)
        self.assertEqual(self.call("/api/gear/reforge", {"item": "a"}, auth)[1]["error"], "碎片不足")

        # 突破: needs +5; three tiers, +8% each on top of enhancement; salvage refunds half the shards.
        put([item("c", level=4)])
        self.assertEqual(self.call("/api/gear/limit", {"item": "c"}, auth)[0], 400)
        put([item("c", level=5)])
        for tier in range(service.GEAR_LIMIT_MAX):
            code, r = self.call("/api/gear/limit", {"item": "c"}, auth)
            self.assertEqual(code, 200)
        self.assertEqual(self.call("/api/gear/limit", {"item": "c"}, auth)[0], 400)
        c = find(r["profile"], "c")
        self.assertEqual(c["limit"], 3)
        self.assertEqual(c["defense"], 5 * service.GEAR_DEF_PER_LEVEL + 3 * service.GEAR_DEF_PER_LIMIT)
        put([item("c", level=5, limit=3)], {"module": "c"})
        self.assertEqual(self.store.profile(aid)["bonus"]["def"], 5 * service.GEAR_DEF_PER_LEVEL + 3 * service.GEAR_DEF_PER_LIMIT)
        put([item("c", level=5)])
        for tier in range(service.GEAR_LIMIT_MAX):
            r = self.call("/api/gear/limit", {"item": "c"}, auth)[1]
        c = find(r["profile"], "c")
        self.assertEqual(c["stats"]["damage"], round(service.GEAR_VALUES["damage"][2] * 1.6 * 1.24))
        shards = r["profile"]["shards"]
        self.assertEqual(shards, 100 - sum(service.LIMIT_SHARDS))
        code, r = self.call("/api/gear/salvage", {"item": "c"}, auth)
        self.assertEqual(r["profile"]["shards"], shards + sum(service.LIMIT_SHARDS) // 2)

        # 升階: same slot and rarity, unequipped fodder; rarity +1, primary at the new value, level kept.
        put([item("d", level=3), item("e"), item("f", slot="boots"), item("g", rarity=3)], {"module": "e"})
        self.assertEqual(self.call("/api/gear/ascend", {"item": "d", "fodder": "e"}, auth)[0], 400)   # fodder worn
        self.assertEqual(self.call("/api/gear/ascend", {"item": "d", "fodder": "f"}, auth)[0], 400)   # other slot
        self.assertEqual(self.call("/api/gear/ascend", {"item": "d", "fodder": "g"}, auth)[0], 400)   # other rarity
        self.assertEqual(self.call("/api/gear/ascend", {"item": "d", "fodder": "d"}, auth)[0], 400)
        put([item("d", level=3), item("e")])
        code, r = self.call("/api/gear/ascend", {"item": "d", "fodder": "e"}, auth)
        self.assertEqual(code, 200)
        d = find(r["profile"], "d")
        self.assertEqual((d["rarity"], d["level"], d["base"]["damage"], len(r["profile"]["inventory"])), (3, 3, service.GEAR_VALUES["damage"][3], 1))
        self.assertIn(d["set"], service.GEAR_SETS)
        # 普通 -> 稀世 gains a secondary stat and a set.
        put([item("h", rarity=1), item("i", rarity=1)])
        h = find(self.call("/api/gear/ascend", {"item": "h", "fodder": "i"}, auth)[1]["profile"], "h")
        self.assertEqual((h["rarity"], len(h["base"])), (2, 2))
        self.assertIn(h["set"], service.GEAR_SETS)

        # Sets: two pieces of 堡壘 add hp; four add more; mixed sets add nothing.
        pieces = [item(f"s{n}", slot=slot, rarity=2, set="bulwark") for n, slot in enumerate(service.GEAR_SLOTS)]
        put(pieces[:2], {pieces[0]["slot"]: "s0", pieces[1]["slot"]: "s1"})
        # armor: hp 20 + crit 5; module/boots/chip: their primary + hp 5; every piece adds its rarity's base life
        life = service.GEAR_BASE_LIFE[2]
        self.assertEqual(self.store.profile(aid)["bonus"]["hp"], 20 + 5 + 2 * life + 60)
        put(pieces[:3] + [dict(pieces[3], set="gale")], {p["slot"]: p["id"] for p in pieces})
        self.assertEqual(self.store.profile(aid)["bonus"]["hp"], 20 + 5 * 3 + 4 * life + 60)   # three of a set: still the 2-piece bonus
        self.assertNotIn("def", self.store.profile(aid)["bonus"])
        put(pieces, {p["slot"]: p["id"] for p in pieces})
        self.assertEqual(self.store.profile(aid)["bonus"]["hp"], 20 + 5 * 3 + 4 * life + 60)
        self.assertEqual(self.store.profile(aid)["bonus"]["def"], 10)   # 堡壘 four pieces: defense

    def test_limited_banner_odds_guarantee_and_admin(self):
        from unittest import mock
        account = self.register()
        auth = {"Authorization": "Bearer " + account["token"]}
        aid = account["profile"]["id"]
        with self.store.connect() as db:
            db.execute("UPDATE progress SET weapon_keys=100 WHERE account_id=?", (aid,))
        # Closed until an admin opens it; only admins can.
        self.assertEqual(self.call("/api/gacha/limited", {"count": 1}, auth)[1]["error"], "限定補給目前未開放")
        self.assertEqual(self.call("/api/admin/limited", {"days": 7}, auth)[0], 403)
        self.store.admin_file.write_text("beta_test" + chr(10), encoding="utf-8")
        for bad in (-1, 91, "7", True):
            self.assertEqual(self.call("/api/admin/limited", {"days": bad}, auth)[0], 400)
        code, r = self.call("/api/admin/limited", {"days": 7}, auth)
        self.assertEqual((code, r["banner"]["weapon"]), (200, "minigun"))
        self.assertAlmostEqual(r["banner"]["ends_at"] - service.time.time(), 7 * 86400, delta=5)
        self.assertEqual(r["profile"]["limited"]["guarantee"], False)
        # A 神話 that loses the coin flip (歪) sets the guarantee; the next 神話 is the limited weapon.
        class Fixed(service.random.SystemRandom):
            def __init__(self, flips): super().__init__(); self.flips = list(flips)
            def random(self): return self.flips.pop(0)
            def choices(self, population, weights=None, k=1):
                return [4] if population == range(len(service.RARITIES)) else [list(population)[0]]
        with mock.patch.object(service.random, "SystemRandom", lambda: Fixed([0.9])):
            code, r = self.call("/api/gacha/limited", {"count": 1}, auth)
        self.assertEqual((r["weapons"][0]["rarity"], r["weapons"][0]["limited"], r["weapons"][0]["weapon"]), (4, False, "pistol"))
        self.assertTrue(r["profile"]["limited"]["guarantee"])
        with mock.patch.object(service.random, "SystemRandom", lambda: Fixed([0.9])):   # would lose again, but guaranteed
            code, r = self.call("/api/gacha/limited", {"count": 1}, auth)
        self.assertEqual((r["weapons"][0]["weapon"], r["weapons"][0]["limited"]), ("minigun", True))
        self.assertFalse(r["profile"]["limited"]["guarantee"])
        self.assertIn("minigun", r["profile"]["loadout"]["owned"])
        with mock.patch.object(service.random, "SystemRandom", lambda: Fixed([0.1])):    # wins the flip outright
            r = self.call("/api/gacha/limited", {"count": 1}, auth)[1]
        self.assertEqual(r["weapons"][0]["weapon"], "minigun")
        self.assertEqual(r["profile"]["weapon_keys"], 97)
        # Ten at once, real odds: keys spent, pity counted on its own counter, logged as the limited pool.
        r = self.call("/api/gacha/limited", {"count": 10}, auth)[1]
        self.assertEqual((len(r["weapons"]), r["profile"]["weapon_keys"]), (10, 87))
        self.assertEqual(r["profile"]["pity"]["weapon"], service.PITY_LIMIT)
        self.assertEqual(self.store.gacha_history(aid, "limited")["totals"]["limited"], 13)
        # 80-draw pity: 79 draws without a 神話, the next one is a 神話 (here the guarantee is clear, so 50/50).
        with self.store.connect() as db:
            db.execute("UPDATE progress SET pity=? WHERE account_id=?", (json.dumps({"limited": service.LIMITED_PITY - 1}), aid))
        self.assertEqual(self.store.profile(aid)["pity"]["limited"], 1)
        r = self.call("/api/gacha/limited", {"count": 1}, auth)[1]
        self.assertEqual(r["weapons"][0]["rarity"], 4)
        self.assertEqual(r["profile"]["pity"]["limited"], service.LIMITED_PITY)
        self.assertEqual(service.roll_limited_rarity(service.random.Random(1), service.LIMITED_PITY - 1), (4, 0))
        # The regular supply never gives the limited weapon.
        self.assertNotIn("minigun", service.WEAPON_WEIGHTS)
        # Closing it.
        self.assertIsNone(self.call("/api/admin/limited", {"days": 0}, auth)[1]["banner"])
        self.assertEqual(self.call("/api/gacha/limited", {"count": 1}, auth)[0], 400)

    def test_feedback_to_admins_and_replies(self):
        account = self.register()
        auth = {"Authorization": "Bearer " + account["token"]}
        aid = account["profile"]["id"]
        for bad in ({"category": "suggest", "body": "短"}, {"category": "nope", "body": "這是一個建議內容"}, {"category": "bug", "body": "x" * 501}):
            self.assertEqual(self.call("/api/feedback/send", bad, auth)[0], 400)
        for i in range(7):   # no daily cap
            code, r = self.call("/api/feedback/send", {"category": "bug", "body": f"  第 {i} 波會卡住，請修正  "}, auth)
            self.assertEqual(code, 200)
        self.assertEqual(len(r["feedback"]), 7)
        self.assertEqual(r["feedback"][0]["body"], "第 6 波會卡住，請修正")   # trimmed, newest first
        # Another player sees only their own.
        self.store.register("Other_Player", "other-password-1")
        with self.store.connect() as db:
            other = db.execute("SELECT id FROM accounts WHERE username='Other_Player'").fetchone()[0]
        self.assertEqual(self.store.my_feedback(other)["feedback"], [])
        # Admin only.
        self.assertEqual(self.call("/api/admin/feedback", {}, auth)[0], 403)
        self.store.admin_file.write_text("beta_test" + chr(10), encoding="utf-8")
        code, r = self.call("/api/admin/feedback", {"status": "open"}, auth)
        self.assertEqual((code, r["open"], r["feedback"][0]["username"]), (200, 7, "Beta_Test"))
        fid = r["feedback"][0]["id"]
        mail_before = len(self.store._mail_rows(aid))
        r = self.call("/api/admin/feedback/resolve", {"id": fid, "reply": "已修正，謝謝回報！"}, auth)[1]
        self.assertEqual(r["open"], 6)
        mine = self.store.my_feedback(aid)["feedback"][0]
        self.assertEqual((mine["status"], mine["reply"]), ("done", "已修正，謝謝回報！"))
        mails = self.store._mail_rows(aid)
        self.assertEqual(len(mails), mail_before + 1)
        self.assertIn("已修正", mails[0]["body"])
        # Marking handled without a reply sends no mail; reopening puts it back in the open list.
        r = self.call("/api/admin/feedback/resolve", {"id": r["feedback"][1]["id"]}, auth)[1]
        self.assertEqual(len(self.store._mail_rows(aid)), mail_before + 1)
        self.assertEqual(self.call("/api/admin/feedback/resolve", {"id": fid, "reopen": True}, auth)[1]["open"], 6)
        self.assertEqual(self.call("/api/admin/feedback/resolve", {"id": 99999}, auth)[0], 400)

    def test_bulk_salvage_one_slot(self):
        account = self.register()
        auth = {"Authorization": "Bearer " + account["token"]}
        aid = account["profile"]["id"]
        items = [{"id": f"i{n}", "slot": slot, "rarity": 0, "level": 0, "stats": {service.GEAR_SLOTS[slot]: 1}}
                 for n, slot in enumerate(["armor", "armor", "boots", "chip"])]
        self.store._save_gear(aid, {"inventory": items, "equipped": {}})
        self.assertEqual(self.call("/api/salvage/bulk", {"kind": "gear", "rarities": [0], "slot": "hat"}, auth)[0], 400)
        self.assertEqual(self.call("/api/salvage/bulk", {"kind": "weapon", "rarities": [0], "slot": "armor"}, auth)[0], 400)
        code, r = self.call("/api/salvage/bulk", {"kind": "gear", "rarities": [0], "slot": "armor"}, auth)
        self.assertEqual((code, r["count"]), (200, 2))
        self.assertEqual(sorted(i["slot"] for i in r["profile"]["inventory"]), ["boots", "chip"])
        r = self.call("/api/salvage/bulk", {"kind": "gear", "rarities": [0]}, auth)[1]   # no slot: every slot, as before
        self.assertEqual((r["count"], r["profile"]["inventory"]), (2, []))

    def test_login_event_one_claim_per_day_in_order(self):
        self.store.register("Admin_Boss", "admin-password-1")
        account = self.register()
        auth = {"Authorization": "Bearer " + account["token"]}
        aid = account["profile"]["id"]
        self.assertEqual(self.call("/api/login/claim", {}, auth)[0], 400)
        with self.assertRaises(ValueError):
            self.store.set_login_event("", [{"gold": 1}], 7)
        with self.assertRaises(ValueError):
            self.store.set_login_event("週年", [{"gold": 0}], 7)
        with self.assertRaises(ValueError):
            self.store.set_login_event("週年", [{"gold": 1}] * 8, 9)
        self.store.set_login_event("週年慶", [{"gold": 100}, {"weapon_keys": 5}, {"weapon": "sniper", "rarity": 3}], 7)
        event = self.store.profile(aid)["login_event"]
        self.assertEqual((event["title"], event["claimed"], event["can_claim"]), ("週年慶", 0, True))
        gold = self.store.profile(aid)["gold"]
        code, r = self.call("/api/login/claim", {}, auth)
        self.assertEqual((code, r["profile"]["gold"] - gold, r["profile"]["login_event"]["can_claim"]), (200, 100, False))
        self.assertEqual(self.call("/api/login/claim", {}, auth)[0], 400)
        with self.store.connect() as db:   # next day
            db.execute("UPDATE login_claims SET last_day='2000-01-01'")
        keys = self.store.profile(aid)["weapon_keys"]
        self.assertEqual(self.call("/api/login/claim", {}, auth)[1]["profile"]["weapon_keys"] - keys, 5)
        with self.store.connect() as db:
            db.execute("UPDATE login_claims SET last_day='2000-01-01'")
        self.assertIn("sniper", self.call("/api/login/claim", {}, auth)[1]["profile"]["loadout"]["owned"])
        with self.store.connect() as db:
            db.execute("UPDATE login_claims SET last_day='2000-01-01'")
        self.assertIn("全部", self.call("/api/login/claim", {}, auth)[1]["error"])
        self.store.set_login_event("", None, 0)
        self.assertNotIn("login_event", self.store.profile(aid))

    def test_player_card_is_public_and_shows_relation(self):
        account = self.register()
        auth = {"Authorization": "Bearer " + account["token"]}
        self.store.register("Other_Player", "other-password-1")
        code, r = self.call("/api/player/card", {"username": "Other_Player"}, auth)
        card = r["card"]
        self.assertEqual((code, card["username"], card["relation"]), (200, "Other_Player", "none"))
        for private in ("gold", "keys", "weapon_keys", "shards", "inventory", "token", "loadout"):
            self.assertNotIn(private, card)
        self.assertEqual(card["weapons"][0]["weapon"], "rifle")
        self.assertEqual(self.call("/api/player/card", {"username": "Beta_Test"}, auth)[1]["card"]["relation"], "self")
        self.call("/api/friends/request", {"username": "Other_Player"}, auth)
        self.assertEqual(self.call("/api/player/card", {"username": "Other_Player"}, auth)[1]["card"]["relation"], "sent")
        self.assertEqual(self.call("/api/player/card", {"username": "Nobody_Here"}, auth)[0], 400)

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
        # Gear enhancement: price by rarity and level, +12% stats per level, half refunded on salvage.
        with self.store.connect() as db:
            db.execute("UPDATE progress SET gold=10000 WHERE account_id=?", (r["profile"]["id"],))
        for level in range(service.GEAR_MAX_LEVEL):
            code, up = self.call("/api/gear/upgrade", {"item": other["id"]}, auth)
            self.assertEqual(code, 200)
        upgraded = next(i for i in up["profile"]["inventory"] if i["id"] == other["id"])
        paid = sum(service.gear_upgrade_cost(other["rarity"], n) for n in range(service.GEAR_MAX_LEVEL))
        self.assertEqual((upgraded["level"], up["profile"]["gold"]), (5, 10000 - paid))
        base_life = round(service.GEAR_BASE_LIFE[upgraded["rarity"]] * 1.6)   # every piece's rarity life, also +60%
        for k, v in upgraded["base"].items():
            self.assertEqual(upgraded["stats"][k], round(v * 1.6) + (base_life if k == "hp" else 0))   # +12% per level, +60% at +5
        self.assertEqual(upgraded["stats"]["hp"], round(upgraded["base"].get("hp", 0) * 1.6) + base_life)
        self.assertEqual(self.call("/api/gear/upgrade", {"item": other["id"]}, auth)[0], 400)
        self.assertGreater(service.gear_upgrade_cost(4, 0), service.gear_upgrade_cost(0, 0))
        gold = up["profile"]["gold"]
        code, r = self.call("/api/gear/salvage", {"item": other["id"]}, auth)
        self.assertEqual(r["profile"]["gold"], gold + service.GEAR_SALVAGE_GOLD[other["rarity"]] + paid // 2)
        other = r["profile"]["inventory"][1]
        gold = r["profile"]["gold"]
        code, r = self.call("/api/gear/salvage", {"item": other["id"]}, auth)
        self.assertEqual((code, len(r["profile"]["inventory"])), (200, 8))   # 10 drawn, 2 salvaged
        self.assertGreater(r["profile"]["gold"], gold)
        # Bulk salvage: 傳奇+ needs confirm; worn gear always survives.
        self.assertEqual(self.call("/api/salvage/bulk", {"kind": "gear", "rarities": [3]}, auth)[0], 400)
        self.assertEqual(self.call("/api/salvage/bulk", {"kind": "gear", "rarities": []}, auth)[0], 400)
        gold = r["profile"]["gold"]
        code, r = self.call("/api/salvage/bulk", {"kind": "gear", "rarities": [0, 1, 2, 3, 4], "confirm": True}, auth)
        self.assertEqual((code, r["count"], [i["id"] for i in r["profile"]["inventory"]]), (200, 7, [item["id"]]))
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

    def test_first_clear_keys_per_tier(self):
        account = self.register()
        key = {"X-Server-Key":self.store.server_key}
        aid = account["profile"]["id"]
        start = (account["profile"]["keys"], account["profile"]["weapon_keys"])
        def keys(mission, tier):
            p = self.call("/api/server/progress",{"account_id":aid,"mission":mission,"difficulty":tier},key)[1]["profile"]
            return (p["keys"] - start[0], p["weapon_keys"] - start[1])
        self.assertEqual(keys(0,0),(0,0))      # 普通 pays no keys
        self.assertEqual(keys(0,1),(5,5))      # 困難 first clear
        self.assertEqual(keys(0,1),(5,5))      # only once
        self.assertEqual(keys(0,2),(15,15))    # 噩夢 +10
        self.assertEqual(keys(0,3),(30,30))    # 地獄 +15
        self.assertEqual(keys(0,3),(30,30))
        self.assertEqual(self.store.profile(aid)["shards"], 5)   # 噩夢 2 + 地獄 3
        self.assertEqual(keys(1,0),(30,30))    # each region pays its own
        self.assertEqual(keys(1,1),(35,35))

    def test_difficulty_unlocks_per_region(self):
        account = self.register()
        key = {"X-Server-Key":self.store.server_key}
        aid = account["profile"]["id"]
        self.assertEqual(account["profile"]["cleared"],[-1]*6)
        # 困難 stays locked until 普通 has been cleared in that region.
        code, response = self.call("/api/server/progress",{"account_id":aid,"mission":0,"difficulty":1},key)
        self.assertEqual((code,response["error"]),(400,"難度尚未解鎖"))
        profile = self.call("/api/server/progress",{"account_id":aid,"mission":0,"difficulty":0},key)[1]["profile"]
        self.assertEqual(profile["cleared"],[0,-1,-1,-1,-1,-1])
        profile = self.call("/api/server/progress",{"account_id":aid,"mission":0,"difficulty":1},key)[1]["profile"]
        self.assertEqual(profile["cleared"],[1,-1,-1,-1,-1,-1])
        self.assertEqual(self.call("/api/server/progress",{"account_id":aid,"mission":1,"difficulty":2},key)[0],400)
        for bad in (3,-1,True,"1"):
            self.assertEqual(self.call("/api/server/progress",{"account_id":aid,"mission":0,"difficulty":bad},key)[0],400)
        # Clearing a lower tier never lowers the record; higher tiers pay more XP.
        profile = self.call("/api/server/progress",{"account_id":aid,"mission":0,"difficulty":0},key)[1]["profile"]
        self.assertEqual(profile["cleared"][0],1)
        base = {"kills":40,"wave":20,"victory":True}
        self.assertEqual(service.mission_xp(base),240)
        self.assertEqual(service.mission_xp({**base,"difficulty":1}),300)
        self.assertEqual(service.mission_xp({**base,"difficulty":2}),360)
        self.assertEqual(service.mission_xp({**base,"difficulty":9}),240)
        # 噩夢 gold above the old 3000 cap is accepted, the new cap is not exceeded.
        self.assertEqual(self.call("/api/server/reward",{"account_id":aid,"gold":6000},key)[1]["profile"]["gold"],6000)
        self.assertEqual(self.call("/api/server/reward",{"account_id":aid,"gold":service.MAX_REWARD+1},key)[0],400)

    def test_chapter_two_opens_after_the_rift(self):
        account = self.register()
        key = {"X-Server-Key":self.store.server_key}
        aid = account["profile"]["id"]
        # An account from before chapter 2: progress capped at region 2, with 帝國裂隙 cleared on 困難.
        with self.store.connect() as db:
            db.execute("UPDATE progress SET unlocked=2, cleared=? WHERE account_id=?", (json.dumps([1, 0, 1]), aid))
        profile = self.store.profile(aid)
        self.assertEqual(profile["unlocked"],3)
        self.assertEqual(profile["cleared"],[1,0,1,-1,-1,-1])
        # Chapter 2 progresses region by region up to the throne, and no further.
        for region in (3,4,5):
            profile = self.call("/api/server/progress",{"account_id":aid,"mission":region,"difficulty":0},key)[1]["profile"]
        self.assertEqual(profile["unlocked"],5)
        self.assertEqual(profile["cleared"][5],0)
        self.assertEqual(self.call("/api/server/progress",{"account_id":aid,"mission":6,"difficulty":0},key)[0],400)

    def test_legacy_accounts_count_passed_regions_as_cleared(self):
        self.assertEqual(service.Store._parse_cleared("[]",2),[0,0,-1,-1,-1,-1])
        self.assertEqual(service.Store._parse_cleared("[2,\"x\",true,7]",0),[2,-1,-1,3,-1,-1])   # 7 is capped at 地獄
        self.assertEqual(service.Store._parse_cleared("not json",1),[0,-1,-1,-1,-1,-1])

    def test_daily_missions(self):
        account = self.register()
        auth = {"Authorization":"Bearer "+account["token"]}
        key = {"X-Server-Key":self.store.server_key}
        aid = account["profile"]["id"]
        daily = account["profile"]["daily"]
        self.assertEqual(len(daily["tasks"]),3)
        self.assertEqual(daily["day"],service.daily_day())
        self.assertTrue(0 < daily["resets_in"] <= 86400)
        ids = [t["id"] for t in daily["tasks"]]
        self.assertNotIn("hard",ids)   # never offered before 普通 has been cleared
        self.assertEqual(len(set(ids)),3)
        # The same account gets the same tasks all day.
        self.assertEqual(self.call("/api/me",{},auth)[1]["profile"]["daily"]["tasks"],daily["tasks"])
        first = ids[0]
        code, response = self.call("/api/daily/claim",{"task":first},auth)
        self.assertEqual((code,response["error"]),(400,"任務尚未完成"))
        self.assertEqual(self.call("/api/daily/claim",{"task":"bonus"},auth)[0],400)
        self.assertEqual(self.call("/api/daily/claim",{"task":"nope"},auth)[0],400)
        # One huge cleared mission completes every task type that can appear without 困難.
        record = {"kills":400,"headshots":100,"shots":3000,"hits":900,"wave":20,"victory":True}
        for _ in range(3):
            self.assertTrue(self.call("/api/server/reward",{"account_id":aid,"gold":0,"stats":record},key)[1]["ok"])
        daily = self.call("/api/me",{},auth)[1]["profile"]["daily"]
        for task in daily["tasks"]:
            self.assertTrue(task["done"])
            self.assertEqual(task["progress"],task["goal"])   # progress is capped at the goal
        before = self.call("/api/me",{},auth)[1]["profile"]
        gained = {"gold":0,"keys":0,"weapon_keys":0}
        for task in daily["tasks"]:
            code, response = self.call("/api/daily/claim",{"task":task["id"]},auth)
            self.assertEqual(code,200)
            for k, v in response["reward"].items():
                gained[k] += v
            self.assertEqual(self.call("/api/daily/claim",{"task":task["id"]},auth)[1]["error"],"此任務獎勵已領取")
        profile = response["profile"]
        for k in gained:
            self.assertEqual(profile[k],before[k]+gained[k])
        self.assertTrue(profile["daily"]["bonus"]["ready"])
        code, response = self.call("/api/daily/claim",{"task":"bonus"},auth)
        self.assertEqual(code,200)
        self.assertEqual(response["profile"]["keys"],profile["keys"]+service.DAILY_BONUS["keys"])
        self.assertTrue(response["profile"]["daily"]["bonus"]["claimed"])
        self.assertEqual(self.call("/api/daily/claim",{"task":"bonus"},auth)[0],400)
        # A bad difficulty in a combat record is rejected.
        self.assertEqual(self.call("/api/server/reward",{"account_id":aid,"gold":0,"stats":{**record,"difficulty":5}},key)[0],400)

    def test_daily_rollover_and_hard_task(self):
        self.assertEqual(service.daily_day(0),"1970-01-01")
        self.assertEqual(service.daily_day(16*3600),"1970-01-02")   # 08:00 UTC is midnight in Taiwan
        self.assertEqual(service.daily_resets_in(16*3600),86400)
        offered = set()
        for day in range(60):
            offered.update(service.roll_daily(7,f"day-{day}",True))
            self.assertNotIn("hard",service.roll_daily(7,f"day-{day}",False))
        self.assertIn("hard",offered)
        account = self.register()
        aid = account["profile"]["id"]
        # A stale day in storage is replaced by a fresh, unclaimed set.
        with self.store.connect() as db:
            db.execute("UPDATE progress SET daily=? WHERE account_id=?",
                       (json.dumps({"day":"2000-01-01","tasks":["kills","waves","deep"],"progress":{"kills":150},"claimed":["kills"],"bonus":True}),aid))
        daily = self.store.profile(aid)["daily"]
        self.assertEqual(daily["day"],service.daily_day())
        self.assertFalse(any(t["claimed"] or t["progress"] for t in daily["tasks"]))
        self.assertFalse(daily["bonus"]["claimed"])

    def test_achievements_and_titles(self):
        account = self.register()
        auth = {"Authorization":"Bearer "+account["token"]}
        key = {"X-Server-Key":self.store.server_key}
        aid = account["profile"]["id"]
        view = {a["id"]:a for a in account["profile"]["achievements"]}
        self.assertEqual(len(view),len(service.ACHIEVEMENTS))
        self.assertEqual(account["profile"]["title"],{"id":"","text":""})
        self.assertEqual(view["kills"]["text"],"累計擊殺 500 名敵人")
        self.assertEqual((view["kills"]["reached"],view["kills"]["claimed"]),(0,0))
        self.assertEqual(self.call("/api/achievement/claim",{"achievement":"kills"},auth)[1]["error"],"尚未達成下一階段")
        self.assertEqual(self.call("/api/achievement/claim",{"achievement":"nope"},auth)[0],400)
        # A flawless S-rank 困難 clear with a boss, elites and revives feeds every combat record.
        record = {"kills":4000,"headshots":1500,"shots":9000,"hits":5000,"wave":20,"victory":True,"difficulty":1,
                  "downs":0,"revives":3,"boss_kills":2,"elite_kills":6,"grade":"S"}
        for _ in range(3):
            self.assertTrue(self.call("/api/server/reward",{"account_id":aid,"gold":0,"stats":record},key)[1]["ok"])
        stats = self.call("/api/me",{},auth)[1]["profile"]["stats"]
        self.assertEqual((stats["flawless"],stats["s_grades"],stats["hard_victories"],stats["boss_kills"],stats["elite_kills"],stats["revives"]),(3,3,3,6,18,9))
        # A defeat or a downed player does not count as flawless; a record without downs (old server) never does.
        self.call("/api/server/reward",{"account_id":aid,"gold":0,"stats":{**record,"downs":1}},key)
        self.call("/api/server/reward",{"account_id":aid,"gold":0,"stats":{k:v for k,v in record.items() if k!="downs"}},key)
        self.assertEqual(self.call("/api/me",{},auth)[1]["profile"]["stats"]["flawless"],3)
        for bad in ({"boss_kills":3},{"grade":"X"},{"revives":-1},{"downs":True}):
            self.assertEqual(self.call("/api/server/reward",{"account_id":aid,"gold":0,"stats":{**record,**bad}},key)[0],400)
        # Kills: 20000 reaches all three tiers; they are claimed one by one, each paying its own reward.
        before = self.call("/api/me",{},auth)[1]["profile"]
        rewards = []
        for tier in range(3):
            code, response = self.call("/api/achievement/claim",{"achievement":"kills"},auth)
            self.assertEqual(code,200)
            self.assertEqual(response["claim"]["tier"],tier)
            rewards.append(response["claim"]["reward"])
        self.assertEqual(rewards,service.ACHIEVEMENT_REWARDS)
        self.assertEqual(response["claim"]["title"],"殲滅者")
        profile = response["profile"]
        self.assertEqual(profile["gold"],before["gold"]+900)
        self.assertEqual(profile["keys"],before["keys"]+5)
        self.assertEqual(profile["weapon_keys"],before["weapon_keys"]+2)
        self.assertEqual(profile["title"],{"id":"kills","text":"殲滅者"})   # the first title is worn automatically
        self.assertEqual(self.call("/api/achievement/claim",{"achievement":"kills"},auth)[1]["error"],"此成就已全部完成")
        # Titles: only earned ones can be worn; empty removes it.
        self.assertEqual(self.call("/api/achievement/title",{"title":"headshots"},auth)[1]["error"],"尚未獲得此稱號")
        self.assertEqual(self.call("/api/achievement/title",{"title":"elites"},auth)[0],400)   # 金 tier without a title
        for _ in range(3):
            self.call("/api/achievement/claim",{"achievement":"headshots"},auth)
        profile = self.call("/api/achievement/title",{"title":"headshots"},auth)[1]["profile"]
        self.assertEqual(profile["title"]["text"],"神射手")
        self.assertEqual(self.call("/api/achievement/title",{"title":""},auth)[1]["profile"]["title"]["id"],"")
        # Draws and daily rewards count toward their achievements.
        self.store.admin_grant("Beta_Test",0,None,keys=10)
        self.call("/api/gacha/draw",{"count":10},auth)
        view = {a["id"]:a for a in self.call("/api/me",{},auth)[1]["profile"]["achievements"]}
        self.assertEqual((view["draws"]["progress"],view["draws"]["reached"]),(10,1))
        self.assertEqual(view["flawless"]["reached"],1)
        self.assertEqual(view["bosses"]["reached"],1)

    def second_account(self, name="Second_Test"):
        code, response = self.call("/api/register",{"username":name,"password":"test-password-8921"})
        self.assertEqual(code,201)
        return response

    def test_anti_cheat_rejects_impossible_reports(self):
        self.store.min_wave_seconds = 4
        account = self.register()
        key = {"X-Server-Key":self.store.server_key}
        aid = account["profile"]["id"]
        honest = {"kills":40,"team_kills":86,"headshots":13,"shots":290,"hits":176,"wave":20,"victory":True,"difficulty":0,
                  "level":9,"region":0,"elapsed":742.0,"downs":0,"revives":0,"boss_kills":2,"elite_kills":6,"grade":"A"}
        expected = service.mission_gold(86,20,9,True,0,0)
        self.assertEqual(expected,86*3+240+120+200)
        self.assertEqual(service.mission_gold(86,20,9,True,0,2),int(round(expected*2.4)))
        self.assertTrue(self.call("/api/server/reward",{"account_id":aid,"gold":expected,"stats":honest},key)[1]["ok"])
        self.store.last_reward.clear()
        cases = [
            ({"gold":expected+50}, "金幣"),
            ({"stats":{**honest,"wave":12}}, "勝利但未完成"),
            ({"stats":{**honest,"kills":90}}, "個人擊殺"),
            ({"stats":{**honest,"elapsed":30.0}}, "作戰時間過短"),
            ({"stats":{**honest,"team_kills":5000}}, "全隊擊殺"),
        ]
        for change, reason in cases:
            body = {"account_id":aid,"gold":change.get("gold",expected),"stats":change.get("stats",honest)}
            code, response = self.call("/api/server/reward",body,key)
            self.assertEqual((code,response["error"]),(400,"結算資料異常，已記錄並暫停發放"),reason)
            self.assertIn(reason,self.store.audit_log(1)[0]["reason"])
            self.assertEqual(self.store.audit_log(1)[0]["rejected"],1)
        # Nothing was paid for the rejected reports.
        self.assertEqual(self.store.profile(aid)["gold"],expected)
        # A second payout right after the first is too soon.
        self.assertTrue(self.call("/api/server/reward",{"account_id":aid,"gold":expected,"stats":honest},key)[1]["ok"])
        self.assertEqual(self.call("/api/server/reward",{"account_id":aid,"gold":expected,"stats":honest},key)[0],400)
        self.assertIn("頻繁",self.store.audit_log(1)[0]["reason"])
        # Unusual but possible play is paid and only logged.
        self.store.last_reward.clear()
        sharp = {**honest,"shots":400,"hits":399,"headshots":380,"aim_flags":45}
        self.assertTrue(self.call("/api/server/reward",{"account_id":aid,"gold":expected,"stats":sharp},key)[1]["ok"])
        entry = self.store.audit_log(1)[0]
        self.assertEqual(entry["rejected"],0)
        for word in ("瞄準","命中率","爆頭率"):
            self.assertIn(word,entry["reason"])
        # Admins see the log and a flag count per player.
        self.store.admin_file.write_text("Beta_Test\n",encoding="utf-8")
        auth = {"Authorization":"Bearer "+account["token"]}
        entries = self.call("/api/admin/audit",{},auth)[1]["entries"]
        self.assertEqual(len(entries),7)
        self.assertEqual(entries[0]["username"],"Beta_Test")
        players = self.call("/api/admin/players",{},auth)[1]["players"]
        self.assertEqual(players[0]["flags"],7)

    def test_system_mail(self):
        account = self.register()
        auth = {"Authorization":"Bearer "+account["token"]}
        self.assertEqual(account["profile"]["mail"],{"unread":1,"claimable":1})
        mail = self.call("/api/mail/list",{},auth)[1]["mail"]
        self.assertEqual(len(mail),1)
        welcome = mail[0]
        self.assertEqual(welcome["title"],"歡迎加入裂隙守衛")
        self.assertEqual(welcome["reward"],{"keys":3,"weapon_keys":1})
        self.assertEqual(self.call("/api/mail/delete",{"id":welcome["id"]},auth)[1]["error"],"請先領取附件再刪除")
        self.assertEqual(self.call("/api/mail/read",{"id":welcome["id"]},auth)[1]["profile"]["mail"]["unread"],0)
        code, response = self.call("/api/mail/claim",{"id":welcome["id"]},auth)
        self.assertEqual((code,response["reward"]),(200,{"keys":3,"weapon_keys":1}))
        self.assertEqual((response["profile"]["keys"],response["profile"]["weapon_keys"]),(3,1))
        self.assertEqual(self.call("/api/mail/claim",{"id":welcome["id"]},auth)[0],400)
        # Admin mail: validation, one player, everyone; claim all collects every attachment at once.
        self.store.admin_file.write_text("Beta_Test\n",encoding="utf-8")
        other = self.second_account()
        for bad in ({"title":""},{"title":"x"*41},{"gold":-1},{"days":0},{"username":"nobody_here"}):
            self.assertEqual(self.call("/api/admin/mail",{"username":"*","title":"維護補償","body":"感謝耐心等候",**bad},auth)[0],400)
        self.assertEqual(self.call("/api/admin/mail",{"username":"*","title":"維護補償","body":"感謝耐心等候","gold":500,"keys":2},auth)[1]["count"],2)
        self.assertEqual(self.call("/api/admin/mail",{"username":"Second_Test","title":"活動獎勵","gold":100},auth)[1]["count"],1)
        self.assertEqual(self.call("/api/admin/mail",{"username":"*","title":"公告","body":"下週更新"},{"Authorization":"Bearer "+other["token"]})[0],403)
        oauth = {"Authorization":"Bearer "+other["token"]}
        listing = self.call("/api/mail/list",{},oauth)[1]
        self.assertEqual(len(listing["mail"]),3)
        self.assertEqual(listing["profile"]["mail"]["claimable"],3)
        code, response = self.call("/api/mail/claim_all",{},oauth)
        self.assertEqual((response["count"],response["reward"]),(3,{"gold":600,"keys":5,"weapon_keys":1}))
        self.assertEqual(response["profile"]["gold"],600)
        self.assertEqual(self.call("/api/mail/claim_all",{},oauth)[1]["error"],"沒有可領取的附件")
        # A text-only mail can be deleted right away; others' mail is out of reach.
        self.call("/api/admin/mail",{"username":"Second_Test","title":"公告","body":"下週更新"},auth)
        notice = self.call("/api/mail/list",{},oauth)[1]["mail"][0]
        self.assertTrue(notice["claimed"])
        self.assertEqual(self.call("/api/mail/delete",{"id":notice["id"]},auth)[0],400)
        self.assertEqual(len(self.call("/api/mail/delete",{"id":notice["id"]},oauth)[1]["mail"]),3)
        # Expired mail disappears.
        with self.store.connect() as db:
            db.execute("UPDATE mail SET expires=1")
        self.assertEqual(self.call("/api/mail/list",{},oauth)[1]["mail"],[])

    def test_friends(self):
        me = self.register()
        other = self.second_account()
        auth = {"Authorization":"Bearer "+me["token"]}
        oauth = {"Authorization":"Bearer "+other["token"]}
        key = {"X-Server-Key":self.store.server_key}
        self.assertEqual(self.call("/api/friends/request",{"username":"Beta_Test"},auth)[1]["error"],"不能加自己為好友")
        self.assertEqual(self.call("/api/friends/request",{"username":"nobody_here"},auth)[1]["error"],"找不到此玩家")
        code, response = self.call("/api/friends/request",{"username":"second_test"},auth)   # names are case-insensitive
        self.assertEqual((code,response["status"]),(200,"pending"))
        self.assertEqual([f["username"] for f in response["outgoing"]],["Second_Test"])
        self.assertEqual(self.call("/api/friends/request",{"username":"Second_Test"},auth)[1]["error"],"已送出邀請，等待對方回應")
        listing = self.call("/api/friends/list",{},oauth)[1]
        self.assertEqual(listing["profile"]["friend_requests"],1)
        self.assertEqual([f["username"] for f in listing["incoming"]],["Beta_Test"])
        response = self.call("/api/friends/respond",{"username":"Beta_Test","accept":True},oauth)[1]
        self.assertEqual([f["username"] for f in response["friends"]],["Beta_Test"])
        self.assertEqual(response["profile"]["friend_requests"],0)
        self.assertEqual(self.call("/api/friends/request",{"username":"Second_Test"},auth)[1]["error"],"Second_Test 已經是你的好友")
        # The game server reads friend ids for presence and invites.
        ids = self.call("/api/server/friends",{"account_id":me["profile"]["id"]},key)[1]["friends"]
        self.assertEqual(ids,[other["profile"]["id"]])
        self.assertEqual(self.call("/api/server/friends",{"account_id":me["profile"]["id"]})[0],403)
        # Removing works from either side; asking someone who already asked you accepts at once.
        self.assertEqual(self.call("/api/friends/remove",{"username":"Beta_Test"},oauth)[1]["friends"],[])
        self.assertEqual(self.call("/api/friends/remove",{"username":"Beta_Test"},oauth)[0],400)
        self.call("/api/friends/request",{"username":"Beta_Test"},oauth)
        self.assertEqual(self.call("/api/friends/request",{"username":"Second_Test"},auth)[1]["status"],"accepted")
        self.assertEqual(len(self.call("/api/friends/list",{},auth)[1]["friends"]),1)
        # Declining removes the request.
        third = self.second_account("Third_Test")
        self.call("/api/friends/request",{"username":"Beta_Test"},{"Authorization":"Bearer "+third["token"]})
        self.assertEqual(self.call("/api/friends/respond",{"username":"Third_Test","accept":False},auth)[1]["incoming"],[])
        self.assertEqual(self.call("/api/friends/respond",{"username":"Third_Test","accept":True},auth)[0],400)

    def test_supply_odds(self):
        self.assertEqual(sum(service.RARITY_WEIGHTS),100)
        self.assertEqual(service.RARITY_WEIGHTS[3:],[2.5,0.5])
        # Over many draws without pity the rates land on the published odds.
        rng = random.Random(1234)
        counts = [0]*5
        n = 200000
        for _ in range(n):
            counts[service.roll_rarity(rng, 0)[0]] += 1
        for rarity, percent in enumerate(service.RARITY_WEIGHTS):
            self.assertAlmostEqual(100*counts[rarity]/n, percent, delta=0.25)
        # Pity still forces 傳奇 or 神話 on the 50th draw, at the same 5:1 ratio.
        forced = [service.roll_rarity(rng, service.PITY_LIMIT-1)[0] for _ in range(6000)]
        self.assertTrue(all(r >= 3 for r in forced))
        self.assertAlmostEqual(forced.count(4)/len(forced), 1/6, delta=0.02)

    def test_saber_is_a_rare_weapon_draw(self):
        weights = service.WEAPON_WEIGHTS
        self.assertAlmostEqual(weights["saber"]/sum(weights.values()), 0.048, delta=0.001)
        self.assertLess(weights["saber"], min(v for k, v in weights.items() if k != "saber"))
        # Drawn, it joins the armory like any gun and can be upgraded and equipped.
        account = self.register()
        auth = {"Authorization":"Bearer "+account["token"]}
        aid = account["profile"]["id"]
        self.store.admin_grant("Beta_Test",5000,"saber")
        profile = self.call("/api/me",{},auth)[1]["profile"]
        self.assertIn("saber",profile["loadout"]["owned"])
        self.assertIn("saber",profile["loadout"]["equipped"])
        response = self.call("/api/shop/upgrade",{"weapon":"saber","stat":"mag"},auth)
        self.assertEqual(response[1]["profile"]["loadout"]["levels"]["saber"]["mag"],1)
        self.assertEqual(response[1]["profile"]["gold"],5000-service.upgrade_cost("saber",0,1))

    def test_admin_grants_weapons_at_a_rarity(self):
        account = self.register()
        auth = {"Authorization":"Bearer "+account["token"]}
        self.store.admin_file.write_text("Beta_Test\n",encoding="utf-8")
        grant = lambda **kw: self.call("/api/admin/grant",{"username":"Beta_Test","gold":0,**kw},auth)
        # A new weapon arrives at the chosen rarity.
        profile = grant(weapon="saber",rarity=4)[1]["profile"]
        self.assertEqual(profile["loadout"]["copies"]["saber"],[4])
        self.assertEqual(profile["loadout"]["rarity"]["saber"],4)
        # Granting one the player already owns adds a copy; the active copy stays as it was.
        profile = grant(weapon="saber",rarity=0)[1]["profile"]
        self.assertEqual(profile["loadout"]["copies"]["saber"],[4,0])
        self.assertEqual(profile["loadout"]["rarity"]["saber"],4)
        # Without a rarity the old default (普通) applies.
        self.assertEqual(grant(weapon="sniper")[1]["profile"]["loadout"]["copies"]["sniper"],[1])
        for bad in (5,-1,True,"4"):
            self.assertEqual(grant(weapon="saber",rarity=bad)[1]["error"],"稀有度不正確")

    def test_friend_list_shows_level_and_earned_title(self):
        me = self.register()
        other = self.second_account()
        oid = other["profile"]["id"]
        self.store.request_friend(oid,"Beta_Test")
        self.store.respond_friend(me["profile"]["id"],"Second_Test",True)
        with self.store.connect() as db:
            db.execute("UPDATE progress SET xp=?, achievements=? WHERE account_id=?",
                       (1000, json.dumps({"claimed":{"kills":3,"headshots":1},"title":"kills"}), oid))
        friend = self.store.friends(me["profile"]["id"])["friends"][0]
        self.assertEqual((friend["username"],friend["level"],friend["title"]),("Second_Test",service.level_for(1000)[0],"殲滅者"))
        # A worn title whose 金 tier is not claimed is not shown.
        with self.store.connect() as db:
            db.execute("UPDATE progress SET achievements=? WHERE account_id=?", (json.dumps({"claimed":{"headshots":1},"title":"headshots"}), oid))
        self.assertEqual(self.store.friends(me["profile"]["id"])["friends"][0]["title"],"")
        # The full profile agrees with the brief.
        self.assertEqual(self.store.profile(oid)["title"]["text"],"")

    def test_gacha_history_is_per_account(self):
        me = self.register()
        other = self.second_account()
        mid, oid = me["profile"]["id"], other["profile"]["id"]
        with self.store.connect() as db:
            db.execute("UPDATE progress SET keys=20, weapon_keys=20 WHERE account_id IN (?,?)", (mid, oid))
        items, _ = self.store.draw(mid, 10)
        weapons, _ = self.store.draw_weapons(mid, 1)
        self.store.draw_weapons(oid, 10)
        mine = self.store.gacha_history(mid)
        self.assertEqual(mine["totals"], {"gear": 10, "weapon": 1, "limited": 0})
        self.assertEqual(len(mine["history"]), 11)
        # Newest first, matching what was drawn.
        self.assertEqual((mine["history"][0]["pool"], mine["history"][0]["item"], mine["history"][0]["rarity"]), ("weapon", weapons[0]["weapon"], weapons[0]["rarity"]))
        self.assertEqual(sorted(h["rarity"] for h in mine["history"] if h["pool"] == "gear"), sorted(i["rarity"] for i in items))
        self.assertEqual(len(self.store.gacha_history(mid, "gear")["history"]), 10)
        self.assertEqual(self.store.gacha_history(oid)["totals"], {"gear": 0, "weapon": 10, "limited": 0})
        # Over HTTP the account comes from the session: asking for someone else's id still returns your own.
        code, body = self.call("/api/gacha/history", {"account_id": oid, "pool": "weapon"}, {"Authorization": "Bearer " + me["token"]})
        self.assertEqual(code, 200)
        self.assertEqual(len(body["history"]), 1)
        with self.assertRaises(ValueError):
            self.store.gacha_history(mid, "nope")

    def test_last_seen_in_friend_list(self):
        me = self.register()
        other = self.second_account()
        oid = other["profile"]["id"]
        self.store.request_friend(oid, "Beta_Test")
        self.store.respond_friend(me["profile"]["id"], "Second_Test", True)
        self.assertEqual(self.store.friends(me["profile"]["id"])["friends"][0]["last_seen"], 0)
        self.store.seen(oid)
        self.assertGreater(self.store.friends(me["profile"]["id"])["friends"][0]["last_seen"], 0)

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
