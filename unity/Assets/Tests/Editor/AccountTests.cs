using System.Collections.Generic;
using NUnit.Framework;
using RiftWard.Client;

namespace RiftWard.Tests
{
    public class AccountTests
    {
        const string ProfileJson = "{\"ok\": true, \"profile\": {\"id\": 7, \"username\": \"DVRBXTW\", \"unlocked\": 1, \"tutorial_complete\": true, \"gold\": 70734, \"admin\": true, " +
            "\"loadout\": {\"owned\": [\"pistol\", \"rifle\", \"sniper\"], \"equipped\": [\"pistol\", \"sniper\"], \"levels\": {\"sniper\": {\"damage\": 5, \"mag\": 2}}}}, \"game_online\": false, \"note\": \"\u5e33\u865f \\\"ok\\\"\"}";

        [Test]
        public void ParsesServiceProfile()
        {
            var root = (Dictionary<string, object>)Json.Parse(ProfileJson);
            Assert.IsTrue(root.Bool("ok"));
            Assert.AreEqual("帳號 \"ok\"", root.Str("note"));
            var p = Profile.From(root.Obj("profile"));
            Assert.AreEqual(7, p.Id);
            Assert.AreEqual(70734, p.Gold);
            Assert.IsTrue(p.Admin && p.TutorialComplete);
            CollectionAssert.AreEqual(new[] { "pistol", "sniper" }, p.Loadout.Equipped);
            Assert.AreEqual(5, p.Loadout.Level("sniper", "damage"));
            Assert.AreEqual(0, p.Loadout.Level("rifle", "damage"));
        }

        [Test]
        public void WritesJsonTheServiceAccepts()
        {
            string body = Json.Write(new Dictionary<string, object> { ["weapons"] = new List<object> { "pistol", "sniper" }, ["gold"] = 500, ["name"] = "a\"b" });
            Assert.AreEqual("{\"weapons\":[\"pistol\",\"sniper\"],\"gold\":500,\"name\":\"a\\\"b\"}", body);
            var back = (Dictionary<string, object>)Json.Parse(body);
            Assert.AreEqual(500, back.Int("gold"));
        }
    }
}
