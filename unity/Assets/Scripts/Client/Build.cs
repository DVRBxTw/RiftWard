using System.Collections.Generic;
using UnityEngine;

namespace RiftWard.Client
{
    /// Design tokens of the "field manual" look, shared by UI and world.
    public static class Palette
    {
        public static readonly Color Ink = Hex("101113");
        public static readonly Color Sheet = Hex("16171a");
        public static readonly Color Line = Hex("2e3136");
        public static readonly Color Bone = Hex("ebe6db");
        public static readonly Color Muted = Hex("8c8f93");
        public static readonly Color Amber = Hex("f0b24a");
        public static readonly Color Danger = Hex("e5534b");
        public static readonly Color Player = Hex("f0b24a");
        public static readonly Color Teammate = Hex("82aaff");

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }

    /// Primitive-based modelling helpers with a shared material cache.
    public static class Build
    {
        static readonly Dictionary<(Color, float, float), Material> cache = new Dictionary<(Color, float, float), Material>();
        static Shader standard;

        static readonly Dictionary<Color, Material> matte = new Dictionary<Color, Material>();

        /// Rough, non-reflective surface for floors and walls: grazing sunlight must not wash them out.
        public static Material Matte(Color color)
        {
            if (matte.TryGetValue(color, out var m)) return m;
            m = new Material(Mat(color));
            m.SetFloat("_Glossiness", 0.04f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_SpecularHighlights", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.SetFloat("_GlossyReflections", 0f);
            m.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            matte[color] = m;
            return m;
        }

        public static Material Mat(Color color, float glow = 0f, float alpha = 1f)
        {
            var key = (color, glow, alpha);
            if (cache.TryGetValue(key, out var m)) return m;
            if (standard == null) standard = Shader.Find("Standard") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Hidden/InternalErrorShader");
            m = new Material(standard) { color = new Color(color.r, color.g, color.b, alpha) };
            // Material classes by look: glowing parts are glassy, dark neutral parts read as gunmetal,
            // saturated parts as painted armour, the rest as matte fabric/ground.
            Color.RGBToHSV(color, out _, out float sat, out float val);
            float smooth = 0.18f, metal = 0f;
            if (glow > 0) smooth = 0.75f;
            else if (val < 0.3f && sat < 0.35f) { smooth = 0.5f; metal = 0.3f; }
            else if (sat > 0.35f) smooth = 0.38f;
            m.SetFloat("_Glossiness", smooth);
            m.SetFloat("_Metallic", metal);
            if (glow > 0)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * glow * 1.6f);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            if (alpha < 1f)
            {
                m.SetFloat("_Mode", 3);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_ALPHABLEND_ON");
                m.renderQueue = 3000;
            }
            cache[key] = m;
            return m;
        }

        public static GameObject Prim(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color, float glow = 0f, bool shadows = true)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = Mat(color, glow);
            if (!shadows) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        public static GameObject Cube(Transform parent, Vector3 position, Vector3 size, Color color, float glow = 0f, bool shadows = true) =>
            Prim(PrimitiveType.Cube, parent, position, size, color, glow, shadows);

        public static GameObject Sphere(Transform parent, Vector3 position, float diameter, Color color, float glow = 0f, bool shadows = true) =>
            Prim(PrimitiveType.Sphere, parent, position, Vector3.one * diameter, color, glow, shadows);

        public static GameObject Cylinder(Transform parent, Vector3 position, float diameter, float height, Color color, float glow = 0f) =>
            Prim(PrimitiveType.Cylinder, parent, position, new Vector3(diameter, height / 2f, diameter), color, glow);

        /// A cone built from a mesh (Unity has no cone primitive); used for trees.
        public static GameObject Cone(Transform parent, Vector3 position, float radius, float height, Color color)
        {
            var go = new GameObject("Cone");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.AddComponent<MeshFilter>().sharedMesh = ConeMesh();
            go.AddComponent<MeshRenderer>().sharedMaterial = Mat(color);
            go.transform.localScale = new Vector3(radius, height, radius);
            return go;
        }

        static Mesh cone;
        static Mesh ConeMesh()
        {
            if (cone != null) return cone;
            const int sides = 12;
            var verts = new List<Vector3> { new Vector3(0, 1, 0), Vector3.zero };
            var tris = new List<int>();
            for (int i = 0; i <= sides; i++)
            {
                float a = Mathf.PI * 2f * i / sides;
                verts.Add(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)));
            }
            for (int i = 0; i < sides; i++)
            {
                tris.AddRange(new[] { 0, i + 3, i + 2 });
                tris.AddRange(new[] { 1, i + 2, i + 3 });
            }
            cone = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
            cone.RecalculateNormals();
            return cone;
        }

        public static void NoShadows(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
