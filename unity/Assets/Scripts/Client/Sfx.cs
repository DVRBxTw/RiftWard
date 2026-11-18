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
            v.volume = volume * GameSettings.Effects;
            v.Play();
        }

        /// Weapon fire sound name for a weapon at a rarity (傳奇+ adds a shimmer layer).
        public static string FireName(string weapon, int rarity) => "fire_" + weapon + (rarity >= 3 ? "_rare" : "");

        static AudioClip Make(string name)
        {
            if (name.StartsWith("fire_")) return Fire(name);
            switch (name)
            {
                case "shot": return Tone(name, 360, 0.05f, 0.1f, 0.25f);
                case "hit": return Tone(name, 1320, 0.035f, 0.07f, 0.05f);
                case "head": return Events(name, new[] { (0f, 0.03f, 1760f, 0.05f, 0.45f), (0.035f, 0.05f, 2640f, 0.05f, 0.4f) });
                case "explosion": return Tone(name, 90, 0.35f, 0.4f, 0.85f, 9f);
                case "reload_out": return Events(name, new[] { (0f, 0.05f, 520f, 0.7f, 0.5f), (0.06f, 0.12f, 180f, 0.85f, 0.35f) });
                case "reload_in": return Events(name, new[] { (0f, 0.035f, 1500f, 0.5f, 0.6f), (0.04f, 0.06f, 300f, 0.6f, 0.5f) });
                case "reload_rack": return Events(name, new[] { (0f, 0.05f, 900f, 0.75f, 0.5f), (0.12f, 0.04f, 1900f, 0.45f, 0.65f) });
                case "heartbeat": return Events(name, new[] { (0f, 0.09f, 55f, 0.1f, 0.9f), (0.22f, 0.08f, 50f, 0.1f, 0.7f) });
                default: return Tone(name, 440, 0.05f, 0.1f, 0f);
            }
        }

        // ------------------------------------------------------------ sci-fi weapons

        const int FireRate = 44100;

        /// Synthesised energy-weapon shots, one character per gun:
        /// P-9 laser zap, AR-27 pulse rifle, SG-8 plasma blast, LR-50 rail gun.
        static AudioClip Fire(string name)
        {
            bool rare = name.EndsWith("_rare");
            string weapon = name.Substring(5).Replace("_rare", "");
            float duration = weapon == "sniper" ? 0.75f : weapon == "shotgun" ? 0.38f : weapon == "rifle" ? 0.16f : 0.12f;
            int count = (int)(FireRate * duration);
            var data = new float[count];
            var rng = new System.Random(name.GetHashCode());
            float p1 = 0, p2 = 0, p3 = 0, p4 = 0, low = 0, band = 0, crackle = 0;
            float dt = 1f / FireRate;
            for (int i = 0; i < count; i++)
            {
                float t = i * dt;
                float noise = (float)rng.NextDouble() * 2 - 1;
                float v;
                switch (weapon)
                {
                    case "pistol":
                    {
                        // Laser zap: a fast downward sweep with a square-ish edge and a tiny noise click.
                        float f = 300f + 1900f * Mathf.Exp(-t * 38f);
                        p1 += 2 * Mathf.PI * f * dt;
                        float tone = Mathf.Sin(p1) + 0.35f * Mathf.Sin(p1 * 3f) + 0.15f * Mathf.Sin(p1 * 5f);
                        v = tone * Mathf.Exp(-t * 26f) * 0.5f + noise * Mathf.Exp(-t * 300f) * 0.35f;
                        break;
                    }
                    case "rifle":
                    {
                        // Pulse rifle: two detuned sweeps (chorus), FM bite and a low thump.
                        float f = 170f + 820f * Mathf.Exp(-t * 30f);
                        p1 += 2 * Mathf.PI * f * dt;
                        p2 += 2 * Mathf.PI * f * 1.013f * dt;
                        p3 += 2 * Mathf.PI * (70f + 60f * Mathf.Exp(-t * 25f)) * dt;
                        float fm = Mathf.Sin(p1 + 2.2f * Mathf.Sin(p1 * 2f) * Mathf.Exp(-t * 40f));
                        v = (fm + Mathf.Sin(p2) * 0.7f) * Mathf.Exp(-t * 22f) * 0.42f + Mathf.Sin(p3) * Mathf.Exp(-t * 18f) * 0.55f
                            + noise * Mathf.Exp(-t * 160f) * 0.3f;
                        break;
                    }
                    case "shotgun":
                    {
                        // Plasma blast: low-passed noise burst, a sub drop and electric crackle.
                        low += (noise - low) * Mathf.Lerp(0.5f, 0.06f, Mathf.Clamp01(t * 6f));
                        p1 += 2 * Mathf.PI * (38f + 170f * Mathf.Exp(-t * 14f)) * dt;
                        if (rng.NextDouble() < 0.004 * Mathf.Exp(-t * 6f)) crackle = 1f;
                        crackle *= 0.93f;
                        p2 += 2 * Mathf.PI * 2400f * dt;
                        v = low * Mathf.Exp(-t * 9f) * 0.9f + Mathf.Sin(p1) * Mathf.Exp(-t * 7f) * 0.75f + Mathf.Sin(p2) * crackle * 0.25f;
                        break;
                    }
                    default:
                    {
                        // Rail gun: a rising charge whine, then a deep boom and a long metallic ring with echoes.
                        const float charge = 0.05f;
                        if (t < charge)
                        {
                            float k = t / charge;
                            p1 += 2 * Mathf.PI * (2000f + 4500f * k * k) * dt;
                            v = Mathf.Sin(p1) * k * 0.28f;
                        }
                        else
                        {
                            float u = t - charge;
                            p2 += 2 * Mathf.PI * (32f + 110f * Mathf.Exp(-u * 9f)) * dt;
                            p3 += 2 * Mathf.PI * 1180f * dt;
                            p4 += 2 * Mathf.PI * 1777f * dt;
                            band += (noise - band) * 0.25f;
                            float ring = (Mathf.Sin(p3) + 0.6f * Mathf.Sin(p4)) * Mathf.Exp(-u * 6f);
                            float echo = 0.35f * Mathf.Exp(-u * 4f) * (Mathf.Sin(2 * Mathf.PI * 4f * u) > 0.6f ? 1f : 0f);
                            v = Mathf.Sin(p2) * Mathf.Exp(-u * 5f) * 0.85f + band * Mathf.Exp(-u * 30f) * 0.6f + ring * (0.18f + echo);
                        }
                        break;
                    }
                }
                if (rare)
                {
                    // 傳奇+: a bright shimmering overtone that rides on top of the shot.
                    float s = Mathf.Sin(2 * Mathf.PI * 3520f * t + 3f * Mathf.Sin(2 * Mathf.PI * 9f * t)) * Mathf.Exp(-t * 14f);
                    v += s * 0.12f;
                }
                float attack = Mathf.Min(1f, i / 30f);
                data[i] = Mathf.Clamp(v * attack, -1f, 1f) * 0.8f;
            }
            var clip = AudioClip.Create(name, count, 1, FireRate, false);
            clip.SetData(data, 0);
            return clip;
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
