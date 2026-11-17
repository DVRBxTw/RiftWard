using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RiftWard.Client
{
    /// Currency icons drawn procedurally (anti-aliased signed-distance shapes), cached per colour.
    /// Coin: embossed gold disc with rim and highlight. Key: ring bow, shaft and teeth with an outline.
    public static class Icons
    {
        const int Size = 96;
        static Sprite coin;
        static readonly Dictionary<Color, Sprite> keys = new Dictionary<Color, Sprite>();

        public static Image Coin(Transform parent, float size)
        {
            if (coin == null) coin = Make(CoinPixel);
            return Place(parent, coin, size);
        }

        public static Image Key(Transform parent, float size, Color color)
        {
            if (!keys.TryGetValue(color, out var sprite)) keys[color] = sprite = Make((x, y) => KeyPixel(x, y, color));
            return Place(parent, sprite, size);
        }

        static Image Place(Transform parent, Sprite sprite, float size)
        {
            var image = Ui.Box(parent, Color.white, "Icon");
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.rectTransform.sizeDelta = new Vector2(size, size);
            return image;
        }

        delegate Color Pixel(float x, float y);

        static Sprite Make(Pixel shade)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[Size * Size];
            for (int j = 0; j < Size; j++)
                for (int i = 0; i < Size; i++)
                    px[j * Size + i] = shade((i + 0.5f) / Size * 2f - 1f, (j + 0.5f) / Size * 2f - 1f);   // -1..1, y up
            tex.SetPixels(px);
            tex.Apply(true, true);
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size);
        }

        static float Cover(float distance) => Mathf.Clamp01(0.5f - distance * Size * 0.5f);   // 1 inside, AA edge

        static Color Over(Color under, Color top, float a) => new Color(
            Mathf.Lerp(under.r, top.r, a), Mathf.Lerp(under.g, top.g, a), Mathf.Lerp(under.b, top.b, a), Mathf.Max(under.a, a));

        static Color CoinPixel(float x, float y)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            var c = new Color(0, 0, 0, 0);
            c = Over(c, new Color(0.45f, 0.29f, 0.06f), Cover(r - 0.94f));                     // dark rim
            float light = Mathf.Clamp01(0.55f + 0.45f * (y * 0.6f - x * 0.4f));
            c = Over(c, Color.Lerp(new Color(0.85f, 0.56f, 0.12f), new Color(1f, 0.86f, 0.4f), light), Cover(r - 0.82f));   // face
            c = Over(c, new Color(0.6f, 0.38f, 0.08f), Cover(Mathf.Abs(r - 0.6f) - 0.045f));    // inner ring
            // Embossed diamond in the middle.
            float d = Mathf.Abs(x) + Mathf.Abs(y) * 0.8f - 0.3f;
            c = Over(c, new Color(1f, 0.93f, 0.62f), Cover(d));
            c = Over(c, new Color(0.7f, 0.45f, 0.1f), Cover(Mathf.Abs(d + 0.1f) - 0.025f) * Cover(d));
            // Specular glint, upper left.
            float gx = x + 0.38f, gy = y - 0.42f;
            c = Over(c, new Color(1f, 1f, 0.92f), 0.85f * Cover(Mathf.Sqrt(gx * gx * 2.2f + gy * gy) - 0.13f) * Cover(r - 0.82f));
            return c;
        }

        static Color KeyPixel(float x, float y, Color tint)
        {
            // Key laid diagonally: rotate the plane 45 degrees so the bow sits upper-left.
            float u = (x - y) * 0.7071f, v = (x + y) * 0.7071f;
            float bow = Mathf.Abs(Mathf.Sqrt((u + 0.5f) * (u + 0.5f) + v * v) - 0.27f) - 0.11f;
            float shaft = Box(u - 0.28f, v, 0.5f, 0.075f);
            float tooth1 = Box(u - 0.62f, v - 0.15f, 0.06f, 0.1f);
            float tooth2 = Box(u - 0.4f, v - 0.12f, 0.05f, 0.07f);
            float shape = Mathf.Min(Mathf.Min(bow, shaft), Mathf.Min(tooth1, tooth2));
            var c = new Color(0, 0, 0, 0);
            Color deep = new Color(tint.r * 0.35f, tint.g * 0.35f, tint.b * 0.35f);
            c = Over(c, deep, Cover(shape - 0.06f));                                         // outline
            float light = Mathf.Clamp01(0.5f + 0.5f * (y - x) * 0.7f);
            c = Over(c, Color.Lerp(tint * 0.8f, Color.Lerp(tint, Color.white, 0.45f), light), Cover(shape));
            float hole = Mathf.Sqrt((u + 0.5f) * (u + 0.5f) + v * v) - 0.13f;                 // punched centre of the bow
            c = Over(c, deep, Cover(Mathf.Abs(hole) - 0.025f) * Cover(shape));
            c.a *= 1f - Cover(hole);
            return c;
        }

        static float Box(float x, float y, float hx, float hy)
        {
            float dx = Mathf.Abs(x) - hx, dy = Mathf.Abs(y) - hy;
            return Mathf.Sqrt(Mathf.Max(dx, 0) * Mathf.Max(dx, 0) + Mathf.Max(dy, 0) * Mathf.Max(dy, 0)) + Mathf.Min(Mathf.Max(dx, dy), 0);
        }
    }
}
