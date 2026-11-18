using System;
using UnityEngine;
using UnityEngine.UI;

namespace RiftWard.Client
{
    /// Player options saved on this computer: display mode, resolution, quality, volumes and mouse sensitivity.
    public static class GameSettings
    {
        public static bool Fullscreen = true;
        public static int Resolution = -1, Quality = 2, FrameCap = 3;
        public static float Master = 0.9f, Music = 0.7f, Effects = 0.9f, Sensitivity = 1f;
        /// Extra multiplier on top of the field-of-view scaling while aimed down sights.
        public static float AdsSensitivity = 1f;

        public static readonly (int w, int h, string label)[] Resolutions =
        {
            (1280, 720, "1280 × 720（16:9）"), (1600, 900, "1600 × 900（16:9）"), (1920, 1080, "1920 × 1080（16:9）"),
            (2560, 1440, "2560 × 1440（16:9）"), (1280, 800, "1280 × 800（16:10）"), (1680, 1050, "1680 × 1050（16:10）"),
            (1920, 1200, "1920 × 1200（16:10）"), (2560, 1080, "2560 × 1080（21:9）"), (3440, 1440, "3440 × 1440（21:9）"),
        };
        public static readonly string[] QualityNames = { "低", "中", "高", "極致" };
        /// 0 = vertical sync, otherwise a fixed cap (-1 = unlimited).
        public static readonly (int fps, string label)[] FrameCaps = { (0, "垂直同步"), (60, "60 FPS"), (120, "120 FPS"), (144, "144 FPS"), (240, "240 FPS"), (-1, "無上限") };

        static float F(string key, float fallback) { try { return PlayerPrefs.GetFloat(key, fallback); } catch { return fallback; } }
        static int I(string key, int fallback) { try { return PlayerPrefs.GetInt(key, fallback); } catch { return fallback; } }

        public static void Load()
        {
            Fullscreen = I("rw.fullscreen", 1) == 1;
            Resolution = I("rw.resolution", -1);
            Quality = Mathf.Clamp(I("rw.quality", 2), 0, QualityNames.Length - 1);
            FrameCap = Mathf.Clamp(I("rw.framecap", 3), 0, FrameCaps.Length - 1);
            Master = Mathf.Clamp01(F("rw.master", 0.9f));
            Music = Mathf.Clamp01(F("rw.music", 0.7f));
            Effects = Mathf.Clamp01(F("rw.effects", 0.9f));
            Sensitivity = Mathf.Clamp(F("rw.sensitivity", 1f), 0.2f, 3f);
            AdsSensitivity = Mathf.Clamp(F("rw.adssensitivity", 1f), 0.3f, 2f);
        }

        public static void Save()
        {
            try
            {
                PlayerPrefs.SetInt("rw.fullscreen", Fullscreen ? 1 : 0);
                PlayerPrefs.SetInt("rw.resolution", Resolution);
                PlayerPrefs.SetInt("rw.quality", Quality);
                PlayerPrefs.SetInt("rw.framecap", FrameCap);
                PlayerPrefs.SetFloat("rw.master", Master);
                PlayerPrefs.SetFloat("rw.music", Music);
                PlayerPrefs.SetFloat("rw.effects", Effects);
                PlayerPrefs.SetFloat("rw.sensitivity", Sensitivity);
                PlayerPrefs.SetFloat("rw.adssensitivity", AdsSensitivity);
                PlayerPrefs.Save();
            }
            catch { }
        }

        /// Volume-only refresh, cheap enough for every slider tick.
        public static void ApplyAudio() => AudioListener.volume = Master;

        /// Applies everything; display changes are skipped for headless/capture runs.
        public static void Apply(bool display = true)
        {
            ApplyAudio();
            int levels = QualitySettings.names.Length;
            if (levels > 0) QualitySettings.SetQualityLevel(Mathf.RoundToInt(Quality * (levels - 1) / 3f), true);
            QualitySettings.shadowDistance = new[] { 25f, 45f, 70f, 110f }[Quality];
            foreach (var fx in UnityEngine.Object.FindObjectsByType<PostFx>(FindObjectsSortMode.None)) fx.enabled = Quality >= 1;
            // After the quality level (which carries its own vSync flag): an explicit cap keeps frame pacing even.
            int cap = FrameCaps[FrameCap].fps;
            QualitySettings.vSyncCount = cap == 0 ? 1 : 0;
            Application.targetFrameRate = cap > 0 ? cap : -1;
            if (!display) return;
            var mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (Resolution >= 0 && Resolution < Resolutions.Length)
                Screen.SetResolution(Resolutions[Resolution].w, Resolutions[Resolution].h, mode);
            else
                Screen.SetResolution(Fullscreen ? Screen.currentResolution.width : 1600, Fullscreen ? Screen.currentResolution.height : 900, mode);
        }

