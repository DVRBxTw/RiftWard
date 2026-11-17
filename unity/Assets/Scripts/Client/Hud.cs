using System.Collections.Generic;
using RiftWard.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace RiftWard.Client
{
    /// Frameless combat HUD from the design board: text over the scene with soft shading.
    public sealed class Hud
    {
        public readonly Canvas Canvas;
        readonly Text region, wave, stats, level, xpText, banner, combo, comboCaption, hitMarker, skillLabel, skillTime;
        readonly Text shieldValue, shieldMax, buffCount, dashLabel, weaponName, ammo, ammoMax, hint, revive, toast, bossName;
        readonly Ui.Bar xpBar, shieldBar, skillBar, reloadBar, bossBar;
        readonly RectTransform bossRoot;
        readonly Text[] slotLabels = new Text[2];
        readonly Image[] slotLines = new Image[2];
        readonly Image vignette;
        readonly RectTransform floaterLayer;
        readonly List<(Text text, Vector3 world, float life)> floaters = new List<(Text, Vector3, float)>();
        float bannerTimer, hitTimer, headTimer, damageFlash, comboTimer;
        int comboCount;
        float lastHp = -1;

        public Hud()
        {
            Canvas = Ui.Canvas("HUD", 10);
            var root = Canvas.transform;
            vignette = Ui.Box(root, Color.white, "Vignette");
            vignette.sprite = RadialSprite();
            vignette.rectTransform.Fill();
            vignette.raycastTarget = false;
            vignette.color = new Color(0.9f, 0.05f, 0.1f, 0);
            Ui.Shade(root, true, 0.45f).rectTransform.Place(new Vector2(0.5f, 1), Vector2.zero, new Vector2(1280, 170));
            Ui.Shade(root, false, 0.55f).rectTransform.Place(new Vector2(0.5f, 0), Vector2.zero, new Vector2(1280, 220));

            region = Mono(root, "", 13, Palette.Amber);
            region.rectTransform.Place(new Vector2(0, 1), new Vector2(40, -32), new Vector2(400, 18));
            wave = Ui.Label(root, "", 30, Palette.Bone, TextAnchor.UpperLeft, true).Outlined();
            wave.rectTransform.Place(new Vector2(0, 1), new Vector2(40, -54), new Vector2(400, 40));
            stats = Mono(root, "", 13, Palette.Hex("d6d1c6"));
            stats.rectTransform.Place(new Vector2(0, 1), new Vector2(40, -98), new Vector2(400, 20));

            level = Mono(root, "LV 1", 15, Palette.Amber);
            level.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(-200, -34), new Vector2(120, 20), new Vector2(0, 1));
            xpText = Mono(root, "", 13, Palette.Hex("c9c5bb"));
            xpText.alignment = TextAnchor.UpperRight;
            xpText.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(200, -36), new Vector2(300, 20), new Vector2(1, 1));
            xpBar = new Ui.Bar(root, new Color(1, 1, 1, 0.18f), Palette.Amber);
            xpBar.Root.Place(new Vector2(0.5f, 1), new Vector2(0, -62), new Vector2(400, 3));

            bossRoot = Ui.Rect(root, "Boss").Place(new Vector2(0.5f, 1), new Vector2(0, -84), new Vector2(560, 40));
            bossName = Ui.Label(bossRoot, "", 15, Palette.Bone, TextAnchor.UpperCenter).Outlined();
            bossName.rectTransform.Place(new Vector2(0.5f, 1), Vector2.zero, new Vector2(560, 20));
            bossBar = new Ui.Bar(bossRoot, new Color(1, 1, 1, 0.12f), Palette.Danger);
            bossBar.Root.Place(new Vector2(0.5f, 1), new Vector2(0, -26), new Vector2(560, 6));

            banner = Ui.Label(root, "", 40, Palette.Bone, TextAnchor.MiddleCenter, true).Outlined();
            banner.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -230), new Vector2(900, 56));

            foreach (var (pos, size) in new[] { (new Vector2(-11, 0), new Vector2(8, 2)), (new Vector2(11, 0), new Vector2(8, 2)), (new Vector2(0, 11), new Vector2(2, 8)), (new Vector2(0, -11), new Vector2(2, 8)), (Vector2.zero, new Vector2(2, 2)) })
                Ui.Box(root, Palette.Bone, "Cross").rectTransform.Place(new Vector2(0.5f, 0.5f), pos, size);
            hitMarker = Mono(root, "", 13, Palette.Bone);
            hitMarker.alignment = TextAnchor.UpperCenter;
            hitMarker.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, -22), new Vector2(200, 20), new Vector2(0.5f, 1));

            comboCaption = Mono(root, "連殺", 13, Palette.Hex("c9c5bb"));
            comboCaption.alignment = TextAnchor.UpperRight;
            comboCaption.rectTransform.Place(new Vector2(1, 0.5f), new Vector2(-40, 110), new Vector2(200, 16), new Vector2(1, 1));
            combo = Mono(root, "", 44, Palette.Amber);
            combo.alignment = TextAnchor.UpperRight;
            combo.rectTransform.Place(new Vector2(1, 0.5f), new Vector2(-40, 92), new Vector2(200, 52), new Vector2(1, 1));

            // Bottom-left vitals
            skillLabel = Ui.Label(root, "[Q] 手榴彈", 13, Palette.Bone).Outlined();
            skillLabel.rectTransform.Place(new Vector2(0, 0), new Vector2(40, 132), new Vector2(110, 18), new Vector2(0, 0));
            skillBar = new Ui.Bar(root, new Color(1, 1, 1, 0.2f), Palette.Bone);
            skillBar.Root.Place(new Vector2(0, 0), new Vector2(150, 140), new Vector2(190, 2), new Vector2(0, 0));
            skillTime = Mono(root, "", 13, Palette.Hex("c9c5bb"));
            skillTime.alignment = TextAnchor.LowerRight;
            skillTime.rectTransform.Place(new Vector2(0, 0), new Vector2(400, 132), new Vector2(54, 18), new Vector2(1, 0));
            Ui.Label(root, "護盾", 13, Palette.Hex("c9c5bb")).Outlined().rectTransform.Place(new Vector2(0, 0), new Vector2(40, 76), new Vector2(40, 18), new Vector2(0, 0));
            shieldValue = Mono(root, "100", 40, Palette.Bone);
            shieldValue.rectTransform.Place(new Vector2(0, 0), new Vector2(82, 68), new Vector2(110, 46), new Vector2(0, 0));
            shieldMax = Mono(root, "/ 100", 14, Palette.Hex("c9c5bb"));
            shieldMax.rectTransform.Place(new Vector2(0, 0), new Vector2(170, 74), new Vector2(90, 18), new Vector2(0, 0));
            buffCount = Ui.Label(root, "", 13, Palette.Hex("c9c5bb"), TextAnchor.LowerRight).Outlined();
            buffCount.rectTransform.Place(new Vector2(0, 0), new Vector2(400, 76), new Vector2(120, 18), new Vector2(1, 0));
            shieldBar = new Ui.Bar(root, new Color(1, 1, 1, 0.15f), Palette.Bone);
            shieldBar.Root.Place(new Vector2(0, 0), new Vector2(40, 58), new Vector2(360, 6), new Vector2(0, 0));
            dashLabel = Ui.Label(root, "", 13, Palette.Hex("c9c5bb")).Outlined();
            dashLabel.rectTransform.Place(new Vector2(0, 0), new Vector2(40, 30), new Vector2(360, 18), new Vector2(0, 0));

            // Bottom-right weapon
            for (int i = 0; i < 2; i++)
            {
                float x = -40 - (1 - i) * 170;
                slotLabels[i] = Ui.Label(root, "", 13, Palette.Bone, i == 0 ? TextAnchor.LowerLeft : TextAnchor.LowerRight).Outlined();
                slotLabels[i].rectTransform.Place(new Vector2(1, 0), new Vector2(x, 186), new Vector2(170, 18), new Vector2(1, 0));
                slotLines[i] = Ui.Box(root, Palette.Amber, "SlotLine");
                slotLines[i].rectTransform.Place(new Vector2(1, 0), new Vector2(x, 180), new Vector2(170, 2), new Vector2(1, 0));
            }
            weaponName = Ui.Label(root, "", 14, Palette.Bone, TextAnchor.LowerRight).Outlined();
            weaponName.rectTransform.Place(new Vector2(1, 0), new Vector2(-40, 152), new Vector2(340, 20), new Vector2(1, 0));
            ammo = Mono(root, "12", 64, Palette.Bone);
            ammo.alignment = TextAnchor.LowerRight;
            ammo.rectTransform.Place(new Vector2(1, 0), new Vector2(-104, 68), new Vector2(200, 80), new Vector2(1, 0));
            ammoMax = Mono(root, "/ 12", 20, Palette.Hex("c9c5bb"));
            ammoMax.alignment = TextAnchor.LowerLeft;
            ammoMax.rectTransform.Place(new Vector2(1, 0), new Vector2(-100, 76), new Vector2(64, 26), new Vector2(0, 0));
            reloadBar = new Ui.Bar(root, new Color(1, 1, 1, 0.2f), Palette.Bone);
            reloadBar.Root.Place(new Vector2(1, 0), new Vector2(-40, 58), new Vector2(200, 3), new Vector2(1, 0));
            hint = Mono(root, "R 換彈 · 右鍵 瞄準 · V 第一人稱", 13, Palette.Hex("c9c5bb"));
            hint.alignment = TextAnchor.LowerRight;
            hint.rectTransform.Place(new Vector2(1, 0), new Vector2(-40, 30), new Vector2(400, 18), new Vector2(1, 0));

            revive = Ui.Label(root, "", 20, Palette.Hex("7dff9a"), TextAnchor.MiddleCenter).Outlined();
            revive.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 250), new Vector2(720, 30));
            toast = Ui.Label(root, "", 20, Palette.Danger, TextAnchor.MiddleCenter).Outlined();
            toast.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, -80), new Vector2(720, 30));
            floaterLayer = Ui.Rect(root, "Floaters").Fill();
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

        public void Announce(string text) { banner.text = text; bannerTimer = 2.2f; }

        public void ResetTransient()
        {
            banner.text = ""; bannerTimer = 0; comboCount = 0; comboTimer = 0; damageFlash = 0; lastHp = -1;
            foreach (var f in floaters) Object.Destroy(f.text.gameObject);
            floaters.Clear();
        }

        public void OnLocalShot(ShotEvent shot, Camera camera)
        {
            if (shot.Hit) hitTimer = 0.18f;
            if (shot.Head) headTimer = 0.35f;
            if (shot.Killed) { comboCount = comboTimer > 0 ? comboCount + 1 : 1; comboTimer = 2.5f; }
            if (shot.Dealt > 0)
            {
                var t = Mono(floaterLayer, Mathf.RoundToInt(shot.Dealt).ToString(), shot.Head ? 22 : 15, shot.Head ? Palette.Amber : Palette.Bone);
                t.alignment = TextAnchor.MiddleCenter;
                t.rectTransform.sizeDelta = new Vector2(80, 30);
                floaters.Add((t, shot.End + new Vector3(Random.Range(-0.2f, 0.2f), 0.3f, 0), 0.7f));
            }
        }

        public void Tick(float dt, GameSim sim, Actor local, Camera camera, bool thirdPerson, string toastText)
        {
            bannerTimer = Mathf.Max(0, bannerTimer - dt);
            hitTimer = Mathf.Max(0, hitTimer - dt);
            headTimer = Mathf.Max(0, headTimer - dt);
            comboTimer = Mathf.Max(0, comboTimer - dt);
            damageFlash = Mathf.Max(0, damageFlash - dt);
            if (comboTimer <= 0) comboCount = 0;
            banner.color = new Color(1, 1, 1, Mathf.Clamp01(bannerTimer / 0.4f)) * Palette.Bone;
            hitMarker.text = headTimer > 0 ? "爆頭 ×2" : hitTimer > 0 ? "×" : "";
            hitMarker.color = headTimer > 0 ? Palette.Amber : Palette.Bone;
            combo.text = comboCount >= 2 ? $"×{comboCount}" : "";
            comboCaption.enabled = comboCount >= 2;
            toast.text = toastText;

            region.text = Defs.Regions[sim.Region].Name;
            wave.text = $"第 {sim.Wave} 波  / {Defs.WavesPerMission}";
            stats.text = $"剩餘 {sim.Enemies.Count:00}    擊破 {sim.Kills:00}    {(int)sim.Elapsed / 60:00}:{(int)sim.Elapsed % 60:00}";
            level.text = $"LV {sim.Level:00}";
            xpText.text = $"{sim.Xp} / {sim.XpNeed} XP · 敵人 ×{sim.LevelScale:0.00}";
            xpBar.Value = sim.XpNeed > 0 ? (float)sim.Xp / sim.XpNeed : 0;

            Enemy boss = null;
            foreach (var e in sim.Enemies.Values) if (e.Boss) { boss = e; break; }
            bossRoot.gameObject.SetActive(boss != null && !sim.BuffPending);
            if (boss != null)
            {
                bossName.text = $"{Defs.Kinds[boss.Kind].Name}{(boss.State == 2 ? "  ·  狂暴" : "")}   {Mathf.CeilToInt(boss.Hp)} / {Mathf.CeilToInt(boss.MaxHp)}";
                bossBar.Value = boss.Hp / Mathf.Max(1, boss.MaxHp);
            }

            if (local != null)
            {
                if (lastHp >= 0 && local.Hp < lastHp) damageFlash = 0.45f;
                lastHp = local.Hp;
                bool low = local.Alive && local.Hp <= local.MaxHp * 0.3f;
                float alpha = low ? 0.45f + 0.25f * Mathf.Sin(Time.time * 6) : Mathf.Clamp01(damageFlash * 1.8f);
                vignette.color = new Color(0.9f, 0.05f, 0.1f, alpha);
                shieldValue.text = Mathf.CeilToInt(local.Hp).ToString();
                shieldMax.text = $"/ {Mathf.RoundToInt(local.MaxHp)}";
                shieldBar.Value = local.Hp / local.MaxHp;
                shieldBar.Color = low ? Palette.Danger : Palette.Bone;
                dashLabel.text = local.DashCooldown <= 0 ? "[Shift] 衝刺就緒" : $"[Shift] 衝刺冷卻 {local.DashCooldown:0.0}s";
                float maxCd = Defs.SkillCooldown * local.SkillMult;
                skillBar.Value = 1f - Mathf.Clamp01(local.SkillCooldown / Mathf.Max(0.1f, maxCd));
                skillTime.text = local.SkillCooldown <= 0 ? "就緒" : $"{local.SkillCooldown:0.0}s";
                var kit = local.Loadout.Equipped;
                for (int i = 0; i < 2; i++)
                {
                    bool has = i < kit.Count;
                    slotLabels[i].enabled = slotLines[i].enabled = has;
                    if (!has) continue;
                    bool current = i == local.Slot;
                    slotLabels[i].text = $"{i + 1}   {Defs.Weapons[kit[i]].Short}";
                    slotLabels[i].color = current ? Palette.Bone : Palette.Hex("c9c5bb");
                    slotLines[i].color = current ? Palette.Amber : new Color(1, 1, 1, 0.25f);
                    slotLines[i].rectTransform.sizeDelta = new Vector2(170, current ? 2 : 1);
                }
                weaponName.text = Defs.Weapons[local.WeaponId].Name;
                bool reloading = local.Reload > 0;
                ammo.text = reloading ? "換彈" : local.Ammo.ToString();
                ammo.fontSize = reloading ? 32 : 64;
                ammo.color = reloading || local.Ammo <= local.Mag / 5 ? Palette.Amber : Palette.Bone;
                ammoMax.text = $"/ {local.Mag}";
                reloadBar.Value = reloading ? 1f - local.Reload / local.ReloadTime : (float)local.Ammo / Mathf.Max(1, local.Mag);
                hint.text = $"R 換彈 · 右鍵 瞄準 · V {(thirdPerson ? "第三人稱" : "第一人稱")}";
                buffCount.text = "";
                revive.text = "";
                if (!local.Alive && sim.Actors.Count > 1)
                    revive.text = $"你已倒地 · 等待隊友按住 E 救援  {Mathf.RoundToInt(100 * local.Revive / Defs.ReviveTime)}%";
                else
                    foreach (var other in sim.Actors.Values)
                        if (other.Id != local.Id && !other.Alive && Vector3.Distance(other.Position, local.Position) < 2.4f)
                            revive.text = $"按住 E 救援隊友  {Mathf.RoundToInt(100 * other.Revive / Defs.ReviveTime)}%";
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
                text.color = new Color(text.color.r, text.color.g, text.color.b, Mathf.Clamp01(life / 0.4f));
                floaters[i] = (text, world, life);
            }
        }

        public void SetBuffCount(int count) { }
        public void Show(bool visible) => Canvas.gameObject.SetActive(visible);
    }
}
