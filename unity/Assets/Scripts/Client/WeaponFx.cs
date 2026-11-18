using System.Collections.Generic;
using RiftWard.Sim;
using UnityEngine;

namespace RiftWard.Client
{
    /// Per-weapon shot visuals for ordinary weapons, impact scorch marks and barrel steam.
    public static class WeaponFx
    {
        /// The tracer that suits each gun: a thin laser for the P-9, travelling pulse bolts for the AR-27,
        /// fat plasma blobs per SG-8 pellet and a lingering rail beam for the LR-50.
        public static void Tracer(string weapon, Vector3 from, Vector3 to)
        {
            switch (weapon)
            {
                case "rifle":
                    Bolt.Spawn(from, to, Palette.Hex("ffc46a"), 0.045f, 2.2f, 320f);
                    Beam.Spawn(from, to, Palette.Hex("ffd98a"), 0.012f, 0.05f, 0.35f);
                    break;
                case "shotgun":
                    Bolt.Spawn(from, to, Palette.Hex("ff8a3a"), 0.075f, 0.9f, 170f);
                    break;
                case "sniper":
                    Beam.Spawn(from, to, Palette.Hex("b58cff"), 0.075f, 0.45f, 0.8f);
                    Beam.Spawn(from, to, Color.white, 0.022f, 0.22f, 1f);
                    break;
                default:
                    Beam.Spawn(from, to, Palette.Hex("ffe7a0"), 0.02f, 0.06f, 0.95f);
                    break;
            }
        }

        /// Sword swing: a curved, fading ribbon of light sweeping through the arc in front of the swinger.
        public static void Slash(Vector3 centre, float yaw, float radius, Color color, bool leftToRight)
        {
            const int points = 14;
            var go = new GameObject("Slash");
            var line = Line(go, color, 0.12f, 0.12f);
            line.positionCount = points;
            float half = Defs.MeleeHalfArc * Mathf.Deg2Rad;
            for (int i = 0; i < points; i++)
            {
                float t = i / (points - 1f);
                float a = yaw + Mathf.Lerp(-half, half, leftToRight ? t : 1f - t);
                line.SetPosition(i, centre + new Vector3(Mathf.Sin(a), Mathf.Lerp(0.18f, -0.22f, t), Mathf.Cos(a)) * radius);
            }
            // Thin at the start of the stroke, full at the leading edge.
            line.widthCurve = new AnimationCurve(new Keyframe(0, 0.15f), new Keyframe(0.75f, 1f), new Keyframe(1, 0.4f));
            go.AddComponent<Beam>().Init(line, color, 0.12f, 0.16f);
            var core = new GameObject("SlashCore");
            var coreLine = Line(core, Color.white, 0.04f, 0.04f);
            coreLine.positionCount = points;
            for (int i = 0; i < points; i++) coreLine.SetPosition(i, line.GetPosition(i));
            coreLine.widthCurve = line.widthCurve;
            core.AddComponent<Beam>().Init(coreLine, Color.white, 0.04f, 0.1f);
        }

        static readonly Queue<GameObject> scorches = new Queue<GameObject>();
        const int MaxScorches = 48;

        /// A dark burn mark on the surface that was hit; it fades over a few seconds. Oldest marks go first.
        public static void Scorch(Vector3 at, Vector3 normal)
        {
            while (scorches.Count >= MaxScorches) { var old = scorches.Dequeue(); if (old != null) Object.Destroy(old); }
            float size = Random.Range(0.16f, 0.24f);
            // A soft round burn (flattened sphere) with a darker centre and a short-lived ember.
            var go = Build.Sphere(null, at + normal * 0.012f, 1f, Palette.Hex("3a2e24"), 0f, false);
            go.transform.localScale = new Vector3(size, size * Random.Range(0.75f, 1f), 0.004f);
            go.transform.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0, 0, Random.Range(0f, 180f));
            Burst.Attach(go, Palette.Hex("3a2e24"), 0.45f, 0f, go.transform.localScale, go.transform.localScale * 1.05f, 9f, Vector3.zero);
            var centre = Build.Sphere(go.transform, Vector3.zero, 1f, Palette.Hex("15100c"), 0f, false);
            centre.transform.localScale = new Vector3(0.45f, 0.45f, 1.5f);
            Burst.Attach(centre, Palette.Hex("15100c"), 0.6f, 0f, centre.transform.localScale, centre.transform.localScale, 9f, Vector3.zero);
            var glow = Build.Sphere(go.transform, Vector3.zero, 1f, Palette.Hex("ff7a2a"), 3f, false);
            glow.transform.localScale = new Vector3(0.35f, 0.35f, 2f);
            Burst.Attach(glow, Palette.Hex("ff7a2a"), 0.8f, 3f, glow.transform.localScale, glow.transform.localScale * 0.4f, 0.7f, Vector3.zero);
            scorches.Enqueue(go);
        }

