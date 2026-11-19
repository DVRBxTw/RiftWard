using System.Collections.Generic;
using RiftWard.Sim;
using UnityEngine;

namespace RiftWard.Client
{
    /// Briefly turns every part of a model white-hot when it takes damage.
    public sealed class HitFlash : MonoBehaviour
    {
        MeshRenderer[] parts;
        Material[] original;
        float left;
        static Material white;

        public static HitFlash On(GameObject go) => go.GetComponent<HitFlash>() ?? go.AddComponent<HitFlash>();

        public void Flash(float seconds = 0.07f)
        {
            if (white == null) white = Build.Mat(new Color(1f, 0.96f, 0.9f), 2.5f);
            if (left <= 0f)
            {
                // Cached at the first hit (elite trim and the like are added after the model is built).
                parts = GetComponentsInChildren<MeshRenderer>(true);
                original = new Material[parts.Length];
                for (int i = 0; i < parts.Length; i++) { original[i] = parts[i].sharedMaterial; parts[i].sharedMaterial = white; }
            }
            left = seconds;
        }

        void Update()
        {
            if (left <= 0f) return;
            left -= Time.deltaTime;
            if (left > 0f) return;
            for (int i = 0; i < parts.Length; i++) if (parts[i] != null) parts[i].sharedMaterial = original[i];
        }
    }

    /// Detail pass on top of the primitive models: enemy shots, hit debris, pickup beacons, cover trim,
    /// ground variation, ambient particles and scorch marks.
    public static class Art
    {
        /// Each teammate gets their own accent colour on the soldier (shoulder lamps, visor, chest plate).
        public static readonly Color[] TeamColors = { Palette.Hex("82aaff"), Palette.Hex("6fe0a8"), Palette.Hex("ff8fd0"), Palette.Hex("7fe6ff") };

        /// Pulsing glow disc under elites (gold) and bosses (red), so they read from across the arena.
        public static void GroundRing(Transform root, Color color, float radius)
        {
            var ring = new GameObject("GroundRing").transform;
            ring.SetParent(root, false);
            var outer = Build.Cylinder(ring, new Vector3(0, 0.03f, 0), radius * 2f, 0.01f, color, 2f);
            outer.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(color, 2f, 0.28f);
            outer.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var inner = Build.Cylinder(ring, new Vector3(0, 0.035f, 0), radius * 1.4f, 0.01f, color, 3f);
            inner.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(Color.Lerp(color, Color.white, 0.3f), 3f, 0.18f);
            inner.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.gameObject.AddComponent<Spinner>().Pulse = 0.12f;
        }

