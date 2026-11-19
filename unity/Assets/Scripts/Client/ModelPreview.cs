using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RiftWard.Client
{
    /// Renders a slowly turning 3D model into a UI RawImage. Each preview owns an isolated stage far below the
    /// world on a private layer (lit by its own lights) and cleans everything up when the UI element is destroyed.
    public sealed class ModelPreview : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        bool draggable, dragging;
        float yawVelocity, idleFor = 99f, pitchOffset;
        public const int Layer = 31;
        static int counter;

        Transform stage, pivot;
        RenderTexture texture;
        Camera cam;
        float spin, renderTimer;
        const float RenderInterval = 1f / 30f;   // previews redraw at 30 fps instead of every frame

        /// draggable: the viewer can turn the model with the mouse (it keeps a little spin after a flick, then
        /// resumes the idle turn after a moment).
        public static RawImage Create(Transform parent, Action<Transform> build, int resolution = 256, float spinSpeed = 35f, float tilt = 12f, bool draggable = false)
        {
            var go = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<RawImage>();
            image.raycastTarget = draggable;
            var preview = go.AddComponent<ModelPreview>();
            preview.spin = spinSpeed;
            preview.draggable = draggable;

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
            foreach (var trail in model.GetComponentsInChildren<TrailRenderer>()) trail.Clear();   // no streak from the reframing move

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
            cam.enabled = false;           // rendered by hand in Update, only while the preview is on screen
            preview.cam = cam;
            preview.renderTimer = (index % 4) * RenderInterval / 4f;   // stagger previews across frames
            cam.Render();
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
            // Trails and particles have no size of their own (a fresh saber trail reports bounds at the world origin,
            // far from this stage), so only solid meshes decide the framing.
            var renderers = model.GetComponentsInChildren<Renderer>().Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float scale = size > 0 ? 1.6f / size : 1f;
            model.localScale = Vector3.one * scale;
            model.position -= (bounds.center - pivot.position) * scale;
            pivot.localRotation = Quaternion.Euler(tilt, 0, 0);
        }

        public void OnBeginDrag(PointerEventData e) { if (draggable) dragging = true; }

        public void OnDrag(PointerEventData e)
        {
            if (!draggable || pivot == null) return;
            float yaw = -e.delta.x * 0.5f;
            pivot.Rotate(0, yaw, 0, Space.World);
            float pitch = Mathf.Clamp(pitchOffset + e.delta.y * 0.3f, -35f, 35f);
            pivot.Rotate(pitch - pitchOffset, 0, 0, Space.Self);
            pitchOffset = pitch;
            yawVelocity = yaw / Mathf.Max(0.001f, Time.unscaledDeltaTime);
            idleFor = 0f;
            renderTimer = 0f;
        }

        public void OnEndDrag(PointerEventData e) { dragging = false; }

        void Update()
        {
            if (pivot != null && !dragging)
            {
                idleFor += Time.unscaledDeltaTime;
                yawVelocity *= Mathf.Exp(-4f * Time.unscaledDeltaTime);
                float idleSpin = idleFor > 2.5f ? spin * Mathf.Clamp01((idleFor - 2.5f) / 1.5f) : 0f;
                pivot.Rotate(0, (yawVelocity + idleSpin) * Time.unscaledDeltaTime, 0, Space.World);
            }
            renderTimer -= Time.unscaledDeltaTime;
            if (renderTimer > 0 || cam == null) return;
            renderTimer += RenderInterval;
            if (renderTimer < 0) renderTimer = RenderInterval;
            cam.Render();
        }

        void OnDestroy()
        {
            if (stage != null) Destroy(stage.gameObject);
            if (texture != null) { texture.Release(); Destroy(texture); }
        }
    }
}