        /// The outward normal of whatever was hit: the ground, a cover block or the arena wall.
        public static Vector3 SurfaceNormal(Vector3 at, Vector3 incoming)
        {
            if (at.y < 0.06f) return Vector3.up;
            foreach (var c in Defs.Covers)
            {
                float dx = (at.x - c[0]) / c[2], dz = (at.z - c[1]) / c[3];
                if (Mathf.Abs(dx) > 1.08f || Mathf.Abs(dz) > 1.08f || at.y > c[4] + 0.05f) continue;
                if (at.y > c[4] - 0.04f) return Vector3.up;
                return Mathf.Abs(dx) > Mathf.Abs(dz) ? new Vector3(Mathf.Sign(dx), 0, 0) : new Vector3(0, 0, Mathf.Sign(dz));
            }
            if (Mathf.Abs(at.x) > Defs.ArenaHalf - 0.6f) return new Vector3(-Mathf.Sign(at.x), 0, 0);
            if (Mathf.Abs(at.z) > Defs.ArenaHalf - 0.6f) return new Vector3(0, 0, -Mathf.Sign(at.z));
            return -incoming.normalized;
        }

        /// A few soft puffs rising from a hot barrel or an opened weapon.
        public static void Steam(Vector3 at, float scale = 1f, int puffs = 4)
        {
            for (int i = 0; i < puffs; i++)
            {
                var drift = new Vector3(Random.Range(-0.08f, 0.08f), Random.Range(0.25f, 0.45f), Random.Range(-0.05f, 0.08f)) * scale;
                Burst.Sphere(at + Random.insideUnitSphere * 0.02f * scale, Palette.Hex("d8dde3"), 0.32f, 0f, 0.025f * scale, Random.Range(0.1f, 0.16f) * scale, Random.Range(0.6f, 1f), drift);
            }
        }

        /// A short glowing segment that travels from the muzzle to the impact point.
        sealed class Bolt : MonoBehaviour
        {
            LineRenderer line;
            Vector3 from, dir;
            float length, speed, travelled, total;

            public static void Spawn(Vector3 from, Vector3 to, Color color, float width, float length, float speed)
            {
                var go = new GameObject("Bolt");
                var b = go.AddComponent<Bolt>();
                b.line = Line(go, color, width, width * 0.5f);
                b.from = from;
                b.total = Vector3.Distance(from, to);
                b.dir = b.total > 0.001f ? (to - from) / b.total : Vector3.forward;
                b.length = length; b.speed = speed;
                b.Place();
            }

            void Place()
            {
                float head = Mathf.Min(total, travelled + length);
                float tail = Mathf.Min(total, travelled);
                line.SetPosition(0, from + dir * head);
                line.SetPosition(1, from + dir * tail);
            }

            void Update()
            {
                travelled += speed * Time.deltaTime;
                if (travelled >= total) { Destroy(gameObject); return; }
                Place();
            }
        }

        /// A straight beam that thins and fades out.
        public sealed class Beam : MonoBehaviour
        {
            LineRenderer line;
            float life, t, width;
            Color color;

            public static void Spawn(Vector3 from, Vector3 to, Color color, float width, float life, float alpha)
            {
                var go = new GameObject("Beam");
                var b = go.AddComponent<Beam>();
                b.color = new Color(color.r, color.g, color.b, alpha);
                b.line = Line(go, b.color, width, width);
                b.line.SetPosition(0, from);
                b.line.SetPosition(1, to);
                b.line.startWidth = b.line.endWidth = 1f;   // the width lives in widthMultiplier so it can fade
                b.line.widthMultiplier = width;
                b.life = life; b.width = width;
            }

            /// Takes over an existing line (its widthCurve gives the shape, w the overall width).
            public void Init(LineRenderer existing, Color c, float w, float l) { line = existing; color = c; width = w; life = l; line.widthMultiplier = w; }

            void Update()
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / life);
                var c = color; c.a *= (1f - k) * (1f - k);
                line.startColor = line.endColor = c;
                line.widthMultiplier = width * (1f - 0.6f * k);
                if (t >= life) Destroy(gameObject);
            }
        }

        static LineRenderer Line(GameObject go, Color color, float startWidth, float endWidth)
        {
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = startWidth; line.endWidth = endWidth;
            line.sharedMaterial = Build.Mat(color, 4f, 0.99f);
            line.startColor = line.endColor = color;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }
    }

    /// Master limiter on the listener: keeps a pile-up of shots and explosions from clipping. A fast envelope
    /// follower pulls the gain down above the threshold and recovers smoothly; a soft knee catches the rest.
    public sealed class MasterLimiter : MonoBehaviour
    {
        const float Threshold = 0.72f, Attack = 0.6f, Release = 0.00012f;
        float envelope, gain = 1f;

        void OnAudioFilterRead(float[] data, int channels)
        {
            for (int i = 0; i < data.Length; i += channels)
            {
                float peak = 0f;
                for (int c = 0; c < channels; c++) peak = Mathf.Max(peak, Mathf.Abs(data[i + c]));
                envelope = peak > envelope ? envelope + (peak - envelope) * Attack : envelope + (peak - envelope) * Release;
                float target = envelope > Threshold ? Threshold / envelope : 1f;
                gain += (target - gain) * (target < gain ? Attack : 0.0008f);
                for (int c = 0; c < channels; c++)
                {
                    float x = data[i + c] * gain;
                    data[i + c] = x / (1f + Mathf.Max(0f, Mathf.Abs(x) - 0.85f) * 2f);
                }
            }
        }
    }
}
