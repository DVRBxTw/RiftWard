using System.Collections.Generic;
using RiftWard.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace RiftWard.Client
{
    /// Middle-mouse map pings, shown to everyone in the room for a few seconds.
    /// Tap: mark what the crosshair points at (an enemy, which the mark then follows; a supply; or the ground).
    /// Double tap: 危險 at that spot. Hold: a four-way wheel (集合 / 危險 / 需要彈藥 / 這裡), pick by moving the mouse.
    public sealed class PingMarkers
    {
        /// Kind on the wire. 1–4 are PickupKind + 1.
        public const byte Ground = 0, Enemy = 5, Danger = 6, Regroup = 7, NeedAmmo = 8, MaxKind = 8;
        const float Life = 8f, EnemyLife = 6f, HoldForWheel = 0.22f, DoubleTap = 0.3f;
        const int MaxPerPlayer = 2;

        sealed class Mark
        {
            public int Owner, Target; public string Name; public Vector3 Position; public byte Kind; public float Born;
            public RectTransform Root; public Text Label, Arrow; public Image Diamond, Back;
        }

        readonly Canvas canvas;
        readonly List<Mark> marks = new List<Mark>();
        readonly Sfx sfx;

        // Input state: press time, last tap time, and the wheel.
        float pressedAt = -1f, lastTapAt = -9f;
        bool wheelOpen;
        Vector2 wheelAim;
        int wheelChoice = -1;
        readonly RectTransform wheel;
        readonly Image[] wheelSlots = new Image[4];
        readonly Text[] wheelLabels = new Text[4];
        static readonly byte[] WheelKinds = { Regroup, Danger, NeedAmmo, Ground };   // up, right, down, left
        static readonly Vector2[] WheelDirs = { Vector2.up, Vector2.right, Vector2.down, Vector2.left };

        public PingMarkers(Sfx sfx)
        {
            this.sfx = sfx;
            canvas = Ui.Canvas("Pings", 12);
            wheel = Ui.Rect(canvas.transform, "Wheel").Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 300));
            var hub = Ui.Box(wheel, new Color(0.03f, 0.031f, 0.034f, 0.55f), "Hub");
            hub.sprite = Disc(); hub.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56, 56));
            for (int i = 0; i < 4; i++)
            {
                var slot = Ui.Box(wheel, new Color(0.03f, 0.031f, 0.034f, 0.72f), "Slot");
                slot.rectTransform.Place(new Vector2(0.5f, 0.5f), WheelDirs[i] * 96f, new Vector2(112, 40));
                slot.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.6f);
                wheelSlots[i] = slot;
                var label = Ui.Label(slot.rectTransform, NameOf(WheelKinds[i]), 16, ColorOf(WheelKinds[i]), TextAnchor.MiddleCenter, true);
                label.rectTransform.Fill();
                wheelLabels[i] = label;
            }
            wheel.gameObject.SetActive(false);
        }

        /// The wheel is open: the game should not turn the view with the mouse.
        public bool WheelOpen => wheelOpen;

        public static string NameOf(byte kind)
        {
            switch (kind)
            {
                case 1: return "醫療包";
                case 2: return "彈藥";
                case 3: return "經驗結晶";
                case 4: return "補給箱";
                case Enemy: return "敵人";
                case Danger: return "危險";
                case Regroup: return "集合";
                case NeedAmmo: return "需要彈藥";
                default: return "這裡";
            }
        }

        public static Color ColorOf(byte kind)
        {
            switch (kind)
            {
                case 1: return Palette.Hex("5dff8f");
                case 2: return Palette.Hex("ffd166");
                case 3: return Palette.Hex("5ee6ff");
                case 4: return Palette.Hex("ffcf4a");
                case Enemy: return Palette.Hex("ff5c4d");
                case Danger: return Palette.Hex("ff3b6b");
                case Regroup: return Palette.Hex("b693ff");
                case NeedAmmo: return Palette.Hex("ffd166");
                default: return Palette.Teammate;
            }
        }

        /// Called every frame while playing (Active). Returns a ping to send, if one was made this frame.
        public bool Poll(Camera cam, GameSim sim, out Vector3 position, out byte kind, out int target)
        {
            position = default; kind = Ground; target = 0;
            if (Input.GetMouseButtonDown(2)) { pressedAt = Time.unscaledTime; wheelAim = Vector2.zero; wheelChoice = -1; }
            if (pressedAt < 0) return false;
            float held = Time.unscaledTime - pressedAt;
            if (Input.GetMouseButton(2))
            {
                if (held >= HoldForWheel && !wheelOpen) { wheelOpen = true; wheel.gameObject.SetActive(true); }
                if (wheelOpen)
                {
                    wheelAim += new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 12f;
                    wheelAim = Vector2.ClampMagnitude(wheelAim, 120f);
                    wheelChoice = -1;
                    if (wheelAim.magnitude > 30f)
                    {
                        float best = -2f;
                        for (int i = 0; i < 4; i++) { float d = Vector2.Dot(wheelAim.normalized, WheelDirs[i]); if (d > best) { best = d; wheelChoice = i; } }
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        bool on = i == wheelChoice;
                        var c = ColorOf(WheelKinds[i]);
                        wheelSlots[i].color = on ? new Color(c.r * 0.35f, c.g * 0.35f, c.b * 0.35f, 0.9f) : new Color(0.03f, 0.031f, 0.034f, 0.72f);
                        wheelSlots[i].rectTransform.localScale = Vector3.one * (on ? 1.08f : 1f);
                        wheelLabels[i].color = on ? Color.white : c;
                    }
                }
                return false;
            }
            // Released.
            pressedAt = -1f;
            if (wheelOpen)
            {
                wheelOpen = false; wheel.gameObject.SetActive(false);
                if (wheelChoice < 0 || !Target(cam, sim, out position, out _, out _)) return false;
                kind = WheelKinds[wheelChoice];
                return true;
            }
            if (!Target(cam, sim, out position, out kind, out target)) return false;
            if (Time.unscaledTime - lastTapAt < DoubleTap) { kind = Danger; target = 0; lastTapAt = -9f; }
            else lastTapAt = Time.unscaledTime;
            return true;
        }

        /// For the automated screenshots: the wheel open with one slot highlighted.
        public void ShowWheelForCapture(int choice)
        {
            wheel.gameObject.SetActive(true);
            for (int i = 0; i < 4; i++)
            {
                bool on = i == choice;
                var c = ColorOf(WheelKinds[i]);
                wheelSlots[i].color = on ? new Color(c.r * 0.35f, c.g * 0.35f, c.b * 0.35f, 0.9f) : new Color(0.03f, 0.031f, 0.034f, 0.72f);
                wheelSlots[i].rectTransform.localScale = Vector3.one * (on ? 1.08f : 1f);
                wheelLabels[i].color = on ? Color.white : c;
            }
        }

        public void CancelInput() { pressedAt = -1f; wheelOpen = false; if (wheel.gameObject.activeSelf) wheel.gameObject.SetActive(false); }

        /// Picks what the crosshair points at: an enemy near the aim line, else the pickup closest to it, else the ground.
        public static bool Target(Camera cam, GameSim sim, out Vector3 position, out byte kind, out int target)
        {
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            position = default; kind = Ground; target = 0;
            float best = float.MaxValue;
            float wall = Physics.Raycast(ray, out var wallHit, 150f) ? wallHit.distance : 150f;
            foreach (var e in sim.Enemies.Values)
            {
                var to = e.Position + Vector3.up * (e.Boss ? 1.6f : 1f) - ray.origin;
                float dist = to.magnitude;
                if (dist > 120f || dist < 0.5f || dist > wall + 2f) continue;
                float angle = Vector3.Angle(ray.direction, to);
                if (angle > Mathf.Max(2.5f, Mathf.Rad2Deg * Mathf.Atan2(e.Boss ? 2f : 0.9f, dist))) continue;
                if (angle < best) { best = angle; position = e.Position; kind = Enemy; target = e.Id; }
            }
            if (kind == Enemy) return true;
            foreach (var p in sim.Pickups.Values)
            {
                var to = p.Position + Vector3.up * 0.4f - ray.origin;
                float dist = to.magnitude;
                if (dist > 60f || dist < 0.5f) continue;
                float angle = Vector3.Angle(ray.direction, to);
                // Within a small cone (wider up close), favouring what sits nearest the crosshair.
                float limit = Mathf.Max(4f, Mathf.Rad2Deg * Mathf.Atan2(1.2f, dist));
                if (angle > limit) continue;
                float score = angle + (p.Kind == PickupKind.Xp ? 2f : 0f);   // crystals are everywhere; prefer real supplies
                if (score < best) { best = score; position = p.Position; kind = (byte)(Mathf.Min((int)p.Kind, (int)PickupKind.Chest) + 1); }   // the super chest pings as a chest
            }
            if (kind != Ground) return true;
            if (wall < 150f) { position = wallHit.point; return true; }
            if (ray.direction.y < -0.02f) { position = ray.origin + ray.direction * (-ray.origin.y / ray.direction.y); return true; }
            return false;
        }

        /// A ping arrived (or was made offline). name is empty for your own; teammates' pings also go to the chat log.
        public void Add(int owner, string name, Vector3 position, byte kind, int target = 0)
        {
            // A double tap's danger replaces the tap's mark made a moment ago.
            for (int i = marks.Count - 1; i >= 0; i--)
                if (marks[i].Owner == owner && kind == Danger && Time.time - marks[i].Born < 0.6f) Remove(i);
            int mine = 0;
            for (int i = marks.Count - 1; i >= 0; i--)
                if (marks[i].Owner == owner && ++mine >= MaxPerPlayer) Remove(i);
            var color = ColorOf(kind);
            var root = Ui.Rect(canvas.transform, "Ping");
            root.sizeDelta = new Vector2(160, 60);
            root.SetAsFirstSibling();   // under the wheel
            var diamond = Ui.Box(root, color, "Diamond");
            diamond.rectTransform.Place(new Vector2(0.5f, 0), Vector2.zero, new Vector2(14, 14), new Vector2(0.5f, 0.5f));
            diamond.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            diamond.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.7f);
            var arrow = Ui.Label(root, "▲", 20, color, TextAnchor.MiddleCenter, true).Outlined(0.9f);
            arrow.rectTransform.Place(new Vector2(0.5f, 0), Vector2.zero, new Vector2(24, 24), new Vector2(0.5f, 0.5f));
            arrow.enabled = false;
            bool named = !string.IsNullOrEmpty(name);
            var back = Ui.Box(root, new Color(0.03f, 0.031f, 0.034f, 0.6f), "Back");
            back.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 14), new Vector2(named ? 128 : 112, named ? 40 : 24), new Vector2(0.5f, 0));
            var label = Ui.Label(root, "", 15, color, TextAnchor.MiddleCenter, true).Outlined(0.9f);
            label.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 14), back.rectTransform.sizeDelta, new Vector2(0.5f, 0));
            label.supportRichText = true;
            marks.Add(new Mark { Owner = owner, Target = target, Name = name, Position = position, Kind = kind, Born = Time.time, Root = root, Label = label, Arrow = arrow, Diamond = diamond, Back = back });
            sfx?.Play(kind == Danger || kind == Enemy ? "ping_danger" : "ping", 0.5f);
            if (named) ChatLog.Add(new ChatLog.Line { Channel = ChatChannel.Room, Name = name, Text = $"標記了「{NameOf(kind)}」" });
        }

        void Remove(int i) { Object.Destroy(marks[i].Root.gameObject); marks.RemoveAt(i); }

        public void Clear() { CancelInput(); for (int i = marks.Count - 1; i >= 0; i--) Remove(i); }

        /// Live marks for the radar.
        public IEnumerable<(Vector3 position, Color color)> Radar()
        {
            foreach (var m in marks) yield return (m.Position, ColorOf(m.Kind));
        }

        public void Update(Camera cam, GameSim sim, Vector3 viewer)
        {
            float scale = canvas.transform.localScale.x <= 0 ? 1f : canvas.transform.localScale.x;
            for (int i = marks.Count - 1; i >= 0; i--)
            {
                var m = marks[i];
                float age = Time.time - m.Born;
                bool gone = false;
                if (m.Kind == Enemy)
                {
                    // Follows the enemy; ends when it dies.
                    if (sim.Enemies.TryGetValue(m.Target, out var e)) m.Position = e.Position;
                    else gone = true;
                }
                else if (m.Kind >= 1 && m.Kind <= 4) gone = !AnyPickupNear(sim, m.Position);   // someone picked it up
                float life = m.Kind == Enemy ? EnemyLife : Life;
                if (age > life || gone || cam == null) { Remove(i); continue; }

                var screen = cam.WorldToScreenPoint(m.Position + Vector3.up * (m.Kind == Enemy ? 2.2f : 0.6f));
                float margin = 48f;
                bool behind = screen.z < 0;
                bool onScreen = !behind && screen.x > margin && screen.x < Screen.width - margin && screen.y > margin && screen.y < Screen.height - margin;
                Vector2 centre = new Vector2(Screen.width, Screen.height) * 0.5f;
                Vector2 pos = new Vector2(screen.x, screen.y);
                if (!onScreen)
                {
                    // Off screen: slide to the edge along the direction from the centre and point an arrow that way.
                    Vector2 dir = pos - centre;
                    if (behind) dir = -dir;
                    if (dir.sqrMagnitude < 1f) dir = Vector2.down;
                    float half = Mathf.Min((Screen.width * 0.5f - margin) / Mathf.Max(0.001f, Mathf.Abs(dir.x)), (Screen.height * 0.5f - margin - 30f) / Mathf.Max(0.001f, Mathf.Abs(dir.y)));
                    pos = centre + dir * half;
                    m.Arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
                }
                m.Arrow.enabled = !onScreen;
                m.Diamond.enabled = onScreen;
                m.Root.anchorMin = m.Root.anchorMax = Vector2.zero;
                m.Root.pivot = new Vector2(0.5f, 0);
                m.Root.anchoredPosition = pos / scale;
                int metres = Mathf.RoundToInt(Vector3.Distance(viewer, m.Position));
                string head = $"{NameOf(m.Kind)}  {metres}m";
                m.Label.text = string.IsNullOrEmpty(m.Name) ? head : $"{head}\n<size=12><color=#d6d1c6>{ChatLog.Safe(m.Name)}</color></size>";
                float pulse = 1f + 0.25f * Mathf.Max(0, 1f - age * 3f) + 0.06f * Mathf.Sin(age * 6f);
                m.Diamond.rectTransform.localScale = Vector3.one * pulse;
                float fade = Mathf.Clamp01((life - age) / 1f);
                var c = ColorOf(m.Kind); c.a = fade;
                m.Diamond.color = c; m.Label.color = c; m.Arrow.color = c;
                m.Back.color = new Color(0.03f, 0.031f, 0.034f, 0.6f * fade);
            }
        }

        static bool AnyPickupNear(GameSim sim, Vector3 at)
        {
            foreach (var p in sim.Pickups.Values) if ((p.Position - at).sqrMagnitude < 0.25f) return true;
            return false;
        }

        static Sprite disc;
        static Sprite Disc()
        {
            if (disc != null) return disc;
            const int n = 64;
            var tex = new Texture2D(n, n) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude;
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(n / 2f - d)));
                }
            tex.Apply();
            return disc = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }
    }
}
