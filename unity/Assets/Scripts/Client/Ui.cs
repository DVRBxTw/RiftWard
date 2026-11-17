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
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
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
            o.effectDistance = new Vector2(1.2f, -1.2f);
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
            return button;
        }

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
}
