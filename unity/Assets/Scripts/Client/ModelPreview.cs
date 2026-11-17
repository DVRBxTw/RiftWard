using System;
using UnityEngine;
using UnityEngine.UI;

namespace RiftWard.Client
{
    /// Renders a slowly turning 3D model into a UI RawImage. Each preview owns an isolated stage far below the
    /// world on a private layer (lit by its own lights) and cleans everything up when the UI element is destroyed.
    public sealed class ModelPreview : MonoBehaviour
    {
        public const int Layer = 31;
        static int counter;

        Transform stage, pivot;
        RenderTexture texture;
        float spin;

        public static RawImage Create(Transform parent, Action<Transform> build, int resolution = 256, float spinSpeed = 35f, float tilt = 12f)
        {
            var go = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<RawImage>();
            image.raycastTarget = false;
            var preview = go.AddComponent<ModelPreview>();
            preview.spin = spinSpeed;

            int index = counter++ % 128;
            preview.stage = new GameObject("PreviewStage").transform;
            preview.stage.position = new Vector3(2000f + index * 30f, -800f, 0f);
            preview.pivot = new GameObject("Pivot").transform;
            preview.pivot.SetParent(preview.stage, false);
            var model = new GameObject("Model").transform;
            model.SetParent(preview.pivot, false);
            build(model);
            SetLayer(preview.stage.gameObject);
            preview.Frame(model, tilt);

            foreach (var (pos, intensity, color) in new[] { (new Vector3(-1.6f, 1.8f, -2.2f), 3.2f, new Color(1f, 0.96f, 0.9f)), (new Vector3(2f, 0.6f, -1f), 1.6f, new Color(0.7f, 0.8f, 1f)), (new Vector3(0, 1.2f, 2.2f), 2.2f, Color.white) })
            {
                var light = new GameObject("Light").AddComponent<Light>();
                light.transform.SetParent(preview.stage, false);
                light.transform.localPosition = pos;
                light.type = LightType.Point;
                light.range = 8f;
                light.intensity = intensity;
                light.color = color;
                light.cullingMask = 1 << Layer;
                light.renderMode = LightRenderMode.ForcePixel;
            }

            var cam = new GameObject("PreviewCamera").AddComponent<Camera>();
            cam.transform.SetParent(preview.stage, false);
            cam.transform.localPosition = new Vector3(0, 0.45f, -3.2f);
            cam.transform.LookAt(preview.stage.position);
            cam.fieldOfView = 30f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.cullingMask = 1 << Layer;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 12f;
            preview.texture = new RenderTexture(resolution, resolution, 16, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = preview.texture;
            image.texture = preview.texture;
            return image;
        }

        static void SetLayer(GameObject go)
        {
            go.layer = Layer;
            foreach (Transform child in go.transform) SetLayer(child.gameObject);
        }

        /// Centres the model on the pivot and scales it so its largest dimension is about 1.6 units.
        void Frame(Transform model, float tilt)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float scale = size > 0 ? 1.6f / size : 1f;
            model.localScale = Vector3.one * scale;
            model.position -= (bounds.center - pivot.position) * scale;
            pivot.localRotation = Quaternion.Euler(tilt, 0, 0);
        }

        void Update()
        {
            if (pivot != null) pivot.Rotate(0, spin * Time.unscaledDeltaTime, 0, Space.World);
        }

        void OnDestroy()
        {
            if (stage != null) Destroy(stage.gameObject);
            if (texture != null) { texture.Release(); Destroy(texture); }
        }
    }
}
