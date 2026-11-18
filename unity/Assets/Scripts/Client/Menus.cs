using System.Linq;
using RiftWard.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace RiftWard.Client
{
    /// In-mission overlays: level-up buff cards, tactical pause menu and the result sheet.
    public sealed class Menus
    {
        readonly GameClient client;
        readonly Canvas canvas;
        GameObject buffs, pause, result;
        Text resultNotes;
        public bool ResultOpen => result != null;

        /// Rich-text lines under the result's XP bar (daily missions, nearest achievement).
        public void SetResultNotes(string text)
        {
            if (result != null && resultNotes != null) resultNotes.text = text;
        }
        int[] shownOffer;
        public System.Action LeaveRequested;
        public System.Action RetryRequested;
        /// Set for admins: builds the ability controls into the given parent.
        public System.Action<Transform> AdminPanel;
        public System.Action<Transform> SettingsPanel;

        public Menus(GameClient client)
        {
            this.client = client;
            canvas = Ui.Canvas("Menus", 20);
        }

        static RectTransform Scrim(Transform parent, float alpha)
        {
            var bg = Ui.Box(parent, new Color(0.03f, 0.031f, 0.034f, alpha), "Scrim");
            bg.rectTransform.Fill();
            return bg.rectTransform;
        }

        static RectTransform Sheet(Transform parent, Color accent, Vector2 size)
        {
            var sheet = Ui.Box(parent, new Color(0.07f, 0.072f, 0.078f, 0.97f), "Sheet").rectTransform;
            sheet.Place(new Vector2(0.5f, 0.5f), Vector2.zero, size);
            Ui.Box(sheet, accent, "Rule").rectTransform.Place(new Vector2(0.5f, 1), Vector2.zero, new Vector2(size.x, 2), new Vector2(0.5f, 1));
            return sheet;
        }

        // ------------------------------------------------------------ buffs

        public void ShowBuffs(int[] offer, int level, int superPick = 0)
        {
            if (buffs != null && shownOffer == offer) return;
            HideBuffs();
            shownOffer = offer;
            GameClient.LockCursor(false);
            var root = Scrim(canvas.transform, 0.86f);
            buffs = root.gameObject;
            bool chest = superPick > 0;
            Ui.Label(root, chest ? "SUPER CHEST" : "LEVEL UP", 13, chest ? Palette.Hex("ff3b5c") : Palette.Amber, TextAnchor.MiddleCenter).rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, 250), new Vector2(600, 20));
            Ui.Label(root, chest ? $"超級寶箱  {superPick} / {Defs.SuperChestPicks}" : $"等級 {level}", 44, Palette.Bone, TextAnchor.MiddleCenter, true).rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, 205), new Vector2(600, 56));
            Ui.Label(root, "選擇一項加成 · 可疊加 · 敵人也會隨等級變強 · 30 秒後自動選擇", 14, Palette.Muted, TextAnchor.MiddleCenter)
                .rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, 160), new Vector2(900, 22));
            var row = Ui.HStack(root, 20);
            row.Place(new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(810, 280));
            Button[] buttons = new Button[offer.Length];
            for (int i = 0; i < offer.Length; i++)
            {
                int code = offer[i];
                int tier = Defs.BuffTier(code);
                Color rarity = Defs.RarityColors[tier];
                var card = Ui.Button(row, "", () =>
                {
                    foreach (var b in buttons) b.interactable = false;
                    client.ChooseBuff(code);
                }, 18, Palette.Bone, new Color(rarity.r * 0.12f, rarity.g * 0.12f, rarity.b * 0.14f, 0.96f), rarity);
                buttons[i] = card;
                card.GetComponentInChildren<Text>().text = "";
                var stack = Ui.VStack(card.transform, 14, TextAnchor.MiddleCenter);
                stack.Fill();
                stack.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(18, 18, 30, 30);
                Ui.Label(stack, Defs.RarityNames[tier], 13, rarity, TextAnchor.MiddleCenter).Sized(20);
                Ui.Label(stack, Defs.Buffs[Defs.BuffIndex(code)].Name, 26, Palette.Bone, TextAnchor.MiddleCenter, true).Sized(36);
                Ui.Label(stack, Defs.BuffDescription(code), 17, Palette.Hex("d6d1c6"), TextAnchor.MiddleCenter).Sized(60);
                int limit = Defs.BuffStackLimit(Defs.BuffIndex(code), client.Sim != null && client.Sim.Endless);
                if (limit < 99) Ui.Label(stack, $"最多 {limit} 層", 12, Palette.Muted, TextAnchor.MiddleCenter).Sized(18);
                else if (limit == int.MaxValue) Ui.Label(stack, "無盡模式 · 無上限", 12, Palette.Hex("ff6a8a"), TextAnchor.MiddleCenter).Sized(18);
            }
            Ui.Label(root, "選擇後等待隊友；所有人選完就繼續作戰。", 14, Palette.Muted, TextAnchor.MiddleCenter)
                .rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, -180), new Vector2(700, 22));
        }

        public void HideBuffs()
        {
            if (buffs == null) return;
            Object.Destroy(buffs);
            buffs = null;
            shownOffer = null;
            if (client.Sim != null && client.Sim.State == MissionState.Playing && !client.Paused) GameClient.LockCursor(true);
        }

        // ------------------------------------------------------------ pause

        public void OpenPause()
        {
            if (pause != null) return;
            client.SetPaused(true);
            bool frozen = client.CanPause();
            GameClient.LockCursor(false);
            var root = Scrim(canvas.transform, 0.8f);
            pause = root.gameObject;
            var sheet = Sheet(root, Palette.Amber, new Vector2(460, 300));
            var stack = Ui.VStack(sheet, 12);
            stack.Fill();
            stack.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(28, 28, 28, 28);
            Ui.Label(stack, frozen ? "PAUSED · 遊戲已暫停" : "MENU · 隊友仍在戰鬥", 13, frozen ? Palette.Amber : Palette.Danger).Sized(18);
            Ui.Label(stack, "戰術選單", 32, Palette.Bone, TextAnchor.UpperLeft, true).Sized(42);
            Ui.Label(stack, frozen ? "戰鬥已凍結，敵人不會移動或攻擊。退出後回到基地主頁。" : "多人房間不會暫停，敵人仍會攻擊你；退出後隊友可繼續任務。", 15, Palette.Muted).Sized(40);
            Ui.Button(stack, "繼續戰鬥", ClosePause, 18).Sized(52);
            Ui.Button(stack, "退出到主頁", () => { ClosePause(false); LeaveRequested?.Invoke(); }, 18, Palette.Danger).Sized(48);
            if (SettingsPanel != null)
            {
                sheet.sizeDelta += new Vector2(0, 56);
                Ui.Button(stack, "設定", () =>
                {
                    var overlay = Scrim(pause.transform, 0.6f);
                    var panel = Sheet(overlay, Palette.Amber, new Vector2(620, 600));
                    var ps = Ui.VStack(panel, 6);
                    ps.Fill();
                    ps.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(28, 28, 22, 22);
                    Ui.Label(ps, "設定", 26, Palette.Bone, TextAnchor.UpperLeft, true).Sized(36);
                    SettingsPanel(ps);
                    Ui.Button(ps, "關閉", () => Object.Destroy(overlay.gameObject), 16).Sized(44);
                }, 16).Sized(44);
            }
            if (AdminPanel != null)
            {
                sheet.sizeDelta += new Vector2(0, 60);
                Ui.Button(stack, "管理員能力", () =>
                {
                    var overlay = Scrim(pause.transform, 0.6f);
                    var panel = Sheet(overlay, Palette.Danger, new Vector2(520, 600));
                    var ps = Ui.VStack(panel, 10);
                    ps.Fill();
                    ps.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(26, 26, 22, 22);
                    AdminPanel(ps);
                    Ui.Button(ps, "關閉", () => Object.Destroy(overlay.gameObject), 16).Sized(44);
                }, 16, Palette.Danger).Sized(44);
            }
        }

        public void ClosePause() => ClosePause(true);

        void ClosePause(bool resume)
        {
            if (pause != null) Object.Destroy(pause);
            pause = null;
            client.SetPaused(false);
            if (resume && client.Sim != null) GameClient.LockCursor(true);
        }

        // ------------------------------------------------------------ result

        /// Account progress shown on the result sheet (online missions only).
        public sealed class ResultProgress
        {
            public int Xp, Level, LevelXp, Wave, BestWave;
            /// Difficulty tier this clear opened for the region, or -1.
            public int Unlocked = -1;
        }

        public void ShowResult(bool success, int region, int kills, float elapsed, int gold, int totalGold, string grade = "", int[] record = null, ResultProgress progress = null, int difficulty = 0)
        {
            HideAll();
            GameClient.LockCursor(false);
            var root = Scrim(canvas.transform, 0.9f);
            result = root.gameObject;
            bool opened = progress != null && progress.Unlocked > 0;
            var sheet = Sheet(root, success ? Palette.Amber : Palette.Danger, new Vector2(600, (progress != null ? 500 : 480) + (opened ? 34 : 0)));
            if (!string.IsNullOrEmpty(grade))
            {
                var g = Ui.Label(sheet, grade, 96, grade == "S" ? Palette.Amber : Palette.Bone, TextAnchor.UpperRight, true);
                g.rectTransform.Place(new Vector2(1, 1), new Vector2(-36, -24), new Vector2(160, 110), new Vector2(1, 1));
                Ui.Label(sheet, "任務評分", 13, Palette.Muted, TextAnchor.UpperRight).rectTransform.Place(new Vector2(1, 1), new Vector2(-38, -132), new Vector2(160, 18), new Vector2(1, 1));
            }
            var stack = Ui.VStack(sheet, 14);
            stack.Fill();
            stack.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(36, 36, 32, 32);
            Ui.Label(stack, success ? "MISSION COMPLETE" : "SIGNAL LOST", 13, success ? Palette.Amber : Palette.Danger).Sized(18);
            Ui.Label(stack, success ? "任務完成" : "任務失敗", 46, Palette.Bone, TextAnchor.UpperLeft, true).Sized(56);
            var tier = Defs.Difficulty(difficulty);
            var place = Ui.HStack(stack, 12).Sized(30);
            place.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            Ui.Label(place, Defs.Regions[region].Name, 22, Palette.Bone).Sized(-1, Defs.Regions[region].Name.Length * 23 + 4);
            var badge = Ui.Box(place, new Color(tier.Accent.r / 255f, tier.Accent.g / 255f, tier.Accent.b / 255f, 0.16f), "Tier").Sized(-1, 64);
            Ui.Label(badge.transform, tier.Name, 15, tier.Accent, TextAnchor.MiddleCenter, true).rectTransform.Fill();
            Ui.Rect(place, "Rest").gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            if (opened)
            {
                var next = Defs.Difficulty(progress.Unlocked);
                Ui.Label(stack, $"已解鎖  {next.Name}難度  ·  {Defs.Regions[region].Name}", 17, next.Accent, TextAnchor.UpperLeft, true).Sized(20);
            }
            Ui.Label(stack, $"全隊擊破  {kills}      作戰時間  {(int)elapsed / 60:00}:{(int)elapsed % 60:00}", 18, Palette.Hex("d6d1c6")).Sized(26);
            if (record != null)
            {
                float accuracy = record[2] > 0 ? 100f * record[3] / record[2] : 0f;
                Ui.Label(stack, $"個人擊殺  {record[0]}      爆頭  {record[1]}      命中率  {accuracy:0}%", 16, Palette.Muted).Sized(24);
            }
            Ui.Label(stack, gold >= 0 ? $"獲得金幣  +{gold} G      持有  {totalGold} G" : "離線任務不發放金幣；連線至基地後出擊才會獲得金幣。", gold >= 0 ? 20 : 15, gold >= 0 ? Palette.Amber : Palette.Muted).Sized(28);
            if (progress != null) ProgressBlock(stack, progress);
            if (progress != null)
            {
                // Daily missions and the nearest achievement; filled in once the updated profile arrives.
                resultNotes = Ui.Label(stack, "<color=#8c8f93>正在同步任務進度…</color>", 14, Palette.Bone);
                resultNotes.supportRichText = true;
                resultNotes.Sized(66);
                sheet.sizeDelta += new Vector2(0, 80);
            }
            var row = Ui.HStack(stack, 10).Sized(52);
            Ui.Button(row, "返回主頁", () => { HideAll(); LeaveRequested?.Invoke(); }, 18);
            Ui.Button(row, "再次挑戰", () => { HideAll(); RetryRequested?.Invoke(); }, 18, Palette.Amber, null, Palette.Amber);
        }

        /// XP gained, a bar that fills from the old to the new amount (level-ups called out) and a record badge.
        void ProgressBlock(Transform stack, ResultProgress p)
        {
            int level = p.Level, into = p.LevelXp + p.Xp, ups = 0;
            while (into >= Defs.AccountLevelNeed(level) && level < 99) { into -= Defs.AccountLevelNeed(level); level++; ups++; }
            var head = Ui.Rect(stack, "XpHead").Sized(24);
            Ui.Label(head, $"獲得經驗  +{p.Xp} XP", 18, Palette.Bone, TextAnchor.MiddleLeft, true).rectTransform.Fill();
            Ui.Label(head, ups > 0 ? $"升級！ Lv {p.Level} → Lv {level}   天賦點 +{ups}" : $"Lv {level}   {into} / {Defs.AccountLevelNeed(level)}", 15,
                ups > 0 ? Palette.Amber : Palette.Muted, TextAnchor.MiddleRight, ups > 0).rectTransform.Fill();
            var track = Ui.Box(stack, new Color(1, 1, 1, 0.08f), "XpTrack").Sized(8);
            float before = ups > 0 ? 0f : (float)p.LevelXp / Defs.AccountLevelNeed(level);
            float after = (float)into / Defs.AccountLevelNeed(level);
            var old = Ui.Box(track.transform, Palette.Muted, "Before").rectTransform;
            old.anchorMin = Vector2.zero; old.anchorMax = new Vector2(before, 1); old.offsetMin = old.offsetMax = Vector2.zero;
            var gain = Ui.Box(track.transform, Palette.Amber, "Gain").rectTransform;
            gain.anchorMin = new Vector2(before, 0); gain.anchorMax = new Vector2(Mathf.Max(before, after), 1); gain.offsetMin = gain.offsetMax = Vector2.zero;
            bool record = p.Wave > p.BestWave;
            Ui.Label(stack, record ? $"新紀錄！最高波次 {p.BestWave} → {p.Wave}" : $"本次到達第 {p.Wave} 波    最高紀錄 第 {p.BestWave} 波", 14,
                record ? Palette.Hex("ffcf4a") : Palette.Muted, TextAnchor.UpperLeft, record).Sized(20);
        }

        public void HideAll()
        {
            HideBuffs();
            if (pause != null) Object.Destroy(pause);
            if (result != null) Object.Destroy(result);
            pause = result = null;
            client.SetPaused(false);
        }
    }
}
