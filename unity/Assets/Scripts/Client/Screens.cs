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
        public RectTransform Root => root;
        public string Mode { get; private set; } = "";

        public Screens(AppRoot app)
        {
            this.app = app;
            canvas = Ui.Canvas("Screens", 5);
            ChatLog.Added += OnChatLine;
            FriendPresence.Updated += RepaintPresence;
        }

        RectTransform Begin(string mode, bool backdrop = true)
        {
            Clear();
            Mode = mode;
            root = Ui.Rect(canvas.transform, mode).Fill();
            if (backdrop) Ui.Box(root, new Color(0.063f, 0.067f, 0.075f, 0.94f), "Backdrop").rectTransform.Fill();
            Hints.Maybe(root, mode);
            GameClient.LockCursor(false);
            return root;
        }

        public void Clear()
        {
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            root = null;
            Ui.DefaultButton = null; Ui.DefaultWhileTyping = false;
            Mode = "";
        }

        static void TopRule(Transform parent, Color color)
        {
            var rule = Ui.Box(parent, color, "Rule").rectTransform;
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1); rule.pivot = new Vector2(0.5f, 1);
            rule.offsetMin = new Vector2(0, -2); rule.offsetMax = Vector2.zero;
        }

        const float NavHeight = 72f;
        /// Capture only: draw the 多人房間 section without a server connection.
        public static bool PreviewRoom;

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
            Ui.Label(bar, "RIFTWARD  " + Defs.Version + (string.IsNullOrEmpty(app.Api.Channel) ? "" : "  ·  " + app.Api.Channel), 11, app.Api.Channel == "測試服" ? Palette.Hex("6fd3ff") : Palette.Amber).rectTransform.Place(new Vector2(0, 0.5f), new Vector2(40, 14), new Vector2(200, 16), new Vector2(0, 0.5f));
            Ui.Label(bar, "裂隙守衛", 24, Palette.Bone, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 0.5f), new Vector2(40, -6), new Vector2(200, 32), new Vector2(0, 0.5f));

            string[] ids = { "missions", "daily", "social", "shop", "gear" };
            string[] names = { "關卡", "任務", "社交", "商城", "裝備" };
            Action[] go = { app.ShowHome, ShowDaily, () => ShowFriends(), ShowShop, ShowCharacter };
            for (int i = 0; i < ids.Length; i++)
            {
                bool active = ids[i] == tab;
                var b = Ui.Button(bar, names[i], go[i], 19, active ? Palette.Bone : Palette.Muted, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0));
                b.GetComponent<RectTransform>().Place(new Vector2(0, 0.5f), new Vector2(196 + i * 74, 0), new Vector2(72, NavHeight), new Vector2(0, 0.5f));
                if (active)
                {
                    b.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;
                    Ui.Box(b.transform, Palette.Amber, "Underline").rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 1), new Vector2(40, 3), new Vector2(0.5f, 0));
                }
                if (ids[i] == "gear" && p.TalentPoints > 0 || ids[i] == "daily" && p.Daily.Claimable + p.AchievementsClaimable > 0
                    || ids[i] == "social" && p.FriendRequests + p.MailUnread + p.MailClaimable + (app.PendingInvite != null ? 1 : 0) > 0)
                    Ui.Box(b.transform, Palette.Amber, "Badge").rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(30, 12), new Vector2(7, 7));
            }

            // Account block on the far right, resource chips to its left.
            var account = Ui.Rect(bar, "Account").Place(new Vector2(1, 0.5f), new Vector2(-40, 0), new Vector2(210, 52), new Vector2(1, 0.5f));
            // Title + name + level share about 225 px before the resource chips: shrink the text, then shorten the
            // name with an ellipsis, rather than run into the chips.
            const float NameRoom = 225f;
            var who = Ui.Label(account, $"{p.Username}  ·  Lv {p.Level}", 15, Palette.Bone, TextAnchor.UpperRight, true);
            who.rectTransform.Place(new Vector2(1, 1), Vector2.zero, new Vector2(NameRoom, 22), new Vector2(1, 1));
            Text worn = null;
            if (!string.IsNullOrEmpty(p.Title))
                worn = Ui.Label(account, $"〔{p.Title}〕", 15, Palette.Amber, TextAnchor.UpperRight, true);
            float Total() => who.preferredWidth + (worn != null ? worn.preferredWidth + 2 : 0);
            if (Total() > NameRoom) { who.fontSize = 13; if (worn != null) worn.fontSize = 13; }
            string shortName = p.Username;
            while (Total() > NameRoom && shortName.Length > 3)
            {
                shortName = shortName.Substring(0, shortName.Length - 1);
                who.text = $"{shortName}…  ·  Lv {p.Level}";
            }
            if (worn != null)
                worn.rectTransform.Place(new Vector2(1, 1), new Vector2(-who.preferredWidth - 2, 0), new Vector2(160, 22), new Vector2(1, 1));
            var links = Ui.HStack(account, 10);
            links.Place(new Vector2(1, 0), Vector2.zero, new Vector2(p.Admin ? 270 : 206, 26), new Vector2(1, 0));
            if (p.Admin) Ui.Button(links, "管理員", ShowAdmin, 13, Palette.Danger, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0));
            Ui.Button(links, "設定", ShowSettings, 13, Palette.Amber, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0));
            Ui.Button(links, "登出", app.Logout, 13, Palette.Muted, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0));
            // Leaving the game asks once: the first click arms it, the second quits (resets after 3 s).
            Button quit = null;
            float armedAt = -9f;
            quit = Ui.Button(links, "離開遊戲", () =>
            {
                if (Time.unscaledTime - armedAt > 3f) { armedAt = Time.unscaledTime; quit.SetCaption("確定離開？"); quit.GetComponentInChildren<Text>().color = Palette.Danger; return; }
                app.Quit();
            }, 13, Palette.Muted, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0));
            quit.gameObject.AddComponent<RevertCaption>().Arm(() => Time.unscaledTime - armedAt > 3f, "離開遊戲", Palette.Muted);
            var chips = new[] { ("金幣", $"{p.Gold}", Palette.Amber), ("裂隙碎片", p.Shards.ToString(), Gear.ShardColor), ("裝備鑰匙", p.Keys.ToString(), Gear.GearKeyColor), ("武器鑰匙", p.WeaponKeys.ToString(), Gear.WeaponKeyColor) };
            for (int i = 0; i < chips.Length; i++)
            {
                var (label, value, color) = chips[chips.Length - 1 - i];
                // Four currencies: tight chips so the row clears the tabs at 1280 wide.
                var chip = Ui.Rect(bar, "Chip").Place(new Vector2(1, 0.5f), new Vector2(-300 - i * 94, 0), new Vector2(88, 46), new Vector2(1, 0.5f));
                Ui.Box(chip, color, "Mark").rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(2, 34), new Vector2(0, 0.5f));
                Ui.Label(chip, label, 11, Palette.Muted).rectTransform.Place(new Vector2(0, 1), new Vector2(12, -2), new Vector2(76, 16), new Vector2(0, 1));
                var icon = i == chips.Length - 1 ? Icons.Coin(chip, 22) : i == chips.Length - 2 ? Icons.Shard(chip, 22) : Icons.Key(chip, 22, color);
                icon.rectTransform.Place(new Vector2(0, 1), new Vector2(10, -20), new Vector2(22, 22), new Vector2(0, 1));
                var amount = Ui.Label(chip, value, value.Length > 5 ? 16 : 19, color, TextAnchor.UpperLeft, true);
                amount.rectTransform.Place(new Vector2(0, 1), new Vector2(34, value.Length > 5 ? -20 : -18), new Vector2(56, 26), new Vector2(0, 1));
            }

            var body = Ui.Rect(r, "Body").Fill();
            body.offsetMax = new Vector2(0, -NavHeight);
            return body;
        }

        /// Sub-navigation inside the 裝備 tab.
        void GearTabs(RectTransform pad, string active)
        {
            SubTabs(pad, new[] { "天賦與裝備", "武器" }, new Action[] { ShowCharacter, ShowArmory }, active == "character" ? 0 : 1);
            var best = Ui.Button(pad, "一鍵裝備最強武器與裝備", EquipBest, 15, Palette.Ink, Palette.Amber, Palette.Amber);
            best.GetComponent<RectTransform>().Place(new Vector2(0, 1), new Vector2(156 * 2 + 16, 0), new Vector2(240, 40), new Vector2(0, 1));
            best.interactable = !equippingBest;
        }

        bool equippingBest;

        /// Sustained damage per second of one weapon copy: what "strongest" means for 一鍵裝備.
        static float WeaponScore(string id, int rarity, Dictionary<string, int> levels)
        {
            if (!Defs.Weapons.TryGetValue(id, out var def)) return 0;
            int Lv(string k) => levels != null && levels.TryGetValue(k, out var v) ? Mathf.Clamp(v, 0, Defs.MaxUpgradeLevel) : 0;
            var st = def.StatsAt(Lv("damage"), Lv("mag"), Lv("reload"), Lv("over"));
            float hit = st.Damage * Mathf.Max(1, st.Pellets) * Defs.WeaponRarityMult[Mathf.Clamp(rarity, 0, Defs.WeaponRarityMult.Length - 1)];
            float cycle = def.Melee ? st.Interval : st.Interval + st.Reload / Mathf.Max(1, st.Mag);
            return hit / Mathf.Max(0.05f, cycle);
        }

        /// 一鍵裝備: the strongest copy of each owned weapon, the two strongest weapons equipped, and the best item
        /// (rarity, then enhancement, then total stats) in every gear slot. Sends only the changes, one after another.
        void EquipBest()
        {
            var p = app.Profile;
            if (p == null || equippingBest) return;
            var lo = p.Loadout;
            var steps = new List<(string path, Dictionary<string, object> data)>();
            var picks = new List<(string id, float score)>();
            foreach (var id in lo.Owned)
            {
                int active = lo.Active.TryGetValue(id, out var a) ? a : 0;
                int bestIndex = active;
                float bestScore = WeaponScore(id, lo.RarityOf(id), lo.Levels.TryGetValue(id, out var cur) ? cur : null);
                if (lo.Copies.TryGetValue(id, out var copies))
                    for (int i = 0; i < copies.Count; i++)
                    {
                        float s = WeaponScore(id, copies[i], lo.CopyLevel(id, i));
                        if (s > bestScore + 0.001f) { bestScore = s; bestIndex = i; }
                    }
                picks.Add((id, bestScore));
                if (bestIndex != active)
                    steps.Add(("/api/weapon/select", new Dictionary<string, object> { ["weapon"] = id, ["index"] = bestIndex, ["rarity"] = copies[bestIndex] }));
            }
            var kit = picks.OrderByDescending(x => x.score).Take(2).Select(x => x.id).ToList();
            // Only the two weapons that end up equipped need their best copy switched in.
            steps.RemoveAll(s => !kit.Contains((string)s.data["weapon"]));
            if (kit.Count > 0 && !(kit.Count == lo.Equipped.Count && kit.All(lo.Equipped.Contains)))
                steps.Add(("/api/shop/equip", new Dictionary<string, object> { ["weapons"] = kit.Cast<object>().ToList() }));
            foreach (var slot in p.Inventory.Select(i => i.Slot).Distinct().ToList())
            {
                var top = p.Inventory.Where(i => i.Slot == slot)
                    .OrderByDescending(i => i.Rarity).ThenByDescending(i => i.Level).ThenByDescending(i => i.Stats.Values.Sum()).First();
                if (!(p.Equipment.TryGetValue(slot, out var worn) && worn == top.Id))
                    steps.Add(("/api/gear/equip", new Dictionary<string, object> { ["item"] = top.Id }));
            }
            if (steps.Count == 0) { app.Overlay.Toast("已經是最強配置"); return; }
            equippingBest = true;
            string mode = Mode;
            void Next(int i)
            {
                if (i >= steps.Count)
                {
                    equippingBest = false;
                    app.Overlay.Toast("已裝備最強武器與裝備");
                    if (Mode == mode) { if (mode == "character") ShowCharacter(); else ShowArmory(); }
                    return;
                }
                app.Api.Post(steps[i].path, steps[i].data, result =>
                {
                    if (!result.Bool("ok"))
                    {
                        equippingBest = false;
                        app.Overlay.Toast(result.Str("error", "一鍵裝備失敗"));
                        if (Mode == mode) { if (mode == "character") ShowCharacter(); else ShowArmory(); }
                        return;
                    }
                    app.UpdateProfile(Profile.From(result.Obj("profile")), i == steps.Count - 1);
                    Next(i + 1);
                });
            }
            Next(0);
            if (Mode == "character") ShowCharacter(); else if (Mode == "armory") ShowArmory();
        }

        /// Sub-navigation inside the 任務 tab; the 成就 tab carries a dot while a tier waits to be claimed.
        void MissionTabs(RectTransform pad, int active)
        {
            SubTabs(pad, new[] { "每日任務", "成就" }, new Action[] { ShowDaily, ShowAchievements }, active);
            var p = app.Profile;
            int[] waiting = { p.Daily.Claimable, p.AchievementsClaimable };
            for (int i = 0; i < 2; i++)
                if (waiting[i] > 0 && i != active)
                    Ui.Box(pad, Palette.Amber, "Badge").rectTransform.Place(new Vector2(0, 1), new Vector2(i * 156 + 134, -8), new Vector2(7, 7), new Vector2(0, 1));
        }

        void SubTabs(RectTransform pad, string[] names, Action[] go, int active)
        {
            var row = Ui.Rect(pad, "SubTabs").Place(new Vector2(0, 1), Vector2.zero, new Vector2(156 * names.Length, 40), new Vector2(0, 1));
            for (int i = 0; i < names.Length; i++)
            {
                bool on = i == active;
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
                Ui.DefaultButton = submit; Ui.DefaultWhileTyping = true;   // Enter in the form submits it
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

        /// 第二章 opening, shown the first time 帝國裂隙 is cleared (and from 劇情回顧).
        public static readonly string[][] ChapterTwo =
        {
            new[] { "第二章 / 帝國母星", "穿過裂隙的瞬間，空氣變得冰冷刺骨。\n眼前是赫利奧帝國的母星：一座被冰雪與鋼鐵覆蓋的世界。\n你們是第一批踏上這片土地的地球士兵。" },
            new[] { "帝國的反擊", "北境的霜冕要塞擋住了南下的道路，\n高空船塢裡，進攻地球的艦隊正在集結。\n而在一切的盡頭，赫利奧皇帝在王座上等著你。" },
            new[] { "新的敵人", "帝國近衛兵手持能量盾，正面幾乎刀槍不入。\n他們全身發出紅光時，代表即將衝撞，立刻閃開。\n繞到側面或瞄準頭部，才能擊倒他們。" },
        };

        /// Ending, shown the first time the Emperor falls.
        public static readonly string[][] Ending =
        {
            new[] { "王座崩塌", "赫利奧皇帝倒在他的王座前，\n那道籠罩帝國千年的金色光芒，第一次熄滅了。" },
            new[] { "回家的路", "船塢裡的艦隊再也沒有起飛。\n裂隙另一端，那顆救過你的翠綠星球，終於迎來了和平。\n而地球，正等著你們凱旋。" },
            new[] { "感謝遊玩", "你完成了《裂隙守衛》的全部故事。\n挑戰更高的難度、收集傳說武器、和好友一起刷新紀錄吧。\n—— 遠征軍指揮部，向你致敬。" },
        };

        /// A sequence of story pages; done runs after the last one (or when skipped).
        public void ShowPages(string[][] pages, int page, string finish, Action done)
        {
            var r = Begin("story");
            var stack = Ui.VStack(r, 18);
            stack.Place(new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(820, 440));
            Caps(stack, $"RIFTWARD  ·  {page + 1} / {pages.Length}", Palette.Amber).Sized(18);
            Ui.Label(stack, pages[page][0], 40, Palette.Bone, TextAnchor.UpperLeft, true).Sized(54);
            Ui.Label(stack, pages[page][1], 21, Palette.Hex("d6d1c6")).Sized(150);
            var row = Ui.HStack(stack, 12).Sized(54);
            PrimaryButton(row, page < pages.Length - 1 ? "下一頁" : finish, () =>
            {
                if (page < pages.Length - 1) ShowPages(pages, page + 1, finish, done);
                else done();
            });
            if (page < pages.Length - 1) Ui.Button(row, "略過", done, 18);
        }

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

        /// The room's lineup on the home screen: each member's soldier, in their team colour and holding the gun they
        /// will deploy with, so joining a party visibly puts you in it. Empty seats stay as outlines.
        void PartyRow(RectTransform body, float top, float height)
        {
            var members = PreviewRoom
                ? new List<AppRoot.PartyMember>
                {
                    new AppRoot.PartyMember { Id = app.Net.LocalId, Name = app.Profile?.Username ?? "你", Weapon = app.Profile?.Loadout.Equipped.FirstOrDefault() ?? "rifle", Rarity = 3 },
                    new AppRoot.PartyMember { Id = 11, Name = "NightOwl", Weapon = "sniper", Rarity = 4 },
                    new AppRoot.PartyMember { Id = 12, Name = "Kairo_77", Weapon = "saber", Rarity = 2 },
                }
                : app.PartyMembers;
            int leader = PreviewRoom ? app.Net.LocalId : app.LeaderId;
            var panel = Ui.Box(body, new Color(0.063f, 0.067f, 0.075f, 0.92f), "Party").rectTransform;
            panel.Place(new Vector2(1, 1), new Vector2(-40, -top), new Vector2(520, height), new Vector2(1, 1));
            Caps(panel, $"隊伍 · {members.Count} / 4").rectTransform.Place(new Vector2(0, 1), new Vector2(20, -10), new Vector2(300, 16), new Vector2(0, 1));
            var row = Ui.HStack(panel, 10);
            row.Place(new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(480, height - 42), new Vector2(0.5f, 0));
            float modelSize = height - 42 - 34;
            for (int seat = 0; seat < 4; seat++)
            {
                var cell = Ui.Box(row, new Color(1, 1, 1, 0.03f), "Seat").rectTransform;
                var line = cell.gameObject.AddComponent<Outline>();
                line.effectDistance = new Vector2(1, -1);
                if (seat >= members.Count)
                {
                    line.effectColor = Palette.Line;
                    Ui.Label(cell, "空位", 13, Palette.Hex("6a6d71"), TextAnchor.MiddleCenter).rectTransform.Fill();
                    continue;
                }
                var m = members[seat];
                bool me = m.Id == app.Net.LocalId;
                Color color = me ? Palette.Player : Art.TeamColors[Mathf.Abs(m.Id) % Art.TeamColors.Length];
                line.effectColor = me ? Palette.Amber : new Color(color.r, color.g, color.b, 0.6f);
                string weapon = string.IsNullOrEmpty(m.Weapon) ? "rifle" : m.Weapon;
                int rarity = m.Rarity;
                var model = ModelPreview.Create(cell, t =>
                {
                    var soldier = Models.Soldier(color);
                    soldier.transform.SetParent(t, false);
                    soldier.GetComponent<CharacterRig>()?.SetWeapon(weapon, rarity);
                }, 192, 30f, 6f);
                model.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(modelSize, modelSize), new Vector2(0.5f, 1));
                var name = Ui.Label(cell, m.Name, 13, me ? Palette.Amber : Palette.Bone, TextAnchor.MiddleCenter, me);
                name.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(108, 18), new Vector2(0.5f, 0));
                name.resizeTextForBestFit = true; name.resizeTextMinSize = 9; name.resizeTextMaxSize = 13;
                if (m.Id == leader || me)
                {
                    var tag = Ui.Label(cell, m.Id == leader ? (me ? "隊長 · 你" : "隊長") : "你", 11, m.Id == leader ? Palette.Amber : Palette.Muted, TextAnchor.UpperCenter, true);
                    tag.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -3), new Vector2(108, 14), new Vector2(0.5f, 1));
                }
            }
        }

        public void ShowHome(string status)
        {
            var r = Begin("home", false);
            var p = app.Profile;
            int selected = Mathf.Clamp(app.SelectedRegion, 0, Mathf.Max(0, p.Unlocked));
            var region = Defs.Regions[selected];
            int tierIndex = Mathf.Clamp(app.SelectedDifficulty, 0, Defs.Difficulties.Length - 1);
            var tier = Defs.Difficulty(tierIndex);

            var body = Page(r, "missions");
            var column = Ui.Box(body, new Color(0.063f, 0.067f, 0.075f, 0.96f), "Column").rectTransform;
            column.anchorMin = new Vector2(0, 0); column.anchorMax = new Vector2(0, 1); column.pivot = new Vector2(0, 0.5f);
            column.sizeDelta = new Vector2(500, 0);
            var stack = Ui.VStack(column, 0);
            stack.Fill();
            stack.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(48, 48, 26, 20);
            Ui.Label(stack, "選擇關卡", 34, Palette.Bone, TextAnchor.UpperLeft, true).Sized(48);
            Ui.Rect(stack, "Gap").Sized(8);
            // Chapter tabs: a chapter opens once its first region is unlocked.
            int chapter = Defs.ChapterOf(selected);
            var chapters = Ui.HStack(stack, 8).Sized(40);
            for (int c = 0; c < Defs.Chapters.Length; c++)
            {
                int first = c * Defs.RegionsPerChapter;
                bool open = first <= p.Unlocked, on = c == chapter;
                var (number, title) = Defs.Chapters[c];
                var tab = Ui.Button(chapters, open ? $"{number} · {title}" : $"{number} · 未解鎖", () =>
                {
                    int target = Mathf.Min(p.Unlocked, first + Defs.RegionsPerChapter - 1);
                    app.SelectRegion(Mathf.Max(first, target));
                }, 15, on ? Palette.Amber : open ? Palette.Bone : Palette.Muted,
                    on ? new Color(Palette.Amber.r, Palette.Amber.g, Palette.Amber.b, 0.1f) : new Color(1, 1, 1, 0.03f), on ? Palette.Amber : Palette.Line);
                tab.interactable = open && !on;
            }
            Ui.Rect(stack, "Gap").Sized(8);
            Widgets.Section(stack, "部署區域").Sized(26);
            for (int i = chapter * Defs.RegionsPerChapter; i < Mathf.Min(Defs.Regions.Length, (chapter + 1) * Defs.RegionsPerChapter); i++)
            {
                int index = i;
                bool locked = i > p.Unlocked, current = i == selected;
                var b = Ui.Button(stack, "", () => { app.SelectRegion(index); }, 15, null, current ? new Color(Palette.Amber.r, Palette.Amber.g, Palette.Amber.b, 0.1f) : new Color(0, 0, 0, 0), current ? Palette.Amber : new Color(0, 0, 0, 0)).Sized(58);
                b.interactable = !locked;
                var t = b.GetComponentInChildren<Text>();
                t.text = "";
                var num = Ui.Label(b.transform, $"0{i + 1}", 14, current ? Palette.Amber : Palette.Muted, TextAnchor.UpperLeft);
                num.rectTransform.Place(new Vector2(0, 1), new Vector2(18, -15), new Vector2(40, 20), new Vector2(0, 1));
                var name = Ui.Label(b.transform, Defs.Regions[i].Name, 22, current ? Palette.Bone : Palette.Hex("c9c5bb"), TextAnchor.UpperLeft, current);
                name.rectTransform.Place(new Vector2(0, 1), new Vector2(64, -7), new Vector2(300, 30), new Vector2(0, 1));
                var sub = Ui.Label(b.transform, locked ? "完成前一區域後解鎖" : "20 波 · 第 10、20 波 Boss", 13, Palette.Muted, TextAnchor.UpperLeft);
                sub.rectTransform.Place(new Vector2(0, 1), new Vector2(64, -36), new Vector2(300, 20), new Vector2(0, 1));
                int best = i < p.Cleared.Count ? p.Cleared[i] : -1;
                if (!locked && best >= 0)
                {
                    var mark = Defs.Difficulty(best);
                    Ui.Label(b.transform, "已通關 " + mark.Name, 12, mark.Accent, TextAnchor.UpperRight, true).rectTransform
                        .Place(new Vector2(1, 1), new Vector2(-16, -15), new Vector2(120, 18), new Vector2(1, 1));
                }
                if (current) Ui.Box(b.transform, Palette.Amber, "Mark").rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(3, 58), new Vector2(0, 0.5f));
            }
            Ui.Rect(stack, "Gap").Sized(14);
            Widgets.Section(stack, "難度").Sized(26);
            var tiers = Ui.HStack(stack, 8).Sized(54);
            for (int t = 0; t < Defs.Difficulties.Length; t++)
            {
                int pick = t;
                var def = Defs.Difficulties[t];
                bool open = p.DifficultyOpen(selected, t), on = t == tierIndex;
                Color accent = def.Accent;
                var tb = Ui.Button(tiers, "", () => app.SelectDifficulty(pick), 15, null,
                    on ? new Color(accent.r, accent.g, accent.b, 0.14f) : new Color(1, 1, 1, open ? 0.05f : 0.02f), on ? accent : Palette.Line);
                tb.interactable = open && app.CanDeploy;
                tb.GetComponentInChildren<Text>().text = "";
                Ui.Label(tb.transform, def.Name, 18, open ? (on ? accent : Palette.Bone) : Palette.Hex("6a6d71"), TextAnchor.UpperCenter, on).rectTransform
                    .Place(new Vector2(0.5f, 1), new Vector2(0, -7), new Vector2(120, 24), new Vector2(0.5f, 1));
                Ui.Label(tb.transform, open ? def.Code : "未解鎖", 12, open ? (on ? accent : Palette.Muted) : Palette.Muted, TextAnchor.UpperCenter).rectTransform
                    .Place(new Vector2(0.5f, 1), new Vector2(0, -32), new Vector2(120, 14), new Vector2(0.5f, 1));
            }
            string tierNote = tier.Summary;
            int nextTier = tierIndex + 1;
            if (nextTier < Defs.Difficulties.Length && !p.DifficultyOpen(selected, nextTier))
                tierNote += $"在此區域通關{tier.Name}即可解鎖{Defs.Difficulty(nextTier).Name}。";
            if (app.InRoom && !app.IsLeader) tierNote = "難度由隊長選擇：" + tier.Name;
            Ui.Label(stack, tierNote, 13, Palette.Muted).Sized(36);
            // 無盡模式: opens per region once it is cleared; waves never stop, a boss every 10 waves.
            bool endlessOpen = app.EndlessOpen(selected), endless = app.SelectedEndless && endlessOpen;
            var endlessRow = Widgets.Toggle(stack, endlessOpen ? "無盡模式  ·  波次無上限，每 10 波 Boss" : "無盡模式  ·  通關此區域後開放", endless,
                on => app.SetEndless(on), "開啟", "關閉").Sized(38);
            endlessRow.interactable = endlessOpen && app.CanDeploy && (!app.InRoom || app.IsLeader);
            if (endless) endlessRow.GetComponentInChildren<Text>().color = Palette.Hex("ff6a8a");
            Ui.Rect(stack, "Gap").Sized(8);
            var deploy = PrimaryButton(stack, "", app.Deploy).Sized(56);
            string mode = endless ? " · 無盡" : "";
            deploy.SetCaption(tierIndex > 0 || endless ? $"部署          0{selected + 1} {region.Name} · {tier.Name}{mode}" : $"部署          0{selected + 1} {region.Name}");
            deploy.interactable = app.CanDeploy;
            Ui.DefaultButton = deploy;   // Enter deploys
            Ui.Rect(stack, "Gap").Sized(8);
            Ui.Button(stack, chapter == 0 ? "故事與教學" : "第二章劇情回顧", () => { if (chapter == 0) ShowStory(0); else ShowPages(ChapterTwo, 0, "返回基地", app.ShowHome); }, 14, Palette.Muted).Sized(36);
            if (app.Net.Welcomed || PreviewRoom)
            {
                Ui.Rect(stack, "Gap").Sized(10);
                Widgets.Section(stack, "多人房間").Sized(26);
                if (app.InRoom || PreviewRoom)
                {
                    var info = Ui.HStack(stack, 8).Sized(48);
                    Ui.Label(info, PreviewRoom ? "K7QM" : app.RoomCode, 30, Palette.Amber, TextAnchor.MiddleLeft, true);
                    Ui.Label(info, $"{(PreviewRoom ? 3 : app.PartySize)} / 4 名隊員\n{(PreviewRoom || app.IsLeader ? "你是隊長，把房號告訴隊友" : "等待隊長部署")}", 13, Palette.Muted, TextAnchor.MiddleLeft);
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
            var statusRow = Ui.Rect(stack, "Status").Sized(20);
            Ui.Label(statusRow, status, 13, Palette.Muted, TextAnchor.MiddleLeft).rectTransform.Fill();
            // The version notes stay one click away after the one-time pop-up.
            var notes = Ui.Button(statusRow, $"更新內容 v{Defs.Version} →", () => PatchNotes.Show(app.Overlay.Root), 13, Palette.Amber, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0));
            notes.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(170, 22), new Vector2(1, 0.5f));
            notes.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleRight;

            // Briefing sheet over the live region backdrop.
            var brief = Ui.Box(body, new Color(0.063f, 0.067f, 0.075f, 0.92f), "Briefing").rectTransform;
            // Top-aligned with the left column's title, so the two read as one layout.
            float briefHeight = endless ? 552 : 456;   // the key-hint row moved out: the HUD and Settings show controls
            brief.Place(new Vector2(1, 1), new Vector2(-40, -32), new Vector2(520, briefHeight), new Vector2(1, 1));
            // In a room, the party lineup sits under the briefing, in the same column.
            if ((app.Net.Welcomed && app.InRoom) || PreviewRoom) PartyRow(body, 32 + briefHeight + 12, endless ? 92 : 168);
            Ui.Box(brief, region.Accent, "Rule").rectTransform.Place(new Vector2(0.5f, 1), Vector2.zero, new Vector2(520, 2), new Vector2(0.5f, 1));
            var b2 = Ui.VStack(brief, 12);
            b2.Fill();
            b2.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(32, 32, 28, 26);
            Caps(b2, $"任務簡報 / 0{selected + 1}").Sized(18);
            Ui.Label(b2, region.Name, 34, region.Accent, TextAnchor.UpperLeft, true).Sized(44);
            if (endless) EndlessBoard(b2);
            else Ui.Label(b2, region.Story.Replace("\n", ""), 16, Palette.Hex("d6d1c6")).Sized(54);
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
            if (p.Missions == 0)
                Ui.Label(b2, "完成第一場任務後，這裡會顯示你的擊殺、最高波次與命中率。", 14, Palette.Muted).Sized(52);
            else
            {
            var record = Ui.HStack(b2, 10).Sized(52);
            foreach (var (label, value) in new[] { ("總擊殺", p.Kills.ToString()), ("最高波次", p.BestWave.ToString()), ("命中率", $"{p.Accuracy * 100:0}%"), ("勝場", $"{p.Victories} / {p.Missions}") })
            {
                var cell = Ui.VStack(record, 2);
                Ui.Label(cell, label, 12, Palette.Muted).Sized(18);
                Ui.Label(cell, value, 22, Palette.Bone, TextAnchor.UpperLeft, true).Sized(28);
            }
            }
        }

        /// 無盡排行榜 on the briefing sheet: top five, then your own rank. Fetched at most every 30 s, cached between redraws.
        List<(int rank, string name, int wave)> endlessTop;
        (int rank, int wave)? endlessMe;
        float endlessFetched = -99f;
        public void SetEndlessBoardForCapture(List<(int, string, int)> top, (int, int)? me) { endlessTop = top; endlessMe = me; endlessFetched = Time.unscaledTime + 9999f; }

        void EndlessBoard(Transform parent)
        {
            var box = Ui.Rect(parent, "EndlessBoard").Sized(150);
            Caps(box, "無盡排行榜").rectTransform.Place(new Vector2(0, 1), Vector2.zero, new Vector2(200, 18), new Vector2(0, 1));
            Ui.Label(box, app.Profile.EndlessBest > 0 ? $"我的最佳  第 {app.Profile.EndlessBest} 波" : "尚無紀錄", 13, Palette.Hex("ff6a8a"), TextAnchor.UpperRight).rectTransform
                .Place(new Vector2(1, 1), Vector2.zero, new Vector2(220, 18), new Vector2(1, 1));
            if (Time.unscaledTime - endlessFetched > 30f && app.Net.Welcomed)
            {
                endlessFetched = Time.unscaledTime;
                app.Api.Post("/api/endless/leaderboard", new Dictionary<string, object>(), result =>
                {
                    if (!result.Bool("ok")) return;
                    endlessTop = new List<(int, string, int)>();
                    foreach (var o in result.Arr("top") ?? new List<object>())
                        if (o is Dictionary<string, object> e) endlessTop.Add((e.Int("rank"), e.Str("username"), e.Int("wave")));
                    var me = result.Obj("me");
                    endlessMe = me != null ? (me.Int("rank"), me.Int("wave")) : ((int, int)?)null;
                    if (Mode == "home") app.ShowHome();
                });
            }
            if (endlessTop == null) { Ui.Label(box, "載入中…", 14, Palette.Muted).rectTransform.Place(new Vector2(0, 1), new Vector2(0, -28), new Vector2(300, 20), new Vector2(0, 1)); return; }
            if (endlessTop.Count == 0) { Ui.Label(box, "還沒有人挑戰過無盡模式，成為第一個上榜的人！", 14, Palette.Muted).rectTransform.Place(new Vector2(0, 1), new Vector2(0, -28), new Vector2(456, 20), new Vector2(0, 1)); return; }
            for (int i = 0; i < Mathf.Min(5, endlessTop.Count); i++)
            {
                var (rank, name, wave) = endlessTop[i];
                bool mine = name == app.Profile.Username;
                Color c = rank == 1 ? Palette.Amber : mine ? Palette.Hex("ff6a8a") : Palette.Bone;
                float y = -26 - i * 22;
                Ui.Label(box, rank.ToString(), 14, c, TextAnchor.UpperLeft, rank <= 3).rectTransform.Place(new Vector2(0, 1), new Vector2(0, y), new Vector2(30, 20), new Vector2(0, 1));
                Ui.Label(box, name, 14, c, TextAnchor.UpperLeft, mine).rectTransform.Place(new Vector2(0, 1), new Vector2(34, y), new Vector2(300, 20), new Vector2(0, 1));
                Ui.Label(box, $"第 {wave} 波", 14, c, TextAnchor.UpperRight).rectTransform.Place(new Vector2(1, 1), new Vector2(0, y), new Vector2(120, 20), new Vector2(1, 1));
            }
            if (endlessMe.HasValue && endlessMe.Value.rank > 5)
                Ui.Label(box, $"你的排名  第 {endlessMe.Value.rank} 名 · 第 {endlessMe.Value.wave} 波", 13, Palette.Hex("ff6a8a")).rectTransform
                    .Place(new Vector2(0, 1), new Vector2(0, -26 - 5 * 22), new Vector2(456, 18), new Vector2(0, 1));
        }

        // ------------------------------------------------------------ armory

        string armoryPick = "pistol";
        public void ShowArmoryFor(string weapon) { armoryPick = weapon; ShowArmory(); }
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
                var row = Ui.Button(list, "", () => { armoryPick = id; ShowArmory(); }, 15, null, current ? new Color(Palette.Amber.r, Palette.Amber.g, Palette.Amber.b, 0.08f) : new Color(0, 0, 0, 0), current ? Palette.Amber : new Color(0, 0, 0, 0)).Sized(58);   // five weapons fit above the copies list
                row.GetComponentInChildren<Text>().text = "";
                Ui.Label(row.transform, $"0{i + 1}", 13, current ? Palette.Amber : Palette.Muted).rectTransform.Place(new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(30, 20), new Vector2(0, 0.5f));
                Ui.Label(row.transform, owned ? $"{w.Name}  ·  {Gear.RarityNames[p.Loadout.RarityOf(id)]}" : w.Name, 18, owned ? Gear.RarityColors[p.Loadout.RarityOf(id)] : Palette.Hex("a9abae"), TextAnchor.UpperLeft, owned).rectTransform.Place(new Vector2(0, 1), new Vector2(60, -8), new Vector2(300, 24), new Vector2(0, 1));
                string detail = owned ? string.Join(" · ", Defs.UpgradeKeys.Where(k => p.Loadout.Level(id, k) > 0).Select(k => $"{UpgradeName(k, Defs.Weapons[id].Melee)} Lv {p.Loadout.Level(id, k)}")) : "未持有";
                if (owned && p.Loadout.Level(id, "over") > 0) detail += $" · 超載 {p.Loadout.Level(id, "over")}";
                if (owned && p.Loadout.Level(id, "pierce") > 0)
                {
                    // Too long for one line with every stat listed: fall back to the total.
                    int sum = Defs.UpgradeKeys.Sum(k => p.Loadout.Level(id, k)), over = p.Loadout.Level(id, "over");
                    detail = string.Join(" · ", new[] { sum > 0 ? $"強化 {sum}" : null, over > 0 ? $"超載 {over}" : null, "穿透" }.Where(x => x != null));
                }
                Ui.Label(row.transform, detail == "" ? "已擁有" : detail, 12, Palette.Muted).rectTransform.Place(new Vector2(0, 1), new Vector2(60, -33), new Vector2(260, 18), new Vector2(0, 1));
                var tag = Ui.Label(row.transform, owned ? (slot >= 0 ? $"{slot + 1} 號" : "") : Defs.LimitedWeapons.Contains(id) ? "限定補給" : "武器補給", 13, owned ? Palette.Bone : Palette.Muted, TextAnchor.MiddleRight);
                tag.rectTransform.Place(new Vector2(1, 0.5f), new Vector2(-16, 0), new Vector2(80, 20), new Vector2(1, 0.5f));
                if (current) Ui.Box(row.transform, Palette.Amber, "Mark").rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(3, 58), new Vector2(0, 0.5f));
            }
            // Copies of the selected weapon: choose which one to use, salvage the rest by hand.
            if (p.Loadout.Owned.Contains(armoryPick) && p.Loadout.Copies.TryGetValue(armoryPick, out var copies))
            {
                int activeCopy = p.Loadout.Active.TryGetValue(armoryPick, out var ac) ? ac : 0;
                var copyBox = Ui.Rect(pad, "Copies").Place(new Vector2(0, 1), new Vector2(0, -400), new Vector2(400, 170), new Vector2(0, 1));
                Widgets.Section(copyBox, $"武器庫  ·  {copies.Count} 把").Place(new Vector2(0, 1), Vector2.zero, new Vector2(290, 26), new Vector2(0, 1));
                Ui.Button(copyBox, "一鍵分解", () => BulkSalvage("weapon"), 12, Palette.Amber).GetComponent<RectTransform>().Place(new Vector2(1, 1), new Vector2(0, 1), new Vector2(100, 26), new Vector2(1, 1));
                var host = Ui.Rect(copyBox, "Host");
                host.anchorMin = Vector2.zero; host.anchorMax = Vector2.one; host.offsetMin = Vector2.zero; host.offsetMax = new Vector2(0, -30);
                var copyList = Widgets.ScrollList(host, out _);
                copyList.parent.GetComponent<RectTransform>().Fill();
                var order = new List<int>();
                for (int i = 0; i < copies.Count; i++) order.Add(i);
                order.Sort((x, y) => copies[y] != copies[x] ? copies[y].CompareTo(copies[x]) : x.CompareTo(y));
                foreach (int index in order)
                {
                    int rarity = Mathf.Clamp(copies[index], 0, 4), idx = index;
                    bool using_ = index == activeCopy;
                    var row = Ui.Rect(copyList, "Copy").Sized(36);
                    Ui.Label(row, Gear.WeaponName(armoryPick, rarity), 14, Gear.RarityColors[rarity], TextAnchor.MiddleLeft, true).rectTransform.Fill().offsetMin = new Vector2(10, 0);
                    // Each copy keeps its own enhancement: show it so the player knows what a switch or a salvage means.
                    var copyLv = p.Loadout.CopyLevel(armoryPick, index);
                    int lvSum = Defs.UpgradeKeys.Sum(k => copyLv.TryGetValue(k, out var v) ? v : 0), lvOver = copyLv.TryGetValue("over", out var ov) ? ov : 0;
                    bool lvPierce = copyLv.TryGetValue("pierce", out var pv) && pv > 0;
                    var marks = new List<string>();
                    if (lvSum > 0) marks.Add($"強化 {lvSum}");
                    if (lvOver > 0) marks.Add($"超載 {lvOver}");
                    if (lvPierce) marks.Add("穿透");
                    if (marks.Count > 0)
                        Ui.Label(row, string.Join(" · ", marks), 11, lvPierce ? Gear.RarityColors[4] : lvOver > 0 ? Gear.ShardColor : Palette.Muted, TextAnchor.MiddleLeft)
                            .rectTransform.Place(new Vector2(0, 0.5f), new Vector2(170, 0), new Vector2(150, 20), new Vector2(0, 0.5f));
                    if (using_)
                    {
                        Ui.Label(row, "使用中", 12, Palette.Amber, TextAnchor.MiddleRight).rectTransform.Fill().offsetMax = new Vector2(-10, 0);
                        continue;
                    }
                    string weaponId = armoryPick;
                    var use = Ui.Button(row, "使用", () => ArmoryRequest("/api/weapon/select", new Dictionary<string, object> { ["weapon"] = weaponId, ["index"] = idx, ["rarity"] = rarity }), 12, Palette.Amber);
                    use.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), new Vector2(-70, 0), new Vector2(60, 28), new Vector2(1, 0.5f));
                    // 傳奇 / 神話 need a second click to salvage; the button arms itself for 3 seconds.
                    Button scrap = null;
                    float armedUntil = -1f;
                    scrap = Ui.Button(row, "分解", () =>
                    {
                        if (rarity >= 3 && Time.unscaledTime > armedUntil)
                        {
                            armedUntil = Time.unscaledTime + 3f;
                            scrap.SetCaption("確定？");
                            if (armoryStatus != null) { armoryStatus.color = Palette.Danger; armoryStatus.text = $"再按一次「確定？」分解 {Gear.WeaponName(weaponId, rarity)}，無法復原。"; }
                            return;
                        }
                        ArmoryRequest("/api/weapon/salvage", new Dictionary<string, object> { ["weapon"] = weaponId, ["index"] = idx, ["rarity"] = rarity });
                    }, 12, rarity >= 3 ? Palette.Danger : Palette.Muted);
                    scrap.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), new Vector2(-4, 0), new Vector2(60, 28), new Vector2(1, 0.5f));
                }
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
            var ds = Ui.VStack(sheet, 10);
            ds.Fill();
            ds.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(32, 32, 26, 24);
            int shownRarity = own ? p.Loadout.RarityOf(armoryPick) : 1;
            string pick = armoryPick;
            int shownEnhance = own ? Defs.UpgradeKeys.Sum(k => p.Loadout.Level(pick, k)) : 0, shownOver = own ? p.Loadout.Level(pick, "over") : 0;
            bool shownPierce = own && p.Loadout.Level(pick, "pierce") > 0;
            var show = ModelPreview.Create(sheet, t => Models.WeaponDisplay(t, pick, shownRarity, shownEnhance, shownOver, shownPierce), 512, 25f, 8f, true);
            show.rectTransform.Place(new Vector2(1, 1), new Vector2(-24, -14), new Vector2(150, 150), new Vector2(1, 1));
            Ui.Label(sheet, "拖曳旋轉", 11, Palette.Muted, TextAnchor.UpperCenter).rectTransform.Place(new Vector2(1, 1), new Vector2(-24, -166), new Vector2(150, 16), new Vector2(1, 1));
            if (own) RarityFx.Attach(show.rectTransform, shownRarity);
            if (!own) show.color = new Color(1, 1, 1, 0.35f);
            Ui.Label(ds, own ? Gear.WeaponName(armoryPick, shownRarity) : wd.Name, 32, own ? Gear.RarityColors[shownRarity] : Palette.Bone, TextAnchor.UpperLeft, true).Sized(44);
            if (own) Ui.Label(ds, $"{Gear.RarityNames[p.Loadout.RarityOf(armoryPick)]}  ·  傷害 ×{Defs.WeaponRarityMult[p.Loadout.RarityOf(armoryPick)]:0.00}", 15, Gear.RarityColors[p.Loadout.RarityOf(armoryPick)], TextAnchor.UpperLeft, true).Sized(22);
            // 傳奇+ perk; below 傳奇 show what 傳奇 would unlock, as a goal.
            var perk = Defs.Perk(armoryPick, own ? Mathf.Max(3, shownRarity) : 3);
            if (perk.HasValue)
            {
                bool active = own && shownRarity >= 3;
                Ui.Label(ds, active ? $"專屬屬性【{perk.Value.name}】 {perk.Value.text}" : $"傳奇以上解鎖【{perk.Value.name}】 {perk.Value.text}",
                    13, active ? Gear.RarityColors[shownRarity] : Palette.Muted, TextAnchor.UpperLeft, active).Sized(20);
            }
            Ui.Label(ds, wd.Description, 15, Palette.Hex("c9c5bb")).Sized(22);
            var st = wd.StatsAt(p.Loadout.Level(armoryPick, "damage"), p.Loadout.Level(armoryPick, "mag"), p.Loadout.Level(armoryPick, "reload"), p.Loadout.Level(armoryPick, "over"));
            st.Damage *= Defs.WeaponRarityMult[own ? p.Loadout.RarityOf(armoryPick) : 1];
            float dps = st.Damage * st.Pellets / st.Interval;
            var stats = Ui.HStack(ds, 22).Sized(70);
            StatCell(stats, "傷害", st.Pellets > 1 ? $"{st.Damage:0}×{st.Pellets}" : $"{st.Damage:0}", st.Damage * st.Pellets / 700f);
            StatCell(stats, "每秒傷害", $"{dps:0}", dps / 280f);
            if (wd.Melee)
            {
                StatCell(stats, "攻擊範圍", $"{st.Range:0.0}m", st.Range / 4f);
                StatCell(stats, "揮砍間隔", $"{st.Interval:0.00}s", 1f - st.Interval / 1f);
            }
            else
            {
                StatCell(stats, "彈匣", st.Mag.ToString(), st.Mag / 45f);
                StatCell(stats, "換彈", $"{st.Reload:0.00}s", 1f - st.Reload / 3f);
            }
            Ui.Box(ds, Palette.Line, "Rule").Sized(1);
            if (!own)
            {
                if (Defs.LimitedWeapons.Contains(pick))
                    Ui.Label(ds, "尚未持有。限定武器，只在「限定補給」開放期間可以抽取。", 15, Palette.Muted).Sized(26);
                else
                {
                    Ui.Label(ds, "尚未持有。武器改由「商城 → 武器補給」抽取取得。", 15, Palette.Muted).Sized(26);
                    PrimaryButton(ds, "前往商城 · 武器補給", ShowShop, 18).Sized(52);
                }
            }
            else
            {
                int rarityNow = p.Loadout.RarityOf(armoryPick);
                bool allMaxed = true;
                foreach (var key in Defs.UpgradeKeys)
                {
                    int lv = p.Loadout.Level(armoryPick, key);
                    allMaxed &= lv >= Defs.MaxUpgradeLevel;
                    var row = Ui.Rect(ds, "Upgrade").Sized(34);
                    Ui.Label(row, UpgradeName(key, wd.Melee), 16, Palette.Bone, TextAnchor.MiddleLeft, true).rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(80, 24), new Vector2(0, 0.5f));
                    string step = key == "damage" ? "+12%" : key == "mag" ? (wd.Melee ? "+8%" : "+15%") : (wd.Melee ? "-4%" : "-8%");
                    Ui.Label(row, lv >= Defs.SoftUpgradeLevel ? $"每級 {step} · 6 級起減半" : $"每級 {step}", 12, Palette.Muted, TextAnchor.MiddleLeft)
                        .rectTransform.Place(new Vector2(0, 0.5f), new Vector2(80, 0), new Vector2(150, 20), new Vector2(0, 0.5f));
                    // Ten pips; the second five (half-step, steeper price) sit after a gap and read darker when lit.
                    for (int k = 0; k < Defs.MaxUpgradeLevel; k++)
                    {
                        bool deep = k >= Defs.SoftUpgradeLevel;
                        var lit = deep ? Palette.Hex("ff7a2e") : Palette.Amber;
                        Ui.Box(row, k < lv ? lit : Palette.Line, "Pip").rectTransform.Place(new Vector2(0, 0.5f), new Vector2(236 + k * 20 + (deep ? 10 : 0), 0), new Vector2(16, 6), new Vector2(0, 0.5f));
                    }
                    Ui.Label(row, $"{lv}/{Defs.MaxUpgradeLevel}", 13, lv >= Defs.MaxUpgradeLevel ? Palette.Amber : Palette.Muted, TextAnchor.MiddleLeft)
                        .rectTransform.Place(new Vector2(0, 0.5f), new Vector2(246 + Defs.MaxUpgradeLevel * 20, 0), new Vector2(50, 20), new Vector2(0, 0.5f));
                    bool maxed = lv >= Defs.MaxUpgradeLevel;
                    int cost = Defs.UpgradeCost(armoryPick, lv, rarityNow);
                    var up = Ui.Button(row, maxed ? "已滿級" : $"升級  {cost} G", () => ArmoryRequest("/api/shop/upgrade", new Dictionary<string, object> { ["weapon"] = armoryPick, ["stat"] = key }), 14, Palette.Amber, new Color(0, 0, 0, 0), Palette.Amber);
                    up.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(160, 32), new Vector2(1, 0.5f));
                    up.interactable = !maxed && p.Gold >= cost;
                }
                // 超載: the shard-paid top tier, only for 傳奇+ copies with all three stats maxed.
                {
                    int over = p.Loadout.Level(armoryPick, "over");
                    bool eligible = rarityNow >= Defs.OverloadMinRarity;
                    var row = Ui.Rect(ds, "Overload").Sized(34);
                    Ui.Label(row, "超載", 16, eligible ? Gear.ShardColor : Palette.Muted, TextAnchor.MiddleLeft, true).rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(80, 24), new Vector2(0, 0.5f));
                    string why = !eligible ? "傳奇以上的武器才能超載" : !allMaxed ? "三項強化滿級後開放" : "每級 傷害、射速、換彈 +5%";
                    Ui.Label(row, why, 12, Palette.Muted, TextAnchor.MiddleLeft).rectTransform.Place(new Vector2(0, 0.5f), new Vector2(80, 0), new Vector2(160, 20), new Vector2(0, 0.5f));
                    for (int k = 0; k < Defs.OverloadMax; k++)
                        Ui.Box(row, k < over ? Gear.ShardColor : Palette.Line, "Pip").rectTransform.Place(new Vector2(0, 0.5f), new Vector2(256 + k * 42, 0), new Vector2(36, 6), new Vector2(0, 0.5f));
                    Ui.Label(row, $"{over}/{Defs.OverloadMax}", 13, over >= Defs.OverloadMax ? Gear.ShardColor : Palette.Muted, TextAnchor.MiddleLeft)
                        .rectTransform.Place(new Vector2(0, 0.5f), new Vector2(246 + Defs.MaxUpgradeLevel * 20, 0), new Vector2(50, 20), new Vector2(0, 0.5f));
                    if (eligible)
                    {
                        bool full = over >= Defs.OverloadMax;
                        int shards = full ? 0 : Defs.OverloadShards[over], gold = full ? 0 : Defs.OverloadGold[over];
                        var go = Ui.Button(row, full ? "超載已滿" : $"{shards} 碎片 + {gold / 1000}K G",
                            () => ArmoryRequest("/api/shop/overload", new Dictionary<string, object> { ["weapon"] = armoryPick }), 14, Gear.ShardColor, new Color(0, 0, 0, 0), Gear.ShardColor);
                        go.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(160, 32), new Vector2(1, 0.5f));
                        go.interactable = !full && allMaxed && p.Shards >= shards && p.Gold >= gold;
                    }
                }
                // 穿透: a one-time unlock for 神話 guns; rounds go through up to two more enemies.
                if (!wd.Melee)
                {
                    bool has = p.Loadout.Level(armoryPick, "pierce") > 0, eligible = rarityNow >= Defs.PierceMinRarity;
                    Color pc = Gear.RarityColors[4];
                    var row = Ui.Rect(ds, "Pierce").Sized(34);
                    Ui.Label(row, "穿透", 16, eligible ? pc : Palette.Muted, TextAnchor.MiddleLeft, true).rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(80, 24), new Vector2(0, 0.5f));
                    string what = !eligible ? "神話武器才能加裝穿透" : $"子彈貫穿 {Defs.PierceExtra(armoryPick)} 名敵人，每穿過一名傷害 -{Defs.PierceFalloff(armoryPick) * 100:0}%";
                    Ui.Label(row, what, 12, has ? pc : Palette.Muted, TextAnchor.MiddleLeft).rectTransform.Place(new Vector2(0, 0.5f), new Vector2(80, 0), new Vector2(420, 20), new Vector2(0, 0.5f));
                    if (eligible)
                    {
                        var buy = Ui.Button(row, has ? "已加裝" : $"{Defs.PierceShards} 碎片 + {Defs.PierceGold / 1000}K G",
                            () => ArmoryRequest("/api/shop/pierce", new Dictionary<string, object> { ["weapon"] = armoryPick }), 14, pc, new Color(0, 0, 0, 0), pc);
                        buy.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(160, 32), new Vector2(1, 0.5f));
                        buy.interactable = !has && p.Shards >= Defs.PierceShards && p.Gold >= Defs.PierceGold;
                    }
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

        /// On the 光劍 the 彈匣 / 換彈 upgrades lengthen the reach and speed up the swing.
        static string UpgradeName(string key, bool melee = false) => key == "damage" ? "傷害" : key == "mag" ? (melee ? "攻擊範圍" : "彈匣") : (melee ? "揮砍速度" : "換彈");

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

        bool armoryBusy;
        void ArmoryRequest(string path, Dictionary<string, object> data)
        {
            if (armoryBusy) return;   // one request at a time: a double click must not act on a shifted index
            armoryBusy = true;
            if (armoryStatus != null) { armoryStatus.color = Palette.Amber; armoryStatus.text = "處理中…"; }
            app.Api.Post(path, data, result =>
            {
                armoryBusy = false;
                if (Mode != "armory") return;
                if (result.Bool("ok")) { app.UpdateProfile(Profile.From(result.Obj("profile"))); ShowArmory(); armoryStatus.text = "完成！裝備已更新。"; }
                else { armoryStatus.color = Palette.Danger; armoryStatus.text = result.Str("error", "操作失敗"); }
            });
        }

        // ------------------------------------------------------------ character: talents, gear, supply draws

        Text characterStatus;
        string drawPool = "gear";
        string gearFilter = "";   // "" = all slots
        public void SetGearFilter(string slot) { gearFilter = slot; ShowCharacter(); }
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
            // Set progress of what is worn (bonuses are already inside p.Bonus, computed by the server).
            var wornSets = new Dictionary<string, int>();
            foreach (var kv in p.Equipment)
            {
                var w = p.Inventory.Find(i => i.Id == kv.Value);
                if (w != null && w.Set != "") wornSets[w.Set] = (wornSets.TryGetValue(w.Set, out var c) ? c : 0) + 1;
            }
            foreach (var kv in wornSets) total.Add($"<color=#{ColorUtility.ToHtmlStringRGB(Gear.Sets[kv.Key].color)}>{Gear.Sets[kv.Key].name} {kv.Value}/4</color>");
            var totalLine = Ui.Label(g, total.Count > 0 ? "總加成  " + string.Join("  ·  ", total) : "尚無加成", 13, Palette.Muted);
            totalLine.supportRichText = true;
            totalLine.Sized(20);
            var slots = Ui.HStack(g, 8).Sized(150);
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
                string slotId = slot;
                int slotRarity = item?.Rarity ?? 0;
                var model = ModelPreview.Create(cell.transform, t => Models.GearModel(t, slotId, slotRarity));
                model.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(84, 84), new Vector2(0.5f, 1));
                if (item != null) RarityFx.Attach(model.rectTransform, item.Rarity);
                if (item == null) model.color = new Color(1, 1, 1, 0.18f);
                Ui.Label(cell.transform, Gear.SlotNames[slot], 12, Palette.Muted).rectTransform.Place(new Vector2(0, 1), new Vector2(10, -8), new Vector2(150, 16), new Vector2(0, 1));
                var title = Ui.Label(cell.transform, item != null ? item.Name : "未裝備", 13, item != null ? item.Color : Palette.Line, TextAnchor.UpperCenter, true).rectTransform;
                title.anchorMin = new Vector2(0, 1); title.anchorMax = new Vector2(1, 1); title.pivot = new Vector2(0.5f, 1);
                title.offsetMin = new Vector2(4, -124); title.offsetMax = new Vector2(-4, -104);
                if (item != null)
                {
                    var stat = Ui.Label(cell.transform, item.StatText().Replace("  ·  ", "\n"), 10, Palette.Hex("c9c5bb")).rectTransform;
                    stat.anchorMin = new Vector2(0, 0); stat.anchorMax = new Vector2(1, 1);
                    stat.offsetMin = new Vector2(8, 2); stat.offsetMax = new Vector2(-4, -124);
                    stat.GetComponent<Text>().alignment = TextAnchor.UpperCenter;
                    stat.GetComponent<Text>().text = item.StatText(false);
                }
            }
            var bagHead = Ui.Rect(g, "BagHead").Sized(28);
            Ui.Label(bagHead, $"背包  {p.Inventory.Count} 件    點選穿上 / 卸下 · 改造內可重鑄、突破、升階、分解", 13, Palette.Muted, TextAnchor.MiddleLeft).rectTransform.Fill().offsetMax = new Vector2(-110, 0);
            Ui.Button(bagHead, "一鍵分解", () => BulkSalvage("gear"), 12, Palette.Amber).GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(100, 28), new Vector2(1, 0.5f));
            // Category tabs, like the weapon list: all, then one per slot with its count.
            var tabs = Ui.HStack(g, 6).Sized(34);
            var filters = new List<(string id, string label)> { ("", "全部") };
            foreach (var slot in Gear.Slots) filters.Add((slot, Gear.SlotNames[slot]));
            foreach (var (id, label) in filters)
            {
                int count = id == "" ? p.Inventory.Count : p.Inventory.FindAll(i => i.Slot == id).Count;
                bool on = gearFilter == id;
                string pick = id;
                Ui.Button(tabs, $"{label}  {count}", () => { gearFilter = pick; ShowCharacter(); }, 13, on ? Palette.Amber : Palette.Muted,
                    on ? new Color(Palette.Amber.r, Palette.Amber.g, Palette.Amber.b, 0.1f) : new Color(1, 1, 1, 0.03f), on ? Palette.Amber : Palette.Line);
            }
            var listHost = Ui.Rect(g, "Inventory");
            listHost.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var list = Widgets.ScrollList(listHost, out _);
            list.parent.GetComponent<RectTransform>().Fill();
            var sorted = gearFilter == "" ? new List<GearItem>(p.Inventory) : p.Inventory.FindAll(i => i.Slot == gearFilter);
            sorted.Sort((a, b) => b.Rarity != a.Rarity ? b.Rarity.CompareTo(a.Rarity) : b.Level != a.Level ? b.Level.CompareTo(a.Level) : string.CompareOrdinal(a.Slot, b.Slot));
            if (sorted.Count == 0) Ui.Label(list, gearFilter == "" ? "背包是空的，到補給站抽取裝備吧" : $"沒有{Gear.SlotNames[gearFilter]}", 13, Palette.Line).Sized(30);
            foreach (var item in sorted)
            {
                bool worn = p.Equipment.TryGetValue(item.Slot, out var wid) && wid == item.Id;
                var row = Ui.Rect(list, "Item").Sized(44);
                var hit = Ui.Button(row, "", () => CharacterRequest("/api/gear/equip", new Dictionary<string, object> { ["item"] = item.Id }), 14, null,
                    worn ? new Color(item.Color.r, item.Color.g, item.Color.b, 0.07f) : new Color(0, 0, 0, 0));
                hit.GetComponent<RectTransform>().Fill().offsetMax = new Vector2(-160, 0);
                // Worn: a thin bar in the rarity colour on the left edge instead of a filled, outlined row.
                if (worn) Ui.Box(hit.transform, item.Color, "Worn").rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(3, 44), new Vector2(0, 0.5f));
                Ui.Label(hit.transform, item.Name + (worn ? "  · 已裝備" : ""), 15, item.Color, TextAnchor.UpperLeft, worn).rectTransform.Place(new Vector2(0, 1), new Vector2(10, -4), new Vector2(260, 20), new Vector2(0, 1));
                var statLine = Ui.Label(hit.transform, item.StatText() + (item.Set != "" ? $"  ·  <color=#{ColorUtility.ToHtmlStringRGB(Gear.Sets[item.Set].color)}>{item.SetName}套裝</color>" : ""), 12, Palette.Muted);
                statLine.supportRichText = true;
                statLine.rectTransform.Place(new Vector2(0, 1), new Vector2(10, -24), new Vector2(340, 16), new Vector2(0, 1));
                var craft = Ui.Button(row, "改造", () => CraftDialog(item.Id), 12, Gear.ShardColor);
                craft.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), new Vector2(-84, 0), new Vector2(68, 34), new Vector2(1, 0.5f));
                // Enhance: price depends on rarity and current level.
                var enhance = Ui.Button(row, item.Maxed ? "已滿強化" : $"強化 {item.UpgradeCost}G", () => CharacterRequest("/api/gear/upgrade", new Dictionary<string, object> { ["item"] = item.Id }),
                    12, item.Maxed ? Palette.Muted : Palette.Amber);
                enhance.interactable = !item.Maxed && p.Gold >= item.UpgradeCost;
                enhance.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(78, 34), new Vector2(1, 0.5f));
            }

        }

        // ------------------------------------------------------------ gear crafting

        RectTransform craftDialog;
        bool craftLock;

        /// 改造: 重鑄 (reroll the secondary stat and set; lock keeps them), 突破 (past +5) and 升階 (consume a twin).
        /// The server does every roll and charge; this only shows prices and sends the request.
        void CraftDialog(string itemId, string message = "", bool error = false)
        {
            var p = app.Profile;
            var item = p?.Inventory.Find(i => i.Id == itemId);
            if (craftDialog != null) UnityEngine.Object.Destroy(craftDialog.gameObject);
            craftDialog = null;
            if (item == null || root == null) return;
            craftDialog = Ui.Box(root, new Color(0, 0, 0, 0.62f), "CraftDialog").rectTransform.Fill();
            var sheet = Ui.Box(craftDialog, Palette.Sheet, "Sheet").rectTransform;
            sheet.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 640));
            TopRule(sheet, item.Color);
            var st = Ui.VStack(sheet, 6);
            st.Fill();
            st.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(28, 28, 22, 22);
            Ui.Label(st, "裝備改造", 13, Gear.ShardColor).Sized(18);
            Ui.Label(st, item.Name, 24, item.Color, TextAnchor.UpperLeft, true).Sized(32);
            Ui.Label(st, item.StatText(), 14, Palette.Hex("d6d1c6")).Sized(20);
            var setLine = Ui.Label(st, item.Set != "" ? Gear.SetText(item.Set) : "無套裝（稀世以上的裝備才有套裝）", 13, item.Set != "" ? Gear.Sets[item.Set].color : Palette.Muted);
            setLine.Sized(20);
            Ui.Label(st, $"持有  {p.Gold} G　·　{p.Shards} 碎片", 13, Palette.Muted).Sized(20);

            void Send(string path, Dictionary<string, object> data, string done)
            {
                app.Api.Post(path, data, result =>
                {
                    if (!result.Bool("ok")) { CraftDialog(itemId, result.Str("error", "操作失敗"), true); return; }
                    app.UpdateProfile(Profile.From(result.Obj("profile")));
                    if (Mode == "character") ShowCharacter();
                    CraftDialog(itemId, done);
                });
            }
            string Cost(int shards, int gold) => shards > 0 && gold > 0 ? $"{shards} 碎片 + {gold} G" : shards > 0 ? $"{shards} 碎片" : $"{gold} G";

            // ---- 重鑄
            Widgets.Section(st, "重鑄").Sized(24);
            bool canReforge = item.Rarity >= 2;
            Ui.Label(st, canReforge ? "重新隨機副屬性（35%–75%）與套裝。鎖定：保留副屬性種類與套裝，只重骰數值。" : "稀世以上的裝備才能重鑄。", 12, Palette.Muted).Sized(34);
            var reforgeRow = Ui.HStack(st, 8).Sized(38);
            Widgets.Toggle(reforgeRow, "鎖定屬性", craftLock, on => { craftLock = on; CraftDialog(itemId); }, "鎖定", "不鎖").interactable = canReforge;
            int rShards = canReforge ? Gear.ReforgeShards[item.Rarity] + (craftLock ? Gear.ReforgeLockShards : 0) : 0, rGold = canReforge ? Gear.ReforgeGold[item.Rarity] : 0;
            var reforge = Ui.Button(reforgeRow, canReforge ? $"重鑄 · {Cost(rShards, rGold)}" : "無法重鑄", () =>
                Send("/api/gear/reforge", new Dictionary<string, object> { ["item"] = itemId, ["lock"] = craftLock }, "重鑄完成"), 14, Gear.ShardColor);
            reforge.interactable = canReforge && p.Shards >= rShards && p.Gold >= rGold;

            // ---- 突破
            Widgets.Section(st, $"突破  {item.Limit} / {Gear.LimitMax}").Sized(24);
            bool limitMax = item.Limit >= Gear.LimitMax;
            Ui.Label(st, !item.Maxed ? $"強化到 +{Gear.MaxLevel} 後才能突破。每階全屬性 +{Gear.LimitBonus * 100:0}%、防禦 +1%。"
                : limitMax ? "已達最高突破。" : $"每階全屬性 +{Gear.LimitBonus * 100:0}%、防禦 +1%（目前 +{item.Limit * Gear.LimitBonus * 100:0}%）。", 12, Palette.Muted).Sized(20);
            int lShards = limitMax ? 0 : Gear.LimitShards[item.Limit], lGold = limitMax ? 0 : Gear.LimitGold[item.Limit];
            var limit = Ui.Button(st, limitMax ? "已滿突破" : $"突破 ★{item.Limit + 1} · {Cost(lShards, lGold)}", () =>
                Send("/api/gear/limit", new Dictionary<string, object> { ["item"] = itemId }, "突破成功"), 14, Gear.ShardColor).Sized(38);
            limit.interactable = item.Maxed && !limitMax && p.Shards >= lShards && p.Gold >= lGold;

            // ---- 升階
            bool top = item.Rarity >= Gear.RarityNames.Length - 1;
            Widgets.Section(st, "升階").Sized(24);
            var worn = new HashSet<string>(p.Equipment.Values);
            var fodder = top ? null : p.Inventory.Where(i => i.Id != item.Id && i.Slot == item.Slot && i.Rarity == item.Rarity && !worn.Contains(i.Id))
                .OrderBy(i => i.Limit).ThenBy(i => i.Level).FirstOrDefault();
            Ui.Label(st, top ? "已是最高稀有度。"
                : fodder != null ? $"消耗素材：{fodder.Name}（自動選擇強化最低的同部位{Gear.RarityNames[item.Rarity]}裝備）。強化與突破保留。"
                : $"需要另一件未裝備的{Gear.RarityNames[item.Rarity]}{Gear.SlotNames[item.Slot]}作為素材。", 12, Palette.Muted).Sized(34);
            int aShards = top ? 0 : Gear.AscendShards[item.Rarity], aGold = top ? 0 : Gear.AscendGold[item.Rarity];
            var ascend = Ui.Button(st, top ? "無法升階" : $"升階為 {Gear.RarityNames[item.Rarity + 1]} · {Cost(aShards, aGold)}", () =>
                Send("/api/gear/ascend", new Dictionary<string, object> { ["item"] = itemId, ["fodder"] = fodder.Id }, "升階成功"), 14, top ? Palette.Muted : Gear.RarityColors[Mathf.Min(4, item.Rarity + 1)]).Sized(38);
            ascend.interactable = !top && fodder != null && p.Shards >= aShards && p.Gold >= aGold;

            Ui.Rect(st, "Spacer").gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var foot = Ui.Rect(st, "Foot").Sized(40);
            Ui.Label(foot, message, 14, error ? Palette.Danger : Palette.Amber, TextAnchor.MiddleLeft).rectTransform.Fill().offsetMax = new Vector2(-250, 0);
            Ui.Button(foot, "關閉", () => { UnityEngine.Object.Destroy(craftDialog.gameObject); craftDialog = null; }, 15, Palette.Muted)
                .GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(120, 40), new Vector2(1, 0.5f));
            if (!worn.Contains(item.Id))
            {
                Button scrap = null;
                bool armed = false;
                scrap = Ui.DangerButton(foot, "分解", () =>
                {
                    if (!armed) { armed = true; scrap.SetCaption("確定分解？"); return; }
                    if (craftDialog != null) UnityEngine.Object.Destroy(craftDialog.gameObject);
                    craftDialog = null;
                    CharacterRequest("/api/gear/salvage", new Dictionary<string, object> { ["item"] = itemId });
                }, 14);
                scrap.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), new Vector2(-130, 0), new Vector2(110, 40), new Vector2(1, 0.5f));
            }
        }

        /// Automated screenshots only: the crafting dialog for the first item in the bag.
        public void ShowCraftForCapture() { var first = app.Profile?.Inventory.FirstOrDefault(i => i.Rarity >= 2) ?? app.Profile?.Inventory.FirstOrDefault(); if (first != null) CraftDialog(first.Id); }

        // ------------------------------------------------------------ bulk salvage

        RectTransform salvageDialog;

        /// Salvage everything of the ticked rarities in one go. Equipped gear and the copy in use are never touched;
        /// ticking 傳奇 or 神話 turns the button red and needs a second click (the server also demands confirm).
        public void BulkSalvage(string kind)
        {
            var p = app.Profile;
            if (p == null) return;
            if (salvageDialog != null) UnityEngine.Object.Destroy(salvageDialog.gameObject);
            bool weapons = kind == "weapon";
            var gold = weapons ? Gear.WeaponSalvageGold : Gear.GearSalvageGold;
            int tiers = Gear.RarityNames.Length;
            var counts = new int[tiers];
            var refunds = new int[tiers];   // half of the enhancement gold comes back on salvage
            var shardRefunds = new int[tiers];
            if (weapons)
            {
                foreach (var w in p.Loadout.Owned)
                {
                    if (!p.Loadout.Copies.TryGetValue(w, out var list)) continue;
                    int active = p.Loadout.Active.TryGetValue(w, out var a) ? a : 0;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (i == active) continue;
                        int rr = Mathf.Clamp(list[i], 0, tiers - 1);
                        counts[rr]++;
                        var (ig, ish) = Gear.WeaponInvested(w, rr, p.Loadout.CopyLevel(w, i));
                        refunds[rr] += ig / 2; shardRefunds[rr] += ish / 2;
                    }
                }
            }
            else
            {
                var worn = new HashSet<string>(p.Equipment.Values);
                foreach (var item in p.Inventory)
                {
                    if (worn.Contains(item.Id)) continue;
                    counts[item.Rarity]++;
                    int invested = 0;
                    for (int n = 0; n < item.Level; n++) invested += Gear.UpgradeCost(item.Rarity, n);
                    refunds[item.Rarity] += invested / 2;
                }
            }
            var chosen = new bool[tiers];
            chosen[0] = chosen[1] = true;   // 劣質 + 普通 ticked by default

            salvageDialog = Ui.Box(root, new Color(0, 0, 0, 0.62f), "SalvageDialog").rectTransform.Fill();
            var sheet = Ui.Box(salvageDialog, Palette.Sheet, "Sheet").rectTransform;
            sheet.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(480, 470));
            TopRule(sheet, Palette.Amber);
            var s = Ui.VStack(sheet, 8);
            s.Fill();
            s.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(28, 28, 22, 22);
            Ui.Label(s, weapons ? "一鍵分解武器" : "一鍵分解裝備", 26, Palette.Bone, TextAnchor.UpperLeft, true).Sized(36);
            Ui.Label(s, weapons ? "勾選要分解的稀有度（所有武器）。使用中的版本不會被分解。" : "勾選要分解的稀有度。已裝備的不會被分解。", 13, Palette.Muted).Sized(22);

            Text summary = null, warning = null;
            Button go = null;
            float armedUntil = -1f;
            void Refresh()
            {
                int n = 0, g = 0, sh = 0, high = 0;
                for (int r = 0; r < tiers; r++)
                    if (chosen[r]) { n += counts[r]; g += counts[r] * gold[r] + refunds[r]; sh += counts[r] * Gear.SalvageShards[r] + shardRefunds[r]; if (r >= 3) high += counts[r]; }
                string gain = sh > 0 ? (g > 0 ? $"{g} 金幣 + {sh} 碎片" : $"{sh} 碎片") : $"{g} 金幣";
                summary.text = n > 0 ? $"將分解 {n} {(weapons ? "把" : "件")}  ·  獲得 {gain}" : "沒有符合條件的項目";
                warning.text = high > 0 ? $"注意：包含 {high} {(weapons ? "把" : "件")}傳奇以上，分解後無法復原" : "";
                armedUntil = -1f;
                go.SetCaption("分解");
                go.GetComponentInChildren<Text>().color = high > 0 ? Palette.Danger : Palette.Amber;
                go.interactable = n > 0;
            }
            for (int r = 0; r < tiers; r++)
            {
                int tier = r;
                string yields = Gear.SalvageShards[r] > 0 ? $"每{(weapons ? "把" : "件")} {Gear.SalvageShards[r]} 碎片" : $"每{(weapons ? "把" : "件")} {gold[r]} G";
                var row = Widgets.Toggle(s, $"{Gear.RarityNames[r]}    {counts[r]} {(weapons ? "把" : "件")}  ·  {yields}", chosen[r], on => { chosen[tier] = on; Refresh(); }, "分解", "保留").Sized(40);
                row.GetComponentInChildren<Text>().color = Gear.RarityColors[r];
            }
            summary = Ui.Label(s, "", 16, Palette.Bone, TextAnchor.MiddleLeft, true).Sized(26);
            warning = Ui.Label(s, "", 13, Palette.Danger, TextAnchor.MiddleLeft).Sized(20);
            var buttons = Ui.HStack(s, 10).Sized(44);
            Ui.Button(buttons, "取消", () => { UnityEngine.Object.Destroy(salvageDialog.gameObject); salvageDialog = null; }, 15, Palette.Muted);
            go = Ui.Button(buttons, "分解", () =>
            {
                var picked = new List<object>();
                bool high = false;
                for (int r = 0; r < tiers; r++) if (chosen[r] && counts[r] > 0) { picked.Add(r); high |= r >= 3; }
                if (picked.Count == 0) return;
                if (high && Time.unscaledTime > armedUntil)
                {
                    armedUntil = Time.unscaledTime + 4f;
                    go.SetCaption("確定分解？再按一次");
                    return;
                }
                go.interactable = false;
                go.SetCaption("處理中…");
                string mode = Mode;
                app.Api.Post("/api/salvage/bulk", new Dictionary<string, object> { ["kind"] = kind, ["rarities"] = picked, ["confirm"] = high }, result =>
                {
                    if (Mode != mode) return;
                    if (!result.Bool("ok")) { go.interactable = true; go.SetCaption("分解"); warning.text = result.Str("error", "操作失敗"); return; }
                    app.UpdateProfile(Profile.From(result.Obj("profile")));
                    salvageDialog = null;
                    if (weapons) ShowArmory(); else ShowCharacter();
                    var status = weapons ? armoryStatus : characterStatus;
                    int gotShards = result.Int("shards");
                    if (status != null) { status.color = Palette.Amber; status.text = $"已分解 {result.Int("count")} {(weapons ? "把" : "件")}，獲得 {result.Int("gold")} 金幣" + (gotShards > 0 ? $"、{gotShards} 碎片" : ""); }
                });
            }, 15, Palette.Amber);
            Refresh();
        }

        // ------------------------------------------------------------ settings

        public void ShowSettings()
        {
            var r = Begin("settings");
            var pad = Page(r, "settings");
            var sheet = Ui.Box(pad, Palette.Sheet, "Settings").rectTransform;
            sheet.Place(new Vector2(0.5f, 1), new Vector2(0, -32), new Vector2(640, 650), new Vector2(0.5f, 1));
            TopRule(sheet, Palette.Amber);
            var stack = Ui.VStack(sheet, 6);
            stack.Fill();
            stack.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(32, 32, 24, 24);
            Ui.Label(stack, "設定", 30, Palette.Bone, TextAnchor.UpperLeft, true).Sized(40);
            GameSettings.BuildPanel(stack);
            Ui.Label(stack, "設定會儲存在這台電腦，下次開啟遊戲自動套用。", 12, Palette.Muted).Sized(24);
        }

        // ------------------------------------------------------------ 商城 (supply depot)

        // ------------------------------------------------------------ daily missions

        Text dailyStatus;

        public void ShowDaily()
        {
            var r = Begin("daily");
            var p = app.Profile;
            var daily = p.Daily;
            var pad = Page(r, "daily");
            pad.offsetMin = new Vector2(48, 36); pad.offsetMax = new Vector2(-48, -NavHeight - 28);
            MissionTabs(pad, 0);
            int left = Mathf.Max(0, daily.ResetsIn - (int)(Time.realtimeSinceStartup - daily.FetchedAt));
            Ui.Label(pad, $"每天 00:00（台灣時間）更換 · 距離更新 {left / 3600} 小時 {left % 3600 / 60} 分 · 進度在連線任務結算時計入", 13, Palette.Muted, TextAnchor.MiddleLeft)
                .rectTransform.Place(new Vector2(0, 1), new Vector2(336, 0), new Vector2(560, 40), new Vector2(0, 1));
            dailyStatus = Ui.Label(pad, "", 14, Palette.Amber, TextAnchor.UpperRight);
            dailyStatus.rectTransform.Place(new Vector2(1, 1), new Vector2(0, -10), new Vector2(420, 20), new Vector2(1, 1));

            // Task list on the left, difficulty guide on the right.
            var list = Ui.VStack(pad, 12);
            list.anchorMin = new Vector2(0, 0); list.anchorMax = new Vector2(0.64f, 1);
            list.offsetMin = Vector2.zero; list.offsetMax = new Vector2(-12, -60);
            if (daily.Tasks.Count == 0) Ui.Label(list, "目前無法取得每日任務，請確認已連線到帳號伺服器。", 16, Palette.Muted).Sized(40);
            else
            {
                for (int i = 0; i < daily.Tasks.Count; i++) TaskCard(list, daily.Tasks[i], i);
                Ui.Rect(list, "Gap").Sized(4);
                BonusCard(list, daily);
            }

            var guide = Ui.Box(pad, Palette.Sheet, "Guide").rectTransform;
            guide.anchorMin = new Vector2(0.64f, 0); guide.anchorMax = new Vector2(1, 1);
            guide.offsetMin = new Vector2(12, 0); guide.offsetMax = new Vector2(0, -60);
            TopRule(guide, Palette.Amber);
            var g = Ui.VStack(guide, 10);
            g.Fill();
            g.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(28, 28, 24, 22);
            Caps(g, "DIFFICULTY", Palette.Amber).Sized(16);
            Ui.Label(g, "難度與通關紀錄", 24, Palette.Bone, TextAnchor.UpperLeft, true).Sized(32);
            Ui.Label(g, "在區域通關後，該區域會開放下一級難度。難度越高，金幣與經驗越多。", 13, Palette.Muted).Sized(38);
            foreach (var def in Defs.Difficulties)
            {
                var row = Ui.Rect(g, "Tier").Sized(58);
                Ui.Box(row, def.Accent, "Mark").rectTransform.Place(new Vector2(0, 1), new Vector2(0, -2), new Vector2(3, 50), new Vector2(0, 1));
                Ui.Label(row, def.Name, 17, def.Accent, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(14, 0), new Vector2(120, 22), new Vector2(0, 1));
                var text = Ui.Label(row, def.Summary, 12, Palette.Hex("c9c5bb")).rectTransform;
                text.anchorMin = Vector2.zero; text.anchorMax = Vector2.one; text.offsetMin = new Vector2(14, 0); text.offsetMax = new Vector2(0, -24);
            }
            Ui.Box(g, Palette.Line, "Rule").Sized(1);
            Caps(g, "各區域最高通關").Sized(18);
            for (int i = 0; i < Defs.Regions.Length; i++)
            {
                var row = Ui.Rect(g, "Cleared").Sized(22);
                bool reachable = i <= p.Unlocked;
                Ui.Label(row, $"0{i + 1}  {Defs.Regions[i].Name}", 14, reachable ? Palette.Bone : Palette.Muted, TextAnchor.MiddleLeft).rectTransform.Fill();
                int best = i < p.Cleared.Count ? p.Cleared[i] : -1;
                Color mark = best >= 0 ? (Color)Defs.Difficulty(best).Accent : Palette.Muted;
                Ui.Label(row, best >= 0 ? Defs.Difficulty(best).Name : reachable ? "尚未通關" : "未解鎖", 14, mark, TextAnchor.MiddleRight, best >= 0).rectTransform.Fill();
            }
        }

        void TaskCard(Transform parent, DailyTask task, int index)
        {
            bool ready = task.Done && !task.Claimed;
            var card = Ui.Box(parent, ready ? new Color(0.115f, 0.105f, 0.085f, 1f) : Palette.Sheet, "Task").Sized(104);
            if (ready) Ui.Frame(card.rectTransform, Palette.Amber, 1f, 3f);
            else card.gameObject.AddComponent<Outline>().effectColor = Palette.Line;
            var c = card.rectTransform;
            Ui.Label(c, $"0{index + 1}", 14, task.Done ? Palette.Amber : Palette.Muted).rectTransform.Place(new Vector2(0, 1), new Vector2(22, -20), new Vector2(30, 20), new Vector2(0, 1));
            Ui.Label(c, task.Text, 20, task.Claimed ? Palette.Muted : Palette.Bone, TextAnchor.UpperLeft, true)
                .rectTransform.Place(new Vector2(0, 1), new Vector2(60, -16), new Vector2(460, 28), new Vector2(0, 1));
            // Progress bar with the count beside it.
            var track = Ui.Box(c, new Color(1, 1, 1, 0.08f), "Track").rectTransform;
            track.Place(new Vector2(0, 1), new Vector2(60, -56), new Vector2(330, 6), new Vector2(0, 1));
            var fill = Ui.Box(track, task.Done ? Palette.Amber : Palette.Bone, "Fill").rectTransform;
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(Mathf.Clamp01((float)task.Progress / task.Goal), 1); fill.offsetMin = fill.offsetMax = Vector2.zero;
            Ui.Label(c, $"{task.Progress} / {task.Goal}", 14, task.Done ? Palette.Amber : Palette.Muted, TextAnchor.MiddleLeft, task.Done)
                .rectTransform.Place(new Vector2(0, 1), new Vector2(402, -49), new Vector2(110, 20), new Vector2(0, 1));
            RewardRow(c, task.Reward, new Vector2(60, -72), task.Claimed);
            if (!ready)
            {
                Ui.Label(c, task.Claimed ? "已領取" : "進行中", 16, task.Claimed ? Palette.Muted : Palette.Hex("b9b5ab"), TextAnchor.MiddleRight).rectTransform
                    .Place(new Vector2(1, 0.5f), new Vector2(-30, 0), new Vector2(124, 48), new Vector2(1, 0.5f));
                return;
            }
            var button = PrimaryButton(c, "領取", () => Claim(task.Id), 18);
            button.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), new Vector2(-22, 0), new Vector2(124, 48), new Vector2(1, 0.5f));
        }

        void BonusCard(Transform parent, DailyMissions daily)
        {
            bool ready = daily.BonusReady && !daily.BonusClaimed;
            var accent = Gear.RarityColors[3];
            var card = Ui.Box(parent, ready ? new Color(0.115f, 0.105f, 0.085f, 1f) : Palette.Sheet, "Bonus").Sized(96);
            if (ready) Ui.Frame(card.rectTransform, accent, 1f, 3f);
            else card.gameObject.AddComponent<Outline>().effectColor = Palette.Line;
            var c = card.rectTransform;
            Ui.Box(c, accent, "Mark").rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(3, 96), new Vector2(0, 0.5f));
            Caps(c, "DAILY BONUS", accent).rectTransform.Place(new Vector2(0, 1), new Vector2(22, -14), new Vector2(200, 16), new Vector2(0, 1));
            Ui.Label(c, $"全部完成獎勵  ·  已領 {daily.ClaimedCount} / {daily.Tasks.Count}", 19, daily.BonusClaimed ? Palette.Muted : Palette.Bone, TextAnchor.UpperLeft, true)
                .rectTransform.Place(new Vector2(0, 1), new Vector2(22, -32), new Vector2(460, 26), new Vector2(0, 1));
            RewardRow(c, daily.Bonus, new Vector2(22, -66), daily.BonusClaimed);
            if (!ready)
            {
                Ui.Label(c, daily.BonusClaimed ? "已領取" : "未完成", 16, Palette.Muted, TextAnchor.MiddleRight).rectTransform
                    .Place(new Vector2(1, 0.5f), new Vector2(-30, 0), new Vector2(124, 48), new Vector2(1, 0.5f));
                return;
            }
            var button = Ui.Button(c, "領取", () => Claim("bonus"), 18, Palette.Ink, accent, accent);
            button.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), new Vector2(-22, 0), new Vector2(124, 48), new Vector2(1, 0.5f));
        }

        /// Gold / gear key / weapon key amounts in a row, icons in their resource colours.
        static void RewardRow(RectTransform parent, DailyReward reward, Vector2 at, bool dim, int size = 14, string extra = null)
        {
            float x = at.x, icon = size + 4, gap = size >= 14 ? 20 : 10;
            void Item(Image mark, string text, Color color)
            {
                if (mark != null)
                {
                    mark.rectTransform.Place(new Vector2(0, 1), new Vector2(x, at.y), new Vector2(icon, icon), new Vector2(0, 1));
                    if (dim) mark.color = new Color(mark.color.r, mark.color.g, mark.color.b, 0.4f);
                    x += icon + 4;
                }
                var label = Ui.Label(parent, text, size, dim ? Palette.Muted : color, TextAnchor.MiddleLeft, true);
                label.rectTransform.Place(new Vector2(0, 1), new Vector2(x, at.y + 1), new Vector2(130, icon + 2), new Vector2(0, 1));
                x += label.preferredWidth + gap;
            }
            bool compact = size < 14;
            if (reward.Gold > 0) Item(Icons.Coin(parent, icon), compact ? $"+{reward.Gold}" : $"+{reward.Gold} 金幣", Palette.Amber);
            if (reward.Keys > 0) Item(Icons.Key(parent, icon, Gear.GearKeyColor), compact ? $"+{reward.Keys}" : $"+{reward.Keys} 裝備鑰匙", Gear.GearKeyColor);
            if (reward.WeaponKeys > 0) Item(Icons.Key(parent, icon, Gear.WeaponKeyColor), compact ? $"+{reward.WeaponKeys}" : $"+{reward.WeaponKeys} 武器鑰匙", Gear.WeaponKeyColor);
            if (reward.Shards > 0) Item(Icons.Shard(parent, icon), compact ? $"+{reward.Shards}" : $"+{reward.Shards} 碎片", Gear.ShardColor);
            if (!string.IsNullOrEmpty(extra)) Item(null, "+" + extra, Palette.Bone);
        }

        void Claim(string task)
        {
            if (dailyStatus != null) { dailyStatus.color = Palette.Amber; dailyStatus.text = "領取中…"; }
            app.Api.Post("/api/daily/claim", new Dictionary<string, object> { ["task"] = task }, result =>
            {
                if (Mode != "daily") return;
                if (!result.Bool("ok")) { dailyStatus.color = Palette.Danger; dailyStatus.text = result.Str("error", "領取失敗"); return; }
                app.UpdateProfile(Profile.From(result.Obj("profile")));
                var got = DailyReward.From(result.Obj("reward"));
                ShowDaily();
                var parts = new List<string>();
                if (got.Gold > 0) parts.Add($"金幣 +{got.Gold}");
                if (got.Keys > 0) parts.Add($"裝備鑰匙 +{got.Keys}");
                if (got.WeaponKeys > 0) parts.Add($"武器鑰匙 +{got.WeaponKeys}");
                dailyStatus.text = "已領取  " + string.Join("、", parts);
            });
        }

        // ------------------------------------------------------------ social: friends, mail, chat

        /// Sub-navigation inside the 社交 tab, with dots for friend requests and unread mail.
        void SocialTabs(RectTransform pad, int active)
        {
            SubTabs(pad, new[] { "好友", "信箱", "聊天" }, new Action[] { () => ShowFriends(), () => ShowMail(), ShowChat }, active);
            var p = app.Profile;
            bool[] waiting = { p.FriendRequests > 0 || app.PendingInvite != null, p.MailUnread + p.MailClaimable > 0, false };
            for (int i = 0; i < waiting.Length; i++)
                if (waiting[i] && i != active)
                    Ui.Box(pad, Palette.Amber, "Badge").rectTransform.Place(new Vector2(0, 1), new Vector2(i * 156 + 134, -8), new Vector2(7, 7), new Vector2(0, 1));
        }

        RectTransform SocialPage(string mode, int tab, out RectTransform pad)
        {
            var r = Begin(mode);
            pad = Page(r, "social");
            pad.offsetMin = new Vector2(48, 36); pad.offsetMax = new Vector2(-48, -NavHeight - 28);
            SocialTabs(pad, tab);
            socialStatus = Ui.Label(pad, "", 14, Palette.Amber, TextAnchor.MiddleRight);
            socialStatus.rectTransform.Place(new Vector2(1, 1), Vector2.zero, new Vector2(560, 40), new Vector2(1, 1));
            return r;
        }

        Text socialStatus;

        void SocialStatus(string text, bool error = false)
        {
            if (socialStatus == null) return;
            socialStatus.color = error ? Palette.Danger : Palette.Amber;
            socialStatus.text = text;
        }

        static RectTransform Panel(RectTransform pad, float x0, float x1, Color accent, string name)
        {
            var box = Ui.Box(pad, Palette.Sheet, name).rectTransform;
            box.anchorMin = new Vector2(x0, 0); box.anchorMax = new Vector2(x1, 1);
            box.offsetMin = new Vector2(x0 > 0 ? 8 : 0, 0); box.offsetMax = new Vector2(x1 < 1 ? -8 : 0, -60);
            TopRule(box, accent);
            var st = Ui.VStack(box, 8);
            st.Fill();
            st.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(24, 24, 20, 20);
            return st;
        }

        // ---- friends

        Dictionary<string, object> friendData;
        readonly Dictionary<int, (Text status, Image dot, Button invite, RectTransform row, string name)> friendRows = new Dictionary<int, (Text, Image, Button, RectTransform, string)>();
        Text friendCount, friendHint; int friendTotal;
        static string FriendHint(bool online) => online ? "按「邀請」會把好友拉進你的房間；你還沒有房間時會自動建立一個。" : "目前未連線到遊戲伺服器，無法顯示上線狀態或邀請好友。";

        public void ShowFriends(bool fetch = true)
        {
            SocialPage("friends", 0, out var pad);
            var p = app.Profile;
            friendRows.Clear();
            friendJoin.Clear();
            var friends = (friendData?.Arr("friends") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
            var incoming = (friendData?.Arr("incoming") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
            var outgoing = (friendData?.Arr("outgoing") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();

            var left = Panel(pad, 0, 0.62f, Palette.Teammate, "Friends");
            var head = Ui.Rect(left, "Head").Sized(30);
            Ui.Label(head, "好友名單", 22, Palette.Bone, TextAnchor.MiddleLeft, true).rectTransform.Fill();
            int online = friends.Count(f => FriendPresence.StateOf(f.Int("id")) != PresenceState.Offline);
            friendTotal = friends.Count;
            friendCount = Ui.Label(head, $"線上 {online} · 共 {friends.Count} / 100", 13, Palette.Muted, TextAnchor.MiddleRight);
            friendCount.rectTransform.Fill();
            friendHint = Ui.Label(left, FriendHint(app.Net.Welcomed), 13, Palette.Muted);
            friendHint.Sized(20);
            foreach (var f in friends) { long seen = f.Int("last_seen"); if (seen > 0 && !FriendPresence.LastSeen.ContainsKey(f.Int("id"))) FriendPresence.LastSeen[f.Int("id")] = seen; }
            var list = Widgets.ScrollList(left, out _);
            list.parent.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            if (friendData == null) Ui.Label(list, "載入中…", 14, Palette.Muted).Sized(40);
            else if (friends.Count == 0) Ui.Label(list, "還沒有好友。在右側輸入對方的帳號送出邀請吧。", 14, Palette.Muted).Sized(40);
            // Online friends first, then by name.
            foreach (var f in friends.OrderBy(f => FriendPresence.StateOf(f.Int("id")) == PresenceState.Offline ? 1 : 0).ThenBy(f => f.Str("username"), StringComparer.OrdinalIgnoreCase))
                FriendRow(list, f);

            var right = Panel(pad, 0.62f, 1, Palette.Amber, "Requests");
            if (app.PendingInvite.HasValue)
            {
                var (from, name, code) = app.PendingInvite.Value;
                var card = Ui.Box(right, new Color(Palette.Teammate.r, Palette.Teammate.g, Palette.Teammate.b, 0.1f), "Invite").Sized(88);
                card.gameObject.AddComponent<Outline>().effectColor = Palette.Teammate;
                Ui.Label(card.transform, "組隊邀請", 12, Palette.Teammate, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(16, -10), new Vector2(200, 16), new Vector2(0, 1));
                Ui.Label(card.transform, $"{name} 邀請你加入房間 {code}", 15, Palette.Bone, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(16, -28), new Vector2(330, 22), new Vector2(0, 1));
                var row = Ui.HStack(card.transform, 8);
                row.Place(new Vector2(1, 0), new Vector2(-12, 10), new Vector2(200, 30), new Vector2(1, 0));
                Ui.Button(row, "婉拒", () => { app.DeclineInvite(from); ShowFriends(false); }, 13, Palette.Muted);
                Ui.Button(row, "加入房間", () => app.AcceptInvite(code), 14, Palette.Ink, Palette.Teammate, Palette.Teammate);
            }
            Ui.Label(right, "新增好友", 18, Palette.Bone, TextAnchor.UpperLeft, true).Sized(26);
            var add = Ui.HStack(right, 8).Sized(42);
            add.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            var nameInput = Widgets.Input(add, "輸入對方的帳號", false, 24);
            nameInput.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var send = PrimaryButton(add, "送出邀請", () => FriendRequest("/api/friends/request", new Dictionary<string, object> { ["username"] = nameInput.text.Trim() }, "已送出好友邀請"), 15);
            send.gameObject.AddComponent<LayoutElement>().preferredWidth = 110;
            Ui.Rect(right, "Gap").Sized(6);
            Widgets.Section(right, $"收到的邀請（{incoming.Count}）").Sized(22);
            if (incoming.Count == 0) Ui.Label(right, "目前沒有新的好友邀請", 13, Palette.Muted).Sized(24);
            foreach (var f in incoming)
            {
                string who = f.Str("username");
                var row = Ui.Rect(right, "Incoming").Sized(38);
                PersonLabel(row, f, 15, 200);
                var buttons = Ui.HStack(row, 6);
                buttons.Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(150, 32), new Vector2(1, 0.5f));
                Ui.DangerButton(buttons, "拒絕", () => FriendRequest("/api/friends/respond", new Dictionary<string, object> { ["username"] = who, ["accept"] = false }, $"已拒絕 {who}"), 13);
                Ui.Button(buttons, "接受", () => FriendRequest("/api/friends/respond", new Dictionary<string, object> { ["username"] = who, ["accept"] = true }, $"{who} 已成為你的好友"), 13, Palette.Ink, Palette.Amber, Palette.Amber);
            }
            Widgets.Section(right, $"已送出（{outgoing.Count}）").Sized(22);
            if (outgoing.Count == 0) Ui.Label(right, "沒有等待回應的邀請", 13, Palette.Muted).Sized(24);
            foreach (var f in outgoing.Take(6))
            {
                string who = f.Str("username");
                var row = Ui.Rect(right, "Outgoing").Sized(34);
                PersonLabel(row, f, 14, 200);
                Ui.Label(row, "等待回應", 12, Palette.Muted, TextAnchor.MiddleRight).rectTransform.Place(new Vector2(1, 0.5f), new Vector2(-78, 0), new Vector2(80, 30), new Vector2(1, 0.5f));
                Ui.DangerButton(row, "取消", () => FriendRequest("/api/friends/remove", new Dictionary<string, object> { ["username"] = who }, "已取消邀請"), 13)
                    .GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(64, 28), new Vector2(1, 0.5f));
            }
            if (fetch) LoadFriends();
        }

        /// Title (amber) + name + level in one row, as a pair of labels.
        static void PersonLabel(RectTransform row, Dictionary<string, object> f, int size, float x)
        {
            string title = f.Str("title");
            float at = 0;
            if (title != "")
            {
                var t = Ui.Label(row, $"〔{title}〕", size, Palette.Amber, TextAnchor.MiddleLeft, true);
                t.rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(160, 30), new Vector2(0, 0.5f));
                at = t.preferredWidth + 2;
            }
            var n = Ui.Label(row, f.Str("username"), size, Palette.Bone, TextAnchor.MiddleLeft, true);
            n.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(at, 0), new Vector2(220, 30), new Vector2(0, 0.5f));
            Ui.Label(row, $"Lv {f.Int("level", 1)}", size - 3, Palette.Muted, TextAnchor.MiddleLeft).rectTransform
                .Place(new Vector2(0, 0.5f), new Vector2(at + n.preferredWidth + 10, 0), new Vector2(60, 30), new Vector2(0, 0.5f));
        }

        void FriendRow(RectTransform list, Dictionary<string, object> f)
        {
            int id = f.Int("id");
            string who = f.Str("username");
            var row = Ui.Box(list, new Color(1, 1, 1, 0.025f), "Friend").Sized(56);
            var rt = row.rectTransform;
            var dot = Ui.Box(rt, FriendPresence.ColorOf(id), "Dot");
            dot.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(9, 9), new Vector2(0, 0.5f));
            var info = Ui.Rect(rt, "Info").Place(new Vector2(0, 1), new Vector2(36, -4), new Vector2(360, 26), new Vector2(0, 1));
            PersonLabel(info, f, 16, 0);
            var status = Ui.Label(rt, FriendPresence.Describe(id), 12, FriendPresence.ColorOf(id), TextAnchor.UpperLeft);
            status.rectTransform.Place(new Vector2(0, 1), new Vector2(36, -32), new Vector2(240, 18), new Vector2(0, 1));
            Button remove = null;
            bool armed = false;
            remove = Ui.DangerButton(rt, "移除", () =>
            {
                if (!armed) { armed = true; remove.SetCaption("確定移除？"); return; }
                FriendRequest("/api/friends/remove", new Dictionary<string, object> { ["username"] = who }, $"已將 {who} 移出好友");
            }, 13);
            remove.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(84, 30), new Vector2(1, 0.5f));
            var invite = Ui.Button(rt, "邀請", () => { app.InviteFriend(id); SocialStatus($"已向 {who} 送出組隊邀請"); }, 14, Palette.Teammate, null, Palette.Teammate);
            invite.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), new Vector2(-104, 0), new Vector2(76, 32), new Vector2(1, 0.5f));
            PaintInvite(invite, app.Net.Welcomed && FriendPresence.StateOf(id) != PresenceState.Offline);
            var whisper = Ui.Button(rt, "私訊", () => ShowWhisper(who), 14, Palette.Hex("ff8fd0"));
            whisper.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), new Vector2(-188, 0), new Vector2(68, 32), new Vector2(1, 0.5f));
            // 申請入隊: only while the friend is in a room (or a mission) that is not yours.
            var join = Ui.Button(rt, "申請入隊", () => { app.RequestJoin(id); SocialStatus($"已向 {who} 申請入隊"); }, 14, Palette.Amber, null, Palette.Amber);
            join.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), new Vector2(-264, 0), new Vector2(96, 32), new Vector2(1, 0.5f));
            friendJoin[id] = join;
            PaintJoin(join, id);
            friendRows[id] = (status, dot, invite, rt, who ?? "");
        }

        readonly Dictionary<int, Button> friendJoin = new Dictionary<int, Button>();

        void PaintJoin(Button join, int id)
        {
            byte state = FriendPresence.StateOf(id);
            bool can = (app.Net.Welcomed || PreviewRoom) && (state == PresenceState.InRoom || state == PresenceState.InMission);
            join.gameObject.SetActive(can);
        }

        static void PaintInvite(Button invite, bool can)
        {
            invite.interactable = can;
            // Disabled dimming is ButtonFeel's job; here only the colour of an available invite.
            invite.GetComponentInChildren<Text>().color = can ? Palette.Teammate : Palette.Muted;
            invite.GetComponent<Outline>().effectColor = can ? Palette.Teammate : Palette.Line;
        }

        /// Presence arrives every few seconds: repaint the rows in place so typing in the page is not interrupted.
        void RepaintPresence()
        {
            if (Mode != "friends") return;
            foreach (var kv in friendRows)
            {
                if (kv.Value.status == null) continue;
                kv.Value.status.text = FriendPresence.Describe(kv.Key);
                kv.Value.status.color = FriendPresence.ColorOf(kv.Key);
                kv.Value.dot.color = FriendPresence.ColorOf(kv.Key);
                PaintInvite(kv.Value.invite, app.Net.Welcomed && FriendPresence.StateOf(kv.Key) != PresenceState.Offline);
                if (friendJoin.TryGetValue(kv.Key, out var join) && join != null) PaintJoin(join, kv.Key);
            }
            // The count and the online-first order were drawn before presence arrived: keep them in step too.
            int online = friendRows.Keys.Count(id => FriendPresence.StateOf(id) != PresenceState.Offline);
            if (friendCount != null) friendCount.text = $"線上 {online} · 共 {friendTotal} / 100";
            if (friendHint != null) friendHint.text = FriendHint(app.Net.Welcomed);
            int index = 0;
            foreach (var kv in friendRows.OrderBy(kv => FriendPresence.StateOf(kv.Key) == PresenceState.Offline ? 1 : 0).ThenBy(kv => kv.Value.name, StringComparer.OrdinalIgnoreCase))
                if (kv.Value.row != null) kv.Value.row.SetSiblingIndex(index++);
        }

        void LoadFriends()
        {
            app.Api.Post("/api/friends/list", null, result =>
            {
                if (Mode != "friends") return;
                if (!result.Bool("ok")) { SocialStatus(result.Str("error", "無法讀取好友名單"), true); return; }
                friendData = result;
                app.UpdateProfile(Profile.From(result.Obj("profile")), false);
                ShowFriends(false);
            });
        }

        void FriendRequest(string path, Dictionary<string, object> data, string done)
        {
            SocialStatus("處理中…");
            app.Api.Post(path, data, result =>
            {
                if (Mode != "friends") return;
                if (!result.Bool("ok")) { SocialStatus(result.Str("error", "操作失敗"), true); return; }
                friendData = result;
                app.UpdateProfile(Profile.From(result.Obj("profile")), false);
                app.RefreshFriends();
                ShowFriends(false);
                if (path == "/api/friends/request" && result.Str("status") != "accepted" && data.TryGetValue("username", out var target)) app.NotifyFriendRequest(target as string);
                SocialStatus(result.Str("status") == "accepted" ? "對方也邀請過你，已直接成為好友" : done);
            });
        }

        // ---- mail

        List<Dictionary<string, object>> mailItems;
        int mailPick = -1;

        static string Date(int unix) => DateTimeOffset.FromUnixTimeSeconds(unix).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd");

        public void ShowMail(bool fetch = true)
        {
            SocialPage("mail", 1, out var pad);
            var items = mailItems ?? new List<Dictionary<string, object>>();
            if (mailPick < 0 && items.Count > 0) mailPick = items[0].Int("id");
            var pick = items.FirstOrDefault(m => m.Int("id") == mailPick);

            var left = Panel(pad, 0, 0.4f, Palette.Amber, "Inbox");
            var head = Ui.Rect(left, "Head").Sized(34);
            Ui.Label(head, "信箱", 22, Palette.Bone, TextAnchor.MiddleLeft, true).rectTransform.Fill();
            int claimable = items.Count(m => !m.Bool("claimed"));
            var all = Ui.Button(head, claimable > 0 ? $"全部領取（{claimable}）" : "全部領取", () => MailRequest("/api/mail/claim_all", null), 13, claimable > 0 ? Palette.Amber : Palette.Muted, new Color(0, 0, 0, 0), claimable > 0 ? Palette.Amber : Palette.Line);
            all.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(130, 32), new Vector2(1, 0.5f));
            all.interactable = claimable > 0;
            Ui.Label(left, "郵件保存 30 天，過期後連同附件一起消失。", 12, Palette.Muted).Sized(18);
            var list = Widgets.ScrollList(left, out _);
            list.parent.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            if (mailItems == null) Ui.Label(list, "載入中…", 14, Palette.Muted).Sized(40);
            else if (items.Count == 0) Ui.Label(list, "信箱是空的", 14, Palette.Muted).Sized(40);
            foreach (var m in items)
            {
                int id = m.Int("id");
                bool on = id == mailPick, unread = !m.Bool("read"), gift = !m.Bool("claimed");
                var b = Ui.Button(list, "", () => { mailPick = id; if (unread) MarkRead(id); ShowMail(false); }, 14, null,
                    on ? new Color(Palette.Amber.r, Palette.Amber.g, Palette.Amber.b, 0.1f) : new Color(1, 1, 1, 0.025f), on ? Palette.Amber : new Color(0, 0, 0, 0)).Sized(60);
                b.GetComponentInChildren<Text>().text = "";
                if (unread) Ui.Box(b.transform, Palette.Amber, "Unread").rectTransform.Place(new Vector2(0, 1), new Vector2(10, -16), new Vector2(6, 6), new Vector2(0, 1));
                Ui.Label(b.transform, m.Str("title"), 15, unread ? Palette.Bone : Palette.Hex("c9c5bb"), TextAnchor.UpperLeft, unread).rectTransform
                    .Place(new Vector2(0, 1), new Vector2(24, -9), new Vector2(250, 22), new Vector2(0, 1));
                Ui.Label(b.transform, $"遠征軍指揮部 · {Date(m.Int("created"))}", 11, Palette.Muted).rectTransform.Place(new Vector2(0, 1), new Vector2(24, -34), new Vector2(250, 16), new Vector2(0, 1));
                if (gift) Icons.Key(b.transform, 18, Gear.GearKeyColor).rectTransform.Place(new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(18, 18), new Vector2(1, 0.5f));
            }

            var right = Panel(pad, 0.4f, 1, Palette.Amber, "Letter");
            if (pick == null)
            {
                Ui.Label(right, mailItems == null ? "" : "選擇左側的郵件查看內容", 15, Palette.Muted).Sized(30);
            }
            else
            {
                Caps(right, "SYSTEM MAIL", Palette.Amber).Sized(16);
                Ui.Label(right, pick.Str("title"), 26, Palette.Bone, TextAnchor.UpperLeft, true).Sized(36);
                int daysLeft = Mathf.Max(0, Mathf.CeilToInt((pick.Int("expires") - (float)DateTimeOffset.UtcNow.ToUnixTimeSeconds()) / 86400f));
                Ui.Label(right, $"寄件者：遠征軍指揮部 · {Date(pick.Int("created"))} · 剩 {daysLeft} 天", 12, Palette.Muted).Sized(18);
                Ui.Box(right, Palette.Line, "Rule").Sized(1);
                var body = Ui.Label(right, pick.Str("body"), 15, Palette.Hex("d6d1c6"));
                body.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
                var reward = DailyReward.From(pick.Obj("reward"));
                bool hasGift = reward.Gold + reward.Keys + reward.WeaponKeys > 0, claimed = pick.Bool("claimed");
                if (hasGift)
                {
                    Widgets.Section(right, claimed ? "附件（已領取）" : "附件").Sized(22);
                    var gifts = Ui.Rect(right, "Gifts").Sized(26);
                    RewardRow(gifts, reward, new Vector2(0, -3), claimed);
                }
                var actions = Ui.HStack(right, 10).Sized(48);
                int id = pick.Int("id");
                if (hasGift && !claimed) PrimaryButton(actions, "領取附件", () => MailRequest("/api/mail/claim", new Dictionary<string, object> { ["id"] = id }), 17);
                Button delete = null;
                bool deleteArmed = false;
                delete = Ui.DangerButton(actions, "刪除郵件", () =>
                {
                    if (!deleteArmed) { deleteArmed = true; delete.SetCaption("確定刪除？"); return; }
                    MailRequest("/api/mail/delete", new Dictionary<string, object> { ["id"] = id }); mailPick = -1;
                }, 15);
                delete.interactable = claimed;
            }
            if (fetch) MailRequest("/api/mail/list", null);
        }

        void MarkRead(int id)
        {
            var m = mailItems?.FirstOrDefault(x => x.Int("id") == id);
            if (m != null) m["read"] = true;
            app.Api.Post("/api/mail/read", new Dictionary<string, object> { ["id"] = id }, result =>
            {
                if (result.Bool("ok")) app.UpdateProfile(Profile.From(result.Obj("profile")), false);
            });
        }

        void MailRequest(string path, Dictionary<string, object> data)
        {
            bool listing = path == "/api/mail/list";
            if (!listing) SocialStatus("處理中…");
            app.Api.Post(path, data, result =>
            {
                if (Mode != "mail") return;
                if (!result.Bool("ok")) { SocialStatus(result.Str("error", "操作失敗"), true); return; }
                mailItems = (result.Arr("mail") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
                app.UpdateProfile(Profile.From(result.Obj("profile")), false);
                ShowMail(false);
                var got = DailyReward.From(result.Obj("reward"));
                var parts = new List<string>();
                if (got.Gold > 0) parts.Add($"金幣 +{got.Gold}");
                if (got.Keys > 0) parts.Add($"裝備鑰匙 +{got.Keys}");
                if (got.WeaponKeys > 0) parts.Add($"武器鑰匙 +{got.WeaponKeys}");
                if (parts.Count > 0) SocialStatus("已領取  " + string.Join("、", parts));
                else if (path == "/api/mail/delete") SocialStatus("郵件已刪除");
            });
        }

        // ---- chat

        RectTransform chatList;
        ScrollRect chatScroll;
        byte chatChannel = ChatChannel.World;

        string chatPrefill;

        /// Opens the chat page with "/w 帳號 " ready in the input.
        public void ShowWhisper(string username) { chatPrefill = $"/w {username} "; ShowChat(); }

        public void ShowChat()
        {
            SocialPage("chat", 2, out var pad);
            var left = Panel(pad, 0, 0.7f, Palette.Amber, "Chat");
            var head = Ui.Rect(left, "Head").Sized(30);
            Ui.Label(head, "聊天室", 22, Palette.Bone, TextAnchor.MiddleLeft, true).rectTransform.Fill();
            Ui.Label(head, app.Net.Welcomed ? (app.InRoom ? $"房間 {app.RoomCode} · {app.PartySize} 人" : "未加入房間") : "未連線", 13, Palette.Muted, TextAnchor.MiddleRight).rectTransform.Fill();
            chatList = Widgets.ScrollList(left, out chatScroll);
            chatList.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(12, 12, 10, 10);
            chatList.parent.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            if (ChatLog.Lines.Count == 0) Ui.Label(chatList, app.Net.Welcomed ? "還沒有訊息，打聲招呼吧！" : "連線到遊戲伺服器後才能聊天。", 14, Palette.Muted).Sized(30);
            foreach (var line in ChatLog.Lines) ChatRow(line);
            // Online friends, one click away from a private message.
            var online = (friendData?.Arr("friends") ?? new List<object>()).OfType<Dictionary<string, object>>()
                .Where(f => FriendPresence.StateOf(f.Int("id")) != PresenceState.Offline).Take(5).ToList();
            var whisperRow = Ui.HStack(left, 6).Sized(30);
            whisperRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            Ui.Label(whisperRow, "私訊", 13, Palette.Hex("ff8fd0"), TextAnchor.MiddleLeft).gameObject.AddComponent<LayoutElement>().preferredWidth = 40;
            if (online.Count == 0)
                Ui.Label(whisperRow, app.Net.Welcomed ? "目前沒有在線的好友 · 也可以輸入 /w 帳號 訊息" : "連線後可私訊在線好友", 13, Palette.Muted, TextAnchor.MiddleLeft).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            foreach (var f in online)
            {
                string who = f.Str("username");
                var chip = Ui.Button(whisperRow, who, () => ShowWhisper(who), 13, Palette.Bone);
                chip.gameObject.AddComponent<LayoutElement>().preferredWidth = Mathf.Max(72, who.Length * 9 + 24);
            }
            var send = Ui.HStack(left, 8).Sized(44);
            send.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            Button channelButton = null;
            void PaintChannel()
            {
                channelButton.SetCaption(chatChannel == ChatChannel.Room ? "房間" : "全頻");
                channelButton.GetComponentInChildren<Text>().color = chatChannel == ChatChannel.Room ? Palette.Teammate : Palette.Bone;
                channelButton.GetComponent<Outline>().effectColor = chatChannel == ChatChannel.Room ? Palette.Teammate : Palette.Line;
            }
            channelButton = Ui.Button(send, "", () => { chatChannel = chatChannel == ChatChannel.Room ? ChatChannel.World : ChatChannel.Room; PaintChannel(); }, 15);
            channelButton.gameObject.AddComponent<LayoutElement>().preferredWidth = 76;
            PaintChannel();
            var input = Widgets.Input(send, app.Net.Welcomed ? "輸入訊息，按 Enter 送出 · /w 帳號 訊息 可私訊" : "未連線，無法發言", false, ChatChannel.MaxLength + 24);
            if (!string.IsNullOrEmpty(chatPrefill)) { input.text = chatPrefill; chatPrefill = null; input.ActivateInputField(); input.caretPosition = input.text.Length; }
            input.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            input.interactable = app.Net.Welcomed;
            void Submit()
            {
                string text = input.text.Trim();
                if (text == "") return;
                app.SendChat(chatChannel, text);
                input.text = "";
                input.ActivateInputField();
            }
            input.onEndEdit.AddListener(_ => { if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Submit(); });
            var go = PrimaryButton(send, "送出", Submit, 16);
            go.gameObject.AddComponent<LayoutElement>().preferredWidth = 96;
            go.interactable = app.Net.Welcomed;
            ScrollChatToEnd();

            var right = Panel(pad, 0.7f, 1, Palette.Line, "Rules");
            Ui.Label(right, "頻道說明", 18, Palette.Bone, TextAnchor.UpperLeft, true).Sized(26);
            Ui.Label(right, "全頻：所有線上玩家都看得到。\n房間：只有同房間的隊友看得到。\n私訊：輸入 /w 帳號 訊息，只有對方看得到；/r 訊息 回覆最後一位私訊你的人。\n\n戰鬥中按 Enter 開啟聊天，Tab 切換頻道。", 13, Palette.Hex("c9c5bb")).Sized(140);
            Widgets.Section(right, "聊天規範").Sized(22);
            Ui.Label(right, "每 10 秒最多 5 則訊息，每則最多 100 字。不當字詞會自動遮蔽，違規者可能被禁言。", 13, Palette.Muted).Sized(60);
            if (app.Profile.Admin)
            {
                Widgets.Section(right, "管理員指令").Sized(22);
                Ui.Label(right, "/mute 帳號 分鐘 — 禁言（預設 10 分鐘）\n/unmute 帳號 — 解除禁言\n過濾字詞：server-data/chat-filter.txt", 12, Palette.Danger).Sized(64);
            }
        }

        void ChatRow(ChatLog.Line line)
        {
            var label = Ui.Label(chatList, ChatLog.Format(line), 15, Palette.Bone);
            label.supportRichText = true;
            label.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        void OnChatLine(ChatLog.Line line)
        {
            if (Mode != "chat" || chatList == null) return;
            if (ChatLog.Lines.Count == 1) foreach (Transform child in chatList) UnityEngine.Object.Destroy(child.gameObject);
            bool atEnd = chatScroll == null || chatScroll.verticalNormalizedPosition < 0.02f;
            ChatRow(line);
            if (chatList.childCount > ChatLog.Capacity) UnityEngine.Object.Destroy(chatList.GetChild(0).gameObject);
            if (atEnd) ScrollChatToEnd();
        }

        void ScrollChatToEnd()
        {
            if (chatScroll == null) return;
            Canvas.ForceUpdateCanvases();
            chatScroll.verticalNormalizedPosition = 0f;
        }

        // ------------------------------------------------------------ achievements

        static readonly (string id, string name)[] AchievementCategories = { ("combat", "戰鬥"), ("clear", "通關"), ("support", "支援與成長"), ("collect", "收集") };
        static readonly Color[] TierColors = { Palette.Hex("cd8a4f"), Palette.Hex("c4ccd4"), Palette.Hex("ffcf4a") };
        Text achievementStatus;

        public void ShowAchievements()
        {
            var r = Begin("achievements");
            var p = app.Profile;
            var pad = Page(r, "daily");
            pad.offsetMin = new Vector2(48, 36); pad.offsetMax = new Vector2(-48, -NavHeight - 28);
            MissionTabs(pad, 1);
            int done = p.Achievements.Sum(a => a.Claimed), total = p.Achievements.Count * 3;
            Ui.Label(pad, $"已完成 {done} / {total} 階 · 每階都有獎勵，部分成就的金階附贈稱號", 13, Palette.Muted, TextAnchor.MiddleLeft)
                .rectTransform.Place(new Vector2(0, 1), new Vector2(336, 0), new Vector2(420, 40), new Vector2(0, 1));

            // Worn title and the button that changes it, top right.
            var titleBox = Ui.Rect(pad, "Title").Place(new Vector2(1, 1), Vector2.zero, new Vector2(330, 40), new Vector2(1, 1));
            Ui.Label(titleBox, "目前稱號", 12, Palette.Muted, TextAnchor.MiddleLeft).rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(60, 40), new Vector2(0, 0.5f));
            Ui.Label(titleBox, string.IsNullOrEmpty(p.Title) ? "未配戴" : $"〔{p.Title}〕", 17, string.IsNullOrEmpty(p.Title) ? Palette.Muted : Palette.Amber, TextAnchor.MiddleLeft, true)
                .rectTransform.Place(new Vector2(0, 0.5f), new Vector2(62, 0), new Vector2(140, 40), new Vector2(0, 0.5f));
            Ui.Button(titleBox, "更換稱號", ShowTitlePicker, 14, Palette.Amber, new Color(0, 0, 0, 0), Palette.Amber)
                .GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(110, 36), new Vector2(1, 0.5f));
            achievementStatus = Ui.Label(pad, "", 14, Palette.Amber, TextAnchor.MiddleRight);
            achievementStatus.rectTransform.Place(new Vector2(1, 1), new Vector2(-346, 0), new Vector2(360, 40), new Vector2(1, 1));

            if (p.Achievements.Count == 0)
            {
                Ui.Label(pad, "目前無法取得成就資料，請確認已連線到帳號伺服器。", 16, Palette.Muted).rectTransform.Place(new Vector2(0, 1), new Vector2(0, -70), new Vector2(800, 30), new Vector2(0, 1));
                return;
            }
            for (int c = 0; c < AchievementCategories.Length; c++)
            {
                var (cat, catName) = AchievementCategories[c];
                var items = p.Achievements.Where(a => a.Category == cat).ToList();
                float x0 = c / (float)AchievementCategories.Length, x1 = (c + 1) / (float)AchievementCategories.Length;
                var area = Ui.Rect(pad, "Column");
                area.anchorMin = new Vector2(x0, 0); area.anchorMax = new Vector2(x1, 1);
                area.offsetMin = new Vector2(c > 0 ? 8 : 0, 0); area.offsetMax = new Vector2(c < AchievementCategories.Length - 1 ? -8 : 0, -60);
                var head = Ui.Rect(area, "Head").Place(new Vector2(0, 1), Vector2.zero, new Vector2(0, 24), new Vector2(0, 1));
                head.anchorMax = new Vector2(1, 1); head.sizeDelta = new Vector2(0, 24);
                Ui.Label(head, catName, 17, Palette.Bone, TextAnchor.MiddleLeft, true).rectTransform.Fill();
                Ui.Label(head, $"{items.Sum(a => a.Claimed)} / {items.Count * 3}", 13, Palette.Muted, TextAnchor.MiddleRight).rectTransform.Fill();
                // The cards scroll inside their column, so more achievements never run off the page.
                var host = Ui.Rect(area, "Cards");
                host.anchorMin = Vector2.zero; host.anchorMax = Vector2.one; host.offsetMin = Vector2.zero; host.offsetMax = new Vector2(0, -34);
                var col = Widgets.ScrollList(host, out _);
                col.parent.GetComponent<RectTransform>().Fill();
                col.parent.GetComponent<Image>().color = new Color(0, 0, 0, 0);
                col.GetComponent<VerticalLayoutGroup>().spacing = 10;
                foreach (var a in items) AchievementCard(col, a);
            }
        }

        void AchievementCard(Transform parent, Achievement a)
        {
            bool ready = a.CanClaim;
            var card = Ui.Box(parent, ready ? new Color(0.115f, 0.105f, 0.085f, 1f) : Palette.Sheet, "Achievement").Sized(100);
            if (ready) Ui.Frame(card.rectTransform, Palette.Amber, 1f, 3f);
            else card.gameObject.AddComponent<Outline>().effectColor = Palette.Line;
            var c = card.rectTransform;
            Color tierColor = a.Complete ? TierColors[2] : TierColors[Mathf.Clamp(a.Claimed, 0, 2)];
            Ui.Label(c, a.Name, 16, a.Complete ? TierColors[2] : Palette.Bone, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(14, -9), new Vector2(170, 22), new Vector2(0, 1));
            // Tier pips: filled once claimed.
            for (int t = 0; t < 3; t++)
            {
                var pip = Ui.Box(c, t < a.Claimed ? TierColors[t] : new Color(1, 1, 1, 0.1f), "Pip").rectTransform;
                pip.Place(new Vector2(1, 1), new Vector2(-14 - (2 - t) * 18, -15), new Vector2(14, 5), new Vector2(1, 1));
            }
            Ui.Label(c, a.Text, 13, Palette.Hex("b9b5ab")).rectTransform.Place(new Vector2(0, 1), new Vector2(14, -33), new Vector2(256, 18), new Vector2(0, 1));
            if (a.Complete)
            {
                Ui.Label(c, string.IsNullOrEmpty(a.Title) ? "全部完成" : $"全部完成 · 稱號〔{a.Title}〕", 13, TierColors[2], TextAnchor.UpperLeft, true)
                    .rectTransform.Place(new Vector2(0, 1), new Vector2(14, -58), new Vector2(240, 20), new Vector2(0, 1));
                return;
            }
            var track = Ui.Box(c, new Color(1, 1, 1, 0.08f), "Track").rectTransform;
            track.Place(new Vector2(0, 1), new Vector2(14, -60), new Vector2(100, 5), new Vector2(0, 1));
            var fill = Ui.Box(track, ready ? Palette.Amber : tierColor, "Fill").rectTransform;
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(Mathf.Clamp01((float)a.Progress / a.Goal), 1); fill.offsetMin = fill.offsetMax = Vector2.zero;
            Ui.Label(c, $"{Mathf.Min(a.Progress, a.Goal)} / {a.Goal}", 12, ready ? Palette.Amber : Palette.Muted, TextAnchor.MiddleLeft, ready)
                .rectTransform.Place(new Vector2(0, 1), new Vector2(122, -53), new Vector2(80, 18), new Vector2(0, 1));
            RewardRow(c, a.Reward, new Vector2(14, -76), false, 12, a.Claimed == 2 && !string.IsNullOrEmpty(a.Title) ? $"稱號〔{a.Title}〕" : null);
            if (!ready)
            {
                Ui.Label(c, $"下一階 · {AchievementTier(a.Claimed)}", 13, tierColor, TextAnchor.MiddleRight).rectTransform
                    .Place(new Vector2(1, 1), new Vector2(-14, -52), new Vector2(90, 32), new Vector2(1, 1));
                return;
            }
            var button = PrimaryButton(c, $"領取{AchievementTier(a.Claimed)}", () => ClaimAchievement(a.Id), 14);
            button.GetComponent<RectTransform>().Place(new Vector2(1, 1), new Vector2(-12, -52), new Vector2(64, 32), new Vector2(1, 1));
        }

        static string AchievementTier(int claimed) => new[] { "銅", "銀", "金" }[Mathf.Clamp(claimed, 0, 2)];

        void ClaimAchievement(string id)
        {
            if (achievementStatus != null) { achievementStatus.color = Palette.Amber; achievementStatus.text = "領取中…"; }
            app.Api.Post("/api/achievement/claim", new Dictionary<string, object> { ["achievement"] = id }, result =>
            {
                if (Mode != "achievements") return;
                if (!result.Bool("ok")) { achievementStatus.color = Palette.Danger; achievementStatus.text = result.Str("error", "領取失敗"); return; }
                app.UpdateProfile(Profile.From(result.Obj("profile")));
                var claim = result.Obj("claim");
                ShowAchievements();
                string title = claim?.Str("title") ?? "";
                achievementStatus.text = title != "" ? $"獲得稱號〔{title}〕！" : "已領取" + AchievementTier(claim?.Int("tier") ?? 0) + "階獎勵";
            });
        }

        public void ShowTitlePickerForCapture() => ShowTitlePicker();
        public void ShowAbilitiesForCapture() => ShowAbilityOverlay();

        // Sample data for the automated screenshots (no account service in capture mode).
        public void DemoFriends(Dictionary<string, object> data) { friendData = data; ShowFriends(false); }
        public void DemoMail(List<Dictionary<string, object>> items) { mailItems = items; mailPick = -1; ShowMail(false); }
        public void DemoAudit(List<Dictionary<string, object>> entries)
        {
            if (auditList == null) return;
            foreach (Transform child in auditList) UnityEngine.Object.Destroy(child.gameObject);
            foreach (var e in entries) AuditRow(e);
        }

        /// Overlay listing every earned title plus "none".
        void ShowTitlePicker()
        {
            var p = app.Profile;
            var earned = p.Achievements.Where(a => a.Complete && !string.IsNullOrEmpty(a.Title)).ToList();
            var scrim = Ui.Box(root, new Color(0.03f, 0.031f, 0.034f, 0.82f), "TitlePicker").rectTransform;
            scrim.Fill();
            scrim.gameObject.AddComponent<Button>().onClick.AddListener(() => UnityEngine.Object.Destroy(scrim.gameObject));
            float height = 150 + Mathf.Max(1, earned.Count + 1) * 50 + (earned.Count == 0 ? 40 : 0);
            var sheet = Ui.Box(scrim, Palette.Sheet, "Sheet").rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440, height));
            sheet.gameObject.AddComponent<Button>();   // swallow clicks so they do not close the picker
            TopRule(sheet, Palette.Amber);
            var s = Ui.VStack(sheet, 8);
            s.Fill();
            s.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(28, 28, 24, 22);
            Caps(s, "TITLE", Palette.Amber).Sized(16);
            Ui.Label(s, "更換稱號", 24, Palette.Bone, TextAnchor.UpperLeft, true).Sized(32);
            Ui.Label(s, "稱號會顯示在你的名字前，隊友在戰鬥中也看得到。", 13, Palette.Muted).Sized(22);
            if (earned.Count == 0) Ui.Label(s, "還沒有稱號。完成成就的金階即可獲得。", 14, Palette.Muted).Sized(36);
            void Option(string id, string label)
            {
                bool on = p.TitleId == id;
                var b = Ui.Button(s, on ? label + "   · 配戴中" : label, () => SetTitle(id, scrim.gameObject), 16, on ? Palette.Amber : Palette.Bone,
                    on ? new Color(Palette.Amber.r, Palette.Amber.g, Palette.Amber.b, 0.1f) : new Color(1, 1, 1, 0.035f), on ? Palette.Amber : Palette.Line).Sized(42);
                b.interactable = !on;
            }
            foreach (var a in earned) Option(a.Id, $"〔{a.Title}〕  {a.Name}");
            Option("", "不顯示稱號");
        }

        void SetTitle(string id, GameObject picker)
        {
            app.Api.Post("/api/achievement/title", new Dictionary<string, object> { ["title"] = id }, result =>
            {
                if (Mode != "achievements") return;
                if (picker != null) UnityEngine.Object.Destroy(picker);
                if (!result.Bool("ok")) { achievementStatus.color = Palette.Danger; achievementStatus.text = result.Str("error", "更換失敗"); return; }
                app.UpdateProfile(Profile.From(result.Obj("profile")));
                ShowAchievements();
                achievementStatus.text = id == "" ? "已取下稱號" : "稱號已更換";
            });
        }

        public void ShowShop()
        {
            var r = Begin("shop");
            characterRoot = r;
            var p = app.Profile;
            var pad = Page(r, "shop");
            pad.offsetMin = new Vector2(48, 36); pad.offsetMax = new Vector2(-48, -NavHeight - 28);
            Ui.Label(pad, "補給站", 34, Palette.Bone, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), Vector2.zero, new Vector2(400, 44), new Vector2(0, 1));
            Ui.Label(pad, "使用鑰匙開啟高科技補給艙。裝備與武器分開抽取，鑰匙可從每日任務取得。", 14, Palette.Muted).rectTransform.Place(new Vector2(0, 1), new Vector2(0, -46), new Vector2(800, 20), new Vector2(0, 1));
            characterStatus = Ui.Label(pad, "", 14, Palette.Amber, TextAnchor.UpperRight);
            characterStatus.rectTransform.Place(new Vector2(1, 1), new Vector2(0, -10), new Vector2(520, 20), new Vector2(1, 1));
            SupplyPanel(pad, 0f, 0.5f, "gear");
            SupplyPanel(pad, 0.5f, 1f, "weapon");
        }

        // ---- gacha history (each account sees only its own draws; the server keeps the latest 500)

        List<Dictionary<string, object>> gachaHistory;
        Dictionary<string, object> gachaTotals;
        string gachaFilter = "";

        public void ShowGachaHistory(string filter, bool fetch = true)
        {
            gachaFilter = filter;
            var r = Begin("gachalog");
            var pad = Page(r, "shop");
            pad.offsetMin = new Vector2(48, 36); pad.offsetMax = new Vector2(-48, -NavHeight - 28);
            SubTabs(pad, new[] { "全部", "裝備補給", "武器補給" }, new Action[] { () => ShowGachaHistory(""), () => ShowGachaHistory("gear"), () => ShowGachaHistory("weapon") },
                filter == "gear" ? 1 : filter == "weapon" ? 2 : 0);
            var back = Ui.Button(pad, "返回補給站", ShowShop, 15, Palette.Bone, new Color(0, 0, 0, 0), Palette.Line);
            back.GetComponent<RectTransform>().Place(new Vector2(1, 1), Vector2.zero, new Vector2(132, 40), new Vector2(1, 1));
            var status = Ui.Label(pad, "", 14, Palette.Amber, TextAnchor.MiddleRight);
            status.rectTransform.Place(new Vector2(1, 1), new Vector2(-150, 0), new Vector2(400, 40), new Vector2(1, 1));

            var rows = (gachaHistory ?? new List<Dictionary<string, object>>()).Where(h => filter == "" || h.Str("pool") == filter).ToList();
            Color accent = filter == "weapon" ? Palette.Danger : filter == "gear" ? Palette.Hex("b07cff") : Palette.Amber;

            // Left: summary — totals, rarity breakdown, draws since the last 傳奇+.
            var left = Panel(pad, 0, 0.32f, accent, "Summary");
            Caps(left, "GACHA HISTORY", accent).Sized(16);
            Ui.Label(left, "抽卡紀錄", 26, Palette.Bone, TextAnchor.UpperLeft, true).Sized(36);
            int total = gachaTotals == null ? 0 : filter == "" ? gachaTotals.Int("gear") + gachaTotals.Int("weapon") : gachaTotals.Int(filter);
            var big = Ui.Rect(left, "Total").Sized(56);
            Ui.Label(big, "累計抽數", 12, Palette.Muted).rectTransform.Place(new Vector2(0, 1), Vector2.zero, new Vector2(200, 18), new Vector2(0, 1));
            Ui.Label(big, gachaHistory == null ? "—" : total.ToString(), 32, accent, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(0, -18), new Vector2(200, 40), new Vector2(0, 1));
            int since = 0;
            foreach (var h in rows) { if (h.Int("rarity") >= 3) break; since++; }
            bool anyLegend = rows.Any(h => h.Int("rarity") >= 3);
            Ui.Label(left, rows.Count == 0 ? "" : anyLegend ? $"距上次傳奇以上：{since} 抽" : $"最近 {rows.Count} 抽尚未出現傳奇以上", 14, Gear.RarityColors[3]).Sized(22);
            Caps(left, $"稀有度分布（最近 {rows.Count} 筆）").Sized(20);
            for (int i = Gear.RarityNames.Length - 1; i >= 0; i--)
            {
                int n = rows.Count(h => h.Int("rarity") == i);
                var line = Ui.Rect(left, "Rarity").Sized(24);
                Ui.Label(line, Gear.RarityNames[i], 14, Gear.RarityColors[i], TextAnchor.MiddleLeft, i >= 3).rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(60, 24), new Vector2(0, 0.5f));
                var track = Ui.Box(line, new Color(1, 1, 1, 0.06f), "Track").rectTransform;
                track.anchorMin = new Vector2(0, 0.5f); track.anchorMax = new Vector2(1, 0.5f); track.pivot = new Vector2(0, 0.5f);
                track.offsetMin = new Vector2(64, -3); track.offsetMax = new Vector2(-92, 3);
                var fill = Ui.Box(track, Gear.RarityColors[i], "Fill").rectTransform;
                fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(rows.Count == 0 ? 0 : (float)n / rows.Count, 1); fill.offsetMin = fill.offsetMax = Vector2.zero;
                Ui.Label(line, rows.Count == 0 ? "0" : $"{n}  ·  {100f * n / rows.Count:0.#}%", 13, Palette.Muted, TextAnchor.MiddleRight).rectTransform.Fill();
            }
            Ui.Label(left, "紀錄只有你自己看得到，伺服器保留最近 500 筆。", 12, Palette.Muted).Sized(36);

            // Right: every draw, newest first.
            var right = Panel(pad, 0.32f, 1f, accent, "Log");
            var head = Ui.Rect(right, "Head").Sized(22);
            foreach (var (text, x, w) in new[] { ("時間", 0f, 120f), ("補給", 128f, 80f), ("獲得", 216f, 360f) })
                Ui.Label(head, text, 12, Palette.Muted, TextAnchor.MiddleLeft).rectTransform.Place(new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(w, 20), new Vector2(0, 0.5f));
            Ui.Label(head, "結果", 12, Palette.Muted, TextAnchor.MiddleRight).rectTransform.Fill();
            var list = Widgets.ScrollList(right, out _);
            list.parent.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            if (gachaHistory == null) Ui.Label(list, "載入中…", 14, Palette.Muted).Sized(40);
            else if (rows.Count == 0) Ui.Label(list, "還沒有抽卡紀錄。", 14, Palette.Muted).Sized(40);
            int index = 0;
            foreach (var h in rows)
            {
                int rarity = Mathf.Clamp(h.Int("rarity"), 0, Gear.RarityNames.Length - 1);
                bool weapon = h.Str("pool") == "weapon";
                var row = Ui.Box(list, new Color(1, 1, 1, index++ % 2 == 0 ? 0.025f : 0f), "Draw").Sized(34);
                var rt = row.rectTransform;
                if (rarity >= 3) Ui.Box(rt, Gear.RarityColors[rarity], "Mark").rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(3, 34), new Vector2(0, 0.5f));
                var when = DateTimeOffset.FromUnixTimeSeconds(h.Int("time")).ToLocalTime();
                Ui.Label(rt, when.ToString("MM/dd HH:mm"), 13, Palette.Muted, TextAnchor.MiddleLeft).rectTransform.Place(new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(116, 30), new Vector2(0, 0.5f));
                Ui.Label(rt, weapon ? "武器" : "裝備", 13, weapon ? Palette.Danger : Palette.Hex("b07cff"), TextAnchor.MiddleLeft).rectTransform.Place(new Vector2(0, 0.5f), new Vector2(128, 0), new Vector2(80, 30), new Vector2(0, 0.5f));
                string item = h.Str("item");
                string name = weapon ? Gear.WeaponName(item, rarity) : $"{Gear.RarityNames[rarity]} {(Gear.SlotNames.TryGetValue(item, out var slot) ? slot : item)}";
                Ui.Label(rt, name, 15, Gear.RarityColors[rarity], TextAnchor.MiddleLeft, rarity >= 3).rectTransform.Place(new Vector2(0, 0.5f), new Vector2(216, 0), new Vector2(360, 30), new Vector2(0, 0.5f));
                string outcome = h.Str("outcome");
                string result = !weapon ? "存入背包" : outcome == "new" ? "新武器" : outcome == "copy" ? "存入武器庫" : outcome == "full" ? "武器庫已滿 · 轉換" : "";
                var res = Ui.Label(rt, result, 13, outcome == "new" ? Palette.Amber : Palette.Muted, TextAnchor.MiddleRight);
                res.rectTransform.Fill(); res.rectTransform.offsetMax = new Vector2(-12, 0);
            }

            if (!fetch) return;
            status.text = "讀取中…";
            app.Api.Post("/api/gacha/history", new Dictionary<string, object> { ["limit"] = 500 }, result =>
            {
                if (Mode != "gachalog") return;
                if (!result.Bool("ok")) { status.color = Palette.Danger; status.text = result.Str("error", "讀取失敗"); return; }
                gachaHistory = (result.Arr("history") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
                gachaTotals = result.Obj("totals") ?? new Dictionary<string, object>();
                ShowGachaHistory(gachaFilter, false);
            });
        }

        /// For the automated screenshots.
        public void DemoGachaHistory(List<Dictionary<string, object>> rows, Dictionary<string, object> totals) { gachaHistory = rows; gachaTotals = totals; ShowGachaHistory("", false); }

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
            var descText = Ui.Label(desc, weaponPool ? "每把抽到的武器都會保留在武器庫，可自由切換使用的版本，不要的再手動分解。" : "隨機獲得護甲、槍械模組、戰術靴或戰術晶片。稀世以上附帶副屬性。", 13, Palette.Muted).rectTransform;
            descText.Fill(); descText.offsetMax = new Vector2(-230, 0);
            int keys = weaponPool ? p.WeaponKeys : p.Keys;
            var keyRow = Ui.Rect(s, "Keys").Sized(58);
            Ui.Label(keyRow, weaponPool ? "持有武器鑰匙" : "持有裝備鑰匙", 12, Palette.Muted).rectTransform.Place(new Vector2(0, 1), Vector2.zero, new Vector2(160, 18), new Vector2(0, 1));
            Icons.Key(keyRow, 34, weaponPool ? Gear.WeaponKeyColor : Gear.GearKeyColor).rectTransform.Place(new Vector2(0, 1), new Vector2(0, -20), new Vector2(34, 34), new Vector2(0, 1));
            Ui.Label(keyRow, keys.ToString(), 36, accent, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(42, -16), new Vector2(160, 42), new Vector2(0, 1));
            int pityLeft = weaponPool ? p.WeaponPity : p.GearPity;
            Ui.Label(s, pityLeft <= 1 ? "保底：下一抽必出傳奇以上！" : $"保底：再 {pityLeft} 抽內必出傳奇以上", 13, pityLeft <= 10 ? Gear.RarityColors[3] : Palette.Muted, TextAnchor.UpperLeft, pityLeft <= 10).Sized(18);
            var odds = Ui.HStack(s, 4).Sized(20);
            float[] weights = { 50f, 33f, 14f, 2.5f, 0.5f };   // mirrors RARITY_WEIGHTS in server/service.py
            for (int i = 0; i < Gear.RarityNames.Length; i++) Ui.Label(odds, $"{Gear.RarityNames[i]} {weights[i]:0.#}%", 12, Gear.RarityColors[i]);
            var draws = Ui.HStack(s, 10).Sized(54);
            var one = PrimaryButton(draws, "抽 1 次", () => { drawPool = pool; Draw(1); }, 17);
            one.interactable = keys >= 1;
            var ten = Ui.Button(draws, "抽 10 次", () => { drawPool = pool; Draw(10); }, 17, Palette.Amber);
            ten.interactable = keys >= 10;
            bool mine = drawPool == pool && lastDraw.Count > 0;
            var recentHead = Ui.Rect(s, "RecentHead").Sized(22);
            Caps(recentHead, mine ? "本次獲得" : "最近獲得").rectTransform.Fill();
            var history = Ui.Button(recentHead, "完整紀錄 →", () => ShowGachaHistory(pool), 13, Palette.Amber, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0));
            history.GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(110, 22), new Vector2(1, 0.5f));
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
                            Id = id, Slot = "weapon", Rarity = Mathf.Clamp(d.Int("rarity"), 0, Gear.RarityNames.Length - 1),
                            Title = Gear.WeaponName(id, d.Int("rarity")),
                            Detail = outcome == "new" ? "新武器！已解鎖" : outcome == "copy" ? "已存入武器庫" : d.Int("shards") > 0 ? $"武器庫已滿 · 轉為 {d.Int("shards")} 碎片" : $"武器庫已滿 · 轉為 {d.Int("gold")} G",
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
                // Taller cards: the drawn weapon / gear piece turns in a 3D preview above its name.
                card.Place(new Vector2(0.5f, 0.5f), new Vector2((col - (inRow - 1) / 2f) * 176, -76 - row * 200 + (items.Count > perRow ? 40 : 0)), new Vector2(164, 190));
                var edge = Ui.Box(card, item.Color, "Edge").rectTransform;
                edge.anchorMin = new Vector2(0, 1); edge.anchorMax = new Vector2(1, 1); edge.pivot = new Vector2(0.5f, 1);
                edge.offsetMin = new Vector2(0, -4); edge.offsetMax = Vector2.zero;
                var glow = Ui.Box(card, new Color(item.Color.r, item.Color.g, item.Color.b, 0.04f + 0.05f * item.Rarity), "Glow").rectTransform;
                if (item.Rarity >= 3)
                {
                    var outline = card.gameObject.AddComponent<Outline>();
                    outline.effectColor = item.Color;
                    outline.effectDistance = new Vector2(item.Rarity, item.Rarity);
                }
                glow.Fill();
                bool isWeapon = item.Slot == "weapon" && !string.IsNullOrEmpty(item.Id);
                string modelId = isWeapon ? item.Id : item.Slot;
                int modelRarity = item.Rarity;
                var model = ModelPreview.Create(card, t => { if (isWeapon) Models.WeaponDisplay(t, modelId, modelRarity); else Models.GearModel(t, modelId, modelRarity); }, 256, 50f, isWeapon ? 6f : 14f);
                model.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(92, 92), new Vector2(0.5f, 1));
                RarityFx.Attach(model.rectTransform, item.Rarity);
                Ui.Label(card, Gear.RarityNames[item.Rarity], 13, item.Color, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(12, -10), new Vector2(140, 18), new Vector2(0, 1));
                Ui.Label(card, item.Title ?? Gear.SlotNames[item.Slot], item.Title != null ? 14 : 18, item.Color, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(10, -100), new Vector2(148, 40), new Vector2(0, 1));
                var stats = Ui.Label(card, item.Detail ?? item.StatText().Replace("  ·  ", "\n"), 12, Palette.Hex("c9c5bb")).rectTransform;
                stats.Place(new Vector2(0, 1), new Vector2(10, -142), new Vector2(148, 46), new Vector2(0, 1));
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
            Ui.Button(pad, "← 返回基地", app.ShowHome, 15)
                .GetComponent<RectTransform>().Place(new Vector2(0, 1), Vector2.zero, new Vector2(130, 32), new Vector2(0, 1));
            Ui.Label(pad, "管理員控制台", 40, Palette.Bone, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(0, -40), new Vector2(500, 52), new Vector2(0, 1));
            Ui.Button(pad, "管理員能力", ShowAbilityOverlay, 15, Palette.Danger, new Color(0, 0, 0, 0), Palette.Danger)
                .GetComponent<RectTransform>().Place(new Vector2(1, 1), new Vector2(0, -38), new Vector2(140, 40), new Vector2(1, 1));

            var grant = Ui.Box(pad, Palette.Sheet, "Grant").rectTransform;
            grant.anchorMin = new Vector2(0, 0); grant.anchorMax = new Vector2(0.36f, 1);
            grant.offsetMin = Vector2.zero; grant.offsetMax = new Vector2(-8, -100);
            TopRule(grant, Palette.Amber);
            var gs = Ui.VStack(grant, 10);
            gs.Fill();
            gs.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(26, 26, 22, 22);
            Ui.Label(gs, "發放福利", 22, Palette.Bone, TextAnchor.UpperLeft, true).Sized(30);
            Ui.Label(gs, "輸入帳號或從名單點選，也可以全服發放。立即入帳，不經過信箱。", 13, Palette.Muted).Sized(36);
            adminTarget = Widgets.Input(gs, "玩家帳號", false, 24).Sized(44);
            var captions = Ui.HStack(gs, 10).Sized(14);
            foreach (var caption in new[] { "金幣", "裝備鑰匙", "武器鑰匙", "裂隙碎片" }) Caps(captions, caption).Sized(-1, 0);
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
            var shardsInput = Widgets.Input(row, "碎片", false, 4);
            shardsInput.contentType = InputField.ContentType.IntegerNumber;
            shardsInput.text = "0";
            int weaponPick = 0;
            string[] choices = { "不發放武器", "解鎖 AR-27 突擊步槍", "解鎖 SG-8 霰彈槍", "解鎖 LR-50 狙擊槍", "解鎖 EX-1 能量光劍" };
            Button weaponButton = null, rarityButton = null;
            int rarityPick = 1;
            var weaponRow = Ui.HStack(gs, 10).Sized(40);
            weaponButton = Ui.Button(weaponRow, choices[0], () => { weaponPick = (weaponPick + 1) % choices.Length; weaponButton.SetCaption(choices[weaponPick]); rarityButton.interactable = weaponPick > 0; }, 14);
            // Rarity of the granted weapon (cycles 劣質 → 神話); an owned weapon gets the copy added to the armory.
            void PaintRarity()
            {
                rarityButton.SetCaption($"稀有度：{Gear.RarityNames[rarityPick]}");
                rarityButton.GetComponentInChildren<Text>().color = Gear.RarityColors[rarityPick];
                rarityButton.GetComponent<Outline>().effectColor = rarityPick >= 3 ? Gear.RarityColors[rarityPick] : Palette.Line;
            }
            rarityButton = Ui.Button(weaponRow, "", () => { rarityPick = (rarityPick + 1) % Gear.RarityNames.Length; PaintRarity(); }, 14);
            rarityButton.gameObject.AddComponent<LayoutElement>().preferredWidth = 130;
            weaponRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            weaponButton.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            rarityButton.interactable = false;
            PaintRarity();
            // Gear: a slot (or a random one per piece), its rarity and how many pieces.
            string[] gearChoices = { "不發放裝備", "隨機部位", "護甲", "模組", "靴子", "晶片" };
            string[] gearIds = { "", "random", "armor", "module", "boots", "chip" };
            int gearPick = 0, gearRarity = 1, gearCount = 1;
            Button gearButton = null, gearRarityButton = null, gearCountButton = null;
            var gearRow = Ui.HStack(gs, 10).Sized(40);
            gearRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            void PaintGear()
            {
                gearButton.SetCaption(gearChoices[gearPick]);
                gearRarityButton.SetCaption($"稀有度：{Gear.RarityNames[gearRarity]}");
                gearRarityButton.GetComponentInChildren<Text>().color = Gear.RarityColors[gearRarity];
                gearRarityButton.GetComponent<Outline>().effectColor = gearRarity >= 3 ? Gear.RarityColors[gearRarity] : Palette.Line;
                gearCountButton.SetCaption($"×{gearCount}");
                gearRarityButton.interactable = gearCountButton.interactable = gearPick > 0;
            }
            gearButton = Ui.Button(gearRow, "", () => { gearPick = (gearPick + 1) % gearChoices.Length; PaintGear(); }, 14);
            gearButton.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            gearRarityButton = Ui.Button(gearRow, "", () => { gearRarity = (gearRarity + 1) % Gear.RarityNames.Length; PaintGear(); }, 14);
            gearRarityButton.gameObject.AddComponent<LayoutElement>().preferredWidth = 130;
            gearCountButton = Ui.Button(gearRow, "", () => { gearCount = gearCount >= 10 ? 1 : gearCount == 1 ? 5 : 10; PaintGear(); }, 14);
            gearCountButton.gameObject.AddComponent<LayoutElement>().preferredWidth = 56;
            PaintGear();
            var actions = Ui.HStack(gs, 10).Sized(46);
            void Send(bool all)
            {
                string who = all ? "*" : adminTarget.text.Trim();
                if (who == "") { adminStatus.color = Palette.Danger; adminStatus.text = "請輸入玩家帳號，或按「發給全部玩家」。"; return; }
                int.TryParse(goldInput.text, out int gold);
                adminStatus.color = Palette.Amber;
                adminStatus.text = "發放中…";
                app.Api.Post("/api/admin/grant", new Dictionary<string, object> { ["username"] = who, ["gold"] = gold, ["keys"] = int.TryParse(keysInput.text, out var keyCount) ? keyCount : 0, ["weapon_keys"] = int.TryParse(weaponKeysInput.text, out var weaponKeyCount) ? weaponKeyCount : 0, ["weapon"] = weaponPick == 0 ? "" : Defs.WeaponOrder[weaponPick], ["rarity"] = rarityPick,
                    ["shards"] = int.TryParse(shardsInput.text, out var shardCount) ? shardCount : 0, ["gear"] = gearIds[gearPick], ["gear_rarity"] = gearRarity, ["gear_count"] = gearCount }, result =>
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
            // Affects every account: asks once before it goes out.
            Button toAll = null;
            float toAllArmed = -9f;
            toAll = Ui.DangerButton(actions, "發給全部玩家", () =>
            {
                if (Time.unscaledTime - toAllArmed > 4f) { toAllArmed = Time.unscaledTime; toAll.SetCaption("確定發給全部？"); return; }
                toAll.SetCaption("發給全部玩家"); toAllArmed = -9f;
                Send(true);
            }, 15);
            toAll.gameObject.AddComponent<RevertCaption>().Arm(() => Time.unscaledTime - toAllArmed > 4f, "發給全部玩家", Palette.Danger);
            // 清除玩家資料: dangerous, so it opens its own dialog with two confirmations.
            Ui.DangerButton(gs, "清除玩家資料…", () => WipeDialog(adminTarget.text.Trim()), 14).Sized(36);
            adminStatus = Ui.Label(gs, "", 14, Palette.Amber).Sized(22);
            Widgets.Section(gs, "玩家名單").Sized(22);
            adminList = Widgets.ScrollList(gs, out _);
            adminList.parent.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;

            AdminMailPanel(pad);
            AuditPanel(pad);
            LoadPlayers();
            LoadAudit();
        }

        RectTransform AdminColumn(RectTransform pad, float x0, float x1, Color accent, string name)
        {
            var box = Ui.Box(pad, Palette.Sheet, name).rectTransform;
            box.anchorMin = new Vector2(x0, 0); box.anchorMax = new Vector2(x1, 1);
            box.offsetMin = new Vector2(8, 0); box.offsetMax = new Vector2(x1 < 1 ? -8 : 0, -100);
            TopRule(box, accent);
            var st = Ui.VStack(box, 10);
            st.Fill();
            st.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(24, 24, 22, 22);
            return st;
        }

        /// System mail to one player or everyone, with optional attachments collected from the 信箱.
        /// 清除玩家資料 with two confirmations: first press 清除資料 (it re-arms after 4 s), then type the account name
        /// exactly to enable 永久清除. The server checks the typed name again and refuses the admin's own account.
        public void WipeDialog(string target)
        {
            if (target == "" || target == "*")
            {
                adminStatus.color = Palette.Danger;
                adminStatus.text = target == "*" ? "清除資料只能針對單一玩家。" : "請先輸入或從名單點選要清除的玩家帳號。";
                return;
            }
            var dialog = Ui.Box(root, new Color(0, 0, 0, 0.7f), "WipeDialog").rectTransform.Fill();
            var sheet = Ui.Box(dialog, Palette.Sheet, "Sheet").rectTransform;
            sheet.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500, 420));
            TopRule(sheet, Palette.Danger);
            var s = Ui.VStack(sheet, 10);
            s.Fill();
            s.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(30, 30, 24, 24);
            Ui.Label(s, "清除玩家資料", 26, Palette.Danger, TextAnchor.UpperLeft, true).Sized(36);
            Ui.Label(s, $"對象：{target}", 18, Palette.Bone, TextAnchor.UpperLeft, true).Sized(26);
            bool wipingSelf = string.Equals(target, app.Profile?.Username, StringComparison.OrdinalIgnoreCase);
            Ui.Label(s, "將清除關卡進度、等級、天賦、金幣、鑰匙、碎片、全部武器與裝備、信件、抽卡紀錄與好友，帳號密碼保留。此操作無法復原。" + (wipingSelf ? "\n這是你自己的帳號：管理員身分會保留。" : ""),
                14, Palette.Hex("e6b8b8")).Sized(wipingSelf ? 80 : 60);
            var step = Ui.Label(s, "第 1 次確認：按下「清除資料」。", 14, Palette.Muted);
            step.Sized(22);
            var confirm = Widgets.Input(s, $"第 2 次確認：輸入「{target}」", false, 24);
            confirm.Sized(44);
            confirm.interactable = false;
            var error = Ui.Label(s, "", 13, Palette.Danger);
            error.Sized(20);
            var buttons = Ui.HStack(s, 12).Sized(46);
            Ui.Button(buttons, "取消", () => UnityEngine.Object.Destroy(dialog.gameObject), 15, Palette.Muted);
            Button go = null;
            bool armed = false;
            go = Ui.DangerButton(buttons, "清除資料", () =>
            {
                if (!armed)
                {
                    armed = true;
                    confirm.interactable = true;
                    confirm.ActivateInputField();
                    step.text = "第 1 次確認完成。";
                    step.color = Palette.Danger;
                    go.SetCaption("永久清除");
                    go.interactable = false;
                    return;
                }
                if (confirm.text.Trim() != target) { error.text = "輸入的帳號不一致。"; return; }
                go.interactable = false;
                error.color = Palette.Amber;
                error.text = "清除中…";
                app.Api.Post("/api/admin/reset", new Dictionary<string, object> { ["username"] = target, ["confirm"] = confirm.text.Trim() }, result =>
                {
                    if (dialog == null) return;
                    if (result.Bool("ok"))
                    {
                        UnityEngine.Object.Destroy(dialog.gameObject);
                        if (Mode != "admin") return;
                        adminStatus.color = Palette.Amber;
                        bool self = string.Equals(result.Str("username", target), app.Profile?.Username, StringComparison.OrdinalIgnoreCase);
                        adminStatus.text = self ? "已清除你自己的資料（保留管理員身分）。" : $"已清除 {result.Str("username", target)} 的資料。";
                        if (self) app.RefreshProfile();   // own wipe: the lobby shows the fresh account right away
                        LoadPlayers();
                    }
                    else { error.color = Palette.Danger; error.text = result.Str("error", "清除失敗"); go.interactable = true; }
                });
            }, 15);
            // The final button only lights up once the name matches exactly.
            confirm.onValueChanged.AddListener(v => { if (armed) { go.interactable = v.Trim() == target; error.text = ""; } });
        }

        void AdminMailPanel(RectTransform pad)
        {
            var ms = AdminColumn(pad, 0.36f, 0.68f, Palette.Amber, "Mail");
            Ui.Label(ms, "系統郵件", 22, Palette.Bone, TextAnchor.UpperLeft, true).Sized(30);
            Ui.Label(ms, "寄到玩家信箱，附件需由玩家自行領取。適合維護補償與活動公告。", 13, Palette.Muted).Sized(36);
            var target = Widgets.Input(ms, "收件帳號（留空＝全部玩家）", false, 24).Sized(44);
            var title = Widgets.Input(ms, "標題（最多 40 字）", false, 40).Sized(44);
            var body = Widgets.Input(ms, "內容（最多 500 字）", false, 500);
            body.lineType = InputField.LineType.MultiLineNewline;
            body.textComponent.alignment = TextAnchor.UpperLeft;
            body.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            ((Text)body.placeholder).alignment = TextAnchor.UpperLeft;
            body.textComponent.rectTransform.offsetMin = new Vector2(16, 10); body.textComponent.rectTransform.offsetMax = new Vector2(-16, -10);
            body.placeholder.rectTransform.offsetMin = new Vector2(16, 10); body.placeholder.rectTransform.offsetMax = new Vector2(-16, -10);
            body.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            Caps(ms, "附件（可留空）").Sized(18);
            var gifts = Ui.HStack(ms, 8).Sized(44);
            InputField Number(string hint)
            {
                var f = Widgets.Input(gifts, hint, false, 6);
                f.contentType = InputField.ContentType.IntegerNumber;
                ((Text)f.placeholder).fontSize = 14;
                return f;
            }
            var gold = Number("金幣");
            var keys = Number("裝備鑰匙");
            var wkeys = Number("武器鑰匙");
            var status = Ui.Label(ms, "", 14, Palette.Amber).Sized(22);
            PrimaryButton(ms, "寄出郵件", () =>
            {
                string who = target.text.Trim();
                int Parse(InputField f) => int.TryParse(f.text, out var v) ? v : 0;
                status.color = Palette.Amber;
                status.text = "寄送中…";
                app.Api.Post("/api/admin/mail", new Dictionary<string, object>
                {
                    ["username"] = who == "" ? "*" : who, ["title"] = title.text, ["body"] = body.text,
                    ["gold"] = Parse(gold), ["keys"] = Parse(keys), ["weapon_keys"] = Parse(wkeys), ["days"] = 30,
                }, result =>
                {
                    if (Mode != "admin") return;
                    if (!result.Bool("ok")) { status.color = Palette.Danger; status.text = result.Str("error", "寄送失敗"); return; }
                    app.UpdateProfile(Profile.From(result.Obj("profile")), false);
                    status.text = $"已寄給 {result.Int("count")} 位玩家";
                    title.text = body.text = gold.text = keys.text = wkeys.text = "";
                });
            }, 17).Sized(50);
        }

        RectTransform auditList;

        /// Anti-cheat log: rejected mission reports (red) and suspicious but paid ones (amber), newest first.
        void AuditPanel(RectTransform pad)
        {
            var au = AdminColumn(pad, 0.68f, 1f, Palette.Danger, "Audit");
            var head = Ui.Rect(au, "Head").Sized(30);
            Ui.Label(head, "稽核記錄", 22, Palette.Bone, TextAnchor.MiddleLeft, true).rectTransform.Fill();
            Ui.Button(head, "重新整理", LoadAudit, 12, Palette.Muted, new Color(0, 0, 0, 0), Palette.Line)
                .GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(84, 28), new Vector2(1, 0.5f));
            Ui.Label(au, "伺服器會檢查每場結算。紅色＝不合理，已拒絕發放；黃色＝數據可疑，已發放但請留意。", 13, Palette.Muted).Sized(54);
            auditList = Widgets.ScrollList(au, out _);
            auditList.parent.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            Ui.Label(auditList, "載入中…", 13, Palette.Muted).Sized(30);
        }

        void LoadAudit()
        {
            app.Api.Post("/api/admin/audit", null, result =>
            {
                if (Mode != "admin" || auditList == null) return;
                foreach (Transform child in auditList) UnityEngine.Object.Destroy(child.gameObject);
                if (!result.Bool("ok")) { Ui.Label(auditList, result.Str("error", "無法讀取稽核記錄"), 13, Palette.Danger).Sized(30); return; }
                var entries = (result.Arr("entries") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
                if (entries.Count == 0) { Ui.Label(auditList, "目前沒有任何異常紀錄", 13, Palette.Muted).Sized(30); return; }
                foreach (var e in entries) AuditRow(e);
            });
        }

        void AuditRow(Dictionary<string, object> e)
        {
            bool rejected = e.Int("rejected") == 1;
            Color accent = rejected ? Palette.Danger : Palette.Amber;
            var row = Ui.Box(auditList, new Color(1, 1, 1, 0.025f), "Entry");
            var mark = Ui.Box(row.transform, accent, "Mark").rectTransform;
            mark.anchorMin = Vector2.zero; mark.anchorMax = new Vector2(0, 1); mark.pivot = new Vector2(0, 0.5f); mark.offsetMin = Vector2.zero; mark.offsetMax = new Vector2(3, 0);
            string when = DateTimeOffset.FromUnixTimeSeconds(e.Int("created")).ToOffset(TimeSpan.FromHours(8)).ToString("MM-dd HH:mm");
            Ui.Label(row.transform, e.Str("username"), 14, Palette.Bone, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(14, -8), new Vector2(200, 18), new Vector2(0, 1));
            Ui.Label(row.transform, (rejected ? "已拒絕 · " : "可疑 · ") + when, 11, accent, TextAnchor.UpperRight).rectTransform.Place(new Vector2(1, 1), new Vector2(-10, -9), new Vector2(160, 16), new Vector2(1, 1));
            var reason = Ui.Label(row.transform, e.Str("reason"), 12, Palette.Hex("c9c5bb"));
            var rt = reason.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(14, 8); rt.offsetMax = new Vector2(-10, -30);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 40 + Mathf.Max(18, Mathf.Ceil(reason.text.Length / 22f) * 17);
        }

        void ShowAbilityOverlay()
        {
            var scrim = Ui.Box(root, new Color(0.03f, 0.031f, 0.034f, 0.82f), "Abilities").rectTransform;
            scrim.Fill();
            var sheet = Ui.Box(scrim, Palette.Sheet, "Sheet").rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 650));
            TopRule(sheet, Palette.Danger);
            var ss = Ui.VStack(sheet, 12);
            ss.Fill();
            ss.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(26, 26, 22, 22);
            AbilityPanel(ss);
            Ui.Button(ss, "關閉", () => UnityEngine.Object.Destroy(scrim.gameObject), 16).Sized(44);
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
            Widgets.Toggle(parent, "自動瞄準（鎖定準星附近 30° 內可見敵人的頭部）", s.AutoAim, on => { s.AutoAim = on; app.PushAdmin(); }).Sized(46);
            Widgets.Section(parent, "測試外掛（本機）").Sized(22);
            Widgets.Toggle(parent, "鎖定自瞄（按住右鍵，視角轉向 45° 內最近的敵人頭部）", GameClient.TestAimbot, on => GameClient.TestAimbot = on).Sized(46);
            SliderRow(parent, "鎖定平滑度", 0, 1, GameClient.TestAimbotSmooth, v => GameClient.TestAimbotSmooth = v);
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
                    int flags = player.Int("flags");
                    if (flags > 0)
                        Ui.Label(b.transform, $"異常 {flags}", 12, Palette.Danger, TextAnchor.MiddleRight, true).rectTransform
                            .Place(new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(80, 30), new Vector2(1, 0.5f));
                }
            });
        }
    }
}
