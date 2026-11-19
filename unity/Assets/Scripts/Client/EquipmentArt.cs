using System.Collections.Generic;
using UnityEngine;

namespace RiftWard.Client
{
    /// Shared hard-surface geometry and finishes. Keeps animation pivots and gameplay bounds intact.
    public static class EquipmentArt
    {
        static readonly Dictionary<Vector3, Mesh> bevels = new Dictionary<Vector3, Mesh>();
        static readonly Dictionary<(Color, float, float), Material> coatings = new Dictionary<(Color, float, float), Material>();
        static readonly Dictionary<Material, Material> emissions = new Dictionary<Material, Material>();
        static Texture2D grain;

        public static Material Coat(Color color, float metallic, float smoothness)
        {
            var key = (color, metallic, smoothness);
            if (coatings.TryGetValue(key, out var material)) return material;
            if (grain == null)
            {
                grain = new Texture2D(64, 64, TextureFormat.RGBA32, true) { name = "Equipment satin grain", wrapMode = TextureWrapMode.Repeat };
                var pixels = new Color[64 * 64];
                var random = new System.Random(7301);
                for (int i = 0; i < pixels.Length; i++)
                {
                    float value = 0.92f + (float)random.NextDouble() * 0.08f;
                    pixels[i] = new Color(value, value, value, 1);
                }
                grain.SetPixels(pixels);
                grain.Apply(true, true);
            }
            material = new Material(Build.Mat(color)) { name = "Equipment satin", mainTexture = grain };
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);
            coatings[key] = material;
            return material;
        }

