using UnityEngine;

namespace RiftWard.Client
{
    /// One-shot effect piece: grows from a start to an end scale (ease-out), fades its own material, drifts,
    /// and dims an optional light, then destroys itself. Used for fireballs, shockwaves, smoke and flashes.
    public sealed class Burst : MonoBehaviour
    {
        float life, t, alpha, lightStart;
        Vector3 from, to, drift;
        Material material;
        Color color;
        Light glow;

        public static GameObject Sphere(Vector3 at, Color color, float alpha, float glowAmount, float fromSize, float toSize, float life, Vector3 drift = default)
        {
            var go = Build.Sphere(null, at, fromSize, color, glowAmount, false);
            Attach(go, color, alpha, glowAmount, Vector3.one * fromSize, Vector3.one * toSize, life, drift);
            return go;
        }

        /// Flat expanding ring on the ground (a squashed cylinder).
        public static GameObject Ring(Vector3 at, Color color, float toRadius, float life)
        {
            var go = Build.Cylinder(null, at, 0.2f, 0.02f, color, 3f);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Attach(go, color, 0.7f, 3f, new Vector3(0.2f, 0.01f, 0.2f), new Vector3(toRadius * 2f, 0.01f, toRadius * 2f), life, Vector3.zero);
            return go;
        }

        public static void Attach(GameObject go, Color color, float alpha, float glowAmount, Vector3 fromScale, Vector3 toScale, float life, Vector3 drift)
        {
            var b = go.AddComponent<Burst>();
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = Build.Mat(color, glowAmount, Mathf.Min(alpha, 0.99f));
            b.material = r.material;   // instance so each piece fades on its own
            b.color = b.material.color;
            b.alpha = alpha;
            b.from = fromScale; b.to = toScale; b.life = life; b.drift = drift;
            go.transform.localScale = fromScale;
        }

        public void AddLight(Color color, float intensity, float range)
        {
            glow = gameObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = color;
            glow.range = range;
            glow.intensity = lightStart = intensity;
        }

        void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);
            float ease = 1f - (1f - k) * (1f - k);
            transform.localScale = Vector3.Lerp(from, to, ease);
            transform.position += drift * Time.deltaTime;
            if (material != null) { var c = color; c.a = alpha * (1f - k); material.color = c; }
            if (glow != null) glow.intensity = lightStart * (1f - k) * (1f - k);
            if (t >= life) Destroy(gameObject);
        }

        void OnDestroy() { if (material != null) Destroy(material); }
    }
}
