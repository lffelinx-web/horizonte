using System.Collections.Generic;
using UnityEngine;

namespace Horizonte.Presentation
{
    /// <summary>
    /// The sound of the town, made by the game itself: wind from far away; up close the hum of the city and birds by
    /// day, crickets at night; and where things happen, near the camera, axes, falling trees, hammers on the building
    /// sites and the beep of the pedestrian lights. A recording in Assets/Resources/Sonidos with the same name (viento,
    /// ciudad, grillos, pajaro_1…3, hacha, caida, martillo, pitido) replaces the generated sound.
    /// </summary>
    public sealed class Sound : MonoBehaviour
    {
        const int Rate = 44100;
        const string MuteKey = "Horizonte.Silencio";

        public static Sound Instance { get; private set; }

        /// <summary>Whether the player turned the sound off (remembered between games).</summary>
        public static bool Muted
        {
            get => PlayerPrefs.GetInt(MuteKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(MuteKey, value ? 1 : 0);
                AudioListener.volume = value ? 0 : 1;
            }
        }

        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly List<AudioSource> voices = new List<AudioSource>();
        AudioSource wind, hum, crickets;
        CameraRig rig;
        float timeOfDay = .3f, birdAt;
        uint random = 99991;

        void Awake()
        {
            Instance = this;
            AudioListener.volume = Muted ? 0 : 1;
            Make();
            wind = Loop("viento");
            hum = Loop("ciudad");
            crickets = Loop("grillos");
            for (int k = 0; k < 10; k++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0;
                voices.Add(s);
            }
        }

        public void Init(CameraRig r) => rig = r;

        AudioSource Loop(string name)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = clips[name];
            s.loop = true;
            s.volume = 0;
            s.spatialBlend = 0;
            s.Play();
            return s;
        }

        /// <summary>How close the camera is: 1 down among the streets, 0 high above the town.</summary>
        float Near => rig == null ? .5f : Mathf.Clamp01(Mathf.InverseLerp(420, 70, rig.Distance));

        public void Tick(float t, float speedFactor)
        {
            timeOfDay = t;
            float day = Mathf.Clamp01(Mathf.InverseLerp(-.1f, .25f, DayNight.SunHeight(t)));
            float near = Near;
            bool running = speedFactor > 0;
            Fade(wind, .11f + .12f * (1 - near));
            Fade(hum, running ? near * (.06f + .12f * day) : 0);
            Fade(crickets, running ? near * (1 - day) * .16f : 0);
            // Birds now and then on a fine morning or afternoon, when the camera is close.
            if (running && day > .5f && near > .3f && Time.time >= birdAt)
            {
                birdAt = Time.time + 1.5f + Next() % 100 / 25f;
                float morning = t < .5f ? 1 : .6f;
                Play("pajaro_" + (1 + Next() % 3), Vector3.zero, .22f * near * morning, false);
            }
        }

        void Fade(AudioSource s, float target) => s.volume = Mathf.MoveTowards(s.volume, target, Time.deltaTime * .25f);

        /// <summary>Plays a sound at a place in the town: louder the closer it is to where the camera looks, and the closer the camera.</summary>
        public static void At(string name, Vector3 where, float volume = 1)
        {
            if (Instance == null) return;
            Instance.Play(name, where, volume, true);
        }

        void Play(string name, Vector3 where, float volume, bool placed)
        {
            if (Muted || rig == null || !clips.TryGetValue(name, out var clip)) return;
            if (placed)
            {
                var d = where - rig.Focus;
                d.y = 0;
                volume *= Mathf.Clamp01(1 - d.magnitude / 110) * (.25f + .75f * Near);
            }
            if (volume < .02f) return;
            AudioSource free = null;
            foreach (var v in voices) if (!v.isPlaying) { free = v; break; }
            if (free == null) return;
            free.clip = clip;
            free.volume = volume;
            free.pitch = .92f + Next() % 100 / 600f;
            // Pan a little toward where it happens on screen.
            free.panStereo = 0;
            if (placed && rig.Cam != null)
            {
                var screen = rig.Cam.WorldToViewportPoint(where);
                free.panStereo = Mathf.Clamp((screen.x - .5f) * 1.2f, -.7f, .7f);
            }
            free.Play();
        }

