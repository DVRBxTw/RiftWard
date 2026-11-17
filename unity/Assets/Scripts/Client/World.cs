using RiftWard.Sim;
using UnityEngine;

namespace RiftWard.Client
{
    /// Builds the arena and swaps region dressing, sky, fog and light.
    public sealed class World
    {
        public readonly Transform Root;
        public readonly Light Sun;
        Transform dressing;
        readonly MeshRenderer ground;
        readonly MeshRenderer[] covers;
        readonly MeshRenderer[] walls;
        readonly Camera camera;

        struct Look
        {
            public Color Sky, Fog, Ambient, Sun, Ground, Cover, Wall;
            public float FogDensity, SunIntensity;
        }

        static readonly Look[] Looks =
        {
            new Look { Sky = Palette.Hex("6fa8d6"), Fog = Palette.Hex("a9c8d8"), FogDensity = 0.006f, Ambient = Palette.Hex("8fa58a"), Sun = Palette.Hex("ffe9c4"), SunIntensity = 1.15f,
                Ground = Palette.Hex("467530"), Cover = Palette.Hex("8a8270"), Wall = Palette.Hex("6e5c46") },
            new Look { Sky = Palette.Hex("2a1510"), Fog = Palette.Hex("3a1d12"), FogDensity = 0.018f, Ambient = Palette.Hex("6b4a3a"), Sun = Palette.Hex("ffc9a0"), SunIntensity = 0.9f,
                Ground = Palette.Hex("493429"), Cover = Palette.Hex("4a3a32"), Wall = Palette.Hex("3e3029") },
            new Look { Sky = Palette.Hex("160f28"), Fog = Palette.Hex("24183d"), FogDensity = 0.018f, Ambient = Palette.Hex("4f4670"), Sun = Palette.Hex("d9c9ff"), SunIntensity = 0.85f,
                Ground = Palette.Hex("352c4c"), Cover = Palette.Hex("3a3450"), Wall = Palette.Hex("332d48") },
        };