        /// Enemy and gadget projectiles: a hot core, a coloured halo and a short fading trail. Sniper rounds are
        /// stretched along their flight (the client turns them to face their velocity).
        public static GameObject Projectile(BulletKind kind)
        {
            Color color = kind == BulletKind.Sniper ? Palette.Hex("c78bff") : kind == BulletKind.Grenade ? Palette.Hex("7dff9a") : Palette.Hex("ff5a3c");
            float size = kind == BulletKind.Sniper ? 0.2f : kind == BulletKind.Grenade ? 0.4f : 0.32f;
            var root = new GameObject("Bullet").transform;
            var core = Build.Sphere(root, Vector3.zero, size * 0.5f, Color.Lerp(color, Color.white, 0.75f), 5f, false);
            var halo = Build.Sphere(root, Vector3.zero, size * 1.5f, color, 3f, false);
            halo.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(color, 3f, 0.32f);
            if (kind == BulletKind.Sniper)
            {
                core.transform.localScale = new Vector3(size * 0.35f, size * 0.35f, size * 3.2f);
                halo.transform.localScale = new Vector3(size * 1.1f, size * 1.1f, size * 4.5f);
            }
            else halo.AddComponent<Spinner>().Pulse = 0.18f;
            var trail = new GameObject("Trail").AddComponent<TrailRenderer>();
            trail.transform.SetParent(root, false);
            trail.time = kind == BulletKind.Sniper ? 0.22f : 0.14f;
            trail.startWidth = size * (kind == BulletKind.Sniper ? 0.5f : 0.8f);
            trail.endWidth = 0f;
            trail.sharedMaterial = Build.Mat(color, 3f, 0.7f);
            trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) },
            };
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.minVertexDistance = 0.05f;
            return root.gameObject;
        }

        /// Pickups: a glowing ring on the ground under the item and a faint light column so they read from afar.
        public static void Beacon(Transform pickup, Color color, bool tall)
        {
            var ring = Build.Cylinder(pickup, new Vector3(0, -0.58f, 0), 0.9f, 0.01f, color, 2f);
            ring.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(color, 2f, 0.3f);
            ring.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (!tall) return;
            var beam = Build.Cylinder(pickup, new Vector3(0, 0.9f, 0), 0.16f, 3f, color, 2f);
            beam.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(color, 2f, 0.16f);
            beam.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// Armour chips flying off an enemy that was hit (enemy-coloured), with a white flash ring on headshots.
        public static void HitDebris(List<(GameObject go, float life, Vector3 velocity, float spin)> effects, Vector3 at, Vector3 from, Color color, bool head)
        {
            var back = (from - at).normalized;
            int n = head ? 7 : 4;
            for (int i = 0; i < n; i++)
            {
                var chip = Build.Cube(null, at, Vector3.one * Random.Range(0.04f, 0.08f), Color.Lerp(color, Color.white, Random.Range(0f, 0.4f)), 2.5f, false);
                var v = (back * 0.6f + Random.insideUnitSphere).normalized * Random.Range(3f, 6f) + Vector3.up * 2f;
                effects.Add((chip, Random.Range(0.25f, 0.45f), v, 900f));
            }
            if (head) Burst.Sphere(at, Color.white, 0.8f, 4f, 0.1f, 0.7f, 0.1f);
        }

        /// A dark scorch disc left where something exploded; fades over a few seconds.
        public static void ScorchMark(Vector3 at, float radius)
        {
            var mark = Build.Cylinder(null, new Vector3(at.x, 0.012f, at.z), radius * 1.6f, 0.005f, Palette.Hex("120e0c"));
            mark.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Burst.Attach(mark, Palette.Hex("120e0c"), 0.6f, 0f, new Vector3(radius * 1.2f, 0.005f, radius * 1.2f), new Vector3(radius * 1.7f, 0.005f, radius * 1.7f), 4.5f, Vector3.zero);
        }

        // ------------------------------------------------------------ arena dressing

        /// Cover blocks get a darker cap and skirt, corner posts, a band (hazard stripes in industrial regions,
        /// a glowing accent strip elsewhere) and small lights on the top corners.
        public static void DressCover(Transform parent, Vector3 centre, Vector3 size, Color cover, Color accent, bool industrial)
        {
            Color dark = cover * 0.62f; dark.a = 1f;
            Color edge = cover * 0.8f; edge.a = 1f;
            float h = size.y, top = centre.y + h / 2f;
            Build.Cube(parent, new Vector3(centre.x, top + 0.035f, centre.z), new Vector3(size.x + 0.08f, 0.07f, size.z + 0.08f), dark);
            Build.Cube(parent, new Vector3(centre.x, 0.07f, centre.z), new Vector3(size.x + 0.07f, 0.14f, size.z + 0.07f), dark, 0f, false);
            foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            {
                var corner = new Vector3(centre.x + sx * size.x / 2f, centre.y, centre.z + sz * size.z / 2f);
                Build.Cube(parent, corner, new Vector3(0.1f, h + 0.02f, 0.1f), edge, 0f, false);
                Build.Cube(parent, new Vector3(corner.x, top + 0.09f, corner.z), new Vector3(0.07f, 0.05f, 0.07f), accent, 4f, false);
            }
            bool alongX = size.x >= size.z;
            if (GameSettings.Quality >= 2) CoverDetail(parent, centre, size, cover, alongX);
            // Band along the two long faces.
            float length = alongX ? size.x : size.z, depth = alongX ? size.z : size.x;
            float bandY = Mathf.Min(top - 0.2f, centre.y + h * 0.15f);
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 face = alongX ? new Vector3(centre.x, bandY, centre.z + side * (depth / 2f + 0.006f)) : new Vector3(centre.x + side * (depth / 2f + 0.006f), bandY, centre.z);
                if (industrial)
                {
                    int stripes = Mathf.Max(2, Mathf.FloorToInt(length / 0.32f));
                    float step = length / stripes;
                    for (int i = 0; i < stripes; i++)
                    {
                        float offset = -length / 2f + step * (i + 0.5f);
                        var at = face + (alongX ? new Vector3(offset, 0, 0) : new Vector3(0, 0, offset));
                        Build.Cube(parent, at, alongX ? new Vector3(step * 0.98f, 0.14f, 0.012f) : new Vector3(0.012f, 0.14f, step * 0.98f),
                            i % 2 == 0 ? Palette.Hex("e0a32e") : Palette.Hex("1b1d20"), 0f, false);
                    }
                }
                else
                {
                    var strip = Build.Cube(parent, face + Vector3.up * (h * 0.45f), alongX ? new Vector3(length * 0.8f, 0.035f, 0.012f) : new Vector3(0.012f, 0.035f, length * 0.8f), accent, 2.2f, false);
                    strip.name = "AccentStrip";
                }
            }
        }

        /// Cover detail: vertical panel seams and a bolt row on the long faces, and chunks knocked off two corners.
        static void CoverDetail(Transform parent, Vector3 centre, Vector3 size, Color cover, bool alongX)
        {
            Color seam = cover * 0.5f; seam.a = 1f;
            Color bolt = cover * 1.25f; bolt.a = 1f;
            Color chip = cover * 0.72f; chip.a = 1f;
            float h = size.y, top = centre.y + h / 2f;
            float length = alongX ? size.x : size.z, depth = alongX ? size.z : size.x;
            int panels = Mathf.Max(1, Mathf.RoundToInt(length / 1.4f));
            foreach (int side in new[] { -1, 1 })
            {
                for (int i = 1; i < panels; i++)
                {
                    float offset = -length / 2f + length * i / panels;
                    Vector3 at = alongX ? new Vector3(centre.x + offset, centre.y, centre.z + side * (depth / 2f + 0.004f)) : new Vector3(centre.x + side * (depth / 2f + 0.004f), centre.y, centre.z + offset);
                    Build.Cube(parent, at, alongX ? new Vector3(0.025f, h * 0.9f, 0.01f) : new Vector3(0.01f, h * 0.9f, 0.025f), seam, 0f, false);
                }
                for (int i = 0; i < panels; i++)
                {
                    float offset = -length / 2f + length * (i + 0.5f) / panels;
                    foreach (float dy in new[] { -0.3f, 0.3f })
                    {
                        Vector3 at = alongX ? new Vector3(centre.x + offset + dy, top - 0.16f, centre.z + side * (depth / 2f + 0.012f)) : new Vector3(centre.x + side * (depth / 2f + 0.012f), top - 0.16f, centre.z + offset + dy);
                        Build.Cube(parent, at, new Vector3(0.05f, 0.05f, 0.05f), bolt, 0f, false);
                    }
                }
            }
            // Battle damage: rubble at the foot of two corners.
            foreach (var (sx, sz) in new[] { (1, 1), (-1, -1) })
            {
                var foot = new Vector3(centre.x + sx * (size.x / 2f + 0.18f), 0.08f, centre.z + sz * (size.z / 2f + 0.12f));
                for (int k = 0; k < 3; k++)
                {
                    var c = Build.Cube(parent, foot + new Vector3(k * 0.13f * sx, 0.02f * k, -k * 0.09f * sz), Vector3.one * (0.16f - 0.03f * k), chip, 0f, false);
                    c.transform.localRotation = Quaternion.Euler(12 * k, 37 * k + 10, 8);
                }
            }
        }

        /// Arena boundary at 高 quality: a post every 4 m with a lit cap, a top rail and a darker base course.
        public static void DressBoundary(Transform parent, Color wall, Color accent, bool industrial)
        {
            Color post = wall * 0.7f; post.a = 1f;
            Color rail = wall * 1.15f; rail.a = 1f;
            foreach (int side in new[] { -1, 1 })
            {
                foreach (bool alongX in new[] { true, false })
                {
                    Vector3 line = alongX ? new Vector3(0, 0, side * 23f) : new Vector3(side * 23f, 0, 0);
                    Build.Cube(parent, line + Vector3.up * 1.03f, alongX ? new Vector3(46.2f, 0.06f, 0.5f) : new Vector3(0.5f, 0.06f, 46.2f), rail, 0f, false);
                    Build.Cube(parent, line + Vector3.up * 0.08f, alongX ? new Vector3(46.2f, 0.16f, 0.52f) : new Vector3(0.52f, 0.16f, 46.2f), post, 0f, false);
                    for (int i = -5; i <= 5; i++)
                    {
                        Vector3 at = line + (alongX ? new Vector3(i * 4.2f, 0, 0) : new Vector3(0, 0, i * 4.2f));
                        Build.Cube(parent, at + Vector3.up * 0.6f, new Vector3(0.32f, 1.2f, 0.32f), post, 0f, false);
                        Build.Cube(parent, at + Vector3.up * 1.24f, new Vector3(0.18f, 0.05f, 0.18f), industrial ? Palette.Hex("e0a32e") : accent, 2.5f, false);
                    }
                }
            }
        }

        /// Patches of lighter and darker ground plus scattered pebbles, so the arena floor is not one flat colour.
        public static void GroundVariation(Transform parent, System.Random rng, Color ground, int patches = 40, int pebbles = 90)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            for (int i = 0; i < patches; i++)
            {
                var c = Color.Lerp(ground, Color.black, R(0.025f, 0.06f)); c.a = 1f;   // only slightly darker: lighter patches read as plates
                var p = Build.Cube(parent, new Vector3(R(-21f, 21f), 0.0015f + i * 0.00002f, R(-21f, 21f)), new Vector3(R(1.2f, 4f), 0.0008f, R(1.2f, 4f)), c, 0f, false);
                p.transform.localRotation = Quaternion.Euler(0, R(0, 90), 0);
                p.GetComponent<MeshRenderer>().sharedMaterial = Build.Matte(c);
            }
            for (int i = 0; i < pebbles; i++)
            {
                var c = Color.Lerp(ground, Color.gray, R(0.3f, 0.6f)) * R(0.6f, 0.9f); c.a = 1f;
                float s = R(0.06f, 0.18f);
                var p = Build.Cube(parent, new Vector3(R(-21.5f, 21.5f), s * 0.35f, R(-21.5f, 21.5f)), new Vector3(s, s * 0.7f, s * R(0.8f, 1.3f)), c, 0f, false);
                p.transform.localRotation = Quaternion.Euler(R(-15, 15), R(0, 360), R(-15, 15));
                p.GetComponent<MeshRenderer>().sharedMaterial = Build.Matte(c);
            }
        }

        static Material particleMat;

        /// Floating motes over the arena, per region: pollen, embers, rift sparks, snow, high-altitude haze, gold dust.
        public static void AmbientParticles(Transform parent, int region)
        {
            var go = new GameObject("Ambient");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, 4f, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Color color; Vector3 drift; float size, rate; bool glow;
            switch (region)
            {
                case 1: color = Palette.Hex("ff9a4a"); drift = new Vector3(0.1f, 0.7f, 0.05f); size = 0.06f; rate = 26f; glow = true; break;   // embers
                case 2: color = Palette.Hex("b693ff"); drift = new Vector3(0, 0.25f, 0); size = 0.07f; rate = 22f; glow = true; break;          // rift sparks
                case 3: color = Color.white; drift = new Vector3(0.35f, -0.9f, 0.1f); size = 0.09f; rate = 70f; glow = false; break;           // snow
                case 4: color = Palette.Hex("ffe2c8"); drift = new Vector3(1.2f, 0.05f, 0.3f); size = 0.05f; rate = 30f; glow = false; break;    // wind-borne haze
                case 5: color = Palette.Hex("ffd36b"); drift = new Vector3(0, 0.35f, 0); size = 0.05f; rate = 24f; glow = true; break;          // gold dust
                default: color = Palette.Hex("f4f0b0"); drift = new Vector3(0.25f, 0.08f, 0.1f); size = 0.045f; rate = 20f; glow = false; break; // pollen
            }
            var main = ps.main;
            main.loop = true; main.prewarm = true;
            main.startLifetime = 9f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size * 1.4f);
            main.startColor = new Color(color.r, color.g, color.b, glow ? 0.9f : 0.65f);
            main.maxParticles = 500;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission; emission.rateOverTime = rate;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(44f, 8f, 44f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(drift.x * 0.6f, drift.x * 1.4f + 0.1f);
            vel.y = new ParticleSystem.MinMaxCurve(drift.y * 0.6f, drift.y * 1.4f + (drift.y >= 0 ? 0.05f : -0.05f));
            vel.z = new ParticleSystem.MinMaxCurve(drift.z - 0.15f, drift.z + 0.15f);
            var fade = ps.colorOverLifetime; fade.enabled = true;
            fade.color = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) },
            };
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (particleMat == null)
            {
                var shader = Shader.Find("Sprites/Default");
                particleMat = new Material(shader) { mainTexture = SoftDot() };
            }
            renderer.sharedMaterial = particleMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        static Texture2D softDot;
        static Texture2D SoftDot()
        {
            if (softDot != null) return softDot;
            const int n = 32;
            softDot = new Texture2D(n, n) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
                    softDot.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d)));
                }
            softDot.Apply();
            return softDot;
        }
    }
}
