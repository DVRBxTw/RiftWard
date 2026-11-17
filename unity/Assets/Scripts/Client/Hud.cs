using System.Collections.Generic;
using RiftWard.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace RiftWard.Client
{
    /// Combat HUD: mission info (top-left), XP and boss bar (top-centre), radar and kill feed (top-right),
    /// vitals panel (bottom-left), weapon panel (bottom-right), crosshair with hit markers, damage-direction
    /// arcs and a capsule for prompts. Corner panels sit on soft dark backings so text never fights the scene.
    public sealed class Hud
    {
        public readonly Canvas Canvas;
        readonly Text region, wave, stats, level, xpText, banner, combo, comboCaption, headTag, skillLabel, skillTime;
        readonly Text shieldValue, shieldMax, dashLabel, dashTime, weaponName, weaponRarity, ammo, ammoMax, hint, revive, toast, bossName, lowAmmo;
        readonly Ui.Bar xpBar, shieldBar, skillBar, dashBar, reloadBar, bossBar;
        readonly RectTransform bossRoot, toastPill, radar, radarDots, killFeed, damageLayer, hitRoot;
        readonly Image bossEnrage;
        readonly Text[] slotLabels = new Text[2];
        readonly Image[] slotChips = new Image[2];
        readonly Image[] hitTicks = new Image[4];
        readonly Image vignette;
        readonly RectTransform floaterLayer;
        readonly List<(Text text, Vector3 world, float life)> floaters = new List<(Text, Vector3, float)>();
        readonly List<(RectTransform row, Text text, float life)> feed = new List<(RectTransform, Text, float)>();
        readonly List<(Image arc, float life)> damageArcs = new List<(Image, float)>();
        readonly List<Image> dots = new List<Image>();
        readonly Dictionary<int, int> lastEnemies = new Dictionary<int, int>();
        readonly HashSet<int> seen = new HashSet<int>();
        float bannerTimer, hitTimer, headTimer, killTimer, damageFlash, comboTimer, toastAlpha;
        int comboCount, localKills;
        float lastHp = -1;
        static Sprite disc;

        const float RadarRange = 28f, RadarSize = 168f;

        public Hud()
        {
            Canvas = Ui.Canvas("HUD", 10);
            var root = Canvas.transform;
            vignette = Ui.Box(root, Color.white, "Vignette");
            vignette.sprite = RadialSprite();
            vignette.rectTransform.Fill();
            vignette.raycastTarget = false;
            vignette.color = new Color(0.9f, 0.05f, 0.1f, 0);
            Ui.Shade(root, true, 0.4f).rectTransform.Place(new Vector2(0.5f, 1), Vector2.zero, new Vector2(1280, 150));
            Ui.Shade(root, false, 0.35f).rectTransform.Place(new Vector2(0.5f, 0), Vector2.zero, new Vector2(1280, 160));

            // ---- top-left mission info
            region = Mono(root, "", 13, Palette.Amber);
            region.rectTransform.Place(new Vector2(0, 1), new Vector2(40, -30), new Vector2(400, 18));
            wave = Ui.Label(root, "", 30, Palette.Bone, TextAnchor.UpperLeft, true).Outlined();
            wave.rectTransform.Place(new Vector2(0, 1), new Vector2(40, -50), new Vector2(400, 40));
            stats = Mono(root, "", 13, Palette.Hex("d6d1c6"));
            stats.rectTransform.Place(new Vector2(0, 1), new Vector2(40, -94), new Vector2(400, 20));

            // ---- top-centre XP and boss
            level = Mono(root, "LV 1", 15, Palette.Amber);
            level.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(-200, -32), new Vector2(120, 20), new Vector2(0, 1));
            xpText = Mono(root, "", 12, Palette.Hex("c9c5bb"));
            xpText.alignment = TextAnchor.UpperRight;
            xpText.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(200, -34), new Vector2(300, 20), new Vector2(1, 1));
            xpBar = new Ui.Bar(root, new Color(1, 1, 1, 0.18f), Palette.Amber);
            xpBar.Root.Place(new Vector2(0.5f, 1), new Vector2(0, -58), new Vector2(400, 3));

            bossRoot = Ui.Rect(root, "Boss").Place(new Vector2(0.5f, 1), new Vector2(0, -78), new Vector2(600, 54));
            var bossBack = Ui.Box(bossRoot, new Color(0.04f, 0.045f, 0.05f, 0.6f), "Back");
            bossBack.rectTransform.Fill();
            bossName = Ui.Label(bossRoot, "", 15, Palette.Bone, TextAnchor.UpperLeft, true).Outlined();
            bossName.rectTransform.Place(new Vector2(0, 1), new Vector2(16, -8), new Vector2(380, 20), new Vector2(0, 1));
            bossEnrage = Ui.Box(bossRoot, Palette.Danger, "Enrage");
            bossEnrage.rectTransform.Place(new Vector2(1, 1), new Vector2(-16, -10), new Vector2(64, 16), new Vector2(1, 1));
            var enrageText = Ui.Label(bossEnrage.transform, "狂暴", 11, Palette.Ink, TextAnchor.MiddleCenter, true);
            enrageText.rectTransform.Fill();
            bossBar = new Ui.Bar(bossRoot, new Color(1, 1, 1, 0.12f), Palette.Danger);
            bossBar.Root.Place(new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(568, 8), new Vector2(0.5f, 0));
            for (int i = 1; i < 10; i++)
                Ui.Box(bossRoot, new Color(0.04f, 0.045f, 0.05f, 0.8f), "Tick").rectTransform.Place(new Vector2(0, 0), new Vector2(16 + 568f * i / 10f, 12), new Vector2(2, 8), new Vector2(0.5f, 0));

            banner = Ui.Label(root, "", 40, Palette.Bone, TextAnchor.MiddleCenter, true).Outlined();
            banner.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -230), new Vector2(900, 56));

            // ---- top-right radar and kill feed
            radar = Ui.Rect(root, "Radar").Place(new Vector2(1, 1), new Vector2(-36, -28), new Vector2(RadarSize, RadarSize), new Vector2(1, 1));
            var radarBack = Ui.Box(radar, new Color(0.04f, 0.045f, 0.05f, 0.62f), "Disc");
            radarBack.sprite = Disc();
            radarBack.rectTransform.Fill();
            radarBack.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            foreach (float r in new[] { 0.33f, 0.66f })
            {
                var ring = Ui.Box(radarBack.transform, new Color(1, 1, 1, 0.08f), "Ring");
                ring.sprite = Disc();
                ring.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * RadarSize * r);
            }
            Ui.Box(radarBack.transform, new Color(1, 1, 1, 0.07f), "AxisH").rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(RadarSize, 1));
            Ui.Box(radarBack.transform, new Color(1, 1, 1, 0.07f), "AxisV").rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1, RadarSize));
            radarDots = Ui.Rect(radarBack.transform, "Dots").Fill();
            var cone = Ui.Box(radarBack.transform, new Color(Palette.Amber.r, Palette.Amber.g, Palette.Amber.b, 0.12f), "View");
            cone.sprite = Wedge();
            cone.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(RadarSize * 0.62f, RadarSize * 0.5f), new Vector2(0.5f, 0));
            var me = Ui.Box(radar, Palette.Amber, "Me");
            me.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(9, 9));
            me.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            var north = Mono(radar, "雷達 · 28m", 10, Palette.Muted);
            north.alignment = TextAnchor.UpperCenter;
            north.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, -4), new Vector2(160, 14), new Vector2(0.5f, 1));
            killFeed = Ui.Rect(root, "KillFeed").Place(new Vector2(1, 1), new Vector2(-36, -RadarSize - 54), new Vector2(300, 200), new Vector2(1, 1));

            comboCaption = Mono(root, "連殺", 13, Palette.Hex("c9c5bb"));
            comboCaption.alignment = TextAnchor.UpperRight;
            comboCaption.rectTransform.Place(new Vector2(1, 0.5f), new Vector2(-40, 40), new Vector2(200, 16), new Vector2(1, 1));
            combo = Mono(root, "", 44, Palette.Amber);
            combo.alignment = TextAnchor.UpperRight;
            combo.rectTransform.Place(new Vector2(1, 0.5f), new Vector2(-40, 22), new Vector2(200, 52), new Vector2(1, 1));

            // ---- crosshair, hit markers, damage arcs
            foreach (var (pos, size) in new[] { (new Vector2(-11, 0), new Vector2(8, 2)), (new Vector2(11, 0), new Vector2(8, 2)), (new Vector2(0, 11), new Vector2(2, 8)), (new Vector2(0, -11), new Vector2(2, 8)), (Vector2.zero, new Vector2(2, 2)) })
                Ui.Box(root, Palette.Bone, "Cross").rectTransform.Place(new Vector2(0.5f, 0.5f), pos, size);
            hitRoot = Ui.Rect(root, "Hit").Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60, 60));
            for (int i = 0; i < 4; i++)
            {
                float a = 45f + 90f * i;
                var dir = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                hitTicks[i] = Ui.Box(hitRoot, Palette.Bone, "Tick");
                hitTicks[i].rectTransform.Place(new Vector2(0.5f, 0.5f), dir * 14f, new Vector2(10, 2.5f));
                hitTicks[i].rectTransform.localRotation = Quaternion.Euler(0, 0, a);
            }
            headTag = Mono(root, "", 13, Palette.Amber);
            headTag.alignment = TextAnchor.UpperCenter;
            headTag.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, -26), new Vector2(200, 20), new Vector2(0.5f, 1));
            lowAmmo = Mono(root, "", 13, Palette.Amber);
            lowAmmo.alignment = TextAnchor.UpperCenter;
            lowAmmo.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(300, 20), new Vector2(0.5f, 1));
            damageLayer = Ui.Rect(root, "Damage").Place(new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            // ---- bottom-left vitals panel
            var vitals = Panel(root, new Vector2(0, 0), new Vector2(28, 24), new Vector2(392, 132), new Vector2(0, 0), Palette.Bone);
            Ui.Label(vitals, "護盾", 12, Palette.Hex("c9c5bb")).Outlined().rectTransform.Place(new Vector2(0, 1), new Vector2(18, -14), new Vector2(60, 16), new Vector2(0, 1));
            shieldValue = Mono(vitals, "100", 38, Palette.Bone);
            shieldValue.rectTransform.Place(new Vector2(0, 1), new Vector2(16, -28), new Vector2(110, 44), new Vector2(0, 1));
            shieldMax = Mono(vitals, "/ 100", 14, Palette.Hex("c9c5bb"));
            shieldMax.rectTransform.Place(new Vector2(0, 1), new Vector2(104, -48), new Vector2(90, 18), new Vector2(0, 1));
            shieldBar = new Ui.Bar(vitals, new Color(1, 1, 1, 0.12f), Palette.Bone);
            shieldBar.Root.Place(new Vector2(0, 1), new Vector2(18, -78), new Vector2(356, 7), new Vector2(0, 1));
            for (int i = 1; i < 10; i++)
                Ui.Box(vitals, new Color(0.04f, 0.045f, 0.05f, 0.85f), "Seg").rectTransform.Place(new Vector2(0, 1), new Vector2(18 + 356f * i / 10f, -78), new Vector2(2, 7), new Vector2(0.5f, 1));
            skillLabel = Mono(vitals, "Q  手榴彈", 12, Palette.Bone);
            skillLabel.rectTransform.Place(new Vector2(0, 0), new Vector2(18, 26), new Vector2(110, 16), new Vector2(0, 0));
            skillTime = Mono(vitals, "", 12, Palette.Hex("c9c5bb"));
            skillTime.alignment = TextAnchor.LowerRight;
            skillTime.rectTransform.Place(new Vector2(0, 0), new Vector2(186, 26), new Vector2(60, 16), new Vector2(1, 0));
            skillBar = new Ui.Bar(vitals, new Color(1, 1, 1, 0.15f), Palette.Amber);
            skillBar.Root.Place(new Vector2(0, 0), new Vector2(18, 16), new Vector2(168, 3), new Vector2(0, 0));
            dashLabel = Mono(vitals, "Shift  衝刺", 12, Palette.Bone);
            dashLabel.rectTransform.Place(new Vector2(0, 0), new Vector2(206, 26), new Vector2(110, 16), new Vector2(0, 0));
            dashTime = Mono(vitals, "", 12, Palette.Hex("c9c5bb"));
            dashTime.alignment = TextAnchor.LowerRight;
            dashTime.rectTransform.Place(new Vector2(0, 0), new Vector2(374, 26), new Vector2(60, 16), new Vector2(1, 0));
            dashBar = new Ui.Bar(vitals, new Color(1, 1, 1, 0.15f), Palette.Hex("82aaff"));
            dashBar.Root.Place(new Vector2(0, 0), new Vector2(206, 16), new Vector2(168, 3), new Vector2(0, 0));

            // ---- bottom-right weapon panel
            var weapon = Panel(root, new Vector2(1, 0), new Vector2(-28, 24), new Vector2(330, 132), new Vector2(1, 0), Palette.Amber);
            for (int i = 0; i < 2; i++)
            {
                slotChips[i] = Ui.Box(weapon, new Color(1, 1, 1, 0.06f), "Slot");
                slotChips[i].rectTransform.Place(new Vector2(0, 1), new Vector2(18 + i * 150, -12), new Vector2(144, 22), new Vector2(0, 1));
                slotLabels[i] = Mono(slotChips[i].transform, "", 12, Palette.Bone);
                slotLabels[i].alignment = TextAnchor.MiddleLeft;
                slotLabels[i].rectTransform.Fill().offsetMin = new Vector2(8, 0);
            }
            weaponName = Ui.Label(weapon, "", 14, Palette.Bone, TextAnchor.UpperLeft, true).Outlined();
            weaponName.rectTransform.Place(new Vector2(0, 1), new Vector2(18, -42), new Vector2(200, 20), new Vector2(0, 1));
            weaponRarity = Mono(weapon, "", 11, Palette.Muted);
            weaponRarity.rectTransform.Place(new Vector2(0, 1), new Vector2(18, -62), new Vector2(200, 16), new Vector2(0, 1));
            ammo = Mono(weapon, "12", 56, Palette.Bone);
            ammo.alignment = TextAnchor.LowerRight;
            ammo.rectTransform.Place(new Vector2(1, 0), new Vector2(-74, 14), new Vector2(160, 70), new Vector2(1, 0));
            ammoMax = Mono(weapon, "/ 12", 18, Palette.Hex("c9c5bb"));
            ammoMax.alignment = TextAnchor.LowerLeft;
            ammoMax.rectTransform.Place(new Vector2(1, 0), new Vector2(-70, 22), new Vector2(60, 24), new Vector2(0, 0));
            reloadBar = new Ui.Bar(weapon, new Color(1, 1, 1, 0.15f), Palette.Bone);
            reloadBar.Root.Place(new Vector2(0, 0), new Vector2(18, 14), new Vector2(294, 3), new Vector2(0, 0));
            hint = Mono(root, "", 11, Palette.Hex("a9a59c"));
            hint.alignment = TextAnchor.LowerRight;
            hint.rectTransform.Place(new Vector2(1, 0), new Vector2(-30, 160), new Vector2(400, 16), new Vector2(1, 0));

            revive = Ui.Label(root, "", 20, Palette.Hex("7dff9a"), TextAnchor.MiddleCenter).Outlined();
            revive.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 230), new Vector2(720, 30));
            toastPill = Ui.Box(root, new Color(0.04f, 0.045f, 0.05f, 0.75f), "Toast").rectTransform;
            toastPill.Place(new Vector2(0.5f, 0), new Vector2(0, 176), new Vector2(360, 34));
            Ui.Box(toastPill, Palette.Amber, "Mark").rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(3, 34), new Vector2(0, 0.5f));
            toast = Ui.Label(toastPill, "", 15, Palette.Amber, TextAnchor.MiddleCenter);
            toast.rectTransform.Fill();
            floaterLayer = Ui.Rect(root, "Floaters").Fill();
        }

        static RectTransform Panel(Transform root, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot, Color accent)
        {
            var back = Ui.Box(root, new Color(0.04f, 0.045f, 0.05f, 0.55f), "Panel");
            back.rectTransform.Place(anchor, pos, size, pivot);
            back.raycastTarget = false;
            var edge = Ui.Box(back.transform, accent, "Edge").rectTransform;
            edge.anchorMin = new Vector2(0, 1); edge.anchorMax = new Vector2(1, 1); edge.pivot = new Vector2(0.5f, 1);
            edge.offsetMin = new Vector2(0, -2); edge.offsetMax = new Vector2(-size.x * 0.6f, 0);
            return back.rectTransform;
        }

        static Text Mono(Transform parent, string text, int size, Color color) => Ui.Label(parent, text, size, color).Outlined();

        static Sprite RadialSprite()
        {
            const int n = 128;
            var tex = new Texture2D(n, n) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x - n / 2f, y - n / 2f).magnitude / (n / 2f);
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((d - 0.6f) / 0.6f) * 0.85f));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }

        /// Field-of-view wedge for the radar: opaque at the apex (bottom centre), fading toward the rim.
        static Sprite Wedge()
        {
            const int w = 128, h = 104;
            var tex = new Texture2D(w, h) { wrapMode = TextureWrapMode.Clamp };
            float slope = (w / 2f) / h;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float half = y * slope, dx = Mathf.Abs(x - w / 2f + 0.5f);
                    float inside = Mathf.Clamp01(half - dx + 0.5f);
                    tex.SetPixel(x, y, new Color(1, 1, 1, inside * (1f - y / (float)h * 0.7f)));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f));
        }

        /// Anti-aliased filled circle used for the radar and its dots.
        static Sprite Disc()
        {
            if (disc != null) return disc;
            const int n = 128;
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

        public void Announce(string text) { banner.text = text; bannerTimer = 2.2f; }

        public void ResetTransient()
        {
            banner.text = ""; bannerTimer = 0; comboCount = 0; comboTimer = 0; damageFlash = 0; lastHp = -1; localKills = 0;
            foreach (var f in floaters) Object.Destroy(f.text.gameObject);
            floaters.Clear();
            foreach (var f in feed) Object.Destroy(f.row.gameObject);
            feed.Clear();
            foreach (var d in damageArcs) Object.Destroy(d.arc.gameObject);
            damageArcs.Clear();
            lastEnemies.Clear();
        }

        public void OnLocalShot(ShotEvent shot, Camera camera)
        {
            if (shot.Hit) hitTimer = 0.2f;
            if (shot.Head) headTimer = 0.4f;
            if (shot.Killed) { comboCount = comboTimer > 0 ? comboCount + 1 : 1; comboTimer = 2.5f; killTimer = 0.3f; localKills++; }
            if (shot.Dealt > 0)
            {
                var t = Mono(floaterLayer, Mathf.RoundToInt(shot.Dealt).ToString(), shot.Head ? 24 : shot.Killed ? 19 : 15, shot.Head ? Palette.Amber : shot.Killed ? Palette.Danger : Palette.Bone);
                t.alignment = TextAnchor.MiddleCenter;
                t.fontStyle = shot.Head || shot.Killed ? FontStyle.Bold : FontStyle.Normal;
                t.rectTransform.sizeDelta = new Vector2(80, 30);
                floaters.Add((t, shot.End + new Vector3(Random.Range(-0.25f, 0.25f), 0.3f, 0), 0.8f));
            }
        }

        void AddFeed(string text, Color color)
        {
            var row = Ui.Box(killFeed, new Color(0.04f, 0.045f, 0.05f, 0.6f), "Kill").rectTransform;
            Ui.Box(row, color, "Mark").rectTransform.Place(new Vector2(1, 0.5f), Vector2.zero, new Vector2(3, 22), new Vector2(1, 0.5f));
            var label = Ui.Label(row, text, 13, Palette.Bone, TextAnchor.MiddleRight);
            label.rectTransform.Fill().offsetMax = new Vector2(-10, 0);
            feed.Insert(0, (row, label, 4f));
            while (feed.Count > 6) { Object.Destroy(feed[feed.Count - 1].row.gameObject); feed.RemoveAt(feed.Count - 1); }
        }

        void AddDamageArc(float angle)
        {
            var arc = Ui.Box(damageLayer, Palette.Danger, "DamageArc");
            arc.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            arc.rectTransform.sizeDelta = new Vector2(70, 6);
            arc.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * 120f;
            arc.rectTransform.localRotation = Quaternion.Euler(0, 0, -angle * Mathf.Rad2Deg);
            damageArcs.Add((arc, 1f));
        }

        // Text is only re-formatted when the value behind it changes: formatting every label every frame
        // produced a steady stream of garbage (and GC hitches) for numbers that rarely change.
        readonly Dictionary<Text, long> textKeys = new Dictionary<Text, long>();
        readonly List<int> gone = new List<int>();
        bool Changed(Text t, long key)
        {
            if (textKeys.TryGetValue(t, out var last) && last == key) return false;
            textKeys[t] = key;
            return true;
        }

        public void Tick(float dt, GameSim sim, Actor local, Camera camera, bool thirdPerson, string toastText)
        {
            bannerTimer = Mathf.Max(0, bannerTimer - dt);
            hitTimer = Mathf.Max(0, hitTimer - dt);
            headTimer = Mathf.Max(0, headTimer - dt);
            killTimer = Mathf.Max(0, killTimer - dt);
            comboTimer = Mathf.Max(0, comboTimer - dt);
            damageFlash = Mathf.Max(0, damageFlash - dt);
            if (comboTimer <= 0) comboCount = 0;
            banner.color = new Color(1, 1, 1, Mathf.Clamp01(bannerTimer / 0.4f)) * Palette.Bone;

            // Hit marker: ticks pop out and fade; gold for headshots, larger red for kills.
            float hit = Mathf.Max(hitTimer / 0.2f, killTimer / 0.3f);
            Color tickColor = killTimer > 0 ? Palette.Danger : headTimer > 0 ? Palette.Amber : Palette.Bone;
            for (int i = 0; i < 4; i++)
            {
                hitTicks[i].color = new Color(tickColor.r, tickColor.g, tickColor.b, Mathf.Clamp01(hit));
                hitTicks[i].rectTransform.sizeDelta = new Vector2(killTimer > 0 ? 14 : 10, killTimer > 0 ? 3.5f : 2.5f);
            }
            hitRoot.localScale = Vector3.one * (1f + 0.35f * (1f - Mathf.Clamp01(hit)) * (hit > 0 ? 1 : 0) + (killTimer > 0 ? 0.25f : 0));
            headTag.text = headTimer > 0 ? "爆頭" : "";
            if (Changed(combo, comboCount)) combo.text = comboCount >= 2 ? $"×{comboCount}" : "";
            comboCaption.enabled = comboCount >= 2;

            // Prompt capsule fades in and out instead of popping.
            if (!string.IsNullOrEmpty(toastText)) toast.text = toastText;
            toastAlpha = Mathf.MoveTowards(toastAlpha, string.IsNullOrEmpty(toastText) ? 0f : 1f, dt * 5f);
            toastPill.gameObject.SetActive(toastAlpha > 0.01f);
            toastPill.localScale = Vector3.one * (0.92f + 0.08f * toastAlpha);
            toastPill.sizeDelta = new Vector2(Mathf.Max(240, toast.preferredWidth + 48), 34);

            region.text = Defs.Regions[sim.Region].Name;
            if (Changed(wave, sim.Wave)) wave.text = $"第 {sim.Wave} 波  / {Defs.WavesPerMission}";
            if (Changed(stats, ((long)sim.Enemies.Count << 42) | ((long)sim.Kills << 21) | (long)(int)sim.Elapsed))
                stats.text = $"剩餘 {sim.Enemies.Count:00}    擊破 {sim.Kills:00}    {(int)sim.Elapsed / 60:00}:{(int)sim.Elapsed % 60:00}";
            if (Changed(level, sim.Level)) level.text = $"LV {sim.Level:00}";
            if (Changed(xpText, ((long)sim.Xp << 42) | ((long)sim.XpNeed << 21) | (long)Mathf.RoundToInt(sim.LevelScale * 100)))
                xpText.text = $"{sim.Xp} / {sim.XpNeed} XP · 敵人 ×{sim.LevelScale:0.00}";
            xpBar.Value = sim.XpNeed > 0 ? (float)sim.Xp / sim.XpNeed : 0;

            Enemy boss = null;
            foreach (var e in sim.Enemies.Values) if (e.Boss) { boss = e; break; }
            bossRoot.gameObject.SetActive(boss != null && !sim.BuffPending);
            if (boss != null)
            {
                if (Changed(bossName, ((long)boss.Kind << 50) | ((long)Mathf.CeilToInt(boss.Hp) << 25) | (long)Mathf.CeilToInt(boss.MaxHp)))
                    bossName.text = $"{Defs.Kinds[boss.Kind].Name}   {Mathf.CeilToInt(boss.Hp)} / {Mathf.CeilToInt(boss.MaxHp)}";
                bossBar.Value = boss.Hp / Mathf.Max(1, boss.MaxHp);
                bossEnrage.gameObject.SetActive(boss.State == 2);
                if (boss.State == 2) bossEnrage.color = Color.Lerp(Palette.Danger, Palette.Amber, 0.5f + 0.5f * Mathf.Sin(Time.time * 8f));
            }

            // Kill feed from enemies that disappeared since last frame (labelled "你" for your own kills).
            seen.Clear();
            foreach (var e in sim.Enemies.Values) { seen.Add(e.Id); lastEnemies[e.Id] = e.Elite ? 100 + e.Kind : e.Kind; }
            gone.Clear();
            foreach (var kv in lastEnemies) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone)
            {
                int code = lastEnemies[id];
                lastEnemies.Remove(id);
                if (sim.State != MissionState.Playing) continue;
                bool elite = code >= 100;
                string name = Defs.Kinds[Mathf.Clamp(code % 100, 0, Defs.Kinds.Length - 1)].Name;
                bool mine = localKills > 0 || sim.Actors.Count <= 1;
                if (mine) localKills--;
                AddFeed($"{(mine ? "你" : "隊伍")}  擊破  {(elite ? "精英 " : "")}{name}", elite ? Palette.Hex("ffcf4a") : mine ? Palette.Amber : Palette.Teammate);
            }
            for (int i = feed.Count - 1; i >= 0; i--)
            {
                var (row, text, life) = feed[i];
                life -= dt;
                if (life <= 0) { Object.Destroy(row.gameObject); feed.RemoveAt(i); continue; }
                row.Place(new Vector2(1, 1), new Vector2(0, -i * 26), new Vector2(Mathf.Max(160, text.preferredWidth + 28), 22), new Vector2(1, 1));
                float a = Mathf.Clamp01(life / 0.6f);
                text.color = new Color(1, 1, 1, a) * Palette.Bone;
                row.GetComponent<Image>().color = new Color(0.04f, 0.045f, 0.05f, 0.6f * a);
                feed[i] = (row, text, life);
            }

            if (local != null)
            {
                Vector3 fwd = camera.transform.forward; fwd.y = 0; fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
                Vector3 right = new Vector3(fwd.z, 0, -fwd.x);
                if (lastHp >= 0 && local.Hp < lastHp - 0.01f)
                {
                    damageFlash = 0.45f;
                    // Point the arc at the nearest enemy, the most likely source of the hit.
                    Enemy source = null; float best = float.MaxValue;
                    foreach (var e in sim.Enemies.Values) { float d = (e.Position - local.Position).sqrMagnitude; if (d < best) { best = d; source = e; } }
                    if (source != null)
                    {
                        var w = source.Position - local.Position;
                        AddDamageArc(Mathf.Atan2(Vector3.Dot(w, right), Vector3.Dot(w, fwd)));
                    }
                }
                lastHp = local.Hp;
                for (int i = damageArcs.Count - 1; i >= 0; i--)
                {
                    var (arc, life) = damageArcs[i];
                    life -= dt;
                    if (life <= 0) { Object.Destroy(arc.gameObject); damageArcs.RemoveAt(i); continue; }
                    arc.color = new Color(Palette.Danger.r, Palette.Danger.g, Palette.Danger.b, life);
                    damageArcs[i] = (arc, life);
                }

                bool low = local.Alive && local.Hp <= local.MaxHp * 0.3f;
                float alpha = low ? 0.45f + 0.25f * Mathf.Sin(Time.time * 6) : Mathf.Clamp01(damageFlash * 1.8f);
                vignette.color = new Color(0.9f, 0.05f, 0.1f, alpha);
                if (Changed(shieldValue, Mathf.CeilToInt(local.Hp))) shieldValue.text = Mathf.CeilToInt(local.Hp).ToString();
                shieldValue.color = low ? Palette.Danger : Palette.Bone;
                if (Changed(shieldMax, Mathf.RoundToInt(local.MaxHp))) shieldMax.text = $"/ {Mathf.RoundToInt(local.MaxHp)}";
                shieldBar.Value = local.Hp / local.MaxHp;
                shieldBar.Color = low ? Palette.Danger : Palette.Bone;
                float maxCd = Defs.SkillCooldown * local.SkillMult;
                skillBar.Value = 1f - Mathf.Clamp01(local.SkillCooldown / Mathf.Max(0.1f, maxCd));
                if (Changed(skillTime, local.SkillCooldown <= 0 ? -1 : Mathf.RoundToInt(local.SkillCooldown * 10))) skillTime.text = local.SkillCooldown <= 0 ? "就緒" : $"{local.SkillCooldown:0.0}s";
                skillTime.color = local.SkillCooldown <= 0 ? Palette.Amber : Palette.Hex("c9c5bb");
                dashBar.Value = 1f - Mathf.Clamp01(local.DashCooldown / Defs.DashCooldown);
                if (Changed(dashTime, local.DashCooldown <= 0 ? -1 : Mathf.RoundToInt(local.DashCooldown * 10))) dashTime.text = local.DashCooldown <= 0 ? "就緒" : $"{local.DashCooldown:0.0}s";
                dashTime.color = local.DashCooldown <= 0 ? Palette.Hex("82aaff") : Palette.Hex("c9c5bb");

                var kit = local.Loadout.Equipped;
                for (int i = 0; i < 2; i++)
                {
                    bool has = i < kit.Count;
                    slotChips[i].enabled = slotLabels[i].enabled = has;
                    if (!has) continue;
                    bool current = i == local.Slot;
                    int r = local.Loadout.RarityOf(kit[i]);
                    if (Changed(slotLabels[i], kit[i].GetHashCode())) slotLabels[i].text = $"{i + 1}   {Defs.Weapons[kit[i]].Short}";
                    slotLabels[i].color = current ? Gear.RarityColors[r] : Palette.Hex("8c8f93");
                    slotChips[i].color = current ? new Color(1, 1, 1, 0.12f) : new Color(1, 1, 1, 0.04f);
                }
                int rarity = local.Loadout.RarityOf(local.WeaponId);
                weaponName.text = Defs.Weapons[local.WeaponId].Name;
                weaponName.color = Gear.RarityColors[rarity];
                if (Changed(weaponRarity, ((long)rarity << 32) | (long)Mathf.RoundToInt(local.Damage))) weaponRarity.text = $"{Gear.RarityNames[rarity]}  ·  傷害 {local.Damage:0}";
                bool reloading = local.Reload > 0;
                bool lowMag = !reloading && local.Ammo <= Mathf.Max(1, local.Mag / 5);
                if (Changed(ammo, reloading ? -1 : local.Ammo)) ammo.text = reloading ? "換彈" : local.Ammo.ToString();
                ammo.fontSize = reloading ? 30 : 56;
                ammo.color = reloading || lowMag ? Palette.Amber : Palette.Bone;
                if (Changed(ammoMax, local.Mag)) ammoMax.text = $"/ {local.Mag}";
                reloadBar.Value = reloading ? 1f - local.Reload / local.ReloadTime : (float)local.Ammo / Mathf.Max(1, local.Mag);
                reloadBar.Color = reloading ? Palette.Amber : Palette.Bone;
                lowAmmo.text = lowMag && local.Alive ? (local.Ammo == 0 ? "彈匣已空 · 按 R 換彈" : "彈藥不足 · 按 R 換彈") : "";
                lowAmmo.color = new Color(Palette.Amber.r, Palette.Amber.g, Palette.Amber.b, 0.6f + 0.4f * Mathf.Sin(Time.time * 8f));
                if (Changed(hint, thirdPerson ? 1 : 0)) hint.text = $"R 換彈  ·  右鍵 瞄準  ·  V {(thirdPerson ? "第三人稱" : "第一人稱")}  ·  Esc 選單";
                revive.text = "";
                if (!local.Alive && sim.Actors.Count > 1)
                    revive.text = $"你已倒地 · 等待隊友按住 E 救援  {Mathf.RoundToInt(100 * local.Revive / Defs.ReviveTime)}%";
                else
                    foreach (var other in sim.Actors.Values)
                        if (other.Id != local.Id && !other.Alive && Vector3.Distance(other.Position, local.Position) < 2.4f)
                            revive.text = $"按住 E 救援隊友  {Mathf.RoundToInt(100 * other.Revive / Defs.ReviveTime)}%";

                UpdateRadar(sim, local, fwd, right);
            }

            var canvasRect = (RectTransform)Canvas.transform;
            for (int i = floaters.Count - 1; i >= 0; i--)
            {
                var (text, world, life) = floaters[i];
                life -= dt;
                world += Vector3.up * dt * 1.4f;
                if (life <= 0) { Object.Destroy(text.gameObject); floaters.RemoveAt(i); continue; }
                Vector3 screen = camera.WorldToScreenPoint(world);
                text.enabled = screen.z > 0;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local2);
                text.rectTransform.anchoredPosition = local2 + canvasRect.sizeDelta / 2f;
                text.rectTransform.anchorMin = text.rectTransform.anchorMax = Vector2.zero;
                float pop = life > 0.65f ? 1f + (life - 0.65f) * 3f : 1f;
                text.rectTransform.localScale = Vector3.one * pop;
                text.color = new Color(text.color.r, text.color.g, text.color.b, Mathf.Clamp01(life / 0.4f));
                floaters[i] = (text, world, life);
            }
        }

        /// Radar: view-relative positions of enemies (red, gold elites, large boss), teammates and pickups.
        void UpdateRadar(GameSim sim, Actor local, Vector3 fwd, Vector3 right)
        {
            int used = 0;
            float scale = (RadarSize * 0.5f - 6f) / RadarRange;
            void Dot(Vector3 world, Color color, float size, bool clampToEdge)
            {
                var w = world - local.Position;
                var p = new Vector2(Vector3.Dot(w, right), Vector3.Dot(w, fwd)) * scale;
                float max = RadarSize * 0.5f - 6f;
                if (p.magnitude > max) { if (!clampToEdge) return; p = p.normalized * max; }
                if (used >= dots.Count)
                {
                    var img = Ui.Box(radarDots, Color.white, "Dot");
                    img.sprite = Disc();
                    img.raycastTarget = false;
                    dots.Add(img);
                }
                var d = dots[used++];
                d.enabled = true;
                d.color = color;
                d.rectTransform.Place(new Vector2(0.5f, 0.5f), p, Vector2.one * size);
            }
            foreach (var p in sim.Pickups.Values)
                Dot(p.Position, p.Kind == PickupKind.Chest ? Palette.Hex("ffcf4a") : p.Kind == PickupKind.Health ? Palette.Hex("5dff8f") : p.Kind == PickupKind.Xp ? new Color(0.37f, 0.9f, 1f, 0.6f) : Palette.Hex("ffd166"), p.Kind == PickupKind.Xp ? 3f : 6f, false);
            foreach (var e in sim.Enemies.Values)
                Dot(e.Position, e.Boss ? Palette.Danger : e.Elite ? Palette.Hex("ffcf4a") : Palette.Hex("ff6a5c"), e.Boss ? 13f : e.Elite ? 9f : 6f, e.Boss || e.Elite);
            foreach (var a in sim.Actors.Values)
                if (a.Id != local.Id) Dot(a.Position, a.Alive ? Palette.Teammate : Palette.Danger, 8f, true);
            for (int i = used; i < dots.Count; i++) dots[i].enabled = false;
        }

        public void SetBuffCount(int count) { }
        public void Show(bool visible) => Canvas.gameObject.SetActive(visible);
    }
}