        public World(Camera camera)
        {
            this.camera = camera;
            Root = new GameObject("World").transform;
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(Root, false);
            sunGo.transform.rotation = Quaternion.Euler(48, 148, 0);
            Sun = sunGo.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.shadows = LightShadows.Soft;
            ground = Build.Cube(Root, new Vector3(0, -0.3f, 0), new Vector3(46, 0.6f, 46), Color.gray).GetComponent<MeshRenderer>();
            walls = new MeshRenderer[4];
            int w = 0;
            foreach (int side in new[] { -1, 1 })
            {
                walls[w++] = Build.Cube(Root, new Vector3(side * 23, 0.5f, 0), new Vector3(0.4f, 1, 46), Color.gray).GetComponent<MeshRenderer>();
                walls[w++] = Build.Cube(Root, new Vector3(0, 0.5f, side * 23), new Vector3(46, 1, 0.4f), Color.gray).GetComponent<MeshRenderer>();
            }
            covers = new MeshRenderer[Defs.Covers.Length];
            for (int i = 0; i < Defs.Covers.Length; i++)
            {
                var c = Defs.Covers[i];
                covers[i] = Build.Cube(Root, new Vector3(c[0], c[4] / 2f, c[1]), new Vector3(c[2] * 2, c[4], c[3] * 2), Color.gray).GetComponent<MeshRenderer>();
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            camera.clearFlags = CameraClearFlags.SolidColor;
        }

        public void ApplyRegion(int region)
        {
            region = Mathf.Clamp(region, 0, Looks.Length - 1);
            var look = Looks[region];
            camera.backgroundColor = look.Sky;
            RenderSettings.fogColor = look.Fog;
            RenderSettings.fogDensity = look.FogDensity;
            RenderSettings.ambientLight = look.Ambient;
            Sun.color = look.Sun;
            Sun.intensity = look.SunIntensity;
            ground.sharedMaterial = Build.Mat(look.Ground);
            foreach (var c in covers) c.sharedMaterial = Build.Mat(look.Cover);
            foreach (var wall in walls) wall.sharedMaterial = Build.Mat(look.Wall);
            if (dressing != null) Object.Destroy(dressing.gameObject);
            dressing = new GameObject("Dressing").transform;
            dressing.SetParent(Root, false);
            var rng = new System.Random(2189 + region);
            if (region == 0) Grassland(rng);
            else if (region == 1) Foundry(rng);
            else Rift(rng);
        }

        float R(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        void Grassland(System.Random rng)
        {
            var greens = new[] { Palette.Hex("355f24"), Palette.Hex("41702b"), Palette.Hex("4e8033"), Palette.Hex("5e8f3c") };
            Build.Cube(dressing, new Vector3(0, -0.62f, 0), new Vector3(170, 0.5f, 170), Palette.Hex("3f6a2b"));
            for (int i = 0; i < 14; i++)
            {
                float angle = Mathf.PI * 2f * i / 14f + R(rng, -0.2f, 0.2f), radius = R(rng, 55, 80), size = R(rng, 28, 48);
                var hill = Build.Sphere(dressing, new Vector3(Mathf.Sin(angle) * radius, -size * 0.22f, Mathf.Cos(angle) * radius), size, greens[rng.Next(2)] * 0.9f);
                hill.transform.localScale = new Vector3(size * 1.6f, size * 0.9f, size * 1.6f);
            }
            for (int i = 0; i < 46; i++)
            {
                float angle = R(rng, 0, Mathf.PI * 2f), radius = R(rng, 26, 48);
                Tree(new Vector3(Mathf.Sin(angle) * radius, 0, Mathf.Cos(angle) * radius), R(rng, 0.8f, 1.5f), greens[rng.Next(greens.Length)]);
            }
            for (int i = 0; i < 220; i++)
            {
                var p = new Vector3(R(rng, -22, 22), 0, R(rng, -22, 22));
                if (GameSim.InCover(p + Vector3.up * 0.1f)) continue;
                float h = R(rng, 0.18f, 0.45f);
                var blade = Build.Cube(dressing, p + Vector3.up * h / 2f, new Vector3(0.06f, h, 0.06f), greens[rng.Next(greens.Length)] * 1.15f, 0, false);
                blade.transform.localRotation = Quaternion.Euler(R(rng, -18, 18), R(rng, 0, 360), R(rng, -18, 18));
            }
            var petals = new[] { Palette.Hex("fff6e0"), Palette.Hex("ffd84d"), Palette.Hex("ff9ec4"), Palette.Hex("c9a6ff") };
            for (int i = 0; i < 70; i++)
                Build.Cube(dressing, new Vector3(R(rng, -22, 22), 0.12f, R(rng, -22, 22)), Vector3.one * 0.1f, petals[rng.Next(petals.Length)], 0.2f, false);
            for (int i = 0; i < 14; i++)
            {
                float angle = R(rng, 0, Mathf.PI * 2f), radius = R(rng, 20, 34), size = R(rng, 0.8f, 2.2f);
                Build.Sphere(dressing, new Vector3(Mathf.Sin(angle) * radius, size * 0.12f, Mathf.Cos(angle) * radius), size, Palette.Hex("8d8a80")).transform.localScale = new Vector3(size * 1.3f, size * 0.7f, size);
            }
            CrashedDropship(new Vector3(7, 0, -31));
        }

        void Tree(Vector3 position, float size, Color leaf)
        {
            Build.Cylinder(dressing, position + Vector3.up * 1.2f * size, 0.44f * size, 2.4f * size, Palette.Hex("6b4a2f"));
            for (int k = 0; k < 3; k++)
                Build.Cone(dressing, position + Vector3.up * (1.6f + 0.9f * k) * size, (1.6f - 0.35f * k) * size, 1.8f * size, leaf * (1f - 0.08f * k));
        }

        void CrashedDropship(Vector3 position)
        {
            var ship = new GameObject("Dropship").transform;
            ship.SetParent(dressing, false);
            ship.localPosition = position;
            ship.localRotation = Quaternion.Euler(-5, 35, 10);
            Color hull = Palette.Hex("5a6068"), burnt = Palette.Hex("2b2a2a");
            Build.Cube(ship, new Vector3(0, 1.2f, 0), new Vector3(3.2f, 2.2f, 9), hull);
            Build.Cube(ship, new Vector3(0, 1.4f, 5), new Vector3(2.4f, 1.6f, 1.6f), hull * 0.9f);
            Build.Cube(ship, new Vector3(0, 1.9f, 5.6f), new Vector3(1.8f, 0.6f, 0.1f), Palette.Hex("8ec5ff"), 0.6f);
            Build.Cube(ship, new Vector3(-3.4f, 0.9f, -0.6f), new Vector3(4, 0.25f, 2.6f), hull * 0.85f);
            Build.Cube(ship, new Vector3(3.2f, 0.4f, -1.8f), new Vector3(3.4f, 0.25f, 2.4f), burnt).transform.localRotation = Quaternion.Euler(0, -20, -28);
            Build.Cube(ship, new Vector3(0, 2.0f, -3.6f), new Vector3(3.3f, 0.9f, 1.4f), burnt);
            Build.Cube(ship, new Vector3(0, 1.2f, -4.6f), new Vector3(1.4f, 1.4f, 0.6f), Palette.Hex("ff8a3d"), 1.6f);
            for (int k = 0; k < 6; k++)
            {
                float size = 1.6f + 0.7f * k;
                var puff = Build.Sphere(dressing, position + new Vector3(0.6f * k, 3.2f + 1.7f * k, -1.2f - 0.4f * k), size, Palette.Hex("2e2e33"), 0, false);
                puff.GetComponent<MeshRenderer>().sharedMaterial = Build.Mat(Palette.Hex("2e2e33"), 0, 0.55f - 0.07f * k);
            }
            Build.Cube(dressing, position + Vector3.up * 0.005f, new Vector3(7, 0.01f, 13), Palette.Hex("3a3326"), 0, false).transform.localRotation = Quaternion.Euler(0, 35, 0);
        }

        void Foundry(System.Random rng)
        {
            Color metal = Palette.Hex("3a2e28"), lava = Palette.Hex("ff6a1f");
            Build.Cube(dressing, new Vector3(0, -0.62f, 0), new Vector3(170, 0.5f, 170), Palette.Hex("221812"));
            foreach (float x in new[] { -30f, 30f })
            {
                Build.Cube(dressing, new Vector3(x, -0.3f, 0), new Vector3(8, 0.2f, 120), lava, 2.2f, false);
            }
            for (int i = 0; i < 36; i++)
            {
                float angle = R(rng, 0, Mathf.PI * 2f), radius = R(rng, 30, 65), h = R(rng, 6, 24);
                var p = new Vector3(Mathf.Sin(angle) * radius, h / 2f - 1, Mathf.Cos(angle) * radius);
                Build.Cube(dressing, p, new Vector3(R(rng, 3, 7), h, R(rng, 3, 7)), metal);
                Build.Cylinder(dressing, p + Vector3.up * (h / 2f + 2f), 1.2f, 4f, Palette.Hex("4a3a30"));
                Build.Cube(dressing, p + Vector3.up * h / 2f, new Vector3(1.6f, 0.2f, 1.6f), lava, 1.6f, false);
            }
            for (int i = 0; i < 9; i++)
            {
                float angle = Mathf.PI * 2f * i / 9f;
                var p = new Vector3(Mathf.Sin(angle) * 19f, 0, Mathf.Cos(angle) * 19f);
                Build.Cylinder(dressing, p + Vector3.up * 1.75f, 1.6f, 3.5f, Palette.Hex("624b36"));
                Build.Cube(dressing, p + Vector3.up * 3.6f, new Vector3(1.6f, 0.2f, 1.6f), Palette.Hex("ffad66"), 1f, false);
            }
        }

        void Rift(System.Random rng)
        {
            Color crystal = Palette.Hex("b492ff"), rock = Palette.Hex("2a2340");
            Build.Cube(dressing, new Vector3(0, -0.62f, 0), new Vector3(170, 0.5f, 170), Palette.Hex("1a1528"));
            var portal = new GameObject("Portal").transform;
            portal.SetParent(dressing, false);
            portal.localPosition = new Vector3(0, 9, 40);
            for (int i = 0; i < 24; i++)
            {
                float a = Mathf.PI * 2f * i / 24f;
                Build.Cube(portal, new Vector3(Mathf.Cos(a) * 7, Mathf.Sin(a) * 7, 0), new Vector3(1.2f, 1.2f, 0.6f), crystal, 2.5f, false).transform.localRotation = Quaternion.Euler(0, 0, a * Mathf.Rad2Deg);
            }
            Build.Cube(portal, Vector3.zero, new Vector3(12, 12, 0.2f), Palette.Hex("4a2c8a"), 1.2f, false);
            for (int i = 0; i < 30; i++)
            {
                float angle = R(rng, 0, Mathf.PI * 2f), radius = R(rng, 28, 60);
                var p = new Vector3(Mathf.Sin(angle) * radius, R(rng, 2, 14), Mathf.Cos(angle) * radius);
                var shard = Build.Cube(dressing, p, new Vector3(R(rng, 1.5f, 4), R(rng, 1, 3), R(rng, 1.5f, 4)), rock);
                shard.transform.localRotation = Quaternion.Euler(R(rng, -30, 30), R(rng, 0, 360), R(rng, -30, 30));
            }
            for (int i = 0; i < 11; i++)
            {
                float angle = Mathf.PI * 2f * i / 11f;
                var p = new Vector3(Mathf.Sin(angle) * 19f, 2.5f, Mathf.Cos(angle) * 19f);
                Build.Cube(dressing, p, new Vector3(0.8f, 5, 0.8f), crystal, 0.5f).transform.localRotation = Quaternion.Euler(0, 0, i % 2 == 0 ? 18 : -18);
            }
        }
    }
}
