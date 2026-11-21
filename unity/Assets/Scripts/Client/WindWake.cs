using RiftWard.Sim;
using UnityEngine;

namespace RiftWard.Client
{
    /// 風之尾流 view: streaks drift along the path and the whole trail fades out over Defs.WakeLife.
    public sealed class WindWake : MonoBehaviour
    {
        float age;
        Renderer[] parts;
        Color[] bases;

        void Awake()
        {
            parts = GetComponentsInChildren<Renderer>();
            bases = new Color[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i].material = new Material(parts[i].sharedMaterial);   // own copy: each wake fades on its own
                bases[i] = parts[i].material.color;
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            float fade = Mathf.Clamp01(1f - age / Defs.WakeLife);
            for (int i = 0; i < parts.Length; i++)
            {
                var t = parts[i].transform;
                if (t.localScale.y > 0.02f)   // the streaks (not the floor) blow along the path
                {
                    var p = t.localPosition;
                    p.z = Mathf.Repeat(p.z + Time.deltaTime * 2.2f, Defs.DashDistance);
                    p.x += Mathf.Sin(age * 3f + i) * Time.deltaTime * 0.3f;
                    t.localPosition = p;
                }
                var c = bases[i];
                parts[i].material.color = new Color(c.r, c.g, c.b, c.a * fade);
            }
        }

        void OnDestroy() { if (parts != null) foreach (var r in parts) if (r != null) Destroy(r.material); }
    }
}
