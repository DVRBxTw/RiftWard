using System;
using System.Collections.Generic;
using System.Linq;
using RiftWard.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace RiftWard.Client
{
    /// Out-of-mission screens: account, story, home base, armory and the admin console.
    public sealed class Screens
    {
        readonly AppRoot app;
        readonly Canvas canvas;
        RectTransform root;
        public string Mode { get; private set; } = "";

        public Screens(AppRoot app)
        {
            this.app = app;
            canvas = Ui.Canvas("Screens", 5);
        }

        RectTransform Begin(string mode, bool backdrop = true)
        {
            Clear();
            Mode = mode;
            root = Ui.Rect(canvas.transform, mode).Fill();
            if (backdrop) Ui.Box(root, new Color(0.063f, 0.067f, 0.075f, 0.94f), "Backdrop").rectTransform.Fill();
            GameClient.LockCursor(false);
            return root;
        }

        public void Clear()
        {
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            root = null;
            Mode = "";
        }

        static void TopRule(Transform parent, Color color)
        {
            var rule = Ui.Box(parent, color, "Rule").rectTransform;
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1); rule.pivot = new Vector2(0.5f, 1);
            rule.offsetMin = new Vector2(0, -2); rule.offsetMax = Vector2.zero;
        }

        static Text Caps(Transform parent, string text, Color? color = null) => Ui.Label(parent, text, 13, color ?? Palette.Muted);

        static Button PrimaryButton(Transform parent, string text, Action onClick, int size = 20)
        {
            var b = Ui.Button(parent, text, onClick, size, Palette.Ink, Palette.Amber, Palette.Amber);
            return b;
        }

        // ------------------------------------------------------------ account

        public void ShowAccount(string notice = "", bool login = false)
        {
            var r = Begin("account");
            bool registering = !login;
            var sheet = Ui.Box(r, Palette.Sheet, "Sheet").rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 600));
            Ui.Box(sheet, Palette.Amber, "Rule").rectTransform.Place(new Vector2(0.5f, 1), Vector2.zero, new Vector2(520, 2), new Vector2(0.5f, 1));
            var stack = Ui.VStack(sheet, 10);
            stack.Fill();
            stack.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(40, 40, 36, 30);
            Caps(stack, "RIFTWARD  " + Defs.Version, Palette.Amber).Sized(18);
            var heading = Ui.Label(stack, "", 34, Palette.Bone, TextAnchor.UpperLeft, true).Sized(46);
            Ui.Label(stack, "登入後即可進入基地；進度、金幣與武器都存在你的帳號中。", 15, Palette.Muted).Sized(44);
            Caps(stack, "使用者名稱").Sized(18);
            var user = Widgets.Input(stack, "3–24 個英文字母、數字或底線", false, 24).Sized(48);
            Caps(stack, "密碼").Sized(18);
            var pass = Widgets.Input(stack, "8–72 個字元", true, 72).Sized(48);
            var confirmLabel = Caps(stack, "確認密碼").Sized(18);
            var confirm = Widgets.Input(stack, "再次輸入密碼", true, 72).Sized(48);
            var status = Ui.Label(stack, notice, 14, Palette.Danger).Sized(36);
            Button submit = null, toggle = null;
            void Paint()
            {
                heading.text = registering ? "建立遊戲帳號" : "登入遊戲帳號";
                confirm.gameObject.SetActive(registering);
                confirmLabel.gameObject.SetActive(registering);
                submit.SetCaption(registering ? "註冊並進入遊戲" : "登入並進入遊戲");
                toggle.SetCaption(registering ? "已有帳號？登入" : "還沒有帳號？註冊");
            }
            submit = PrimaryButton(stack, "", () =>
            {
                string name = user.text.Trim();
                if (name.Length < 3 || name.Length > 24 || !name.All(c => char.IsLetterOrDigit(c) && c < 128 || c == '_'))
                { status.text = "使用者名稱須為 3–24 個英文字母、數字或底線。"; return; }
                if (pass.text.Length < 8 || pass.text.Length > 72) { status.text = "密碼長度須為 8–72 個字元。"; return; }
                if (registering && pass.text != confirm.text) { status.text = "兩次輸入的密碼不一致。"; return; }
                status.color = Palette.Amber;
                status.text = registering ? "正在建立帳號…" : "正在登入…";
                submit.interactable = false;
                app.Api.Post(registering ? "/api/register" : "/api/login", new Dictionary<string, object> { ["username"] = name, ["password"] = pass.text }, result =>
                {
                    submit.interactable = true;
                    if (!result.Bool("ok")) { status.color = Palette.Danger; status.text = result.Str("error", "登入或註冊失敗"); return; }
                    pass.text = confirm.text = "";
                    app.OnAuthenticated(result.Str("token"));
                });
            }).Sized(52);
            toggle = Ui.Button(stack, "", () => { registering = !registering; status.text = ""; Paint(); }, 15).Sized(44);
            Caps(stack, "帳號伺服器  " + app.Api.BaseUrl).Sized(18);
            Paint();
        }

        public void ShowBusy(string message)
        {
            var r = Begin("connecting");
            Ui.Label(r, message, 20, Palette.Bone, TextAnchor.MiddleCenter).rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 40));
        }

        // ------------------------------------------------------------ story

        static readonly string[][] Story =
        {
            new[] { "2189 / 墜落", "你是地球遠征軍的一名士兵。\n運輸艦在穿越星域時遭到赫利奧帝國伏擊，\n你在爆炸中彈射脫困，墜落在一顆陌生的翠綠星球上。" },
            new[] { "守護這個世界", "這顆星球的居民救了你，但帝國軍也盯上了這裡。\n守住翠風草原 → 摧毀熔核工廠 → 打開通往帝國母星的裂隙。\n保護這個世界，然後把戰火帶回帝國的家門口。" },
            new[] { "先學會活下來", "滑鼠瞄準，WASD 移動，左鍵射擊，右鍵瞄準。\nShift 衝刺、Space 跳躍、R 換彈、Q 手榴彈。\n接下來進入安全訓練場，完成瞄準、移動和擊破目標。" },
        };

        public void ShowStory(int page)
        {
            var r = Begin("story");
            var stack = Ui.VStack(r, 18);
            stack.Place(new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(820, 440));
            Caps(stack, $"RIFTWARD  ·  {page + 1} / {Story.Length}", Palette.Amber).Sized(18);
            Ui.Label(stack, Story[page][0], 40, Palette.Bone, TextAnchor.UpperLeft, true).Sized(54);
            Ui.Label(stack, Story[page][1], 21, Palette.Hex("d6d1c6")).Sized(150);
            var row = Ui.HStack(stack, 12).Sized(54);
            PrimaryButton(row, page < Story.Length - 1 ? "下一頁" : "進入新手訓練", () =>
            {
                if (page < Story.Length - 1) ShowStory(page + 1);
                else app.StartTraining();
            });
            if (app.Profile != null && app.Profile.TutorialComplete) Ui.Button(row, "返回基地", app.ShowHome, 18);
        }

        // ------------------------------------------------------------ home

        public void ShowHome(string status)
        {
            var r = Begin("home", false);
            var p = app.Profile;
            int selected = Mathf.Clamp(app.SelectedRegion, 0, Mathf.Max(0, p.Unlocked));
            var region = Defs.Regions[selected];

            var column = Ui.Box(r, Palette.Ink, "Column").rectTransform;
            column.anchorMin = new Vector2(0, 0); column.anchorMax = new Vector2(0, 1); column.pivot = new Vector2(0, 0.5f);
            column.sizeDelta = new Vector2(500, 0);
            var stack = Ui.VStack(column, 0);
            stack.Fill();
            stack.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(56, 56, 44, 36);
            Caps(stack, "RIFTWARD · " + Defs.Version, Palette.Amber).Sized(20);
            Ui.Label(stack, "裂隙守衛", 56, Palette.Bone, TextAnchor.UpperLeft, true).Sized(70);
            Ui.Label(stack, "墜落星球 · 第一章", 15, Palette.Muted).Sized(40);
            Widgets.Section(stack, "部署區域").Sized(26);
            for (int i = 0; i < Defs.Regions.Length; i++)
            {
                int index = i;
                bool locked = i > p.Unlocked, current = i == selected;
                var b = Ui.Button(stack, "", () => { app.SelectRegion(index); }, 15, null, current ? new Color(Palette.Amber.r, Palette.Amber.g, Palette.Amber.b, 0.1f) : new Color(0, 0, 0, 0), current ? Palette.Amber : new Color(0, 0, 0, 0)).Sized(70);
                b.interactable = !locked;
                var t = b.GetComponentInChildren<Text>();
                t.text = "";
                var num = Ui.Label(b.transform, $"0{i + 1}", 14, current ? Palette.Amber : Palette.Muted, TextAnchor.UpperLeft);
                num.rectTransform.Place(new Vector2(0, 1), new Vector2(18, -18), new Vector2(40, 20), new Vector2(0, 1));
                var name = Ui.Label(b.transform, Defs.Regions[i].Name, 22, current ? Palette.Bone : Palette.Hex("c9c5bb"), TextAnchor.UpperLeft, current);
                name.rectTransform.Place(new Vector2(0, 1), new Vector2(64, -12), new Vector2(300, 30), new Vector2(0, 1));
                var sub = Ui.Label(b.transform, locked ? "完成前一區域後解鎖" : "20 波 · 第 10、20 波 Boss", 13, Palette.Muted, TextAnchor.UpperLeft);
                sub.rectTransform.Place(new Vector2(0, 1), new Vector2(64, -44), new Vector2(300, 20), new Vector2(0, 1));
                if (current) Ui.Box(b.transform, Palette.Amber, "Mark").rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(3, 70), new Vector2(0, 0.5f));
            }
            Ui.Rect(stack, "Gap").Sized(24);
            var deploy = PrimaryButton(stack, "", app.Deploy).Sized(64);
            deploy.SetCaption($"部署          0{selected + 1} {region.Name}");
            deploy.interactable = app.CanDeploy;
            Ui.Rect(stack, "Gap").Sized(12);
            var grid = Ui.HStack(stack, 8).Sized(48);
            Ui.Button(grid, "軍械庫", ShowArmory, 15);
            Ui.Button(grid, "故事與教學", () => ShowStory(0), 15);
            Ui.Rect(stack, "Spacer").gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            Ui.Box(stack, Palette.Line, "Rule").Sized(1);
            Ui.Rect(stack, "Gap").Sized(14);
            var pilot = Ui.Rect(stack, "Pilot").Sized(46);
            Ui.Label(pilot, p.Username, 16, Palette.Bone, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), Vector2.zero, new Vector2(240, 22), new Vector2(0, 1));
            Ui.Label(pilot, status, 13, Palette.Muted).rectTransform.Place(new Vector2(0, 1), new Vector2(0, -24), new Vector2(260, 20), new Vector2(0, 1));
            Ui.Label(pilot, $"{p.Gold} G", 22, Palette.Amber, TextAnchor.UpperRight).rectTransform.Place(new Vector2(1, 1), Vector2.zero, new Vector2(150, 30), new Vector2(1, 1));
            var links = Ui.HStack(stack, 8).Sized(36);
            Ui.Button(links, "登出", app.Logout, 14, Palette.Muted, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0));
            Ui.Button(links, "離開遊戲", app.Quit, 14, Palette.Muted, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0));
            if (p.Admin) Ui.Button(links, "管理員", ShowAdmin, 14, Palette.Danger, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0));

            // Briefing sheet over the live region backdrop.
            var brief = Ui.Box(r, new Color(0.063f, 0.067f, 0.075f, 0.92f), "Briefing").rectTransform;
            brief.Place(new Vector2(1, 0), new Vector2(-48, 48), new Vector2(520, 500), new Vector2(1, 0));
            Ui.Box(brief, region.Accent, "Rule").rectTransform.Place(new Vector2(0.5f, 1), Vector2.zero, new Vector2(520, 2), new Vector2(0.5f, 1));
            var b2 = Ui.VStack(brief, 12);
            b2.Fill();
            b2.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(32, 32, 28, 26);
            Caps(b2, $"任務簡報 / 0{selected + 1}").Sized(18);
            Ui.Label(b2, region.Name, 34, region.Accent, TextAnchor.UpperLeft, true).Sized(44);
            Ui.Label(b2, region.Story.Replace("\n", ""), 16, Palette.Hex("d6d1c6")).Sized(54);
            Ui.Box(b2, Palette.Line, "Rule").Sized(1);
            var head = Ui.Rect(b2, "LoadoutHead").Sized(20);
            Caps(head, "出擊配置").rectTransform.Fill();
            var change = Ui.Button(head, "更換武器", ShowArmory, 13, Palette.Amber, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0));
            change.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(100, 24), new Vector2(1, 0.5f));
            var kit = Ui.HStack(b2, 12).Sized(78);
            for (int i = 0; i < 2; i++)
            {
                var cell = Ui.Box(kit, new Color(0, 0, 0, 0), "Slot");
                var o = cell.gameObject.AddComponent<Outline>();
                o.effectColor = Palette.Line;
                if (i >= p.Loadout.Equipped.Count) { Ui.Label(cell.transform, $"{i + 1}  未裝備", 14, Palette.Muted, TextAnchor.MiddleCenter).rectTransform.Fill(); continue; }
                var w = Defs.Weapons[p.Loadout.Equipped[i]];
                var st = w.StatsAt(p.Loadout.Level(w.Id, "damage"), p.Loadout.Level(w.Id, "mag"), p.Loadout.Level(w.Id, "reload"));
                Ui.Label(cell.transform, (i + 1).ToString(), 12, Palette.Amber).rectTransform.Place(new Vector2(0, 1), new Vector2(14, -10), new Vector2(20, 16), new Vector2(0, 1));
                Ui.Label(cell.transform, w.Name, 16, Palette.Bone, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(14, -28), new Vector2(210, 22), new Vector2(0, 1));
                Ui.Label(cell.transform, $"{st.Mag} 發 · 換彈 {st.Reload:0.0} 秒", 12, Palette.Muted).rectTransform.Place(new Vector2(0, 1), new Vector2(14, -52), new Vector2(210, 18), new Vector2(0, 1));
            }
            Caps(b2, "生涯戰績").Sized(18);
            var record = Ui.HStack(b2, 10).Sized(52);
            foreach (var (label, value) in new[] { ("總擊殺", p.Kills.ToString()), ("最高波次", p.BestWave.ToString()), ("命中率", $"{p.Accuracy * 100:0}%"), ("勝場", $"{p.Victories} / {p.Missions}") })
            {
                var cell = Ui.VStack(record, 2);
                Ui.Label(cell, label, 12, Palette.Muted).Sized(18);
                Ui.Label(cell, value, 22, Palette.Bone, TextAnchor.UpperLeft, true).Sized(28);
            }
            Caps(b2, "WASD 移動 · 左鍵射擊 · 右鍵瞄準 · R 換彈 · 1/2 換槍 · Q 手榴彈 · Shift 衝刺 · E 救援 · V 視角").Sized(40);
        }

        // ------------------------------------------------------------ armory

        string armoryPick = "pistol";
        Text armoryStatus;

        public void ShowArmory()
        {
            var r = Begin("armory");
            var p = app.Profile;
            var pad = Ui.Rect(r, "Pad").Fill();
            pad.offsetMin = new Vector2(48, 40); pad.offsetMax = new Vector2(-48, -40);
            Ui.Button(pad, "← 返回基地", app.ShowHome, 14, Palette.Muted, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0))
                .GetComponent<RectTransform>().Place(new Vector2(0, 1), new Vector2(-14, 0), new Vector2(130, 24), new Vector2(0, 1));
            Ui.Label(pad, "軍械庫", 44, Palette.Bone, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(0, -30), new Vector2(300, 56), new Vector2(0, 1));
            Caps(pad, "持有金幣").rectTransform.Place(new Vector2(1, 1), new Vector2(0, -24), new Vector2(200, 18), new Vector2(1, 1)).GetComponent<Text>().alignment = TextAnchor.UpperRight;
            var gold = Ui.Label(pad, $"{p.Gold} G", 32, Palette.Amber, TextAnchor.UpperRight);
            gold.rectTransform.Place(new Vector2(1, 1), new Vector2(0, -44), new Vector2(300, 40), new Vector2(1, 1));

            // Weapon list
            var list = Ui.VStack(pad, 0);
            list.Place(new Vector2(0, 1), new Vector2(0, -110), new Vector2(400, 330), new Vector2(0, 1));
            Widgets.Section(list, "武器").Sized(28);
            for (int i = 0; i < Defs.WeaponOrder.Length; i++)
            {
                string id = Defs.WeaponOrder[i];
                var w = Defs.Weapons[id];
                bool owned = p.Loadout.Owned.Contains(id), current = id == armoryPick;
                int slot = p.Loadout.Equipped.IndexOf(id);
                var row = Ui.Button(list, "", () => { armoryPick = id; ShowArmory(); }, 15, null, current ? new Color(Palette.Amber.r, Palette.Amber.g, Palette.Amber.b, 0.08f) : new Color(0, 0, 0, 0), current ? Palette.Amber : new Color(0, 0, 0, 0)).Sized(74);
                row.GetComponentInChildren<Text>().text = "";
                Ui.Label(row.transform, $"0{i + 1}", 13, current ? Palette.Amber : Palette.Muted).rectTransform.Place(new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(30, 20), new Vector2(0, 0.5f));
                Ui.Label(row.transform, w.Name, 18, owned ? Palette.Bone : Palette.Hex("a9abae"), TextAnchor.UpperLeft, owned).rectTransform.Place(new Vector2(0, 1), new Vector2(60, -14), new Vector2(260, 24), new Vector2(0, 1));
                string detail = owned ? string.Join(" · ", Defs.UpgradeKeys.Where(k => p.Loadout.Level(id, k) > 0).Select(k => $"{UpgradeName(k)} Lv {p.Loadout.Level(id, k)}")) : "未持有";
                Ui.Label(row.transform, detail == "" ? "已擁有" : detail, 12, Palette.Muted).rectTransform.Place(new Vector2(0, 1), new Vector2(60, -42), new Vector2(260, 18), new Vector2(0, 1));
                var tag = Ui.Label(row.transform, owned ? (slot >= 0 ? $"{slot + 1} 號" : "") : $"{w.Price} G", 13, owned ? Palette.Bone : Palette.Amber, TextAnchor.MiddleRight);
                tag.rectTransform.Place(new Vector2(1, 0.5f), new Vector2(-16, 0), new Vector2(80, 20), new Vector2(1, 0.5f));
                if (current) Ui.Box(row.transform, Palette.Amber, "Mark").rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(3, 74), new Vector2(0, 0.5f));
            }
            // Loadout strip
            var strip = Ui.Rect(pad, "Loadout").Place(new Vector2(0, 0), Vector2.zero, new Vector2(400, 80), new Vector2(0, 0));
            Caps(strip, "出擊配置").rectTransform.Place(new Vector2(0, 1), Vector2.zero, new Vector2(200, 18), new Vector2(0, 1));
            for (int i = 0; i < 2; i++)
            {
                var cell = Ui.Box(strip, new Color(0, 0, 0, 0), "Cell");
                cell.rectTransform.Place(new Vector2(0, 0), new Vector2(i * 204, 0), new Vector2(196, 50), new Vector2(0, 0));
                var o = cell.gameObject.AddComponent<Outline>();
                bool has = i < p.Loadout.Equipped.Count;
                o.effectColor = has && p.Loadout.Equipped[i] == armoryPick ? Palette.Amber : Palette.Line;
                Ui.Label(cell.transform, $"{i + 1}    {(has ? Defs.Weapons[p.Loadout.Equipped[i]].Name : "未裝備")}", 15, has ? Palette.Bone : Palette.Muted, TextAnchor.MiddleLeft)
                    .rectTransform.Fill().offsetMin = new Vector2(14, 0);
            }

            // Detail sheet
            var wd = Defs.Weapons[armoryPick];
            bool own = p.Loadout.Owned.Contains(armoryPick);
            var sheet = Ui.Box(pad, Palette.Sheet, "Detail").rectTransform;
            sheet.anchorMin = new Vector2(0, 0); sheet.anchorMax = new Vector2(1, 1);
            sheet.offsetMin = new Vector2(440, 0); sheet.offsetMax = new Vector2(0, -110);
            TopRule(sheet, Palette.Amber);
            var ds = Ui.VStack(sheet, 14);
            ds.Fill();
            ds.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(32, 32, 26, 24);
            Ui.Label(ds, wd.Name, 34, Palette.Bone, TextAnchor.UpperLeft, true).Sized(44);
            Ui.Label(ds, wd.Description, 15, Palette.Hex("c9c5bb")).Sized(22);
            var st = wd.StatsAt(p.Loadout.Level(armoryPick, "damage"), p.Loadout.Level(armoryPick, "mag"), p.Loadout.Level(armoryPick, "reload"));
            float dps = st.Damage * st.Pellets / st.Interval;
            var stats = Ui.HStack(ds, 22).Sized(70);
            StatCell(stats, "傷害", st.Pellets > 1 ? $"{st.Damage:0}×{st.Pellets}" : $"{st.Damage:0}", st.Damage * st.Pellets / 700f);
            StatCell(stats, "每秒傷害", $"{dps:0}", dps / 280f);
            StatCell(stats, "彈匣", st.Mag.ToString(), st.Mag / 45f);
            StatCell(stats, "換彈", $"{st.Reload:0.00}s", 1f - st.Reload / 3f);
            Ui.Box(ds, Palette.Line, "Rule").Sized(1);
            if (!own)
            {
                var buy = PrimaryButton(ds, $"購買    {wd.Price} G", () => ArmoryRequest("/api/shop/buy", new Dictionary<string, object> { ["weapon"] = armoryPick }), 18).Sized(52);
                buy.interactable = p.Gold >= wd.Price;
            }
            else
            {
                foreach (var key in Defs.UpgradeKeys)
                {
                    int lv = p.Loadout.Level(armoryPick, key);
                    var row = Ui.Rect(ds, "Upgrade").Sized(48);
                    Ui.Label(row, UpgradeName(key), 16, Palette.Bone, TextAnchor.MiddleLeft, true).rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(80, 24), new Vector2(0, 0.5f));
                    Ui.Label(row, key == "damage" ? "每級 +12%" : key == "mag" ? "每級 +15%" : "每級 -8%", 12, Palette.Muted, TextAnchor.MiddleLeft)
                        .rectTransform.Place(new Vector2(0, 0.5f), new Vector2(80, 0), new Vector2(110, 20), new Vector2(0, 0.5f));
                    for (int k = 0; k < Defs.MaxUpgradeLevel; k++)
                        Ui.Box(row, k < lv ? Palette.Amber : Palette.Line, "Pip").rectTransform.Place(new Vector2(0, 0.5f), new Vector2(200 + k * 32, 0), new Vector2(28, 6), new Vector2(0, 0.5f));
                    bool maxed = lv >= Defs.MaxUpgradeLevel;
                    int cost = Defs.UpgradeCost(armoryPick, lv);
                    var up = Ui.Button(row, maxed ? "已滿級" : $"升級  {cost} G", () => ArmoryRequest("/api/shop/upgrade", new Dictionary<string, object> { ["weapon"] = armoryPick, ["stat"] = key }), 14, Palette.Amber, new Color(0, 0, 0, 0), Palette.Amber);
                    up.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(150, 44), new Vector2(1, 0.5f));
                    up.interactable = !maxed && p.Gold >= cost;
                }
                var equip = Ui.HStack(ds, 12).Sized(48);
                for (int s = 0; s < 2; s++)
                {
                    int slot = s;
                    bool on = slot < p.Loadout.Equipped.Count && p.Loadout.Equipped[slot] == armoryPick;
                    var b = Ui.Button(equip, on ? $"已裝備 · {slot + 1} 號" : $"裝備到 {slot + 1} 號", () => Equip(armoryPick, slot), 15, on ? Palette.Amber : Palette.Bone, null, on ? Palette.Amber : Palette.Line);
                    b.interactable = !on;
                }
            }
            armoryStatus = Ui.Label(ds, "", 14, Palette.Amber).Sized(22);
        }

        static string UpgradeName(string key) => key == "damage" ? "傷害" : key == "mag" ? "彈匣" : "換彈";

        static void StatCell(Transform parent, string label, string value, float ratio)
        {
            var cell = Ui.VStack(parent, 8);
            Ui.Label(cell, label, 12, Palette.Muted).Sized(18);
            Ui.Label(cell, value, 28, Palette.Bone, TextAnchor.UpperLeft, true).Sized(34);
            var bar = new Ui.Bar(cell, new Color(1, 1, 1, 0.12f), Palette.Bone);
            bar.Root.Sized(3);
            bar.Value = Mathf.Clamp(ratio, 0.04f, 1f);
        }

        void Equip(string weapon, int slot)
        {
            var kit = new List<string>(app.Profile.Loadout.Equipped);
            int other = kit.IndexOf(weapon);
            while (kit.Count <= slot) kit.Add(weapon);
            if (other >= 0 && other != slot) kit[other] = kit[slot];
            kit[slot] = weapon;
            ArmoryRequest("/api/shop/equip", new Dictionary<string, object> { ["weapons"] = kit.Distinct().Cast<object>().ToList() });
        }

        void ArmoryRequest(string path, Dictionary<string, object> data)
        {
            if (armoryStatus != null) armoryStatus.text = "處理中…";
            app.Api.Post(path, data, result =>
            {
                if (Mode != "armory") return;
                if (result.Bool("ok")) { app.UpdateProfile(Profile.From(result.Obj("profile"))); ShowArmory(); armoryStatus.text = "完成！裝備已更新。"; }
                else { armoryStatus.color = Palette.Danger; armoryStatus.text = result.Str("error", "操作失敗"); }
            });
        }

        // ------------------------------------------------------------ admin

        Text adminStatus;
        RectTransform adminList;
        InputField adminTarget;

        public void ShowAdmin()
        {
            if (!app.Profile.Admin) return;
            var r = Begin("admin");
            var pad = Ui.Rect(r, "Pad").Fill();
            pad.offsetMin = new Vector2(48, 40); pad.offsetMax = new Vector2(-48, -40);
            Ui.Button(pad, "← 返回基地", app.ShowHome, 14, Palette.Muted, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0))
                .GetComponent<RectTransform>().Place(new Vector2(0, 1), new Vector2(-14, 0), new Vector2(130, 24), new Vector2(0, 1));
            Ui.Label(pad, "管理員控制台", 40, Palette.Bone, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(0, -30), new Vector2(500, 52), new Vector2(0, 1));

            var grant = Ui.Box(pad, Palette.Sheet, "Grant").rectTransform;
            grant.anchorMin = new Vector2(0, 0); grant.anchorMax = new Vector2(0.56f, 1);
            grant.offsetMin = Vector2.zero; grant.offsetMax = new Vector2(-10, -100);
            TopRule(grant, Palette.Amber);
            var gs = Ui.VStack(grant, 10);
            gs.Fill();
            gs.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(26, 26, 22, 22);
            Ui.Label(gs, "發放福利", 22, Palette.Bone, TextAnchor.UpperLeft, true).Sized(30);
            Ui.Label(gs, "輸入玩家帳號，或從名單點選；按「發給全部玩家」即可全服發放。", 13, Palette.Muted).Sized(20);
            adminTarget = Widgets.Input(gs, "玩家帳號", false, 24).Sized(44);
            var row = Ui.HStack(gs, 10).Sized(44);
            var goldInput = Widgets.Input(row, "金幣數量", false, 6);
            goldInput.contentType = InputField.ContentType.IntegerNumber;
            goldInput.text = "500";
            int weaponPick = 0;
            string[] choices = { "不發放武器", "解鎖 AR-27 突擊步槍", "解鎖 SG-8 霰彈槍", "解鎖 LR-50 狙擊槍" };
            Button weaponButton = null;
            weaponButton = Ui.Button(row, choices[0], () => { weaponPick = (weaponPick + 1) % choices.Length; weaponButton.SetCaption(choices[weaponPick]); }, 14);
            var actions = Ui.HStack(gs, 10).Sized(46);
            void Send(bool all)
            {
                string who = all ? "*" : adminTarget.text.Trim();
                if (who == "") { adminStatus.color = Palette.Danger; adminStatus.text = "請輸入玩家帳號，或按「發給全部玩家」。"; return; }
                int.TryParse(goldInput.text, out int gold);
                adminStatus.color = Palette.Amber;
                adminStatus.text = "發放中…";
                app.Api.Post("/api/admin/grant", new Dictionary<string, object> { ["username"] = who, ["gold"] = gold, ["weapon"] = weaponPick == 0 ? "" : Defs.WeaponOrder[weaponPick] }, result =>
                {
                    if (Mode != "admin") return;
                    if (result.Bool("ok"))
                    {
                        app.UpdateProfile(Profile.From(result.Obj("profile")));
                        adminStatus.color = Palette.Amber;
                        adminStatus.text = $"已發放給 {result.Int("count")} 位玩家。";
                        LoadPlayers();
                    }
                    else { adminStatus.color = Palette.Danger; adminStatus.text = result.Str("error", "發放失敗"); }
                });
            }
            Ui.Button(actions, "發給指定玩家", () => Send(false), 15);
            Ui.Button(actions, "發給全部玩家", () => Send(true), 15, Palette.Amber, null, Palette.Amber);
            adminStatus = Ui.Label(gs, "", 14, Palette.Amber).Sized(22);
            Widgets.Section(gs, "玩家名單").Sized(22);
            adminList = Widgets.ScrollList(gs, out _);
            adminList.parent.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;

            var self = Ui.Box(pad, Palette.Sheet, "Self").rectTransform;
            self.anchorMin = new Vector2(0.56f, 0); self.anchorMax = new Vector2(1, 1);
            self.offsetMin = new Vector2(10, 0); self.offsetMax = new Vector2(0, -100);
            TopRule(self, Palette.Danger);
            var ss = Ui.VStack(self, 12);
            ss.Fill();
            ss.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(26, 26, 22, 22);
            AbilityPanel(ss);
            LoadPlayers();
        }

        /// Admin self-abilities; shared by the console and the in-mission pause menu.
        public void AbilityPanel(Transform parent)
        {
            Ui.Label(parent, "管理員能力", 22, Palette.Bone, TextAnchor.UpperLeft, true).Sized(30);
            Ui.Label(parent, "只對你自己的角色生效，立即同步到伺服器。", 13, Palette.Muted).Sized(20);
            var s = app.AdminSettings;
            SliderRow(parent, "攻擊力倍率", 1, 20, s.Damage, v => { s.Damage = v; app.PushAdmin(); });
            SliderRow(parent, "移動速度倍率", 0.5f, 3, s.Speed, v => { s.Speed = v; app.PushAdmin(); });
            Widgets.Toggle(parent, "無敵（不會受到傷害）", s.God, on => { s.God = on; app.PushAdmin(); }).Sized(46);
            Widgets.Toggle(parent, "無限彈藥（不需換彈）", s.InfiniteAmmo, on => { s.InfiniteAmmo = on; app.PushAdmin(); }).Sized(46);
            Widgets.Toggle(parent, "飛行（Space 上升 · Ctrl 下降）", s.Fly, on => { s.Fly = on; app.PushAdmin(); }).Sized(46);
        }

        static void SliderRow(Transform parent, string label, float min, float max, float value, Action<float> changed)
        {
            var row = Ui.Rect(parent, "SliderRow").Sized(36);
            Ui.Label(row, label, 15, Palette.Bone, TextAnchor.MiddleLeft).rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(130, 24), new Vector2(0, 0.5f));
            var readout = Ui.Label(row, $"×{value:0.0}", 15, Palette.Amber, TextAnchor.MiddleRight);
            readout.rectTransform.Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(60, 24), new Vector2(1, 0.5f));
            var slider = Widgets.Slider(row, min, max, value, v =>
            {
                float stepped = Mathf.Round(v * 10f) / 10f;
                readout.text = $"×{stepped:0.0}";
                changed(stepped);
            });
            var rt = slider.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(1, 0.5f);
            rt.offsetMin = new Vector2(140, -11); rt.offsetMax = new Vector2(-70, 11);
        }

        void LoadPlayers()
        {
            app.Api.Post("/api/admin/players", null, result =>
            {
                if (Mode != "admin" || adminList == null) return;
                foreach (Transform child in adminList) UnityEngine.Object.Destroy(child.gameObject);
                if (!result.Bool("ok")) { adminStatus.color = Palette.Danger; adminStatus.text = result.Str("error", "無法讀取玩家名單"); return; }
                foreach (var item in result.Arr("players") ?? new List<object>())
                {
                    if (!(item is Dictionary<string, object> player)) continue;
                    string name = player.Str("username");
                    var b = Ui.Button(adminList, $"{name}      {player.Int("gold")} G      已解鎖 {player.Int("unlocked") + 1} 區", () => adminTarget.text = name, 14).Sized(36);
                    var t = b.GetComponentInChildren<Text>();
                    t.alignment = TextAnchor.MiddleLeft;
                    t.rectTransform.offsetMin = new Vector2(14, 0);
                }
            });
        }
    }
}
