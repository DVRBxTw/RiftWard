using System.Collections.Generic;
using UnityEngine;

namespace RiftWard.Client
{
    /// PvP: a red rim around an opponent. Every body part gets a slightly larger copy drawn with the back-face
    /// outline shader (Resources/RiftOutline.shader); parts added later (a weapon switch) are picked up on the next scan.
    public sealed class PvpOutline : MonoBehaviour
    {
        /// Rim thickness in metres, the same on thick and thin parts.
        const float Width = 0.03f;
        static Material material;
        readonly HashSet<MeshRenderer> done = new HashSet<MeshRenderer>();
        readonly List<GameObject> rims = new List<GameObject>();
        float scan;

        public static Material Material
        {
            get
            {
                if (material == null)
                {
                    var shader = Shader.Find("Hidden/RiftWard/Outline");
                    material = shader != null ? new Material(shader) : new Material(Shader.Find("Unlit/Color"));
                    material.color = new Color(1f, 0.18f, 0.15f);
                }
                return material;
            }
        }

        void OnEnable() { scan = 0; foreach (var r in rims) if (r != null) r.SetActive(true); }
        void OnDisable() { foreach (var r in rims) if (r != null) r.SetActive(false); }

        void Update()
        {
            if ((scan -= Time.deltaTime) > 0) return;
            scan = 0.5f;
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>())
            {
                if (done.Contains(renderer) || renderer.name == "Rim" || !renderer.enabled) continue;
                done.Add(renderer);
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                // Glowing, see-through effects (muzzle flashes, energy blades) get no rim.
                var shared = renderer.sharedMaterial;
                if (shared != null && shared.HasProperty("_Color") && shared.color.a < 0.95f) continue;
                Vector3 size = Vector3.Scale(filter.sharedMesh.bounds.size, renderer.transform.lossyScale);
                if (size.x < 0.01f || size.y < 0.01f || size.z < 0.01f) continue;
                var rim = new GameObject("Rim");
                rim.transform.SetParent(renderer.transform, false);
                rim.transform.localScale = new Vector3(1f + 2f * Width / size.x, 1f + 2f * Width / size.y, 1f + 2f * Width / size.z);
                rim.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var mr = rim.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Material;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                rims.Add(rim);
            }
        }
    }
}
