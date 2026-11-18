using UnityEngine;

namespace RiftWard.Client
{
    /// Tiny animator for weapon finishes: spins around the local Z axis and/or pulses scale.
    public sealed class Spinner : MonoBehaviour
    {
        public float Speed, Pulse, Blink;
        /// Unsteady light: brief random dropouts (a worn 劣質 blade). 0 = off, higher = more dropouts.
        public float Flicker;
        float seed;
        public Vector3 Axis = Vector3.forward;
        Renderer blinkRenderer;
        Vector3 baseScale;

        void Awake() { baseScale = transform.localScale; blinkRenderer = GetComponent<Renderer>(); seed = Random.value * 100f; }

        void Update()
        {
            if (Speed != 0) transform.Rotate(Axis * Speed * Time.deltaTime, Space.Self);
            if (Blink > 0 && blinkRenderer != null) blinkRenderer.enabled = Mathf.Repeat(Time.time * Blink, 1f) < 0.5f;
            if (Flicker > 0 && blinkRenderer != null) blinkRenderer.enabled = Mathf.PerlinNoise(seed, Time.time * 9f) > Flicker;
            if (Pulse > 0) transform.localScale = baseScale * (1f + Pulse * Mathf.Sin(Time.time * 6f));
        }
    }
}
