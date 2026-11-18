using System.Collections.Generic;
using RiftWard.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace RiftWard.Client
{
    /// Middle-mouse map pings: a marker over the ground or a resource, shown to everyone in the room for a few seconds.
    public sealed class PingMarkers
    {
        /// Kind on the wire: 0 = a spot on the ground, otherwise PickupKind + 1.
        public const byte Ground = 0;
        const float Life = 8f, MaxPerPlayer = 2;

        sealed class Mark { public int Owner; public Vector3 Position; public byte Kind; public float Born; public RectTransform Root; public Text Label; public Image Diamond; }

        readonly Canvas canvas;
        readonly List<Mark> marks = new List<Mark>();

        readonly Sfx sfx;

        public PingMarkers(Sfx sfx) { this.sfx = sfx; canvas = Ui.Canvas("Pings", 12); }

        public static string NameOf(byte kind)
        {
            switch (kind)
            {
                case 1: return "醫療包";
                case 2: return "彈藥";
                case 3: return "經驗結晶";
                case 4: return "補給箱";
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
                default: return Palette.Teammate;
            }
        }

        /// Picks what the crosshair points at: the pickup closest to the aim ray, else the ground hit point.
        public static bool Target(Camera cam, GameSim sim, out Vector3 position, out byte kind)
        {
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            position = default; kind = Ground;
            float best = float.MaxValue;
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
                if (score < best) { best = score; position = p.Position; kind = (byte)(p.Kind + 1); }
            }
            if (kind != Ground) return true;
            if (Physics.Raycast(ray, out var hit, 80f)) { position = hit.point; return true; }
            if (ray.direction.y < -0.02f) { position = ray.origin + ray.direction * (-ray.origin.y / ray.direction.y); return true; }
            return false;
        }

        public void Add(int owner, string name, Vector3 position, byte kind)
        {
            int mine = 0;
            for (int i = marks.Count - 1; i >= 0; i--)
                if (marks[i].Owner == owner && ++mine >= MaxPerPlayer) Remove(i);
            var color = ColorOf(kind);
            var root = Ui.Rect(canvas.transform, "Ping");
            root.sizeDelta = new Vector2(160, 60);
            var diamond = Ui.Box(root, color, "Diamond");
            diamond.rectTransform.Place(new Vector2(0.5f, 0), Vector2.zero, new Vector2(14, 14), new Vector2(0.5f, 0.5f));
            diamond.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            diamond.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.7f);
            var back = Ui.Box(root, new Color(0.03f, 0.031f, 0.034f, 0.6f), "Back");
            back.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 14), new Vector2(string.IsNullOrEmpty(name) ? 104 : 120, string.IsNullOrEmpty(name) ? 24 : 40), new Vector2(0.5f, 0));
            var label = Ui.Label(root, "", 15, color, TextAnchor.MiddleCenter, true).Outlined(0.9f);
            label.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 14), back.rectTransform.sizeDelta, new Vector2(0.5f, 0));
            label.supportRichText = true;
            label.text = string.IsNullOrEmpty(name) ? NameOf(kind) : $"{NameOf(kind)}\n<size=12><color=#d6d1c6>{ChatLog.Safe(name)}</color></size>";
            marks.Add(new Mark { Owner = owner, Position = position, Kind = kind, Born = Time.time, Root = root, Label = label, Diamond = diamond });
            sfx?.Play("switch", 0.5f);
        }

        void Remove(int i) { Object.Destroy(marks[i].Root.gameObject); marks.RemoveAt(i); }

        public void Clear() { for (int i = marks.Count - 1; i >= 0; i--) Remove(i); }

        public void Update(Camera cam, GameSim sim, Vector3 viewer)
        {
            float scale = canvas.transform.localScale.x <= 0 ? 1f : canvas.transform.localScale.x;
            for (int i = marks.Count - 1; i >= 0; i--)
            {
                var m = marks[i];
                float age = Time.time - m.Born;
                // A resource ping ends early once nobody can pick it up any more.
                bool gone = m.Kind != Ground && !AnyPickupNear(sim, m.Position);
                if (age > Life || gone || cam == null) { Remove(i); continue; }
                var screen = cam.WorldToScreenPoint(m.Position + Vector3.up * 0.6f);
                bool behind = screen.z < 0;
                if (behind) { screen.x = Screen.width - screen.x; screen.y = 0; }
                // Clamp to the screen edge so off-screen pings still point the way.
                screen.x = Mathf.Clamp(screen.x, 40, Screen.width - 40);
                screen.y = Mathf.Clamp(screen.y, 40, Screen.height - 60);
                m.Root.anchorMin = m.Root.anchorMax = Vector2.zero;
                m.Root.pivot = new Vector2(0.5f, 0);
                m.Root.anchoredPosition = new Vector2(screen.x, screen.y) / scale;
                int metres = Mathf.RoundToInt(Vector3.Distance(viewer, m.Position));
                string rest = m.Label.text.Contains("\n") ? m.Label.text.Substring(m.Label.text.IndexOf('\n')) : "";
                string baseName = NameOf(m.Kind);
                m.Label.text = $"{baseName}  {metres}m{rest}";
                float pulse = 1f + 0.25f * Mathf.Max(0, 1f - age * 3f) + 0.06f * Mathf.Sin(age * 6f);
                m.Diamond.rectTransform.localScale = Vector3.one * pulse;
                float fade = Mathf.Clamp01((Life - age) / 1f);
                var c = ColorOf(m.Kind); c.a = fade;
                m.Diamond.color = c; m.Label.color = c;
            }
        }

        static bool AnyPickupNear(GameSim sim, Vector3 at)
        {
            foreach (var p in sim.Pickups.Values) if ((p.Position - at).sqrMagnitude < 0.25f) return true;
            return false;
        }
    }
}
