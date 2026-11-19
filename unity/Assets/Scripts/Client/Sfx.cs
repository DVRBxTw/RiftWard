using System.Collections.Generic;
using UnityEngine;

namespace RiftWard.Client
{
    /// Procedurally synthesised sound effects (no audio files shipped).
    public sealed class Sfx
    {
        const int Rate = 22050;
        /// Separate voice pools so a burst of rifle fire never cuts off hit confirmations, reloads or teammates:
        /// your own weapon (2D), feedback and UI (2D), and positioned world sounds (3D, others' shots and blasts).
        readonly Pool weapon, feedback, world;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        Transform listener;

        /// Each fire sound is synthesised in a few variants (different noise) that rotate, plus a small random
        /// pitch and volume change per shot, so automatic fire does not sound like one sample on repeat.
        public const int FireVariants = 3;
        const float WorldNear = 3f, WorldFar = 70f;

        sealed class Pool
        {
            public readonly AudioSource[] Voices;
            readonly float[] started;
            readonly AudioLowPassFilter[] filters;

            public Pool(GameObject host, int count, bool spatial)
            {
                Voices = new AudioSource[count];
                started = new float[count];
                if (spatial) filters = new AudioLowPassFilter[count];
                for (int i = 0; i < count; i++)
                {
                    var go = spatial ? new GameObject("WorldVoice") : host;
                    if (spatial) go.transform.SetParent(host.transform, false);
                    var v = go.AddComponent<AudioSource>();
                    v.playOnAwake = false;
                    v.spatialBlend = spatial ? 1f : 0f;
                    if (spatial)
                    {
                        v.rolloffMode = AudioRolloffMode.Logarithmic;
                        v.minDistance = WorldNear;
                        v.maxDistance = WorldFar;
                        v.dopplerLevel = 0f;
                        v.spread = 30f;
                        filters[i] = go.AddComponent<AudioLowPassFilter>();
                    }
                    Voices[i] = v;
                }
            }

            /// A free voice, or else the one that started longest ago (it is the closest to finishing).
            public int Take()
            {
                int pick = 0;
                for (int i = 0; i < Voices.Length; i++)
                {
                    if (!Voices[i].isPlaying) { pick = i; break; }
                    if (started[i] < started[pick]) pick = i;
                }
                started[pick] = Time.unscaledTime;
                return pick;
            }

            public AudioLowPassFilter Filter(int i) => filters?[i];
        }

        public Sfx(GameObject host, int polyphony = 8)
        {
            weapon = new Pool(host, 10, false);
            feedback = new Pool(host, polyphony, false);
            world = new Pool(host, 16, true);
        }

        AudioClip Clip(string name)
        {
            if (!clips.TryGetValue(name, out var clip)) clips[name] = clip = Make(name);
            return clip;
        }

        /// 2D sound. Fire sounds go to the weapon pool with per-shot variation; everything else to feedback.
        public void Play(string name, float volume = 1f)
        {
            bool fire = name.StartsWith("fire_");
            var pool = fire ? weapon : feedback;
            var v = pool.Voices[pool.Take()];
            v.clip = Clip(fire ? Variant(name) : name);
            v.pitch = fire ? Jitter(name) : 1f;
            v.volume = volume * (fire ? Random.Range(0.92f, 1f) : 1f) * GameSettings.Effects;
            v.Play();
        }

        /// 3D sound at a world position: louder and brighter close by; far away it fades and loses its highs,
        /// so a teammate's shot across the arena reads as a distant, muffled report from that direction.
        public void PlayAt(string name, Vector3 position, float volume = 1f)
        {
            bool fire = name.StartsWith("fire_");
            int i = world.Take();
            var v = world.Voices[i];
            v.transform.position = position;
            v.clip = Clip(fire ? Variant(name) : name);
            v.pitch = fire ? Jitter(name) : Random.Range(0.96f, 1.04f);
            v.volume = volume * (fire ? Random.Range(0.92f, 1f) : 1f) * GameSettings.Effects;
            var filter = world.Filter(i);
            if (filter != null)
            {
                if (listener == null) listener = Object.FindAnyObjectByType<AudioListener>()?.transform;
                float distance = listener != null ? Vector3.Distance(listener.position, position) : 0f;
                filter.cutoffFrequency = Mathf.Lerp(22000f, 1800f, Mathf.Clamp01((distance - WorldNear) / 45f));
            }
            v.Play();
        }

        static string Variant(string name) => name + "#" + Random.Range(0, FireVariants);