        public static void Bevel(Transform root)
        {
            // Retain saturated rarity colours in both HDR gameplay and the LDR inventory previews.
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                var source = renderer.sharedMaterial;
                if (source == null || !source.IsKeywordEnabled("_EMISSION") || source.color.a < 1) continue;
                Color.RGBToHSV(source.color, out _, out float saturation, out _);
                if (saturation < 0.25f) continue;
                if (!emissions.TryGetValue(source, out var finish))
                {
                    Color light = source.GetColor("_EmissionColor");
                    float peak = Mathf.Max(light.r, light.g, light.b);
                    if (peak <= 1.3f) continue;
                    finish = new Material(source) { name = "Chromatic energy" };
                    finish.SetColor("_EmissionColor", light * (1.3f / peak));
                    emissions[source] = finish;
                }
                renderer.sharedMaterial = finish;
            }
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || filter.sharedMesh.name != "Cube") continue;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || renderer.sharedMaterial == null || renderer.sharedMaterial.IsKeywordEnabled("_EMISSION") || renderer.sharedMaterial.color.a < 1) continue;
                Vector3 size = filter.transform.localScale;
                if (Mathf.Min(size.x, size.y, size.z) < 0.006f) continue;
                if (!bevels.TryGetValue(size, out var mesh)) bevels[size] = mesh = BeveledBox(size);
                filter.sharedMesh = mesh;
            }
        }

        // 6 faces, 12 chamfers and 8 corner triangles; vertices split for crisp machined normals.
        // Normalized coordinates preserve existing scales, child parts and animated magazine pivots.
        static Mesh BeveledBox(Vector3 size)
        {
            float cut = Mathf.Min(size.x, size.y, size.z) * 0.15f;
            Vector3 half = size * 0.5f, inset = half - Vector3.one * cut;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uv = new List<Vector2>();
            void Face(params Vector3[] points)
            {
                Vector3 center = Vector3.zero;
                foreach (var p in points) center += p;
                if (Vector3.Dot(Vector3.Cross(points[1] - points[0], points[2] - points[0]), center) < 0) System.Array.Reverse(points);
                int start = vertices.Count;
                for (int i = 0; i < points.Length; i++)
                {
                    vertices.Add(new Vector3(points[i].x / size.x, points[i].y / size.y, points[i].z / size.z));
                    uv.Add(new Vector2(i == 1 || i == 2 ? 1 : 0, i >= 2 ? 1 : 0));
                }
                for (int i = 1; i < points.Length - 1; i++) { triangles.Add(start); triangles.Add(start + i); triangles.Add(start + i + 1); }
            }
            Vector3 V(int axis, float a, float b, float c)
            {
                var p = Vector3.zero; p[axis] = a; p[(axis + 1) % 3] = b; p[(axis + 2) % 3] = c; return p;
            }
            for (int axis = 0; axis < 3; axis++)
            {
                int b = (axis + 1) % 3, c = (axis + 2) % 3;
                foreach (int sign in new[] { -1, 1 })
                    Face(V(axis, sign * half[axis], -inset[b], -inset[c]), V(axis, sign * half[axis], inset[b], -inset[c]),
                         V(axis, sign * half[axis], inset[b], inset[c]), V(axis, sign * half[axis], -inset[b], inset[c]));
                foreach (int sb in new[] { -1, 1 }) foreach (int sc in new[] { -1, 1 })
                    Face(V(axis, -inset[axis], sb * half[b], sc * inset[c]), V(axis, inset[axis], sb * half[b], sc * inset[c]),
                         V(axis, inset[axis], sb * inset[b], sc * half[c]), V(axis, -inset[axis], sb * inset[b], sc * half[c]));
            }
            foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
                Face(new Vector3(x * half.x, y * inset.y, z * inset.z), new Vector3(x * inset.x, y * half.y, z * inset.z), new Vector3(x * inset.x, y * inset.y, z * half.z));
            var mesh = new Mesh { name = "Equipment chamfer" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
            return mesh;
        }

        static GameObject Plate(Transform root, Vector3 at, Vector3 size, Color color, float metal = 0.35f)
        {
            var go = Build.Cube(root, at, size, color, 0, false);
            go.GetComponent<MeshRenderer>().sharedMaterial = Coat(color, metal, metal > 0.6f ? 0.62f : 0.32f);
            return go;
        }

        public static void DressGear(Transform root, string slot, int rarity)
        {
            Color alloy = Palette.Hex(rarity >= 4 ? "bba5ca" : rarity == 3 ? "d6af62" : "647383"), rubber = Palette.Hex("171e27");
            Color accent = Gear.RarityColors[rarity];
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                if (!r.sharedMaterial.IsKeywordEnabled("_EMISSION")) r.sharedMaterial = Coat(r.sharedMaterial.color, 0.3f, 0.34f);
            void P(Vector3 at, Vector3 size, Color color, float metal = 0.35f) => Plate(root, at, size, color, metal);
            switch (slot)
            {
                case "armor":
                    // A split breastplate, raised collar, rear spine and articulated waist.
                    foreach (int side in new[] { -1, 1 })
                    {
                        var chest = Plate(root, new Vector3(side * 0.23f, 0.23f, 0.255f), new Vector3(0.27f, 0.32f, 0.09f), alloy, 0.65f);
                        chest.transform.localRotation = Quaternion.Euler(0, side * 12, side * -8);
                        P(new Vector3(side * 0.3f, 0.48f, 0), new Vector3(0.1f, 0.24f, 0.46f), rubber);
                        for (int i = 0; i < 3; i++) P(new Vector3(side * 0.46f, -0.14f + i * 0.14f, 0), new Vector3(0.07f, 0.075f, 0.35f), rubber);
                        for (int i = 0; i < 2; i++) P(new Vector3(side * 0.55f, 0.4f, -0.15f + i * 0.3f), new Vector3(0.18f, 0.045f, 0.05f), alloy, 0.75f);
                    }
                    P(new Vector3(0, 0, -0.23f), new Vector3(0.22f, 0.76f, 0.09f), rubber);
                    for (int i = 0; i < 5; i++) P(new Vector3(0, -0.28f + i * 0.14f, -0.29f), new Vector3(0.18f, 0.065f, 0.04f), alloy, 0.65f);
                    break;
                case "module":
                    // Optical housing, heat sinks and separate mounting feet.
                    foreach (int side in new[] { -1, 1 })
                    {
                        for (int i = 0; i < 6; i++) P(new Vector3(side * 0.2f, 0, -0.3f + i * 0.1f), new Vector3(0.055f, 0.23f, 0.026f), alloy, 0.75f);
                        P(new Vector3(side * 0.14f, -0.17f, 0), new Vector3(0.07f, 0.09f, 0.65f), rubber);
                    }
                    Ring(root, new Vector3(0, 0.02f, 0.575f), 0.135f, 0.012f, alloy, 0, false);
                    Ring(root, new Vector3(0, 0.02f, 0.583f), 0.108f, 0.006f, accent, rarity > 0 ? 1.5f : 0, false);
                    P(new Vector3(0, 0.25f, -0.14f), new Vector3(0.1f, 0.015f, 0.16f), alloy, 0.8f);
                    break;
                case "boots":
                    foreach (float x in new[] { -0.26f, 0.26f })
                    {
                        P(new Vector3(x, -0.16f, 0.32f), new Vector3(0.3f, 0.15f, 0.2f), alloy, 0.6f);
                        for (int i = 0; i < 3; i++)
                        {
                            P(new Vector3(x, 0.02f + i * 0.11f, 0.14f), new Vector3(0.28f, 0.045f, 0.06f), rubber);
                            P(new Vector3(x + 0.075f, 0.02f + i * 0.11f, 0.18f), new Vector3(0.06f, 0.055f, 0.02f), alloy, 0.8f);
                        }
                        for (int i = 0; i < 5; i++) P(new Vector3(x, -0.34f, -0.13f + i * 0.115f), new Vector3(0.32f, 0.035f, 0.055f), rubber, 0);
                        P(new Vector3(x, 0.12f, -0.245f), new Vector3(0.12f, 0.48f, 0.045f), alloy, 0.65f);
                    }
                    break;
                default:
                    // Board traces, ceramic components, edge connectors and processor heat spreader.
                    foreach (int side in new[] { -1, 1 })
                        for (int i = 0; i < 5; i++)
                        {
                            float z = -0.3f + i * 0.15f;
                            P(new Vector3(side * 0.3f, 0.047f, z), new Vector3(0.22f, 0.008f, 0.012f), alloy, 0.75f);
                            P(new Vector3(side * 0.44f, 0.018f, z), new Vector3(0.08f, 0.055f, 0.04f), alloy, 0.8f);
                            P(new Vector3(side * 0.25f, 0.065f, z + 0.045f), new Vector3(0.065f, 0.04f, 0.04f), rubber);
                        }
                    foreach (float x in new[] { -0.38f, 0.38f }) foreach (float z in new[] { -0.38f, 0.38f })
                        P(new Vector3(x, 0.055f, z), new Vector3(0.065f, 0.02f, 0.065f), alloy, 0.8f);
                    break;
            }
            if (rarity >= 3) Prestige(root, slot, rarity);
            Bevel(root);
            Build.NoShadows(root.gameObject);
        }

        // One mesh per ring, with intentional gaps: no particle systems or realtime lights per item.
        static GameObject Ring(Transform root, Vector3 at, float radius, float width, Color color, float emission, bool gaps)
        {
            var vertices = new List<Vector3>(); var indices = new List<int>();
            const int segments = 64;
            for (int i = 0; i < segments; i++)
            {
                if (gaps && i % 16 >= 12) continue;
                float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
                int n = vertices.Count;
                foreach (var p in new[] { new Vector2(a, radius - width), new Vector2(a, radius), new Vector2(b, radius), new Vector2(b, radius - width) })
                    vertices.Add(new Vector3(Mathf.Cos(p.x) * p.y, Mathf.Sin(p.x) * p.y, 0));
                // Separate back vertices keep opposite normals from cancelling during recalculation.
                for (int j = 0; j < 4; j++) vertices.Add(vertices[n + j]);
                indices.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3, n + 6, n + 5, n + 4, n + 7, n + 6, n + 4 });
            }
            var mesh = new Mesh { name = "Segmented energy halo" };
            mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject("EnergyHalo", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root, false); go.transform.localPosition = at;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = emission > 0 ? Build.Mat(color, emission) : Coat(color, 0.7f, 0.6f);
            go.AddComponent<EquipmentMeshOwner>().Mesh = mesh;
            return go;
        }

        static void Prestige(Transform root, string slot, int rarity)
        {
            bool myth = rarity >= 4;
            Color color = Gear.RarityColors[rarity], metal = Palette.Hex(myth ? "b35e72" : "e2bd70");
            Vector3 center = slot == "module" ? new Vector3(0, 0.02f, 0.4f) : slot == "chip" ? new Vector3(0, 0.18f, 0) : new Vector3(0, 0.1f, -0.27f);
            float radius = slot == "module" ? 0.26f : slot == "chip" ? 0.32f : 0.64f;
            for (int layer = 0; layer < (myth ? 2 : 1); layer++)
            {
                var halo = Ring(root, center, radius + layer * 0.1f, 0.012f, color, myth ? 2.5f : 1.5f, true);
                if (slot == "chip") halo.transform.localRotation = Quaternion.Euler(90, 0, 0);
                var spin = halo.AddComponent<Spinner>(); spin.Speed = layer == 0 ? 22 : -34;
            }
            foreach (int side in new[] { -1, 1 })
            {
                var wing = Plate(root, center + new Vector3(side * radius, 0, 0), new Vector3(0.09f, 0.28f, 0.08f), metal, 0.8f);
                wing.name = "FloatingCrest";
                wing.transform.localRotation = Quaternion.Euler(0, 0, side * -24);
                wing.AddComponent<Spinner>().Pulse = myth ? 0.12f : 0.05f;
                Build.Cube(wing.transform, new Vector3(0, 0, 0.54f), new Vector3(0.18f, 0.7f, 0.12f), color, 2.5f, false);
            }
        }

        public static void DressWeapon(Transform root, string weapon, int rarity, float muzzle)
        {
            if (weapon == "saber")
            {
                var hilt = root.Find("Saber");
                if (hilt != null)
                    for (int i = 0; i < 3; i++)
                    {
                        float angle = i * Mathf.PI * 2 / 3;
                        var fin = Plate(hilt, new Vector3(Mathf.Cos(angle) * 0.028f, Mathf.Sin(angle) * 0.028f, 0.105f),
                            new Vector3(0.012f, 0.012f, 0.07f), Palette.Hex(rarity >= 3 ? "d6af62" : "647383"), 0.75f);
                        fin.transform.localRotation = Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg);
                    }
                if (hilt != null && rarity >= 3)
                    for (int i = 0; i < (rarity >= 4 ? 2 : 1); i++)
                    {
                        var ring = Ring(hilt, new Vector3(0, 0, 0.14f + i * 0.04f), 0.085f + i * 0.02f, 0.005f, Gear.RarityColors[rarity], 2, true);
                        ring.AddComponent<Spinner>().Speed = i == 0 ? 45 : -60;
                    }
            }
            else if (rarity >= 3)
            {
                // Collars stay around the bore and below the sight line, including on pistols.
                for (int i = 0; i < (rarity >= 4 ? 2 : 1); i++)
                {
                    var ring = Ring(root, new Vector3(0, 0.005f, muzzle - 0.06f - i * 0.04f), 0.048f + i * 0.008f, 0.003f, Gear.RarityColors[rarity], 2, true);
                    ring.AddComponent<Spinner>().Speed = i == 0 ? 40 : -55;
                }
            }
            Bevel(root);
        }
    }

    /// Only per-instance halo meshes are owned here; shared bevel meshes/materials remain cached.
    public sealed class EquipmentMeshOwner : MonoBehaviour
    {
        public Mesh Mesh;
        void OnDestroy()
        {
            if (Mesh == null) return;
            if (Application.isPlaying) Destroy(Mesh); else DestroyImmediate(Mesh);
        }
    }
}
