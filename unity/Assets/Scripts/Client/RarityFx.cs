using UnityEngine;
using UnityEngine.UI;

namespace RiftWard.Client
{
    /// Rarity presentation behind a showcase: a soft glow from 稀世 up, a pulse for 傳奇, and for 神話 a pulse
    /// plus counter-rotating rings. Higher tiers are deliberately louder.
    public sealed class RarityFx : MonoBehaviour
    {
        Image glow;
        RectTransform ringA, ringB;
        float baseAlpha, pulse, phase;

        public static void Attach(RectTransform target, int rarity)
        {
            if (rarity < 2) return;
            var color = Gear.RarityColors[Mathf.Clamp(rarity, 0, 4)];
            var host = Ui.Rect(target.parent, "RarityFx");
            host.SetSiblingIndex(target.GetSiblingIndex());
            host.anchorMin = target.anchorMin; host.anchorMax = target.anchorMax; host.pivot = target.pivot;
            host.anchoredPosition = target.anchoredPosition; host.sizeDelta = target.sizeDelta;
            var fx = host.gameObject.AddComponent<RarityFx>();
            fx.phase = Random.value * 6f;
            fx.baseAlpha = rarity == 2 ? 0.12f : rarity == 3 ? 0.18f : 0.24f;
            fx.pulse = rarity >= 3 ? 0.12f : 0f;
            fx.glow = Ui.Box(host, new Color(color.r, color.g, color.b, fx.baseAlpha), "Glow");
            fx.glow.rectTransform.Fill();
            fx.glow.raycastTarget = false;
            if (rarity >= 3)
            {
                // Corner brackets in the rarity colour.
                foreach (var (x, y) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
                {
                    var h = Ui.Box(host, color, "Bracket").rectTransform;
                    h.Place(new Vector2(x, y), Vector2.zero, new Vector2(16, 2), new Vector2(x, y));
                    var v = Ui.Box(host, color, "Bracket").rectTransform;
                    v.Place(new Vector2(x, y), Vector2.zero, new Vector2(2, 16), new Vector2(x, y));
                }
            }
            if (rarity >= 4)
            {
                float size = Mathf.Min(target.sizeDelta.x, target.sizeDelta.y) * 0.9f;
                fx.ringA = Ring(host, color, size, 6);
                fx.ringB = Ring(host, color, size * 0.72f, 4);
            }
        }

        static RectTransform Ring(RectTransform host, Color color, float size, int dashes)
        {
            var ring = Ui.Rect(host, "Ring");
            ring.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            for (int i = 0; i < dashes * 2; i++)
            {
                if (i % 2 == 1) continue;
                float a = i * Mathf.PI / dashes;
                var dash = Ui.Box(ring, new Color(color.r, color.g, color.b, 0.7f), "Dash").rectTransform;
                dash.Place(new Vector2(0.5f, 0.5f), new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * size * 0.5f, new Vector2(size * 0.28f, 2));
                dash.localRotation = Quaternion.Euler(0, 0, a * Mathf.Rad2Deg + 90f);
            }
            return ring;
        }

        void Update()
        {
            float t = Time.unscaledTime + phase;
            if (glow != null && pulse > 0)
            {
                var c = glow.color;
                c.a = baseAlpha + pulse * (0.5f + 0.5f * Mathf.Sin(t * 3f));
                glow.color = c;
            }
            if (ringA != null) ringA.localRotation = Quaternion.Euler(0, 0, t * 40f);
            if (ringB != null) ringB.localRotation = Quaternion.Euler(0, 0, -t * 65f);
        }
    }
}
