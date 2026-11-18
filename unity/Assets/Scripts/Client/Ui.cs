using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RiftWard.Client
{
    /// Small uGUI toolkit: everything is built from code on a 1280x800 reference canvas.
    public static class Ui
    {
        static Font font;
        public static Font Font => font != null ? font : font = Resources.Load<Font>("ui-font");

        public static Canvas Canvas(string name, int order)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            canvas.pixelPerfect = true;   // snap text and edges to whole pixels at any UI scale (no half-pixel blur)
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800);
            scaler.matchWidthOrHeight = 0.5f;
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            return canvas;
        }

        public static RectTransform Rect(Transform parent, string name = "Rect")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// Places a rect by anchor (0..1 per axis) plus pixel offset and size.
        public static RectTransform Place(this RectTransform r, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = pivot ?? anchor;
            r.anchoredPosition = position;
            r.sizeDelta = size;
            return r;
        }

        public static RectTransform Fill(this RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            return r;
        }

        public static Image Box(Transform parent, Color color, string name = "Box")
        {
            var r = Rect(parent, name);
            var img = r.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.UpperLeft, bool bold = false)
        {
            var r = Rect(parent, "Label");
            var t = r.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            // The CJK font has one weight, so bold is synthesised by smearing the glyphs; below 15 px that turns
            // the strokes to mush, so small text stays regular (colour and size carry the emphasis).
            t.fontStyle = bold && size >= 15 ? FontStyle.Bold : FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false;
            t.raycastTarget = false;
            return t;
        }

        public static Text Outlined(this Text t, float alpha = 0.8f)
        {
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0, 0, 0, alpha);
            o.effectDistance = new Vector2(1f, -1f);   // whole pixels: a fractional offset smears the glyphs
            return t;
        }

        /// A flat progress bar: background plus a filled foreground (value 0..1).
        public sealed class Bar
        {
            public readonly RectTransform Root;
            readonly RectTransform fill;
            readonly Image fillImage;
            public Bar(Transform parent, Color back, Color front)
            {
                Root = Box(parent, back, "Bar").rectTransform;
                fillImage = Box(Root, front, "Fill");
                fill = fillImage.rectTransform;
                fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(1, 1); fill.pivot = new Vector2(0, 0.5f);
                fill.offsetMin = fill.offsetMax = Vector2.zero;
            }
            public float Value { set => fill.anchorMax = new Vector2(Mathf.Clamp01(value), 1); }
            public Color Color { set => fillImage.color = value; }
        }

        public static Button Button(Transform parent, string text, Action onClick, int size = 18, Color? textColor = null, Color? fill = null, Color? border = null)
        {
            var bg = Box(parent, fill ?? new Color(1, 1, 1, 0.035f), "Button");
            var button = bg.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.15f, 0.9f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.disabledColor = new Color(1, 1, 1, 0.35f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick?.Invoke());
            var line = bg.gameObject.AddComponent<Outline>();
            line.effectColor = border ?? Palette.Line;
            line.effectDistance = new Vector2(1, -1);
            var label = Label(bg.transform, text, size, textColor ?? Palette.Bone, TextAnchor.MiddleCenter);
            label.rectTransform.Fill();
            bg.gameObject.AddComponent<ButtonFeel>();
            return button;
        }

        /// Destructive actions (移除, 登出, 離開遊戲): red text and a red outline on a faint fill.
        public static Button DangerButton(Transform parent, string text, Action onClick, int size = 15) =>
            Button(parent, text, onClick, size, Palette.Danger, new Color(Palette.Danger.r, Palette.Danger.g, Palette.Danger.b, 0.06f), new Color(Palette.Danger.r, Palette.Danger.g, Palette.Danger.b, 0.6f));

        /// The page's main action: Enter presses it when no text field is being typed in (or even while typing,
        /// when DefaultWhileTyping is set — a login form). Only safe actions are registered; never a purchase.
        public static Button DefaultButton;
        public static bool DefaultWhileTyping;



        public static string Caption(this Button b) => b.GetComponentInChildren<Text>().text;
        public static void SetCaption(this Button b, string text) => b.GetComponentInChildren<Text>().text = text;

        public static RectTransform VStack(Transform parent, float spacing, TextAnchor align = TextAnchor.UpperLeft)
        {
            var r = Rect(parent, "VStack");
            var v = r.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.childAlignment = align;
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            return r;
        }

        public static RectTransform HStack(Transform parent, float spacing)
        {
            var r = Rect(parent, "HStack");
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            return r;
        }

        public static T Sized<T>(this T c, float height = -1, float width = -1) where T : Component
        {
            var le = c.gameObject.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            if (height >= 0) { le.preferredHeight = le.minHeight = height; le.flexibleHeight = 0; }
            if (width >= 0) le.preferredWidth = width;
            return c;
        }

        /// Vertical gradient image built from a 1x2 texture (used for HUD legibility shading).
        public static Image Shade(Transform parent, bool top, float alpha)
        {
            var tex = new Texture2D(1, 2) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixel(0, top ? 1 : 0, new Color(0, 0, 0, alpha));
            tex.SetPixel(0, top ? 0 : 1, new Color(0, 0, 0, 0));
            tex.Apply();
            var img = Box(parent, Color.white, "Shade");
            img.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 2), new Vector2(0.5f, 0.5f));
            img.raycastTarget = false;
            return img;
        }
    }
    /// Hover and disabled feedback for every Ui.Button. The Button's own colour tint only touches the
    /// background image, which is invisible on transparent buttons, and never dims the label.
    public sealed class ButtonFeel : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        Button button; Text label; Image fill; Outline outline;
        // Resting colours (what the caller set) and what this component last wrote over them.
        Color labelColor, fillColor, outlineColor, labelShown, fillShown, outlineShown;
        bool hover, shownHover, shownEnabled = true, started;

        void Start()
        {
            button = GetComponent<Button>(); label = GetComponentInChildren<Text>(); fill = GetComponent<Image>(); outline = GetComponent<Outline>();
            if (label != null) labelColor = labelShown = label.color;
            if (fill != null) fillColor = fillShown = fill.color;
            if (outline != null) outlineColor = outlineShown = outline.effectColor;
            started = true;
        }

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => hover = true;
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => hover = false;
        void OnDisable() => hover = false;

        void LateUpdate()
        {
            if (!started || button == null) return;
            // A caller recoloured the button (selected tab, armed confirm, invite available): that is the new rest.
            bool changed = false;
            if (label != null && label.color != labelShown) { labelColor = label.color; changed = true; }
            if (fill != null && fill.color != fillShown) { fillColor = fill.color; changed = true; }
            if (outline != null && outline.effectColor != outlineShown) { outlineColor = outline.effectColor; changed = true; }
            bool enabled = button.interactable, lit = hover && enabled;
            if (!changed && lit == shownHover && enabled == shownEnabled) return;
            shownHover = lit; shownEnabled = enabled;
            if (label != null)
            {
                var c = labelColor;
                if (lit) c = Color.Lerp(c, Color.white, 0.35f);
                if (!enabled) c.a *= 0.4f;
                label.color = labelShown = c;
            }
            if (fill != null)
            {
                var f = fillColor;
                if (lit) f = f.a >= 0.5f ? Color.Lerp(f, Color.white, 0.12f) : new Color(1f, 1f, 1f, Mathf.Max(f.a, 0.07f));
                fill.color = fillShown = f;
            }
            if (outline != null)
            {
                var o = outlineColor;
                if (lit && o.a >= 0.05f) o = Color.Lerp(o, Palette.Amber, 0.45f);
                if (!enabled) o.a *= 0.4f;
                outline.effectColor = outlineShown = o;
            }
        }
    }

    /// Puts a two-click confirm button back to its resting caption once its arm window has passed.
    public sealed class RevertCaption : MonoBehaviour
    {
        System.Func<bool> expired; string caption; Color color; Text label;
        public void Arm(System.Func<bool> expiredWhen, string restingCaption, Color restingColor)
        {
            expired = expiredWhen; caption = restingCaption; color = restingColor; label = GetComponentInChildren<Text>();
        }
        void Update()
        {
            if (label == null || expired == null || label.text == caption || !expired()) return;
            label.text = caption; label.color = color;
        }
    }
}
