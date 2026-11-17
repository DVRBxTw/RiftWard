using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RiftWard.Client
{
    /// Rebindable keyboard controls, saved on this computer. Mouse buttons (fire / aim) and Esc stay fixed.
    public static class Bindings
    {
        public enum Action { Forward, Back, Left, Right, Jump, Dash, Descend, Reload, Skill, Interact, View, Slot1, Slot2 }

        static readonly (Action action, string label, KeyCode key)[] Defaults =
        {
            (Action.Forward, "前進", KeyCode.W), (Action.Back, "後退", KeyCode.S), (Action.Left, "左移", KeyCode.A), (Action.Right, "右移", KeyCode.D),
            (Action.Jump, "跳躍 / 上升", KeyCode.Space), (Action.Dash, "衝刺", KeyCode.LeftShift), (Action.Descend, "下降（飛行）", KeyCode.LeftControl),
            (Action.Reload, "換彈", KeyCode.R), (Action.Skill, "手榴彈", KeyCode.Q), (Action.Interact, "救援 / 互動", KeyCode.E),
            (Action.View, "切換視角", KeyCode.V), (Action.Slot1, "1 號武器", KeyCode.Alpha1), (Action.Slot2, "2 號武器", KeyCode.Alpha2),
        };

        static readonly KeyCode[] keys = new KeyCode[Defaults.Length];
        static bool loaded;

        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            for (int i = 0; i < Defaults.Length; i++)
            {
                keys[i] = Defaults[i].key;
                try
                {
                    int saved = PlayerPrefs.GetInt("rw.key." + Defaults[i].action, (int)Defaults[i].key);
                    if (Enum.IsDefined(typeof(KeyCode), saved) && saved != (int)KeyCode.Escape && saved != (int)KeyCode.None) keys[i] = (KeyCode)saved;
                }
                catch { }
            }
        }

        public static KeyCode Key(Action a) { EnsureLoaded(); return keys[(int)a]; }

        /// Held. The default modifiers also accept their right-hand twin, as before.
        public static bool Held(Action a)
        {
            var k = Key(a);
            if (Input.GetKey(k)) return true;
            return (k == KeyCode.LeftShift && Input.GetKey(KeyCode.RightShift)) || (k == KeyCode.LeftControl && Input.GetKey(KeyCode.RightControl));
        }

        public static bool Pressed(Action a) => Input.GetKeyDown(Key(a));

        /// Short display name for prompts ("R", "Shift", "空白鍵").
        public static string Name(Action a) => Name(Key(a));

        public static string Name(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.Space: return "空白鍵";
                case KeyCode.LeftShift: case KeyCode.RightShift: return "Shift";
                case KeyCode.LeftControl: case KeyCode.RightControl: return "Ctrl";
                case KeyCode.LeftAlt: case KeyCode.RightAlt: return "Alt";
                case KeyCode.Tab: return "Tab";
                case KeyCode.CapsLock: return "Caps";
                case KeyCode.Mouse2: return "滑鼠中鍵";
                case KeyCode.Mouse3: return "滑鼠側鍵 1";
                case KeyCode.Mouse4: return "滑鼠側鍵 2";
            }
            string s = k.ToString();
            if (s.StartsWith("Alpha")) return s.Substring(5);
            if (s.StartsWith("Keypad")) return "數字鍵盤 " + s.Substring(6);
            return s;
        }

        /// One-line control summary for the lobby, using the current keys.
        public static string Summary()
        {
            string move = Name(Action.Forward) + Name(Action.Left) + Name(Action.Back) + Name(Action.Right);
            return $"{move} 移動 · 左鍵射擊 · 右鍵瞄準 · {Name(Action.Reload)} 換彈 · {Name(Action.Slot1)}/{Name(Action.Slot2)} 換槍 · {Name(Action.Skill)} 手榴彈 · {Name(Action.Dash)} 衝刺 · {Name(Action.Interact)} 救援 · {Name(Action.View)} 視角";
        }

        /// Assigns a key; if another action already uses it, the two swap so nothing is left unbound.
        public static void Set(Action a, KeyCode k)
        {
            EnsureLoaded();
            int i = (int)a;
            for (int j = 0; j < keys.Length; j++)
                if (j != i && keys[j] == k) keys[j] = keys[i];
            keys[i] = k;
            Save();
        }

        public static void Reset()
        {
            EnsureLoaded();
            for (int i = 0; i < Defaults.Length; i++) keys[i] = Defaults[i].key;
            Save();
        }

        static void Save()
        {
            try
            {
                for (int i = 0; i < Defaults.Length; i++) PlayerPrefs.SetInt("rw.key." + Defaults[i].action, (int)keys[i]);
                PlayerPrefs.Save();
            }
            catch { }
        }

        static KeyCode[] candidates;

        /// Key rows for the settings panel: click a row, then press the new key (Esc cancels).
        public static void BuildRows(Transform stack)
        {
            EnsureLoaded();
            var captions = new List<(Button button, Action action)>();
            void Refresh()
            {
                foreach (var (button, action) in captions) button.SetCaption(Name(action));
            }
            for (int i = 0; i < Defaults.Length; i++)
            {
                var action = Defaults[i].action;
                var row = Ui.Rect(stack, "Key").Sized(40);
                Ui.Label(row, Defaults[i].label, 15, Palette.Bone, TextAnchor.MiddleLeft, true).rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(160, 30), new Vector2(0, 0.5f));
                Button b = null;
                b = Ui.Button(row, Name(action), () =>
                {
                    b.SetCaption("請按下新按鍵…（Esc 取消）");
                    var capture = b.gameObject.GetComponent<KeyCapture>() ?? b.gameObject.AddComponent<KeyCapture>();
                    capture.Begin(k => { if (k != KeyCode.None) Set(action, k); Refresh(); });
                }, 15, Palette.Amber);
                var rt = b.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 1); rt.offsetMin = new Vector2(170, 3); rt.offsetMax = new Vector2(0, -3);
                captions.Add((b, action));
            }
            Ui.Label(stack, "點選右側按鍵後，按下想要的新按鍵；Esc 取消。重複的按鍵會自動互換。", 12, Palette.Muted).Sized(22);
            Ui.Button(stack, "恢復預設按鍵", () => { Reset(); Refresh(); }, 14, Palette.Muted).Sized(36);
        }

        /// Listens for the next key press on behalf of one binding row.
        sealed class KeyCapture : MonoBehaviour
        {
            System.Action<KeyCode> done;
            int startFrame;

            public void Begin(System.Action<KeyCode> callback) { done = callback; startFrame = Time.frameCount; enabled = true; }

            void Update()
            {
                if (done == null || Time.frameCount == startFrame) return;   // ignore the click that started capture
                if (Input.GetKeyDown(KeyCode.Escape)) { Finish(KeyCode.None); return; }
                if (candidates == null)
                {
                    var list = new List<KeyCode>();
                    foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
                        if (k != KeyCode.None && k != KeyCode.Escape && k != KeyCode.Mouse0 && k != KeyCode.Mouse1 && (int)k < (int)KeyCode.JoystickButton0) list.Add(k);
                    candidates = list.ToArray();
                }
                foreach (var k in candidates)
                    if (Input.GetKeyDown(k)) { Finish(k); return; }
            }

            void Finish(KeyCode k)
            {
                var callback = done;
                done = null;
                enabled = false;
                callback?.Invoke(k);
            }
        }
    }
}