        /// Only resolutions the monitor can show are offered (larger ones would overflow a window or be rescaled).
        static bool Fits(int index)
        {
            int w = Display.main.systemWidth, h = Display.main.systemHeight;
            if (w <= 0 || h <= 0) { w = Screen.currentResolution.width; h = Screen.currentResolution.height; }
            return Resolutions[index].w <= w && Resolutions[index].h <= h;
        }

        static void NextResolution()
        {
            for (int step = 0; step < Resolutions.Length + 1; step++)
            {
                Resolution = Resolution + 1 >= Resolutions.Length ? -1 : Resolution + 1;
                if (Resolution < 0 || Fits(Resolution)) return;
            }
            Resolution = -1;
        }

        /// Builds the options panel into a vertical stack (used by the lobby page and the pause menu).
        public static void BuildPanel(Transform container)
        {
            // Scrollable: the key list makes the panel taller than the sheets that host it.
            var host = Ui.Rect(container, "SettingsScroll");
            host.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var stack = Widgets.ScrollList(host, out _);
            stack.parent.GetComponent<RectTransform>().Fill();
            stack.parent.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            stack.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(0, 12, 0, 8);
            Text Row(string label, out RectTransform right)
            {
                var row = Ui.Rect(stack, "Row").Sized(44);
                Ui.Label(row, label, 15, Palette.Bone, TextAnchor.MiddleLeft, true).rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(160, 30), new Vector2(0, 0.5f));
                right = Ui.Rect(row, "Control");
                right.anchorMin = new Vector2(0, 0); right.anchorMax = new Vector2(1, 1); right.offsetMin = new Vector2(170, 4); right.offsetMax = new Vector2(0, -4);
                return null;
            }
            void Cycle(string label, Func<string> caption, Action next)
            {
                Row(label, out var right);
                Button b = null;
                b = Ui.Button(right, caption(), () => { next(); b.SetCaption(caption()); Save(); Apply(); }, 15, Palette.Amber);
                b.GetComponent<RectTransform>().Fill();
            }
            void Slide(string label, float min, float max, Func<float> get, Action<float> set, Func<float, string> fmt)
            {
                Row(label, out var right);
                var value = Ui.Label(right, fmt(get()), 14, Palette.Muted, TextAnchor.MiddleRight);
                value.rectTransform.Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(60, 24), new Vector2(1, 0.5f));
                var host = Ui.Rect(right, "Slider");
                host.anchorMin = new Vector2(0, 0.5f); host.anchorMax = new Vector2(1, 0.5f);
                host.sizeDelta = new Vector2(-70, 16); host.anchoredPosition = new Vector2(-35, 0);
                Widgets.Slider(host, min, max, get(), v => { set(v); value.text = fmt(v); Save(); ApplyAudio(); }).GetComponent<RectTransform>().Fill();
            }

            Widgets.Section(stack, "畫面").Sized(26);
            Cycle("顯示模式", () => Fullscreen ? "全螢幕" : "視窗化", () => Fullscreen = !Fullscreen);
            Cycle("解析度 / 比例", () => Resolution >= 0 && Resolution < Resolutions.Length ? Resolutions[Resolution].label : "自動（螢幕原生）", NextResolution);
            Cycle("畫質", () => QualityNames[Quality] + (Quality == 0 ? "（關閉光暈後製）" : ""), () => Quality = (Quality + 1) % QualityNames.Length);
            Cycle("幀率上限", () => FrameCaps[FrameCap].label, () => FrameCap = (FrameCap + 1) % FrameCaps.Length);
            Widgets.Section(stack, "音效").Sized(26);
            Slide("主音量", 0, 1, () => Master, v => Master = v, v => $"{v * 100:0}%");
            Slide("背景音樂", 0, 1, () => Music, v => Music = v, v => $"{v * 100:0}%");
            Slide("戰鬥音效", 0, 1, () => Effects, v => Effects = v, v => $"{v * 100:0}%");
            Widgets.Section(stack, "操作").Sized(26);
            Slide("滑鼠靈敏度", 0.2f, 3f, () => Sensitivity, v => Sensitivity = v, v => $"{v:0.00}×");
            Slide("開鏡靈敏度", 0.3f, 2f, () => AdsSensitivity, v => AdsSensitivity = v, v => $"{v:0.00}×");
            Widgets.Section(stack, "按鍵").Sized(26);
            Bindings.BuildRows(stack);
        }
    }
}
