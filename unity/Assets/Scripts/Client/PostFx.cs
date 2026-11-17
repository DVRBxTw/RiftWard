using UnityEngine;

namespace RiftWard.Client
{
    /// Camera post-processing: bloom on emissive parts, filmic tonemap, light grading and vignette.
    [RequireComponent(typeof(Camera))]
    public class PostFx : MonoBehaviour
    {
        public float Threshold = 1.1f, Intensity = 0.55f, Exposure = 1.25f, Saturation = 1.18f, Contrast = 1.16f, Vignette = 0.3f;
        public Color Tint = new Color(1.0f, 0.98f, 0.95f);
        Material mat;
        readonly RenderTexture[] chain = new RenderTexture[6];

        public static void Attach(Camera cam)
        {
            var shader = Shader.Find("Hidden/RiftWard/Post");
            if (shader == null || !shader.isSupported || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            cam.allowHDR = true;
            cam.gameObject.AddComponent<PostFx>().mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (mat == null) { Graphics.Blit(src, dst); return; }
            mat.SetFloat("_Threshold", Threshold);
            mat.SetFloat("_Intensity", Intensity);
            mat.SetFloat("_Exposure", Exposure);
            mat.SetFloat("_Saturation", Saturation);
            mat.SetFloat("_Contrast", Contrast);
            mat.SetFloat("_Vignette", Vignette);
            mat.SetVector("_Tint", Tint);
            int w = src.width / 2, h = src.height / 2, n = 0;
            var format = src.format;
            RenderTexture current = chain[0] = RenderTexture.GetTemporary(w, h, 0, format);
            Graphics.Blit(src, current, mat, 0);
            for (n = 1; n < chain.Length && w > 16 && h > 16; n++)
            {
                w /= 2; h /= 2;
                chain[n] = RenderTexture.GetTemporary(w, h, 0, format);
                Graphics.Blit(current, chain[n], mat, 1);
                current = chain[n];
            }
            for (int i = n - 2; i >= 0; i--)
            {
                Graphics.Blit(current, chain[i], mat, 2);
                current = chain[i];
            }
            mat.SetTexture("_Bloom", current);
            Graphics.Blit(src, dst, mat, 3);
            for (int i = 0; i < n; i++) { RenderTexture.ReleaseTemporary(chain[i]); chain[i] = null; }
        }

        void OnDestroy() { if (mat != null) Destroy(mat); }
    }
}
