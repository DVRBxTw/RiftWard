using System;
using UnityEngine;
using UnityEngine.UI;

namespace RiftWard.Client
{
    /// Form controls built from code in the field-manual style.
    public static class Widgets
    {
        public static InputField Input(Transform parent, string placeholder, bool secret = false, int maxLength = 0)
        {
            var bg = Ui.Box(parent, new Color(0, 0, 0, 0.35f), "Input");
            var line = bg.gameObject.AddComponent<Outline>();
            line.effectColor = Palette.Line;
            line.effectDistance = new Vector2(1, -1);
            var text = Ui.Label(bg.transform, "", 17, Palette.Bone, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.rectTransform.Fill();
            text.rectTransform.offsetMin = new Vector2(16, 0);
            text.rectTransform.offsetMax = new Vector2(-16, 0);
            var hint = Ui.Label(bg.transform, placeholder, 17, Palette.Hex("5d6064"), TextAnchor.MiddleLeft);
            hint.rectTransform.Fill();
            hint.rectTransform.offsetMin = new Vector2(16, 0);
            hint.rectTransform.offsetMax = new Vector2(-16, 0);
            var field = bg.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.placeholder = hint;
            field.caretColor = Palette.Amber;
            field.selectionColor = new Color(Palette.Amber.r, Palette.Amber.g, Palette.Amber.b, 0.35f);
            if (secret) field.contentType = InputField.ContentType.Password;
            if (maxLength > 0) field.characterLimit = maxLength;
            field.onEndEdit.AddListener(_ => line.effectColor = Palette.Line);
            return field;
        }

        public static Slider Slider(Transform parent, float min, float max, float value, Action<float> changed)
        {
            var root = Ui.Rect(parent, "Slider");
            var track = Ui.Box(root, new Color(1, 1, 1, 0.14f), "Track").rectTransform;
            track.anchorMin = new Vector2(0, 0.5f); track.anchorMax = new Vector2(1, 0.5f);
            track.sizeDelta = new Vector2(0, 4);
            var fillArea = Ui.Rect(root, "FillArea");
            fillArea.anchorMin = new Vector2(0, 0.5f); fillArea.anchorMax = new Vector2(1, 0.5f);
            fillArea.sizeDelta = new Vector2(0, 4);
            var fill = Ui.Box(fillArea, Palette.Amber, "Fill").rectTransform;
            fill.sizeDelta = Vector2.zero;
            var handleArea = Ui.Rect(root, "HandleArea").Fill();
            var handle = Ui.Box(handleArea, Palette.Bone, "Handle").rectTransform;
            handle.sizeDelta = new Vector2(10, 22);
            var slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            slider.onValueChanged.AddListener(v => changed?.Invoke(v));
            return slider;
        }

        /// A switch rendered as a full-width row: label on the left, ON / OFF state on the right.
        public static Button Toggle(Transform parent, string label, bool initial, Action<bool> changed, string onText = "開啟", string offText = "關閉")
        {
            bool on = initial;
            Text state = null;
            var row = Ui.Button(parent, "", null, 16);
            var text = row.GetComponentInChildren<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.rectTransform.offsetMin = new Vector2(18, 0);
            text.text = label;
            state = Ui.Label(row.transform, "", 14, Palette.Muted, TextAnchor.MiddleRight);
            state.rectTransform.Fill();
            state.rectTransform.offsetMax = new Vector2(-18, 0);
            void Paint()
            {
                state.text = on ? onText : offText;
                state.color = on ? Palette.Amber : Palette.Muted;
                row.GetComponent<Outline>().effectColor = on ? Palette.Amber : Palette.Line;
            }
            Paint();
            row.onClick.AddListener(() => { on = !on; Paint(); changed?.Invoke(on); });
            return row;
        }

        /// Scrollable vertical list; add rows to the returned content transform.
        public static RectTransform ScrollList(Transform parent, out ScrollRect scroll)
        {
            var viewport = Ui.Box(parent, new Color(0, 0, 0, 0.2f), "Viewport");
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Ui.VStack(viewport.transform, 4);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            return content;
        }

        /// Mono-spaced small caps caption with a hairline rule, used as a section header.
        public static RectTransform Section(Transform parent, string caption)
        {
            var row = Ui.Rect(parent, "Section");
            var label = Ui.Label(row, caption, 13, Palette.Muted, TextAnchor.MiddleLeft);
            label.rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(140, 20), new Vector2(0, 0.5f));
            var rule = Ui.Box(row, Palette.Line, "Rule").rectTransform;
            rule.anchorMin = new Vector2(0, 0.5f); rule.anchorMax = new Vector2(1, 0.5f);
            rule.offsetMin = new Vector2(label.preferredWidth + 14, -0.5f); rule.offsetMax = new Vector2(0, 0.5f);
            return row;
        }
    }
}
