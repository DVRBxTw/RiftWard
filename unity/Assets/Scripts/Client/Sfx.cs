using System.Collections.Generic;
using UnityEngine;

namespace RiftWard.Client
{
    /// Procedurally synthesised sound effects (no audio files shipped).
    public sealed class Sfx
    {
        const int Rate = 22050;
        readonly AudioSource[] voices;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        int next;

        public Sfx(GameObject host, int polyphony = 8)
        {
            voices = new AudioSource[polyphony];
            for (int i = 0; i < polyphony; i++)
            {
                voices[i] = host.AddComponent<AudioSource>();
                voices[i].playOnAwake = false;
                voices[i].spatialBlend = 0;
            }
        }

        public void Play(string name, float volume = 1f)
        {
            if (!clips.TryGetValue(name, out var clip)) clips[name] = clip = Make(name);
            var v = voices[next];
            next = (next + 1) % voices.Length;
            v.clip = clip;
            v.volume = volume;
            v.Play();
        }

        static AudioClip Make(string name)
        {
            switch (name)
            {
                case "shot": return Tone(name, 360, 0.05f, 0.1f, 0.25f);
                case "hit": return Tone(name, 740, 0.05f, 0.12f, 0.15f);
                case "head": return Tone(name, 1100, 0.06f, 0.14f, 0.1f);
                case "explosion": return Tone(name, 90, 0.35f, 0.4f, 0.85f, 9f);
                case "reload_out": return Events(name, new[] { (0f, 0.05f, 520f, 0.7f, 0.5f), (0.06f, 0.12f, 180f, 0.85f, 0.35f) });
                case "reload_in": return Events(name, new[] { (0f, 0.035f, 1500f, 0.5f, 0.6f), (0.04f, 0.06f, 300f, 0.6f, 0.5f) });
                case "reload_rack": return Events(name, new[] { (0f, 0.05f, 900f, 0.75f, 0.5f), (0.12f, 0.04f, 1900f, 0.45f, 0.65f) });
                case "heartbeat": return Events(name, new[] { (0f, 0.09f, 55f, 0.1f, 0.9f), (0.22f, 0.08f, 50f, 0.1f, 0.7f) });
                default: return Tone(name, 440, 0.05f, 0.1f, 0f);
            }
        }

        /// Decaying sine with optional noise mix.
        static AudioClip Tone(string name, float freq, float duration, float volume, float noise, float decay = 0f)
        {
            int count = (int)(Rate * duration);
            var data = new float[count];
            var rng = new System.Random(name.GetHashCode());
            float smooth = 0;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / Rate;
                float env = decay > 0 ? Mathf.Exp(-t * decay) : 1f - (float)i / count;
                smooth = Mathf.Lerp(smooth, (float)rng.NextDouble() * 2 - 1, 0.3f);
                data[i] = (Mathf.Sin(2 * Mathf.PI * freq * t) * (1 - noise) + smooth * noise) * env * volume * 3f;
            }
            var clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// Mechanical clicks: each event is (start, length, tone Hz, noise mix, volume).
        static AudioClip Events(string name, (float start, float length, float freq, float noise, float volume)[] events)
        {
            float total = 0;
            foreach (var e in events) total = Mathf.Max(total, e.start + e.length);
            int count = (int)(Rate * (total + 0.02f));
            var data = new float[count];
            var rng = new System.Random(name.GetHashCode());
            foreach (var e in events)
            {
                int start = (int)(e.start * Rate), length = (int)(e.length * Rate);
                float smooth = 0;
                for (int i = 0; i < length && start + i < count; i++)
                {
                    float t = (float)i / Rate;
                    float env = Mathf.Exp(-t * 55f) * Mathf.Min(1f, i / 40f);
                    smooth = Mathf.Lerp(smooth, (float)rng.NextDouble() * 2 - 1, 0.35f);
                    data[start + i] += (Mathf.Sin(2 * Mathf.PI * e.freq * t) * (1 - e.noise) + smooth * e.noise) * env * e.volume;
                }
            }
            var clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// Slow looping ambient pad for the lobby: four chords, arpeggio, soft pulse.
        public static AudioClip LobbyMusic()
        {
            const int rate = 16000;
            const float bar = 4f;
            int[][] chords = { new[] { 57, 60, 64, 69 }, new[] { 53, 57, 60, 65 }, new[] { 48, 52, 55, 60 }, new[] { 55, 59, 62, 67 } };
            int perBar = (int)(rate * bar), length = perBar * chords.Length;
            var data = new float[length];
            for (int c = 0; c < chords.Length; c++)
                for (int i = 0; i < perBar; i++)
                {
                    float t = (float)i / rate;
                    float env = Mathf.Min(1f, t / 1.2f) * Mathf.Min(1f, (bar - t) / 1f);
                    float v = 0;
                    foreach (int n in chords[c])
                    {
                        float f = 440f * Mathf.Pow(2f, (n - 69) / 12f);
                        v += Mathf.Sin(2 * Mathf.PI * f * t) * 0.13f + Mathf.Sin(2 * Mathf.PI * f * 1.003f * t) * 0.09f;
                    }
                    int step = (int)(t / 0.5f);
                    float at = t - step * 0.5f;
                    float arp = 440f * Mathf.Pow(2f, (chords[c][step % 4] + 12 - 69) / 12f);
                    v += Mathf.Sin(2 * Mathf.PI * arp * at) * Mathf.Exp(-at * 5f) * 0.18f;
                    float bt = t % 1f;
                    v += Mathf.Sin(2 * Mathf.PI * 55f * bt) * Mathf.Exp(-bt * 7f) * 0.22f;
                    data[c * perBar + i] = v * env * 0.55f;
                }
            var clip = AudioClip.Create("lobby", length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
