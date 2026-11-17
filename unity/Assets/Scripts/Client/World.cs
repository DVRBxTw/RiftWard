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
            new Look { Sky = Palette.Hex("5b9bd5"), Fog = Palette.Hex("9fc3d9"), FogDensity = 0.0045f, Ambient = Palette.Hex("66786a"), Sun = Palette.Hex("fff0d0"), SunIntensity = 1.1f,
                Ground = Palette.Hex("3a6326"), Cover = Palette.Hex("8a8270"), Wall = Palette.Hex("6e5c46") },
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
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            camera.clearFlags = CameraClearFlags.SolidColor;
        }

        public void ApplyRegion(int region)
        {
            region = Mathf.Clamp(region, 0, Looks.Length - 1);
            var look = Looks[region];
            camera.backgroundColor = look.Sky;
            RenderSettings.fogColor = look.Fog;
            RenderSettings.fogDensity = look.FogDensity;
            RenderSettings.ambientSkyColor = look.Ambient * 0.85f;
            RenderSettings.ambientEquatorColor = look.Ambient * 0.8f;
            RenderSettings.ambientGroundColor = look.Ambient * 0.4f;
            Sun.color = look.Sun;
            Sun.intensity = look.SunIntensity;
            ground.sharedMaterial = Build.Matte(look.Ground);
            foreach (var c in covers) c.sharedMaterial = Build.Matte(look.Cover);
            foreach (var wall in walls) wall.sharedMaterial = Build.Matte(look.Wall);
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
            GrasslandDetail(rng, greens);
        }

        /// Ground variation and battlefield clutter: dirt, bushes, supply crates, barrels, debris and lamp posts.
        void GrasslandDetail(System.Random rng, Color[] greens)
        {
            Color dirt = Palette.Hex("6b5a3e"), crate = Palette.Hex("6a5634"), strap = Palette.Hex("2b2a27"), barrel = Palette.Hex("3d5a66");
            for (int i = 0; i < 18; i++)
            {
                var p = new Vector3(R(rng, -21, 21), 0.012f, R(rng, -21, 21));
                float s = R(rng, 1.4f, 3.6f);
                var patch = Build.Cylinder(dressing, p, s, 0.02f, Color.Lerp(dirt, greens[0], R(rng, 0f, 0.5f)));
                patch.transform.localScale = new Vector3(s * R(rng, 0.7f, 1.3f), 0.01f, s);
                patch.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            for (int i = 0; i < 26; i++)
            {
                float angle = R(rng, 0, Mathf.PI * 2f), radius = R(rng, 19, 26);
                var c = new Vector3(Mathf.Sin(angle) * radius, 0, Mathf.Cos(angle) * radius);
                for (int k = 0; k < 3; k++)
                {
                    float s = R(rng, 0.7f, 1.3f);
                    Build.Sphere(dressing, c + new Vector3(R(rng, -0.5f, 0.5f), s * 0.35f, R(rng, -0.5f, 0.5f)), s, greens[rng.Next(greens.Length)] * 0.95f);
                }
            }
            // Supply drops from the crash: stacked crates with dark straps, oil barrels with amber bands.
            var drops = new[] { new Vector3(-19, 0, 14), new Vector3(18, 0, -17), new Vector3(-16, 0, -19), new Vector3(19, 0, 12), new Vector3(3, 0, -24) };
            foreach (var d in drops)
            {
                int stack = rng.Next(2, 4);
                for (int k = 0; k < stack; k++)
                {
                    var pos = d + new Vector3(k == 2 ? 0.45f : k * 1.05f, k == 2 ? 1.5f : 0.5f, 0);
                    var box = Build.Cube(dressing, pos, Vector3.one, crate);
                    box.transform.localRotation = Quaternion.Euler(0, R(rng, -12, 12), 0);
                    Build.Cube(box.transform, new Vector3(0, 0, 0), new Vector3(1.02f, 0.12f, 1.02f), strap);
                    Build.Cube(box.transform, new Vector3(0, 0, 0.51f), new Vector3(0.36f, 0.2f, 0.01f), Palette.Amber, 0.4f);
                }
                for (int k = 0; k < 2; k++)
                {
                    var bp = d + new Vector3(R(rng, -1.8f, -1.1f), 0.6f, R(rng, -0.8f, 0.8f) + k * 0.9f);
                    Build.Cylinder(dressing, bp, 0.7f, 1.2f, barrel);
                    Build.Cylinder(dressing, bp + Vector3.up * 0.3f, 0.72f, 0.08f, Palette.Amber);
                }
            }
            // Scattered hull fragments trailing from the dropship.
            for (int i = 0; i < 10; i++)
            {
                var p = new Vector3(7, 0.12f, -31) + new Vector3(R(rng, -9, 9), 0, R(rng, 2, 9));
                var plate = Build.Cube(dressing, p, new Vector3(R(rng, 0.5f, 1.6f), 0.08f, R(rng, 0.4f, 1.1f)), Palette.Hex(rng.Next(2) == 0 ? "4a4f56" : "2f3237"));
                plate.transform.localRotation = Quaternion.Euler(R(rng, -25, 25), R(rng, 0, 360), R(rng, -25, 25));
            }
            // Field lamps around the arena edge: dark poles with a warm emissive head (picked up by bloom).
            for (int i = 0; i < 8; i++)
            {
                float angle = Mathf.PI * 2f * i / 8f + Mathf.PI / 8f;
                var p = new Vector3(Mathf.Sin(angle) * 24.5f, 0, Mathf.Cos(angle) * 24.5f);
                Build.Cylinder(dressing, p + Vector3.up * 1.6f, 0.12f, 3.2f, Palette.Hex("24262a"));
                Build.Cube(dressing, p + Vector3.up * 3.25f, new Vector3(0.4f, 0.14f, 0.4f), Palette.Hex("24262a"));
                Build.Cube(dressing, p + Vector3.up * 3.15f, new Vector3(0.3f, 0.05f, 0.3f), Palette.Hex("ffd9a0"), 3f, false);
            }
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
            FoundryDetail(rng);
        }

        /// Industrial clutter: floor grating, hazard stripes, pipes, gantry cranes, crucibles and glowing vents.
        void FoundryDetail(System.Random rng)
        {
            Color steel = Palette.Hex("2e2724"), rust = Palette.Hex("6a3b22"), hazard = Palette.Hex("e0a32e"), lava = Palette.Hex("ff6a1f");
            // Floor plates with seams so the arena reads as a factory floor.
            for (int x = -20; x <= 20; x += 5)
                for (int z = -20; z <= 20; z += 5)
                    Build.Cube(dressing, new Vector3(x, 0.006f, z), new Vector3(4.85f, 0.01f, 4.85f), (x + z) % 10 == 0 ? Palette.Hex("2a211c") : Palette.Hex("251d18"), 0, false);
            // Hazard stripes along the arena edge.
            for (int i = -10; i <= 10; i++)
                foreach (float side in new[] { -22.6f, 22.6f })
                {
                    Build.Cube(dressing, new Vector3(i * 2f, 0.016f, side), new Vector3(0.9f, 0.03f, 0.5f), i % 2 == 0 ? hazard : Palette.Hex("1a1614"), 0, false);
                    Build.Cube(dressing, new Vector3(side, 0.016f, i * 2f), new Vector3(0.5f, 0.03f, 0.9f), i % 2 == 0 ? hazard : Palette.Hex("1a1614"), 0, false);
                }
            // Overhead pipes on supports behind the arena wall.
            foreach (float z in new[] { -26f, 26f })
            {
                var pipe = Build.Cylinder(dressing, new Vector3(0, 6.5f, z), 0.8f, 60f, rust);
                pipe.transform.localRotation = Quaternion.Euler(0, 0, 90);
                var pipe2 = Build.Cylinder(dressing, new Vector3(0, 7.6f, z + 1.2f), 0.45f, 60f, steel);
                pipe2.transform.localRotation = Quaternion.Euler(0, 0, 90);
                for (int i = -3; i <= 3; i++)
                {
                    Build.Cube(dressing, new Vector3(i * 9f, 3.4f, z), new Vector3(0.5f, 6.8f, 0.5f), steel);
                    Build.Cube(dressing, new Vector3(i * 9f + 2f, 6.5f, z), new Vector3(0.9f, 0.9f, 0.9f), Palette.Hex("4a3a30"));
                }
            }
            // Gantry crane spanning the arena.
            foreach (float x in new[] { -24f, 24f }) Build.Cube(dressing, new Vector3(x, 6.5f, 4f), new Vector3(0.8f, 13f, 0.8f), steel);
            Build.Cube(dressing, new Vector3(0, 13f, 4f), new Vector3(49f, 0.9f, 1.2f), hazard);
            Build.Cube(dressing, new Vector3(-6f, 12.2f, 4f), new Vector3(2f, 0.8f, 1.6f), steel);
            Build.Cube(dressing, new Vector3(-6f, 9.6f, 4f), new Vector3(0.06f, 4.4f, 0.06f), Palette.Hex("1a1614"));
            Build.Cube(dressing, new Vector3(-6f, 7.2f, 4f), new Vector3(1.4f, 0.6f, 1.4f), rust);
            // Crucibles of molten metal and glowing floor vents.
            foreach (var c in new[] { new Vector3(-27, 0, -12), new Vector3(27, 0, 14), new Vector3(-26, 0, 18) })
            {
                Build.Cylinder(dressing, c + Vector3.up * 1.2f, 3f, 2.4f, steel);
                Build.Cylinder(dressing, c + Vector3.up * 2.42f, 2.5f, 0.05f, lava, 3f);
                Build.Cube(dressing, c + new Vector3(0, 2.6f, 0), new Vector3(3.4f, 0.15f, 0.3f), rust);
            }
            for (int i = 0; i < 8; i++)
            {
                var p = new Vector3(R(rng, -20, 20), 0.02f, R(rng, -20, 20));
                if (GameSim.InCover(p + Vector3.up)) continue;
                Build.Cube(dressing, p, new Vector3(1.4f, 0.04f, 0.8f), Palette.Hex("15100d"), 0, false);
                for (int k = -2; k <= 2; k++) Build.Cube(dressing, p + new Vector3(k * 0.25f, 0.01f, 0), new Vector3(0.08f, 0.04f, 0.6f), lava, 2f, false);
            }
            // Stacked steel barrels and ingots.
            for (int i = 0; i < 6; i++)
            {
                float angle = Mathf.PI * 2f * i / 6f + 0.4f;
                var p = new Vector3(Mathf.Sin(angle) * 21.5f, 0, Mathf.Cos(angle) * 21.5f);
                Build.Cylinder(dressing, p + Vector3.up * 0.6f, 0.7f, 1.2f, i % 2 == 0 ? rust : Palette.Hex("4b4f56"));
                Build.Cylinder(dressing, p + new Vector3(0.8f, 0.6f, 0.2f), 0.7f, 1.2f, Palette.Hex("4b4f56"));
                Build.Cube(dressing, p + new Vector3(-0.9f, 0.2f, 0), new Vector3(0.9f, 0.4f, 0.5f), Palette.Hex("b07a3a"), 0.3f);
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
            RiftDetail(rng, crystal, rock);
        }

        /// Alien battlefield: crystal clusters, cracked glowing ground, floating rocks and imperial pylons.
        void RiftDetail(System.Random rng, Color crystal, Color rock)
        {
            Color glow = Palette.Hex("8a5cff"), imperial = Palette.Hex("3a1f2a"), red = Palette.Hex("ff3b5c");
            // Glowing cracks in the ground.
            for (int i = 0; i < 16; i++)
            {
                var p = new Vector3(R(rng, -21, 21), 0.018f, R(rng, -21, 21));
                var crack = Build.Cube(dressing, p, new Vector3(R(rng, 2f, 5f), 0.02f, 0.12f), glow, 2.5f, false);
                crack.transform.localRotation = Quaternion.Euler(0, R(rng, 0, 180), 0);
            }
            // Crystal clusters around the arena edge.
            for (int i = 0; i < 18; i++)
            {
                float angle = R(rng, 0, Mathf.PI * 2f), radius = R(rng, 19.5f, 25f);
                var c = new Vector3(Mathf.Sin(angle) * radius, 0, Mathf.Cos(angle) * radius);
                int n = rng.Next(3, 6);
                for (int k = 0; k < n; k++)
                {
                    float h = R(rng, 0.8f, 2.6f);
                    var shard = Build.Cube(dressing, c + new Vector3(R(rng, -0.6f, 0.6f), h * 0.4f, R(rng, -0.6f, 0.6f)), new Vector3(0.35f, h, 0.35f), Color.Lerp(crystal, glow, R(rng, 0, 1)), 1.6f, false);
                    shard.transform.localRotation = Quaternion.Euler(R(rng, -25, 25), R(rng, 0, 360), R(rng, -25, 25));
                }
                Build.Sphere(dressing, c + Vector3.up * 0.1f, 1.6f, rock).transform.localScale = new Vector3(1.8f, 0.5f, 1.6f);
            }
            // Floating rock islands with crystal undersides.
            for (int i = 0; i < 10; i++)
            {
                float angle = R(rng, 0, Mathf.PI * 2f), radius = R(rng, 30, 45);
                var p = new Vector3(Mathf.Sin(angle) * radius, R(rng, 8, 18), Mathf.Cos(angle) * radius);
                float s = R(rng, 2f, 5f);
                Build.Sphere(dressing, p, s, rock).transform.localScale = new Vector3(s * 1.4f, s * 0.6f, s * 1.2f);
                var spike = Build.Cube(dressing, p - Vector3.up * s * 0.45f, new Vector3(s * 0.3f, s * 0.8f, s * 0.3f), crystal, 1.8f, false);
                spike.transform.localRotation = Quaternion.Euler(0, 45, 180);
            }
            // Imperial pylons: dark obelisks with red emitters.
            for (int i = 0; i < 4; i++)
            {
                float angle = Mathf.PI * 0.5f * i + Mathf.PI * 0.25f;
                var p = new Vector3(Mathf.Sin(angle) * 23.5f, 0, Mathf.Cos(angle) * 23.5f);
                Build.Cube(dressing, p + Vector3.up * 3f, new Vector3(1.1f, 6f, 1.1f), imperial);
                Build.Cube(dressing, p + Vector3.up * 6.4f, new Vector3(0.7f, 0.8f, 0.7f), imperial).transform.localRotation = Quaternion.Euler(0, 45, 0);
                Build.Cube(dressing, p + Vector3.up * 4.8f, new Vector3(1.14f, 0.12f, 1.14f), red, 3f, false);
                Build.Cube(dressing, p + Vector3.up * 1.2f, new Vector3(1.14f, 0.08f, 1.14f), red, 2f, false);
            }
        }
    }
}