        uint Next()
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            return random;
        }

        // ---------------------------------------------------------------- the sounds, made here

        void Make()
        {
            Add("viento", Wind());
            Add("ciudad", City());
            Add("grillos", Crickets());
            for (int k = 1; k <= 3; k++) Add("pajaro_" + k, Bird(k));
            Add("hacha", Axe());
            Add("caida", Fall());
            Add("martillo", Hammer());
            Add("pitido", Beep());
        }

        void Add(string name, float[] samples)
        {
            var recorded = Resources.Load<AudioClip>("Sonidos/" + name);
            if (recorded != null)
            {
                clips[name] = recorded;
                return;
            }
            var clip = AudioClip.Create(name, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            clips[name] = clip;
        }

        System.Random noise = new System.Random(7);
        float White() => (float)noise.NextDouble() * 2 - 1;

        /// <summary>Makes a loop seamless by blending its end into its start.</summary>
        static float[] Seamless(float[] s, float fadeSeconds)
        {
            int f = (int)(fadeSeconds * Rate);
            var result = new float[s.Length - f];
            for (int i = 0; i < result.Length; i++) result[i] = s[i];
            for (int i = 0; i < f; i++)
            {
                float t = i / (float)f;
                result[i] = s[i] * t + s[result.Length + i] * (1 - t);
            }
            return result;
        }

        float[] Wind()
        {
            var s = new float[Rate * 9];
            float low = 0, lower = 0;
            for (int i = 0; i < s.Length; i++)
            {
                low += (White() - low) * .02f;
                lower += (low - lower) * .05f;
                float gust = .55f + .45f * Mathf.Sin(i / (float)Rate * .7f) * Mathf.Sin(i / (float)Rate * .23f + 1);
                s[i] = lower * 4f * gust;
            }
            return Seamless(s, 1);
        }

        float[] City()
        {
            var s = new float[Rate * 9];
            float brown = 0, smooth = 0;
            for (int i = 0; i < s.Length; i++)
            {
                brown = Mathf.Clamp(brown + White() * .02f, -1, 1) * .998f;
                smooth += (brown - smooth) * .08f;
                float t = i / (float)Rate;
                // A low rumble of traffic far away, with a faint passing swell now and then.
                float swell = .6f + .4f * Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * .9f)), 6);
                s[i] = (smooth * 1.4f + Mathf.Sin(t * 2 * Mathf.PI * 92) * .015f) * swell;
            }
            return Seamless(s, 1);
        }

        float[] Crickets()
        {
            var s = new float[Rate * 4];
            foreach (var (pitch, offset) in new[] { (4400f, 0f), (4850f, .23f) })
            {
                for (float chirp = offset; chirp < 4; chirp += .52f + (float)noise.NextDouble() * .1f)
                for (int pulse = 0; pulse < 3; pulse++)
                {
                    int start = (int)((chirp + pulse * .045f) * Rate), length = (int)(.028f * Rate);
                    for (int i = 0; i < length && start + i < s.Length; i++)
                    {
                        float env = Mathf.Sin(Mathf.PI * i / length);
                        s[start + i] += Mathf.Sin(2 * Mathf.PI * pitch * (start + i) / Rate) * env * .25f;
                    }
                }
            }
            return s;
        }

        float[] Bird(int kind)
        {
            float length = .35f + kind * .15f;
            var s = new float[(int)(Rate * length)];
            float phase = 0;
            int notes = 2 + kind;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)Rate, note = t / length * notes, inNote = note - Mathf.Floor(note);
                float baseFreq = 2600 + kind * 450 + Mathf.Floor(note) % 2 * 500;
                float freq = baseFreq + 900 * Mathf.Sin(inNote * Mathf.PI) + 120 * Mathf.Sin(t * 2 * Mathf.PI * 32);
                phase += 2 * Mathf.PI * freq / Rate;
                float env = Mathf.Sin(Mathf.PI * inNote) * (inNote < .8f ? 1 : 0);
                s[i] = Mathf.Sin(phase) * env * .5f;
            }
            return s;
        }

        float[] Axe()
        {
            var s = new float[(int)(Rate * .35f)];
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)Rate;
                float crack = White() * Mathf.Exp(-t * 90);
                float thump = Mathf.Sin(2 * Mathf.PI * 115 * t) * Mathf.Exp(-t * 18);
                float wood = Mathf.Sin(2 * Mathf.PI * 420 * t) * Mathf.Exp(-t * 30);
                s[i] = (crack * .6f + thump * .7f + wood * .35f) * .8f;
            }
            return s;
        }

        float[] Fall()
        {
            var s = new float[(int)(Rate * 1.6f)];
            float phase = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)Rate;
                // The trunk creaking as it goes, then the crash on the ground.
                if (t < 1)
                {
                    float freq = Mathf.Lerp(95, 45, t);
                    phase += freq / Rate;
                    float saw = (phase - Mathf.Floor(phase)) * 2 - 1;
                    s[i] += (saw * .25f + White() * .08f) * Mathf.Sin(Mathf.PI * t) * .7f;
                }
                if (t >= 1)
                {
                    float u = t - 1;
                    s[i] += (Mathf.Sin(2 * Mathf.PI * 55 * u) * .9f + White() * .5f) * Mathf.Exp(-u * 7);
                }
            }
            return s;
        }

        float[] Hammer()
        {
            var s = new float[(int)(Rate * .25f)];
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)Rate;
                float ring = (Mathf.Sin(2 * Mathf.PI * 2150 * t) + .6f * Mathf.Sin(2 * Mathf.PI * 3370 * t)) * Mathf.Exp(-t * 28);
                float hit = White() * Mathf.Exp(-t * 200);
                s[i] = (ring * .35f + hit * .5f) * .8f;
            }
            return s;
        }

        float[] Beep()
        {
            var s = new float[(int)(Rate * .11f)];
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Min(1, t * 200) * Mathf.Min(1, (s.Length - i) / (float)Rate * 200);
                s[i] = Mathf.Sin(2 * Mathf.PI * 2350 * t) * env * .35f;
            }
            return s;
        }
    }
}