        /// Heavier guns vary less, so the rail gun keeps its weight.
        static float Jitter(string name) => 1f + Random.Range(-1f, 1f) * (name.Contains("sniper") ? 0.015f : name.Contains("shotgun") ? 0.025f : 0.04f);

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
                // Empty magazine: a dry trigger click and the hammer falling on nothing.
                case "dry": return Events(name, new[] { (0f, 0.02f, 2400f, 0.6f, 0.5f), (0.025f, 0.03f, 900f, 0.7f, 0.35f) });
                // Weapon swap: cloth rustle, then the new gun's latch and grip.
                case "switch": return Events(name, new[] { (0f, 0.08f, 200f, 0.95f, 0.22f), (0.09f, 0.03f, 1800f, 0.5f, 0.45f), (0.14f, 0.04f, 1100f, 0.6f, 0.4f) });
                // Low-ammo tick that rides on each shot near the end of the magazine.
                case "lowammo": return Events(name, new[] { (0f, 0.025f, 3000f, 0.1f, 0.2f) });
                // Kill confirmation: a bright tap falling to a soft thud.
                case "kill": return Events(name, new[] { (0f, 0.05f, 1320f, 0.05f, 0.35f), (0.045f, 0.11f, 520f, 0.15f, 0.45f) });
                case "vent": return Hiss(name, 0.75f);
                // Blade connecting: a hot sizzle with a low thump.
                case "saber_hit": return Events(name, new[] { (0f, 0.06f, 160f, 0.15f, 0.6f), (0.01f, 0.1f, 1200f, 0.35f, 0.3f) });
                // Shouldering the gun: cloth shift and a soft stock tap; lowering it is the shift alone, quieter.
                case "ads_in": return Events(name, new[] { (0f, 0.06f, 260f, 0.92f, 0.22f), (0.05f, 0.03f, 700f, 0.55f, 0.3f) });
                case "ads_out": return Events(name, new[] { (0f, 0.06f, 220f, 0.95f, 0.16f) });
                // Running out of held breath: a short exhale.
                case "breath_out": return Hiss(name, 0.35f);
                // Map pings: a bright two-note chime; danger is lower and doubled.
                case "ping": return Events(name, new[] { (0f, 0.06f, 1480f, 0.02f, 0.35f), (0.07f, 0.09f, 1970f, 0.02f, 0.3f) });
                case "ping_danger": return Events(name, new[] { (0f, 0.07f, 740f, 0.05f, 0.45f), (0.1f, 0.07f, 740f, 0.05f, 0.45f) });
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
            string baseName = name.Split('#')[0];   // "#n" picks a variant: same sound, different noise
            bool rare = baseName.EndsWith("_rare");
            string weapon = baseName.Substring(5).Replace("_rare", "");
            float duration = weapon == "sniper" ? 0.75f : weapon == "shotgun" ? 0.38f : weapon == "rifle" ? 0.2f : weapon == "saber" ? 0.34f : weapon == "minigun" ? 0.09f : 0.15f;
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
                        // Body: a short low punch, plus the slide's metallic clack a moment later.
                        p3 += 2 * Mathf.PI * (55f + 110f * Mathf.Exp(-t * 45f)) * dt;
                        v += Mathf.Sin(p3) * Mathf.Exp(-t * 32f) * 0.45f;
                        if (t > 0.022f) { p4 += 2 * Mathf.PI * 2350f * dt; v += (Mathf.Sin(p4) * 0.5f + noise * 0.5f) * Mathf.Exp(-(t - 0.022f) * 260f) * 0.3f; }
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
                        // Weight: a sub-bass kick under the pulse and the bolt cycling right after.
                        p4 += 2 * Mathf.PI * (42f + 70f * Mathf.Exp(-t * 35f)) * dt;
                        v += Mathf.Sin(p4) * Mathf.Exp(-t * 20f) * 0.5f;
                        if (t > 0.014f) { band += (noise - band) * 0.55f; v += (noise - band) * Mathf.Exp(-(t - 0.014f) * 220f) * 0.35f; }
                        break;
                    }
                    case "saber":
                    {
                        // Energy blade swing: a smooth, rounded hum (pure sines, no buzz) whose pitch rises and
                        // falls with the sweep like a passing tone, a soft air whoosh filtered twice, and a faint
                        // glassy shimmer on top.
                        float k = Mathf.Clamp01(t / 0.3f);
                        float swell = Mathf.Sin(Mathf.PI * k);
                        swell *= swell;                                           // smooth in and out
                        float hum = 140f * (1f + 0.22f * Mathf.Sin(Mathf.PI * k)); // gentle Doppler bend
                        p1 += 2 * Mathf.PI * hum * dt;
                        p2 += 2 * Mathf.PI * hum * 1.5f * dt;                      // a fifth above: warm, not buzzy
                        p3 += 2 * Mathf.PI * (880f + 260f * swell) * dt;
                        low += (noise - low) * Mathf.Lerp(0.04f, 0.16f, swell);   // airy whoosh, two-pole low-pass
                        band += (low - band) * 0.3f;
                        float tone = Mathf.Sin(p1) * 0.55f + Mathf.Sin(p2) * 0.2f;
                        v = tone * (0.25f + 0.75f * swell) + band * swell * 1.6f + Mathf.Sin(p3) * swell * 0.06f;
                        v *= Mathf.Min(1f, (0.34f - t) / 0.06f);                  // soft tail, no click
                        break;
                    }
                    case "minigun":
                    {
                        // Rotary round: a tight noise crack over a short low thump, quick enough to stack into a roar.
                        low += (noise - low) * 0.35f;
                        p1 += 2 * Mathf.PI * (60f + 140f * Mathf.Exp(-t * 60f)) * dt;
                        p2 += 2 * Mathf.PI * 1800f * dt;
                        v = low * Mathf.Exp(-t * 45f) * 0.8f + Mathf.Sin(p1) * Mathf.Exp(-t * 35f) * 0.6f + Mathf.Sin(p2) * Mathf.Exp(-t * 90f) * 0.12f;
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

        /// Steam vent: filtered noise that swells briefly and trails off.
        static AudioClip Hiss(string name, float duration)
        {
            int count = (int)(Rate * duration);
            var data = new float[count];
            var rng = new System.Random(name.GetHashCode());
            float low = 0, prev = 0;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / Rate;
                float n = (float)rng.NextDouble() * 2 - 1;
                low += (n - low) * 0.35f;
                float high = low - prev;   // keep the airy band, drop the rumble
                prev = low;
                float env = Mathf.Min(1f, t / 0.04f) * Mathf.Exp(-t * 4.5f);
                data[i] = (high * 1.6f + low * 0.25f) * env * 0.5f;
            }
            var clip = AudioClip.Create(name, count, 1, Rate, false);
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
