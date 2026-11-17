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

        const float NavHeight = 72f;

        /// Top navigation shared by the lobby pages: brand, 關卡 / 商城 / 裝備 tabs, resources and account links.
        /// Returns the content area below the bar.
        RectTransform Page(RectTransform r, string tab)
        {
            var p = app.Profile;
            var bar = Ui.Box(r, new Color(0.04f, 0.043f, 0.05f, 0.97f), "Nav").rectTransform;
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1); bar.pivot = new Vector2(0.5f, 1);
            bar.offsetMin = new Vector2(0, -NavHeight); bar.offsetMax = Vector2.zero;
            var line = Ui.Box(bar, Palette.Line, "Line").rectTransform;
            line.anchorMin = Vector2.zero; line.anchorMax = new Vector2(1, 0); line.offsetMin = Vector2.zero; line.offsetMax = new Vector2(0, 1);
            Ui.Label(bar, "RIFTWARD  " + Defs.Version, 11, Palette.Amber).rectTransform.Place(new Vector2(0, 0.5f), new Vector2(40, 14), new Vector2(200, 16), new Vector2(0, 0.5f));
            Ui.Label(bar, "裂隙守衛", 24, Palette.Bone, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 0.5f), new Vector2(40, -6), new Vector2(200, 32), new Vector2(0, 0.5f));

            string[] ids = { "missions", "shop", "gear" };
            string[] names = { "關卡", "商城", "裝備" };
            Action[] go = { app.ShowHome, ShowShop, ShowCharacter };
            for (int i = 0; i < ids.Length; i++)
            {
                bool active = ids[i] == tab;
                var b = Ui.Button(bar, names[i], go[i], 19, active ? Palette.Bone : Palette.Muted, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0));
                b.GetComponent<RectTransform>().Place(new Vector2(0, 0.5f), new Vector2(250 + i * 116, 0), new Vector2(108, NavHeight), new Vector2(0, 0.5f));
                if (active)
                {
                    b.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;
                    Ui.Box(b.transform, Palette.Amber, "Underline").rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 1), new Vector2(40, 3), new Vector2(0.5f, 0));
                }
                if (ids[i] == "gear" && p.TalentPoints > 0)
                    Ui.Box(b.transform, Palette.Amber, "Badge").rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(30, 12), new Vector2(7, 7));
            }

            // Account block on the far right, resource chips to its left.
            var account = Ui.Rect(bar, "Account").Place(new Vector2(1, 0.5f), new Vector2(-40, 0), new Vector2(210, 52), new Vector2(1, 0.5f));
            Ui.Label(account, $"{p.Username}  ·  Lv {p.Level}", 15, Palette.Bone, TextAnchor.UpperRight, true).rectTransform.Place(new Vector2(1, 1), Vector2.zero, new Vector2(210, 22), new Vector2(1, 1));
            var links = Ui.HStack(account, 4);
            links.Place(new Vector2(1, 0), Vector2.zero, new Vector2(p.Admin ? 200 : 136, 24), new Vector2(1, 0));
            if (p.Admin) Ui.Button(links, "管理員", ShowAdmin, 12, Palette.Danger, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0));
            Ui.Button(links, "登出", app.Logout, 12, Palette.Muted, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0));
            Ui.Button(links, "離開遊戲", app.Quit, 12, Palette.Muted, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0));
            var chips = new[] { ("金幣", $"{p.Gold}", Palette.Amber), ("裝備鑰匙", p.Keys.ToString(), Palette.Hex("b07cff")), ("武器鑰匙", p.WeaponKeys.ToString(), Palette.Danger) };
            for (int i = 0; i < chips.Length; i++)
            {
                var (label, value, color) = chips[chips.Length - 1 - i];
                var chip = Ui.Rect(bar, "Chip").Place(new Vector2(1, 0.5f), new Vector2(-270 - i * 112, 0), new Vector2(100, 46), new Vector2(1, 0.5f));
                Ui.Box(chip, color, "Mark").rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(2, 34), new Vector2(0, 0.5f));
                Ui.Label(chip, label, 11, Palette.Muted).rectTransform.Place(new Vector2(0, 1), new Vector2(12, -2), new Vector2(90, 16), new Vector2(0, 1));
                Ui.Label(chip, value, 20, color, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(12, -18), new Vector2(90, 26), new Vector2(0, 1));
            }

            var body = Ui.Rect(r, "Body").Fill();
            body.offsetMax = new Vector2(0, -NavHeight);
            return body;
        }

        /// Sub-navigation inside the 裝備 tab.
        void GearTabs(RectTransform pad, string active)
        {
            var row = Ui.Rect(pad, "SubTabs").Place(new Vector2(0, 1), Vector2.zero, new Vector2(320, 40), new Vector2(0, 1));
            string[] names = { "天賦與裝備", "武器" };
            Action[] go = { ShowCharacter, ShowArmory };
            for (int i = 0; i < 2; i++)
            {
                bool on = (i == 0 ? "character" : "armory") == active;
                var b = Ui.Button(row, names[i], go[i], 16, on ? Palette.Amber : Palette.Muted,
                    on ? new Color(Palette.Amber.r, Palette.Amber.g, Palette.Amber.b, 0.1f) : new Color(0, 0, 0, 0), on ? Palette.Amber : Palette.Line);
                b.GetComponent<RectTransform>().Place(new Vector2(0, 1), new Vector2(i * 156, 0), new Vector2(148, 40), new Vector2(0, 1));
            }
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

            var body = Page(r, "missions");
            var column = Ui.Box(body, new Color(0.063f, 0.067f, 0.075f, 0.96f), "Column").rectTransform;
            column.anchorMin = new Vector2(0, 0); column.anchorMax = new Vector2(0, 1); column.pivot = new Vector2(0, 0.5f);
            column.sizeDelta = new Vector2(500, 0);
            var stack = Ui.VStack(column, 0);
            stack.Fill();
            stack.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(48, 48, 32, 28);
            Ui.Label(stack, "選擇關卡", 34, Palette.Bone, TextAnchor.UpperLeft, true).Sized(46);
            Ui.Label(stack, "第一章 · 墜落星球", 14, Palette.Muted).Sized(30);
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
            Ui.Button(stack, "故事與教學", () => ShowStory(0), 14, Palette.Muted).Sized(40);
            if (app.Net.Welcomed)
            {
                Ui.Rect(stack, "Gap").Sized(20);
                Widgets.Section(stack, "多人房間").Sized(26);
                if (app.InRoom)
                {
                    var info = Ui.HStack(stack, 8).Sized(48);
                    Ui.Label(info, app.RoomCode, 30, Palette.Amber, TextAnchor.MiddleLeft, true);
                    Ui.Label(info, $"{app.PartySize} / 4 名隊員\n{(app.IsLeader ? "你是隊長，把房號告訴隊友" : "等待隊長部署")}", 13, Palette.Muted, TextAnchor.MiddleLeft);
                    Ui.Button(info, "離開房間", app.LeaveRoom, 14);
                }
                else
                {
                    var row = Ui.HStack(stack, 8).Sized(44);
                    var code = Widgets.Input(row, "輸入房號", false, 4);
                    code.characterValidation = InputField.CharacterValidation.Alphanumeric;
                    Ui.Button(row, "加入", () => app.JoinRoom(code.text), 15);
                    Ui.Button(row, "建立房間", app.CreateRoom, 15);
                }
            }
            Ui.Rect(stack, "Spacer").gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            Ui.Box(stack, Palette.Line, "Rule").Sized(1);
            Ui.Rect(stack, "Gap").Sized(10);
            Ui.Label(stack, status, 13, Palette.Muted).Sized(20);

            // Briefing sheet over the live region backdrop.
            var brief = Ui.Box(body, new Color(0.063f, 0.067f, 0.075f, 0.92f), "Briefing").rectTransform;
            brief.Place(new Vector2(1, 0), new Vector2(-40, 40), new Vector2(520, 500), new Vector2(1, 0));
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
            var change = Ui.Button(head, "更換武器 →", ShowArmory, 13, Palette.Amber, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0));
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
                Ui.Label(cell.transform, w.Name, 16, Gear.RarityColors[p.Loadout.RarityOf(w.Id)], TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(14, -28), new Vector2(210, 22), new Vector2(0, 1));
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
            var pad = Page(r, "gear");
            pad.offsetMin = new Vector2(48, 36); pad.offsetMax = new Vector2(-48, -NavHeight - 28);
            GearTabs(pad, "armory");

            // Weapon list
            var list = Ui.VStack(pad, 0);
            list.Place(new Vector2(0, 1), new Vector2(0, -64), new Vector2(400, 330), new Vector2(0, 1));
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
                Ui.Label(row.transform, owned ? $"{w.Name}  ·  {Gear.RarityNames[p.Loadout.RarityOf(id)]}" : w.Name, 18, owned ? Gear.RarityColors[p.Loadout.RarityOf(id)] : Palette.Hex("a9abae"), TextAnchor.UpperLeft, owned).rectTransform.Place(new Vector2(0, 1), new Vector2(60, -14), new Vector2(300, 24), new Vector2(0, 1));
                string detail = owned ? string.Join(" · ", Defs.UpgradeKeys.Where(k => p.Loadout.Level(id, k) > 0).Select(k => $"{UpgradeName(k)} Lv {p.Loadout.Level(id, k)}")) : "未持有";
                Ui.Label(row.transform, detail == "" ? "已擁有" : detail, 12, Palette.Muted).rectTransform.Place(new Vector2(0, 1), new Vector2(60, -42), new Vector2(260, 18), new Vector2(0, 1));
                var tag = Ui.Label(row.transform, owned ? (slot >= 0 ? $"{slot + 1} 號" : "") : "武器補給", 13, owned ? Palette.Bone : Palette.Muted, TextAnchor.MiddleRight);
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
            sheet.offsetMin = new Vector2(440, 0); sheet.offsetMax = new Vector2(0, -64);
            TopRule(sheet, Palette.Amber);
            var ds = Ui.VStack(sheet, 14);
            ds.Fill();
            ds.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(32, 32, 26, 24);
            Ui.Label(ds, wd.Name, 34, Palette.Bone, TextAnchor.UpperLeft, true).Sized(44);
            if (own) Ui.Label(ds, $"{Gear.RarityNames[p.Loadout.RarityOf(armoryPick)]}  ·  傷害 ×{Defs.WeaponRarityMult[p.Loadout.RarityOf(armoryPick)]:0.00}", 15, Gear.RarityColors[p.Loadout.RarityOf(armoryPick)], TextAnchor.UpperLeft, true).Sized(22);
            Ui.Label(ds, wd.Description, 15, Palette.Hex("c9c5bb")).Sized(22);
            var st = wd.StatsAt(p.Loadout.Level(armoryPick, "damage"), p.Loadout.Level(armoryPick, "mag"), p.Loadout.Level(armoryPick, "reload"));
            st.Damage *= Defs.WeaponRarityMult[own ? p.Loadout.RarityOf(armoryPick) : 1];
            float dps = st.Damage * st.Pellets / st.Interval;
            var stats = Ui.HStack(ds, 22).Sized(70);
            StatCell(stats, "傷害", st.Pellets > 1 ? $"{st.Damage:0}×{st.Pellets}" : $"{st.Damage:0}", st.Damage * st.Pellets / 700f);
            StatCell(stats, "每秒傷害", $"{dps:0}", dps / 280f);
            StatCell(stats, "彈匣", st.Mag.ToString(), st.Mag / 45f);
            StatCell(stats, "換彈", $"{st.Reload:0.00}s", 1f - st.Reload / 3f);
            Ui.Box(ds, Palette.Line, "Rule").Sized(1);
            if (!own)
            {
                Ui.Label(ds, "尚未持有。武器改由「商城 → 武器補給」抽取取得。", 15, Palette.Muted).Sized(26);
                PrimaryButton(ds, "前往商城 · 武器補給", ShowShop, 18).Sized(52);
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

        // ------------------------------------------------------------ character: talents, gear, supply draws

        Text characterStatus;
        string drawPool = "gear";
        List<GearItem> lastDraw = new List<GearItem>();

        RectTransform characterRoot;

        public void ShowCharacter()
        {
            var r = Begin("character");
            characterRoot = r;
            var p = app.Profile;
            var pad = Page(r, "gear");
            pad.offsetMin = new Vector2(48, 36); pad.offsetMax = new Vector2(-48, -NavHeight - 28);
            GearTabs(pad, "character");
            characterStatus = Ui.Label(pad, "", 14, Palette.Amber, TextAnchor.UpperRight);
            characterStatus.rectTransform.Place(new Vector2(1, 1), new Vector2(0, -10), new Vector2(520, 20), new Vector2(1, 1));

            RectTransform Column(float x0, float x1, string title, Color accent)
            {
                var box = Ui.Box(pad, Palette.Sheet, title).rectTransform;
                box.anchorMin = new Vector2(x0, 0); box.anchorMax = new Vector2(x1, 1);
                box.offsetMin = new Vector2(x0 > 0 ? 8 : 0, 0); box.offsetMax = new Vector2(x1 < 1 ? -8 : 0, -60);
                TopRule(box, accent);
                var st = Ui.VStack(box, 8);
                st.Fill();
                st.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(24, 24, 20, 20);
                Ui.Label(st, title, 22, Palette.Bone, TextAnchor.UpperLeft, true).Sized(30);
                return st;
            }

            // ---- talents
            var t = Column(0, 0.36f, "天賦", Palette.Amber);
            Ui.Label(t, $"等級 {p.Level}    經驗 {p.LevelXp} / {p.LevelNeed}", 14, Palette.Muted).Sized(20);
            var bar = Ui.Box(t, Palette.Line, "Xp").Sized(4);
            var fill = Ui.Box(bar.transform, Palette.Amber, "Fill").rectTransform;
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(Mathf.Clamp01((float)p.LevelXp / p.LevelNeed), 1); fill.offsetMin = fill.offsetMax = Vector2.zero;
            Ui.Label(t, p.TalentPoints > 0 ? $"可用天賦點數  {p.TalentPoints}" : "完成任務獲得經驗，每升一級得到 1 點天賦", 14, p.TalentPoints > 0 ? Palette.Amber : Palette.Muted).Sized(26);
            foreach (var key in Gear.Talents)
            {
                var (name, per) = Gear.TalentInfo[key];
                int lv = p.Talents.TryGetValue(key, out var v) ? v : 0;
                var row = Ui.Rect(t, "Talent").Sized(58);
                Ui.Label(row, name, 16, Palette.Bone, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(0, -4), new Vector2(200, 22), new Vector2(0, 1));
                Ui.Label(row, $"{per} / 點    {lv} / {Gear.TalentMax}", 12, Palette.Muted).rectTransform.Place(new Vector2(0, 1), new Vector2(0, -28), new Vector2(220, 18), new Vector2(0, 1));
                for (int i = 0; i < Gear.TalentMax; i++)
                    Ui.Box(row, i < lv ? Palette.Amber : Palette.Line, "Pip").rectTransform.Place(new Vector2(0, 0), new Vector2(i * 14, 2), new Vector2(10, 4), new Vector2(0, 0));
                string k = key;
                var plus = Ui.Button(row, "+", () => CharacterRequest("/api/talent/assign", new Dictionary<string, object> { ["talent"] = k, ["points"] = 1 }), 20);
                plus.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(44, 40), new Vector2(1, 0.5f));
                plus.interactable = p.TalentPoints > 0 && lv < Gear.TalentMax;
            }
            Ui.Rect(t, "Spacer").gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            Ui.Button(t, "重置天賦（返還全部點數）", () => CharacterRequest("/api/talent/reset", new Dictionary<string, object>()), 13, Palette.Muted).Sized(36);

            // ---- equipment
            var g = Column(0.36f, 1f, "裝備", Palette.Hex("b07cff"));
            var total = new List<string>();
            foreach (var kv in p.Bonus) if (kv.Value > 0) total.Add(Gear.StatText(kv.Key, kv.Value));
            Ui.Label(g, total.Count > 0 ? "總加成  " + string.Join("  ·  ", total) : "尚無加成", 13, Palette.Muted).Sized(20);
            var slots = Ui.HStack(g, 8).Sized(86);
            foreach (var slot in Gear.Slots)
            {
                var item = p.Equipment.TryGetValue(slot, out var id) ? p.Inventory.Find(i => i.Id == id) : null;
                var cell = Ui.Box(slots, new Color(0, 0, 0, 0.3f), "Slot");
                if (item != null)
                {
                    var edge = Ui.Box(cell.transform, item.Color, "Edge").rectTransform;
                    edge.anchorMin = new Vector2(0, 1); edge.anchorMax = new Vector2(1, 1); edge.pivot = new Vector2(0.5f, 1);
                    edge.offsetMin = new Vector2(0, -2); edge.offsetMax = Vector2.zero;
                }
                Ui.Label(cell.transform, Gear.SlotNames[slot], 12, Palette.Muted).rectTransform.Place(new Vector2(0, 1), new Vector2(10, -8), new Vector2(150, 16), new Vector2(0, 1));
                Ui.Label(cell.transform, item != null ? Gear.RarityNames[item.Rarity] : "未裝備", 16, item != null ? item.Color : Palette.Line, TextAnchor.UpperLeft, true)
                    .rectTransform.Place(new Vector2(0, 1), new Vector2(10, -28), new Vector2(150, 22), new Vector2(0, 1));
                if (item != null)
                {
                    var stat = Ui.Label(cell.transform, item.StatText().Replace("  ·  ", "\n"), 10, Palette.Hex("c9c5bb")).rectTransform;
                    stat.anchorMin = new Vector2(0, 0); stat.anchorMax = new Vector2(1, 1);
                    stat.offsetMin = new Vector2(10, 4); stat.offsetMax = new Vector2(-4, -52);
                }
            }
            Ui.Label(g, $"背包  {p.Inventory.Count} 件    點選裝備可穿上 / 卸下", 13, Palette.Muted).Sized(20);
            var listHost = Ui.Rect(g, "Inventory");
            listHost.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var list = Widgets.ScrollList(listHost, out _);
            list.parent.GetComponent<RectTransform>().Fill();
            var sorted = new List<GearItem>(p.Inventory);
            sorted.Sort((a, b) => b.Rarity != a.Rarity ? b.Rarity.CompareTo(a.Rarity) : string.CompareOrdinal(a.Slot, b.Slot));
            foreach (var item in sorted)
            {
                bool worn = p.Equipment.TryGetValue(item.Slot, out var wid) && wid == item.Id;
                var row = Ui.Rect(list, "Item").Sized(44);
                var hit = Ui.Button(row, "", () => CharacterRequest("/api/gear/equip", new Dictionary<string, object> { ["item"] = item.Id }), 14, null,
                    worn ? new Color(item.Color.r, item.Color.g, item.Color.b, 0.1f) : new Color(0, 0, 0, 0), worn ? item.Color : new Color(0, 0, 0, 0));
                hit.GetComponent<RectTransform>().Fill().offsetMax = new Vector2(-70, 0);
                Ui.Label(hit.transform, item.Name + (worn ? "  · 已裝備" : ""), 15, item.Color, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(10, -4), new Vector2(260, 20), new Vector2(0, 1));
                Ui.Label(hit.transform, item.StatText(), 12, Palette.Muted).rectTransform.Place(new Vector2(0, 1), new Vector2(10, -24), new Vector2(300, 16), new Vector2(0, 1));
                if (!worn)
                {
                    var scrap = Ui.Button(row, "分解", () => CharacterRequest("/api/gear/salvage", new Dictionary<string, object> { ["item"] = item.Id }), 12, Palette.Muted);
                    scrap.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(62, 34), new Vector2(1, 0.5f));
                }
            }

        }

        // ------------------------------------------------------------ 商城 (supply depot)

        public void ShowShop()
        {
            var r = Begin("shop");
            characterRoot = r;
            var p = app.Profile;
            var pad = Page(r, "shop");
            pad.offsetMin = new Vector2(48, 36); pad.offsetMax = new Vector2(-48, -NavHeight - 28);
            Ui.Label(pad, "補給站", 34, Palette.Bone, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), Vector2.zero, new Vector2(400, 44), new Vector2(0, 1));
            Ui.Label(pad, "使用鑰匙開啟高科技補給艙。裝備與武器分開抽取，鑰匙目前由管理員發放。", 14, Palette.Muted).rectTransform.Place(new Vector2(0, 1), new Vector2(0, -46), new Vector2(800, 20), new Vector2(0, 1));
            characterStatus = Ui.Label(pad, "", 14, Palette.Amber, TextAnchor.UpperRight);
            characterStatus.rectTransform.Place(new Vector2(1, 1), new Vector2(0, -10), new Vector2(520, 20), new Vector2(1, 1));
            SupplyPanel(pad, 0f, 0.5f, "gear");
            SupplyPanel(pad, 0.5f, 1f, "weapon");
        }

        void SupplyPanel(RectTransform pad, float x0, float x1, string pool)
        {
            var p = app.Profile;
            bool weaponPool = pool == "weapon";
            Color accent = weaponPool ? Palette.Danger : Palette.Hex("b07cff");
            var box = Ui.Box(pad, Palette.Sheet, pool).rectTransform;
            box.anchorMin = new Vector2(x0, 0); box.anchorMax = new Vector2(x1, 1);
            box.offsetMin = new Vector2(x0 > 0 ? 10 : 0, 0); box.offsetMax = new Vector2(x1 < 1 ? -10 : 0, -84);
            TopRule(box, accent);
            CratePreview(box, accent);
            var s = Ui.VStack(box, 10);
            s.Fill();
            s.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(30, 30, 26, 24);
            Caps(s, weaponPool ? "WEAPON SUPPLY" : "GEAR SUPPLY", accent).Sized(16);
            Ui.Label(s, weaponPool ? "武器補給" : "裝備補給", 30, Palette.Bone, TextAnchor.UpperLeft, true).Sized(40);
            var desc = Ui.Rect(s, "Desc").Sized(40);
            var descText = Ui.Label(desc, weaponPool ? "抽到新武器直接解鎖；同武器更高稀有度會升級，重複則轉為金幣。" : "隨機獲得護甲、槍械模組、戰術靴或戰術晶片。稀世以上附帶副屬性。", 13, Palette.Muted).rectTransform;
            descText.Fill(); descText.offsetMax = new Vector2(-230, 0);
            int keys = weaponPool ? p.WeaponKeys : p.Keys;
            var keyRow = Ui.Rect(s, "Keys").Sized(58);
            Ui.Label(keyRow, weaponPool ? "持有武器鑰匙" : "持有裝備鑰匙", 12, Palette.Muted).rectTransform.Place(new Vector2(0, 1), Vector2.zero, new Vector2(160, 18), new Vector2(0, 1));
            Ui.Label(keyRow, keys.ToString(), 36, accent, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(0, -16), new Vector2(160, 42), new Vector2(0, 1));
            var odds = Ui.HStack(s, 4).Sized(20);
            int[] weights = { 50, 30, 14, 5, 1 };
            for (int i = 0; i < Gear.RarityNames.Length; i++) Ui.Label(odds, $"{Gear.RarityNames[i]} {weights[i]}%", 12, Gear.RarityColors[i]);
            var draws = Ui.HStack(s, 10).Sized(54);
            var one = PrimaryButton(draws, "抽 1 次", () => { drawPool = pool; Draw(1); }, 17);
            one.interactable = keys >= 1;
            var ten = Ui.Button(draws, "抽 10 次", () => { drawPool = pool; Draw(10); }, 17, Palette.Amber);
            ten.interactable = keys >= 10;
            bool mine = drawPool == pool && lastDraw.Count > 0;
            Caps(s, mine ? "本次獲得" : "最近獲得").Sized(20);
            if (!mine) { Ui.Label(s, "尚無紀錄", 13, Palette.Line).Sized(22); return; }
            foreach (var item in lastDraw)
            {
                var row = Ui.Rect(s, "Drop").Sized(24);
                Ui.Label(row, item.Title != null ? $"{Gear.RarityNames[item.Rarity]} {item.Title}" : item.Name, 14, item.Color, TextAnchor.MiddleLeft, true).rectTransform.Fill();
                Ui.Label(row, item.Detail ?? item.StatText(), 11, Palette.Muted, TextAnchor.MiddleRight).rectTransform.Fill();
            }
        }

        /// Static version of the supply crate, drawn in each panel's corner.
        static void CratePreview(RectTransform box, Color accent)
        {
            var crate = Ui.Rect(box, "CratePreview").Place(new Vector2(1, 1), new Vector2(-34, -30), new Vector2(190, 120), new Vector2(1, 1));
            void P(Vector2 at, Vector2 size, Color c) => Ui.Box(crate, c, "P").rectTransform.Place(new Vector2(0.5f, 0.5f), at, size);
            P(new Vector2(0, -8), new Vector2(190, 92), Palette.Hex("1c1f24"));
            P(new Vector2(0, -8), new Vector2(178, 80), Palette.Hex("262a30"));
            P(new Vector2(0, 46), new Vector2(194, 22), Palette.Hex("2f343b"));
            P(new Vector2(0, 46), new Vector2(3, 22), Palette.Hex("15171a"));
            P(new Vector2(0, -10), new Vector2(150, 9), accent);
            P(new Vector2(0, -10), new Vector2(160, 20), new Color(accent.r, accent.g, accent.b, 0.2f));
            for (int i = -2; i <= 2; i++) P(new Vector2(i * 28, -36), new Vector2(18, 4), Palette.Hex("3a3f46"));
            foreach (var (x, y) in new[] { (-1, 1), (1, 1), (-1, -1), (1, -1) })
            {
                P(new Vector2(x * 88, y * 38 - 8), new Vector2(14, 3), Palette.Amber);
                P(new Vector2(x * 94, y * 32 - 8), new Vector2(3, 14), Palette.Amber);
            }
        }

        void Draw(int count)
        {
            if (characterStatus != null) { characterStatus.color = Palette.Amber; characterStatus.text = "開啟補給中…"; }
            bool weapons = drawPool == "weapon";
            app.Api.Post(weapons ? "/api/gacha/weapon" : "/api/gacha/draw", new Dictionary<string, object> { ["count"] = count }, result =>
            {
                if (Mode != "shop") return;
                if (!result.Bool("ok")) { characterStatus.color = Palette.Danger; characterStatus.text = result.Str("error", "抽獎失敗"); return; }
                lastDraw = new List<GearItem>();
                if (weapons)
                {
                    foreach (var o in result.Arr("weapons") ?? new List<object>())
                    {
                        if (!(o is Dictionary<string, object> d)) continue;
                        string id = d.Str("weapon"), outcome = d.Str("outcome");
                        lastDraw.Add(new GearItem
                        {
                            Slot = "weapon", Rarity = Mathf.Clamp(d.Int("rarity"), 0, Gear.RarityNames.Length - 1),
                            Title = Defs.Weapons.TryGetValue(id, out var wd) ? wd.Name : id,
                            Detail = outcome == "new" ? "新武器！已解鎖" : outcome == "upgrade" ? "稀有度提升！" : $"重複 · 轉為 {d.Int("gold")} G",
                        });
                    }
                }
                else foreach (var o in result.Arr("items") ?? new List<object>()) if (o is Dictionary<string, object> d) lastDraw.Add(GearItem.From(d));
                app.UpdateProfile(Profile.From(result.Obj("profile")));
                ShowShop();
                int best = 0;
                foreach (var i in lastDraw) best = Mathf.Max(best, i.Rarity);
                characterStatus.color = Gear.RarityColors[best];
                characterStatus.text = $"獲得 {lastDraw.Count} 件裝備，最高 {Gear.RarityNames[best]}";
                app.StartCoroutine(ChestReveal(new List<GearItem>(lastDraw), best));
            });
        }

        /// High-tech supply crate: charges up while its core cycles through rarities, bursts open on the best
        /// rarity drawn, then deals the items out as cards. Click anywhere to skip or close.
        public void PlayChestDemo(List<GearItem> items, int best) => app.StartCoroutine(ChestReveal(items, best));

        System.Collections.IEnumerator ChestReveal(List<GearItem> items, int best)
        {
            if (characterRoot == null || items.Count == 0) yield break;
            var sfx = app.Client.Sfx;
            Color bestColor = Gear.RarityColors[best];
            var overlay = Ui.Box(characterRoot, new Color(0.02f, 0.02f, 0.03f, 0f), "ChestOverlay").rectTransform;
            overlay.Fill();
            bool skip = false, done = false;
            var click = overlay.gameObject.AddComponent<Button>();
            click.transition = Selectable.Transition.None;
            click.onClick.AddListener(() => { if (done) UnityEngine.Object.Destroy(overlay.gameObject); else skip = true; });

            // Light rays behind the crate (hidden until it opens).
            var rays = Ui.Rect(overlay, "Rays");
            rays.Place(new Vector2(0.5f, 0.56f), Vector2.zero, Vector2.zero);
            var rayImages = new List<Image>();
            for (int i = 0; i < 12; i++)
            {
                var ray = Ui.Box(rays, new Color(bestColor.r, bestColor.g, bestColor.b, 0f), "Ray");
                ray.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(i % 2 == 0 ? 26 : 12, 900), new Vector2(0.5f, 0f));
                ray.rectTransform.localRotation = Quaternion.Euler(0, 0, i * 30f);
                rayImages.Add(ray);
            }

            // The crate: armoured body, glowing core band, corner brackets, status lights and a split lid.
            var crate = Ui.Rect(overlay, "Crate");
            crate.Place(new Vector2(0.5f, 0.56f), Vector2.zero, new Vector2(300, 190));
            Image Panel(Transform parent, Vector2 at, Vector2 size, Color c)
            {
                var img = Ui.Box(parent, c, "Panel");
                img.rectTransform.Place(new Vector2(0.5f, 0.5f), at, size);
                return img;
            }
            Panel(crate, new Vector2(0, -6), new Vector2(300, 150), Palette.Hex("1c1f24"));
            Panel(crate, new Vector2(0, -6), new Vector2(284, 134), Palette.Hex("262a30"));
            var core = Panel(crate, new Vector2(0, -10), new Vector2(240, 14), Gear.RarityColors[0]);
            var coreGlow = Panel(crate, new Vector2(0, -10), new Vector2(256, 30), new Color(1, 1, 1, 0.15f));
            for (int i = -2; i <= 2; i++) Panel(crate, new Vector2(i * 46, -52), new Vector2(30, 6), Palette.Hex("3a3f46"));
            var lights = new List<Image>();
            for (int i = 0; i < 4; i++) lights.Add(Panel(crate, new Vector2(-120 + i * 14, 50), new Vector2(8, 8), Palette.Line));
            foreach (var (x, y) in new[] { (-1, 1), (1, 1), (-1, -1), (1, -1) })
            {
                Panel(crate, new Vector2(x * 142, y * 60 - 6), new Vector2(20, 4), Palette.Amber);
                Panel(crate, new Vector2(x * 150, y * 52 - 6), new Vector2(4, 20), Palette.Amber);
            }
            var lidL = Panel(crate, new Vector2(-76, 80), new Vector2(152, 34), Palette.Hex("2f343b")).rectTransform;
            var lidR = Panel(crate, new Vector2(76, 80), new Vector2(152, 34), Palette.Hex("2f343b")).rectTransform;
            foreach (var lid in new[] { lidL, lidR })
            {
                var seam = Ui.Box(lid, Palette.Hex("15171a"), "Seam").rectTransform;
                seam.Place(new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(130, 3));
            }
            var caption = Ui.Label(overlay, "正在解鎖補給艙…", 18, Palette.Muted, TextAnchor.MiddleCenter);
            caption.rectTransform.Place(new Vector2(0.5f, 0.56f), new Vector2(0, -150), new Vector2(600, 30));
            var flash = Ui.Box(overlay, new Color(bestColor.r, bestColor.g, bestColor.b, 0f), "Flash");
            flash.rectTransform.Fill();
            flash.raycastTarget = false;

            // Phase 1: fade in and charge up.
            float t = 0f, charge = 1.6f;
            int lastCycle = -1;
            while (t < charge && !skip)
            {
                t += Time.unscaledDeltaTime;
                float k = t / charge;
                overlay.GetComponent<Image>().color = new Color(0.02f, 0.02f, 0.03f, Mathf.Min(0.9f, t * 2f));
                crate.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, Mathf.SmoothStep(0, 1, Mathf.Min(1, t * 2.5f)));
                float shake = k * k * 9f;
                crate.anchoredPosition = new Vector2(UnityEngine.Random.Range(-shake, shake), UnityEngine.Random.Range(-shake, shake) * 0.5f);
                // Core cycles through rarities, settling on the best one drawn in the last third.
                int cycle = k < 0.66f ? (int)(t * 9f) % (best + 1) : best;
                if (cycle != lastCycle) { lastCycle = cycle; core.color = Gear.RarityColors[cycle]; if (k < 0.66f) sfx.Play("reload_out", 0.25f); }
                coreGlow.color = new Color(core.color.r, core.color.g, core.color.b, 0.15f + 0.25f * Mathf.Abs(Mathf.Sin(t * 12f)));
                for (int i = 0; i < lights.Count; i++) lights[i].color = k > (i + 1) / 5f ? Palette.Amber : Palette.Line;
                yield return null;
            }

            // Phase 2: burst open.
            core.color = bestColor;
            crate.anchoredPosition = Vector2.zero;
            crate.localScale = Vector3.one;
            overlay.GetComponent<Image>().color = new Color(0.02f, 0.02f, 0.03f, 0.9f);
            sfx.Play("explosion", 0.5f);
            if (best >= 3) sfx.Play("head", 0.8f);
            caption.text = $"{Gear.RarityNames[best]}補給";
            caption.color = bestColor;
            caption.fontSize = 26;
            t = 0f;
            while (t < 0.6f)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / 0.35f);
                lidL.anchoredPosition = new Vector2(-76 - 150 * k, 80 + 40 * k);
                lidL.localRotation = Quaternion.Euler(0, 0, 25 * k);
                lidR.anchoredPosition = new Vector2(76 + 150 * k, 80 + 40 * k);
                lidR.localRotation = Quaternion.Euler(0, 0, -25 * k);
                flash.color = new Color(bestColor.r, bestColor.g, bestColor.b, 0.55f * (1f - t / 0.6f));
                foreach (var ray in rayImages) ray.color = new Color(bestColor.r, bestColor.g, bestColor.b, 0.18f * k);
                yield return null;
            }
            flash.color = new Color(0, 0, 0, 0);

            // Phase 3: deal the cards out while the rays keep turning.
            crate.localScale = Vector3.one * 0.7f;
            crate.anchoredPosition = new Vector2(0, 150);
            rays.anchoredPosition = new Vector2(0, 150);
            caption.rectTransform.anchoredPosition = new Vector2(0, 40);
            int perRow = Mathf.Min(5, items.Count);
            var cards = new List<RectTransform>();
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                int row = i / perRow, col = i % perRow;
                int inRow = Mathf.Min(perRow, items.Count - row * perRow);
                var card = Ui.Box(overlay, Palette.Hex("17191d"), "Card").rectTransform;
                card.Place(new Vector2(0.5f, 0.5f), new Vector2((col - (inRow - 1) / 2f) * 176, -40 - row * 150), new Vector2(164, 136));
                var edge = Ui.Box(card, item.Color, "Edge").rectTransform;
                edge.anchorMin = new Vector2(0, 1); edge.anchorMax = new Vector2(1, 1); edge.pivot = new Vector2(0.5f, 1);
                edge.offsetMin = new Vector2(0, -4); edge.offsetMax = Vector2.zero;
                var glow = Ui.Box(card, new Color(item.Color.r, item.Color.g, item.Color.b, 0.08f), "Glow").rectTransform;
                glow.Fill();
                Ui.Label(card, Gear.RarityNames[item.Rarity], 13, item.Color, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(12, -14), new Vector2(140, 18), new Vector2(0, 1));
                Ui.Label(card, item.Title ?? Gear.SlotNames[item.Slot], item.Title != null ? 16 : 20, Palette.Bone, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(12, -36), new Vector2(140, 26), new Vector2(0, 1));
                var stats = Ui.Label(card, item.Detail ?? item.StatText().Replace("  ·  ", "\n"), 13, Palette.Hex("c9c5bb")).rectTransform;
                stats.Place(new Vector2(0, 1), new Vector2(12, -72), new Vector2(140, 50), new Vector2(0, 1));
                card.localScale = Vector3.zero;
                cards.Add(card);
            }
            var hint = Ui.Label(overlay, "點擊任意處繼續", 14, Palette.Muted, TextAnchor.MiddleCenter);
            hint.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(400, 24));
            done = true;
            t = 0f;
            while (overlay != null)
            {
                t += Time.unscaledDeltaTime;
                rays.localRotation = Quaternion.Euler(0, 0, t * 20f);
                for (int i = 0; i < cards.Count; i++)
                {
                    float k = Mathf.Clamp01((t - i * (skip ? 0.02f : 0.12f)) / 0.22f);
                    float pop = k < 1 ? Mathf.Sin(k * Mathf.PI * 0.5f) * (1f + 0.15f * Mathf.Sin(k * Mathf.PI)) : 1f;
                    if (k > 0 && cards[i].localScale.x == 0) sfx.Play(items[i].Rarity >= 3 ? "head" : "hit", 0.3f);
                    cards[i].localScale = Vector3.one * pop;
                }
                yield return null;
            }
        }

        void CharacterRequest(string path, Dictionary<string, object> data)
        {
            if (characterStatus != null) { characterStatus.color = Palette.Amber; characterStatus.text = "處理中…"; }
            app.Api.Post(path, data, result =>
            {
                if (Mode != "character") return;
                if (result.Bool("ok")) { app.UpdateProfile(Profile.From(result.Obj("profile"))); ShowCharacter(); characterStatus.text = "已更新"; }
                else { characterStatus.color = Palette.Danger; characterStatus.text = result.Str("error", "操作失敗"); }
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
            var keysInput = Widgets.Input(row, "鑰匙數量", false, 4);
            keysInput.contentType = InputField.ContentType.IntegerNumber;
            keysInput.text = "0";
            var weaponKeysInput = Widgets.Input(row, "武器鑰匙", false, 4);
            weaponKeysInput.contentType = InputField.ContentType.IntegerNumber;
            weaponKeysInput.text = "0";
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
                app.Api.Post("/api/admin/grant", new Dictionary<string, object> { ["username"] = who, ["gold"] = gold, ["keys"] = int.TryParse(keysInput.text, out var keyCount) ? keyCount : 0, ["weapon_keys"] = int.TryParse(weaponKeysInput.text, out var weaponKeyCount) ? weaponKeyCount : 0, ["weapon"] = weaponPick == 0 ? "" : Defs.WeaponOrder[weaponPick] }, result =>
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
