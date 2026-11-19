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
