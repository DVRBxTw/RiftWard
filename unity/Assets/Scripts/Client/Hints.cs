using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RiftWard.Client
{
    /// First-visit guidance: a one-time banner at the bottom of a page explaining what it is for.
    /// Dismissed with 知道了 and remembered on this computer.
    public static class Hints
    {
        public static bool Enabled = true;

        static readonly Dictionary<string, (string title, string text)> pages = new Dictionary<string, (string, string)>
        {
            ["home"] = ("基地主頁", "左側選擇要出擊的區域，按「部署」開始 20 波防守。完成區域會解鎖下一個，並獲得金幣與經驗。"),
            ["character"] = ("天賦與裝備", "升級獲得天賦點，分配到左側天賦。裝備從補給站抽取，點選背包裡的裝備即可穿上，不要的可以分解換金幣。"),
            ["armory"] = ("武器庫", "選擇武器查看數值、花金幣升級。抽到的每把武器都會保留，可在下方切換使用的版本；傳奇以上帶有專屬屬性。"),
            ["shop"] = ("補給站", "用鑰匙抽取裝備或武器。每 50 抽內保證出現傳奇以上，保底進度顯示在抽卡按鈕上方。"),
        };

        static bool Seen(string id) { try { return PlayerPrefs.GetInt("rw.hint." + id, 0) == 1; } catch { return false; } }
        static void MarkSeen(string id) { try { PlayerPrefs.SetInt("rw.hint." + id, 1); PlayerPrefs.Save(); } catch { } }

        /// Shows the page's hint once, unless it was dismissed before.
        public static void Maybe(RectTransform root, string page, bool force = false)
        {
            if ((!Enabled && !force) || !pages.TryGetValue(page, out var hint) || (!force && Seen(page))) return;
            var banner = Ui.Box(root, new Color(0.08f, 0.085f, 0.095f, 0.97f), "Hint").rectTransform;
            banner.Place(new Vector2(0.5f, 0), new Vector2(0, 28), new Vector2(820, 74), new Vector2(0.5f, 0));
            var outline = banner.gameObject.AddComponent<Outline>();
            outline.effectColor = Palette.Amber; outline.effectDistance = new Vector2(1, 1);
            Ui.Box(banner, Palette.Amber, "Mark").rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(4, 74), new Vector2(0, 0.5f));
            Ui.Label(banner, "新手提示  ·  " + hint.title, 13, Palette.Amber, TextAnchor.UpperLeft, true).rectTransform.Place(new Vector2(0, 1), new Vector2(20, -10), new Vector2(600, 18), new Vector2(0, 1));
            var text = Ui.Label(banner, hint.text, 14, Palette.Bone).rectTransform;
            text.anchorMin = Vector2.zero; text.anchorMax = Vector2.one; text.offsetMin = new Vector2(20, 8); text.offsetMax = new Vector2(-130, -32);
            Ui.Button(banner, "知道了", () => { MarkSeen(page); Object.Destroy(banner.gameObject); }, 15, Palette.Amber)
                .GetComponent<RectTransform>().Place(new Vector2(1, 0.5f), new Vector2(-16, 0), new Vector2(96, 40), new Vector2(1, 0.5f));
            banner.gameObject.AddComponent<OnTop>();
        }

        /// Pages are built after Begin() creates the banner; move it above them once the page is complete.
        sealed class OnTop : MonoBehaviour
        {
            void Start() => transform.SetAsLastSibling();
        }
    }
}
