using System;
using RiftWard.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace RiftWard.Client
{
    /// Version notes: shown once per account the first time it reaches the base on a new version, and any time after
    /// from the 「更新內容」 link in the top bar. Newest first; the first entry must match Defs.Version.
    public static class PatchNotes
    {
        public sealed class Entry { public string Version, Date; public string[] Lines; }

        public static readonly Entry[] All =
        {
            new Entry
            {
                Version = "0.8.3-unity.1", Date = "2026-11-22",
                Lines = new[]
                {
                    "延遲補償：命中判定以你畫面上看到的敵人位置為準，近距離打快速移動的敵人不再莫名落空（PvP 同樣適用）。",
                    "新增裝備命之座：消耗一件同部位、同稀有度的未裝備裝備加金幣升級，最高 C6；每級全屬性 +4%，C3 防禦 +2%，C6 全屬性再 +10%。",
                    "M-7 熔爐機槍改為無限彈藥，只受過熱限制；彈匣強化改為延長過熱前的射擊時間。",
                    "無盡模式帳號經驗提高：每撐過 5 波額外 +40，整場經驗 ×1.5。",
                    "修正快速點按 X 龍捲風、Q 手榴彈、Shift 衝刺偶爾沒反應；龍捲風冷卻中或附近沒有敵人時會顯示提示。",
                    "裝備背包快滿時提醒；放不下時抽取前就會跳出提示，可直接前往一鍵分解。",
                },
            },
            new Entry
            {
                Version = "0.8.2-unity.1", Date = "2026-11-22",
                Lines = new[]
                {
                    "PvP：對手身上加上紅色描邊，更容易辨識敵人（會被牆擋住，不會穿牆顯示）。",
                },
            },
            new Entry
            {
                Version = "0.8.1-unity.1", Date = "2026-11-22",
                Lines = new[]
                {
                    "裝備強化上限 +5 → +10：+6 起每級額外消耗裂隙碎片（1～5 個），每級全屬性 +8%（+10 時合計 +100%）；分解時強化碎片全額退還。突破仍在 +5 開放。",
                    "新增裝備副屬性「技能傷害 %」：提高無人機、電漿砲、刃盾、地雷、飛彈、閃電鏈、手榴彈、龍捲風、爆裂彈頭的傷害；可由新裝備或重鑄取得。",
                    "PvP：不再顯示其他玩家的名字與血條，雷達也不會顯示其他玩家的位置。",
                },
            },
            new Entry
            {
                Version = "0.8.0-unity.1", Date = "2026-11-22",
                Lines = new[]
                {
                    "新模式 PvP 對戰（Free For All）：最多 4 人各自為戰，5 分鐘內擊殺最多者獲勝；陣亡 3 秒後復活。公平數值：所有人使用普通、未強化的武器，裝備與天賦不生效。",
                    "PvP 自動配對：滿 4 人立即開始，有 2 人以上最多等 30 秒；也能在多人房間由隊長「房內開打」。依名次獲得少量金幣，並有每週 PvP 排行榜。",
                    "衝刺新增風之尾流：沿衝刺路線留下 3 秒的風帶，踩到的敵人移動速度降為 45%。",
                    "新增大招「龍捲風」（X）：朝最近的敵人放出龍捲風，造成傷害並把敵人吹上空中 1.4 秒（Boss 不會被吹起），冷卻 30 秒。",
                    "Boss 的超級寶箱改為擊破後立即開啟，全隊直接獲得 3 次強化。",
                    "無人機加強：基礎傷害 14 → 26（少量無人機也有感），每次強化 +35% → +25%、進化 ×2 → ×1.6。",
                    "追蹤飛彈加強：傷害 30+8×等級 → 45+12×等級，爆炸範圍 2.2 → 3 公尺。",
                    "肩載電漿砲再削弱：基礎傷害 50 → 40，爆炸範圍 4.2 → 3.6 公尺。",
                    "登入畫面新增「離開遊戲」與顯示密碼（眼睛圖示）。",
                },
            },
            new Entry
            {
                Version = "0.7.40-unity.1", Date = "2026-11-22",
                Lines = new[]
                {
                    "修正任務結算與領取每日獎勵同時發生時，任務進度可能被覆蓋或重複領取的問題。",
                    "成就頁：「一鍵領取」不再蓋住目前配戴的稱號。",
                    "每日任務頁：右側「難度與通關紀錄」不再超出螢幕底部。",
                    "武器名稱不再重複（原本顯示「制式 P-9 制式手槍」）。",
                    "登入畫面：帳號伺服器資訊不再超出外框。",
                },
            },
            new Entry
            {
                Version = "0.7.39-unity.1", Date = "2026-11-22",
                Lines = new[]
                {
                    "技能傷害數字：無人機、電漿砲、刃盾、地雷、飛彈、閃電鏈、手榴彈、爆裂彈頭打中敵人時會顯示傷害數值，每種技能不同顏色，擊殺以粗體顯示。",
                    "敵人接近提示：9 公尺內、畫面外的敵人會在準星周圍以紅色方向條標示，越近越粗越亮，3 公尺內閃爍。",
                    "肩載電漿砲削弱：基礎傷害 70 → 50，每次強化 +40% → +25%，只吃一半的玩家傷害加成（原本滿級可一砲清空困難模式整波小怪）。",
                    "旋轉刃盾加強：傷害 10+6×等級 → 16+9×等級，攻擊間隔 0.35 → 0.3 秒。",
                },
            },
            new Entry
            {
                Version = "0.7.38-unity.1", Date = "2026-11-21",
                Lines = new[]
                {
                    "不會被秒殺：單次受到的傷害最多扣最大生命的 35%，滿血時任何 1 秒內最多掉 70%；長時間被圍攻仍會持續掉血。",
                    "噩夢：敵人血量 ×4、傷害 ×2.2、每波多 2 名（原 ×2.8／×1.7／多 4 名）。地獄：血量 ×6、傷害 ×3、每波多 2 名（原 ×5.5／×2.4／多 6 名）。敵人更少、更硬、更痛。",
                    "修正電腦代打一開槍就自己結束的問題：代打期間只有按鍵、移動或明顯轉動滑鼠才會交還控制。",
                    "成就頁新增「一鍵領取」，一次領完所有已達成的階段。",
                    "登入畫面會記住上次登入的帳號，被登出後只要輸入密碼。",
                    "自動更新：遊戲開著時偵測到新版本會提示，按「立即更新」自動關閉遊戲並下載更新（任務中會等回到選單才提示；需使用本版啟動器開啟）。",
                },
            },
            new Entry
            {
                Version = "0.7.37-unity.1", Date = "2026-11-21",
                Lines = new[]
                {
                    "斷線自動重連：連線中斷後自動重試，任務中斷線會保留座位 60 秒，連回直接回到原本的戰場（組隊時由電腦暫時代打，單人時任務暫停）。",
                    "重試都失敗時跳出「連線狀況」提示框，可再次重新連線或改為離線遊玩。",
                    "畫面角落顯示延遲（Ping）與 FPS，可在設定分別開關，或按 F3 快速切換。",
                    "所有按鍵都列在「設定 › 按鍵」：新增「本場升級（按住）」可改鍵，並列出固定按鍵。",
                    "命之座：素材改為需要與使用中武器同稀有度的同款複本。",
                },
            },
            new Entry
            {
                Version = "0.7.36-unity.1", Date = "2026-11-21",
                Lines = new[]
                {
                    "星圖：關卡頁改為兩顆星球，第一章「墜落星球」、第二章「帝國母星」，進入星球選擇三個區域；關卡顯示最高評分。",
                    "每週任務：每週一更換五項任務，全部完成再領額外獎勵。管理員可舉辦登入活動，在「任務 › 登入獎勵」每天領取。",
                    "圖鑑：在「裝備 › 圖鑑」收集所有武器與裝備的各稀有度，獲得過就永久點亮。",
                    "武器命之座：消耗一把稀世以上的同款複本與金幣升級，最高 C6（每級傷害 +4%，C3 彈匣 +15%，C6 射速 +10%）。",
                    "技能天賦：天賦新增「技能天賦」分頁，強化 Q 手榴彈的傷害、冷卻與爆炸範圍，和基礎天賦共用點數。",
                    "抽卡軟保底：連續 60 抽未出神話後，神話機率每抽 +0.02%。",
                    "分解強化過的武器與裝備：退回 8 成強化金幣，碎片全額退回。",
                    "戰鬥中按住 Tab 查看本場升級；衝刺有風聲；藍色盾兵的盾牌會被打破。",
                    "多人任務中隊友閒置超過 1 分鐘由電腦代打，回來後立即交還。",
                    "點聊天室玩家名字可查看資料卡並加好友。最後一波清完直接勝利；聊天不再保留上次登入前的紀錄。",
                },
            },
            new Entry
            {
                Version = "0.7.35-unity.1", Date = "2026-11-20",
                Lines = new[]
                {
                    "霰彈槍強化：每發彈丸 7 → 9 顆，每顆傷害提高 5%（16 → 16.8）。普通未強化全彈命中傷害 112 → 151.2。",
                    "多人連線：開局等待隊員載入，避免載入較慢或結算期間被判定斷線；慢速連線不再阻塞整個房間。",
                    "修正多人模式第二章劇情的繼續按鈕，隊員可正常返回基地等待隊長部署。",
                    "戰鬥中可在隊友名牌下看到生命條。怪物數量依隊員人數增為 2／3／4 倍，Boss 維持一隻。",
                    "標點按鍵可在設定中自訂，預設滑鼠中鍵，保留單擊、雙擊與長按輪盤。",
                    "回饋表單載入紀錄時不再重建輸入區，切換分類保留草稿，送出失敗後可重試。",
                },
            },
            new Entry
            {
                Version = "0.7.34-unity.1", Date = "2026-11-20",
                Lines = new[]
                {
                    "武器平衡：霰彈槍每顆傷害 13 → 16、射擊間隔 0.8 → 0.75 秒、換彈 2.2 → 2 秒。",
                    "限定機槍：最低射擊間隔 0.045 → 0.025 秒，局內射速升級更有感；保留暖機、過熱與移動限制。",
                    "無人機與追蹤飛彈：目標不足時，額外單位以 35% 傷害支援同一目標。",
                    "刃盾每次判定對同一敵人最多命中兩片，第二片造成 25% 傷害；進化電漿砲重疊轟擊造成 50% 傷害。",
                    "裝置每區額外傷害 25% → 15%；噩夢敵人血量 ×2.8、傷害 ×1.7，適合中後期裝備挑戰。",
                    "無盡超過六次的傷害與生命升級改為固定增量；14 級後經驗需求緩慢成長，敵人等級加成停止成長，波數仍會強化敵人。",
                    "任務評分：Boss 波權重提高至四倍，加入倒地扣分，並依區域與難度調整標準時間。",
                },
            },
            new Entry
            {
                Version = "0.7.33-unity.1", Date = "2026-11-20",
                Lines = new[]
                {
                    "新增意見回饋：社交頁的「回饋」分頁可以直接寫建議或錯誤回報給管理員，回覆會寄到你的信箱。",
                    "限定補給的保底改為「80 抽內必出神話」；抽到神話時 50% 為限定武器，歪了下次神話必中。",
                    "修正管理員發放武器的清單（補上手槍與機槍，選項對應正確），以及玩家名單被擠出畫面的問題。",
                },
            },
            new Entry
            {
                Version = "0.7.32-unity.1", Date = "2026-11-20",
                Lines = new[]
                {
                    "新增限定武器「M-7 熔爐機槍」：六管轉輪機槍，越轉越快、越熱越痛，過熱需冷卻；開火時移動變慢。",
                    "新增「限定補給」：使用武器鑰匙抽取，神話 0.5%；抽到神話時 50% 為限定武器，歪了下次神話必中。開放期間見商城。",
                    "隊伍新增「設為隊長」：隊長可在隊伍面板把隊長轉移給隊友。",
                },
            },
            new Entry
            {
                Version = "0.7.31-unity.1", Date = "2026-11-19",
                Lines = new[]
                {
                    "裝備外觀重製：護甲、槍械模組、戰術靴與戰術晶片的造型更精細（分片胸甲、光學外殼、散熱片、電路與陶瓷元件等）。",
                    "武器與裝備的零件加上倒角，並換上新的金屬、聚合物與金色材質，近看更像機械零件。",
                    "傳奇以上的裝備新增旋轉能量光環與懸浮飾片，神話為雙層光環。",
                    "傳奇以上的槍械槍口新增旋轉能量環（神話雙環）；光劍劍柄新增散熱鰭片與能量環。",
                    "新特效不使用即時光源與粒子，對效能影響很小。",
                },
            },
            new Entry
            {
                Version = "0.7.30-unity.1", Date = "2026-11-19",
                Lines = new[]
                {
                    "圍攻上限：0.85 秒內最多 3 隻敵人能同時近戰打到你，被包圍不再瞬間倒地。",
                    "每件裝備依稀有度附帶基礎生命（5／10／20／35／60），會隨強化與突破成長。",
                    "裝備防禦力提高：每強化 1 級 +0.8%、每突破 1 階 +1.5%（上限 40%）。",
                    "堡壘套裝：2 件生命 +60，4 件防禦 +10%。",
                    "難度調整：噩夢敵人傷害 ×1.7 → ×1.5（建議中期裝備）；地獄敵人血量 ×4.5 → ×5.5。",
                    "光劍：一刀砍中的第 2、3 名敵人改為受 60% 傷害（最近的一名仍為全額）。",
                },
            },
            new Entry
            {
                Version = "0.7.29-unity.1", Date = "2026-11-19",
                Lines = new[]
                {
                    "畫面美化：士兵、所有敵人與 Boss 加上裝甲接縫、護墊、發光節點、排氣管等細節（高畫質以上）。",
                    "場景美化：草原、霜冕要塞、空中船塢換上漸層天空；掩體加上面板與破損細節，場地外牆加上立柱，草地更茂密。",
                    "掉落物改為看得出用途的急救箱與彈藥箱。",
                    "介面簡化：按鈕去除多餘外框；裝備背包每列只留改造與強化，分解移到改造視窗內。",
                    "修正選擇關卡頁標題與章節分頁重疊、Boss 戰時提示文字蓋住 Boss 名稱的問題。",
                    "畫質設為「中」或「低」時會使用較簡單的模型，效能較好。",
                },
            },
            new Entry
            {
                Version = "0.7.28-unity.1", Date = "2026-11-19",
                Lines = new[]
                {
                    "無盡模式：生命強化疊滿 6 層後，之後每次改為生命上限 ×1.10（稀有度越高倍率越大），後期不再一碰就倒。",
                    "裝備防禦力改為獨立計算：在局內護甲（上限 60%）之後再減傷，護甲疊滿後裝備防禦依然有效。",
                    "啟動器支援差異更新：只下載有變動的檔案（需重新下載一次安裝包取得新版啟動器）。",
                },
            },
            new Entry
            {
                Version = "0.7.27-unity.1", Date = "2026-11-19",
                Lines = new[]
                {
                    "裝備強化會增加防禦力：每強化 1 級防禦 +0.5%，每突破 1 階防禦 +1%（減少受到的傷害，與局內護甲合計上限 60%）。",
                    "「護盾」更名為「生命值」：生命強化、恢復生命、生命上限等說明一併更新。",
                },
            },
            new Entry
            {
                Version = "0.7.26-unity.1", Date = "2026-11-19",
                Lines = new[]
                {
                    "天賦「強韌體魄」改為每點護盾上限 +3%（點滿 +30%），局內每次強化護盾也會套用，無盡後期不再失效。",
                    "武器平衡：光劍基礎傷害 62 → 55，揮砍速度強化效果減半（每級 -8% → -4%）。",
                    "武器平衡：霰彈槍每顆彈丸傷害 11 → 13。",
                },
            },
            new Entry
            {
                Version = "0.7.25-unity.1", Date = "2026-11-19",
                Lines = new[]
                {
                    "無盡模式平衡：第 20 波後敵人給更多經驗、升級所需經驗在 14 級後不再增加，後期仍能持續升級。",
                    "無盡模式：傷害強化疊滿 6 層後，之後每次改為目前傷害 ×1.12（稀有度越高倍率越大），跟得上後期敵人。",
                    "升級選卡會顯示該項目前已選幾次；有層數上限的以格子顯示，並標出這次選擇會增加的格數。",
                },
            },
            new Entry
            {
                Version = "0.7.24-unity.1", Date = "2026-11-19",
                Lines = new[]
                {
                    "新增裝備改造（裝備背包每件裝備的「改造」）：重鑄、突破、升階。",
                    "重鑄：重新隨機稀世以上裝備的副屬性與套裝，可鎖定只重骰數值。",
                    "突破：+5 滿級後可再突破 3 階，每階全屬性 +8%。",
                    "升階：消耗一件同部位同稀有度的裝備，稀有度提升一階，強化與突破保留。",
                    "新增套裝（突擊、堡壘、疾風）：同系列穿 2 件、4 件獲得額外加成。",
                    "新增死亡旁觀：多人倒地時鏡頭會跟隨仍站著的隊友，左鍵切換。",
                    "修正有護甲時受到致命傷不會倒地、一直剩 1 滴血的問題。",
                    "修正精英等大體型敵人打頭常被判成打身體的問題。",
                },
            },
            new Entry
            {
                Version = "0.7.23-unity.1", Date = "2026-11-19",
                Lines = new[]
                {
                    "好友名單新增「申請入隊」：好友在房間或任務中時可申請加入，對方同意後直接進入房間。",
                    "修正裝備光劍時，隊伍面板中自己的角色模型縮得幾乎看不見的問題。",
                },
            },
            new Entry
            {
                Version = "0.7.22-unity.1", Date = "2026-11-19",
                Lines = new[]
                {
                    "新增「一鍵裝備最強武器與裝備」按鈕（裝備頁），自動換上最強的武器與各部位裝備。",
                    "多人結算畫面新增「隊伍貢獻」：每位隊員的總傷害占比、擊殺、爆頭、命中率、救援與 MVP。",
                    "組隊房間會顯示每位隊員的角色模型與武器，加入房間後一眼就能確認自己已在隊伍中。",
                    "好友邀請也會像組隊邀請一樣跳出通知，可直接接受或拒絕；多則邀請會依序一則一則顯示。",
                    "多人通關後，按「返回主頁」只會讓自己離開結算畫面，不再把隊友一起帶走。",
                    "修正結算畫面按「再次挑戰」會卡住的問題：隊長會帶全隊重新部署，隊員則回到基地等待隊長。",
                    "BOSS 掉落的超級寶箱由一人開啟，全隊（包含倒地的隊友）都會獲得強化。",
                    "隊友斷線時會自動退出任務且不會獲得該場獎勵；只剩一人時轉為單人模式，可按 ESC 暫停。",
                    "修正任務中聊天框沒有訊息時仍殘留灰色底框的問題。",
                },
            },
        };

        public static Entry Latest => All[0];

        /// True once per account for each new version (then remembered on this computer).
        public static bool DueFor(int accountId)
        {
            string key = $"rw.notes.{accountId}";
            try
            {
                if (PlayerPrefs.GetString(key, "") == Latest.Version) return false;
                PlayerPrefs.SetString(key, Latest.Version);
                PlayerPrefs.Save();
            }
            catch { return false; }
            return true;
        }

        /// Modal sheet listing every version's notes; closes with the button, Esc or a click outside.
        public static void Show(Transform parent, Action closed = null)
        {
            var scrim = Ui.Box(parent, new Color(0.03f, 0.031f, 0.034f, 0.82f), "PatchNotes").rectTransform;
            scrim.Fill();
            void Close() { if (scrim != null) UnityEngine.Object.Destroy(scrim.gameObject); closed?.Invoke(); }
            var outside = scrim.gameObject.AddComponent<Button>();
            outside.transition = Selectable.Transition.None;
            outside.onClick.AddListener(Close);
            scrim.gameObject.AddComponent<EscCloses>().Close = Close;

            var sheet = Ui.Box(scrim, new Color(0.07f, 0.072f, 0.078f, 0.98f), "Sheet").rectTransform;
            sheet.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680, 560));
            sheet.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;   // clicks on the sheet stay on it
            Ui.Box(sheet, Palette.Amber, "Rule").rectTransform.Place(new Vector2(0.5f, 1), Vector2.zero, new Vector2(680, 2), new Vector2(0.5f, 1));
            Ui.Label(sheet, "PATCH NOTES", 13, Palette.Amber, TextAnchor.UpperLeft).rectTransform.Place(new Vector2(0, 1), new Vector2(32, -24), new Vector2(300, 18), new Vector2(0, 1));
            Ui.Label(sheet, "更新內容", 32, Palette.Bone, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(32, -44), new Vector2(400, 42), new Vector2(0, 1));

            var host = Ui.Rect(sheet, "ListHost");
            host.anchorMin = Vector2.zero; host.anchorMax = Vector2.one;
            host.offsetMin = new Vector2(32, 84); host.offsetMax = new Vector2(-32, -100);
            var list = Widgets.ScrollList(host, out _);
            list.parent.GetComponent<RectTransform>().Fill();
            list.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(16, 16, 12, 12);
            for (int i = 0; i < All.Length; i++)
            {
                var e = All[i];
                if (i > 0) Ui.Rect(list, "Gap").Sized(14);
                var head = Ui.Rect(list, "Head").Sized(26);
                Ui.Label(head, "v" + e.Version, 19, i == 0 ? Palette.Amber : Palette.Bone, TextAnchor.MiddleLeft, true).rectTransform.Fill();
                Ui.Label(head, (i == 0 ? "最新 · " : "") + e.Date, 13, Palette.Muted, TextAnchor.MiddleRight).rectTransform.Fill();
                Ui.Box(list, Palette.Line, "Rule").Sized(1);
                foreach (var line in e.Lines)
                {
                    // About 40 full-width characters fit on a line of the list at this size.
                    int rows = Mathf.Max(1, Mathf.CeilToInt(line.Length / 40f));
                    var row = Ui.Rect(list, "Line").Sized(rows * 22 + 8);
                    Ui.Label(row, "•", 15, Palette.Amber).rectTransform.Place(new Vector2(0, 1), new Vector2(0, -4), new Vector2(16, 22), new Vector2(0, 1));
                    var text = Ui.Label(row, line, 15, Palette.Hex("d6d1c6"));
                    text.rectTransform.Fill();
                    text.rectTransform.offsetMin = new Vector2(18, 0); text.rectTransform.offsetMax = new Vector2(0, -4);
                }
            }
            var close = Ui.Button(sheet, "知道了", Close, 17, Palette.Ink, Palette.Amber, Palette.Amber);
            close.GetComponent<RectTransform>().Place(new Vector2(1, 0), new Vector2(-32, 24), new Vector2(160, 44), new Vector2(1, 0));
        }

        sealed class EscCloses : MonoBehaviour
        {
            public Action Close;
            void Update() { if (Input.GetKeyDown(KeyCode.Escape)) Close?.Invoke(); }
        }
    }
}
