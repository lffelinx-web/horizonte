using System.Collections.Generic;
using UnityEngine;

namespace Horizonte.Presentation
{
    /// <summary>
    /// The sound of the town, made by the game itself so nothing repeats in an obvious loop:
    /// - Ambience: wind from far away and, up close, the hum of the city (each from layers of different lengths that
    ///   drift against each other), birds now and then by day (eight kinds, at irregular moments) and crickets at night
    ///   (several, each coming and going on its own).
    /// - Effects where things happen near the camera: axes, falling trees, hammers, and the pedestrian beep at the
    ///   nearest crossing when the camera is right there.
    /// - Music: a calm piano over soft chords, composed as it plays, brighter by day and slower at night.
    /// Recordings replace the generated sounds: Assets/Resources/Sonidos (viento, ciudad, grillos, hacha, caida, martillo,
    /// pitido, pajaro_1…8) and any songs in Assets/Resources/Musica, played in random order instead of the piano.
    /// </summary>
    public sealed class Sound : MonoBehaviour
    {
        const int Rate = 44100;
        const string MusicKey = "Horizonte.SinMusica", EffectsKey = "Horizonte.SinEfectos";
        const float MusicVolume = .2f;

        public static Sound Instance { get; private set; }

        public static bool MusicMuted
        {
            get => PlayerPrefs.GetInt(MusicKey, 0) == 1;
            set => PlayerPrefs.SetInt(MusicKey, value ? 1 : 0);
        }

        /// <summary>Ambience and effects.</summary>
        public static bool EffectsMuted
        {
            get => PlayerPrefs.GetInt(EffectsKey, 0) == 1;
            set => PlayerPrefs.SetInt(EffectsKey, value ? 1 : 0);
        }

        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly List<AudioSource> voices = new List<AudioSource>(), notes = new List<AudioSource>();
        readonly List<(AudioSource source, float level, float speed, float phase)> beds = new List<(AudioSource, float, float, float)>();
        readonly List<(AudioSource source, float speed, float phase)> crickets = new List<(AudioSource, float, float)>();
        AudioSource song;
        AudioClip[] songs;
        CameraRig rig;
        float timeOfDay = .3f, birdAt, beepAt;
        uint random = 99991;
        readonly System.Random noise = new System.Random(7);

        void Awake()
        {
            Instance = this;
            Make();
            // Wind and city, each from two layers of different lengths.
            foreach (var (name, level) in new[] { ("viento", 1f), ("viento_2", .8f), ("ciudad", 1f), ("ciudad_2", .7f) })
                beds.Add((Loop(name), level, .05f + (Next() % 100) / 1500f, Next() % 100 / 16f));
            for (int k = 1; k <= 3; k++) crickets.Add((Loop("grillos_" + k), .07f + k * .031f, Next() % 100 / 16f));
            for (int k = 0; k < 10; k++) voices.Add(Voice());
            for (int k = 0; k < 10; k++) notes.Add(Voice());
            songs = Resources.LoadAll<AudioClip>("Musica");
            if (songs.Length > 0)
            {
                song = Voice();
                song.volume = MusicVolume * 1.6f;
            }
        }

        AudioSource Voice()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0;
            return s;
        }

        public void Init(CameraRig r) => rig = r;

        AudioSource Loop(string name)
        {
            var s = Voice();
            s.clip = clips[name];
            s.loop = true;
            s.volume = 0;
            s.time = (float)noise.NextDouble() * s.clip.length * .9f;
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
            bool running = speedFactor > 0, effects = !EffectsMuted;
            float clock = Time.time;
            for (int k = 0; k < beds.Count; k++)
            {
                var (s, level, speed, phase) = beds[k];
                // Each layer swells and fades slowly on its own, so the mix never comes round the same.
                float drift = .65f + .35f * Mathf.Sin(clock * speed + phase);
                float target = k < 2 ? (.07f + .08f * (1 - near)) : (running ? near * (.04f + .08f * day) : 0);
                Fade(s, effects ? target * level * drift : 0);
            }
            foreach (var (s, speed, phase) in crickets)
            {
                float chorus = Mathf.Clamp01(Mathf.Sin(clock * speed + phase) * 1.4f);
                Fade(s, effects && running ? near * (1 - day) * .11f * chorus : 0);
            }
            if (effects && running && day > .5f && near > .3f && clock >= birdAt)
            {
                // Mornings are busier; now and then two answer each other, sometimes there is a long quiet.
                bool morning = t < .45f;
                birdAt = clock + (morning ? 2.5f : 5f) + Next() % 1000 / (morning ? 110f : 70f);
                float v = .16f * near * (morning ? 1 : .7f);
                Play("pajaro_" + (1 + Next() % 8), v);
                if (Next() % 4 == 0) Play("pajaro_" + (1 + Next() % 8), v * .7f);
            }
            Music(day, running);
        }

        void Fade(AudioSource s, float target) => s.volume = Mathf.MoveTowards(s.volume, target, Time.deltaTime * .2f);

        /// <summary>Plays a sound at a place in the town: louder the closer it is to where the camera looks, and the closer the camera.</summary>
        public static void At(string name, Vector3 where, float volume = 1)
        {
            if (Instance == null || EffectsMuted || Instance.rig == null) return;
            var d = where - Instance.rig.Focus;
            d.y = 0;
            volume *= Mathf.Clamp01(1 - d.magnitude / 110) * (.25f + .75f * Instance.Near);
            Instance.Play(name, volume, where);
        }

        /// <summary>The pedestrian beep, only at the crossing nearest the camera and only when the camera is right there.</summary>
        public static void Beep(Vector3 crossing)
        {
            if (Instance == null || Instance.rig == null || Instance.rig.Distance > 140 || Time.time < Instance.beepAt) return;
            var d = crossing - Instance.rig.Focus;
            d.y = 0;
            if (d.magnitude > 30) return;
            Instance.beepAt = Time.time + 1.4f;
            At("pitido", crossing, .5f);
        }

        void Play(string name, float volume, Vector3? where = null)
        {
            if (volume < .02f || !clips.TryGetValue(name, out var clip)) return;
            AudioSource free = null;
            foreach (var v in voices) if (!v.isPlaying) { free = v; break; }
            if (free == null) return;
            free.clip = clip;
            free.volume = volume;
            free.pitch = .92f + Next() % 100 / 600f;
            free.panStereo = 0;
            if (where.HasValue && rig != null && rig.Cam != null)
                free.panStereo = Mathf.Clamp((rig.Cam.WorldToViewportPoint(where.Value).x - .5f) * 1.2f, -.7f, .7f);
            free.Play();
        }

        uint Next()
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            return random;
        }

        // ---------------------------------------------------------------- music

        static readonly int[] Scale = { 0, 2, 4, 5, 7, 9, 11 };
        /// <summary>Chords as scale degrees of their root: day I vi IV V with a few turns, night vi IV I V.</summary>
        static readonly int[][] DayChords = { new[] { 0, 5, 3, 4 }, new[] { 0, 3, 5, 4 }, new[] { 5, 3, 0, 4 }, new[] { 0, 2, 3, 4 } };
        static readonly int[][] NightChords = { new[] { 5, 3, 0, 4 }, new[] { 5, 1, 3, 4 }, new[] { 5, 3, 4, 0 } };
        double nextBeat;
        int beat, chordStep, melody = 9;
        int[] progression = DayChords[0];
        bool wasNight;

        /// <summary>Plays the music: songs from Resources/Musica if there are any, otherwise the piano composed here.</summary>
        void Music(float day, bool running)
        {
            if (song != null)
            {
                song.volume = MusicMuted ? 0 : MusicVolume * 1.6f;
                if (!song.isPlaying && !MusicMuted)
                {
                    song.clip = songs[Next() % (uint)songs.Length];
                    song.Play();
                }
                return;
            }
            if (MusicMuted || !running) return;
            double now = AudioSettings.dspTime;
            if (now < nextBeat - .05) return;
            if (nextBeat < now - 1) nextBeat = now + .1;
            bool night = day < .4f;
            float beatLength = night ? 1f : .72f;
            if (night != wasNight)
            {
                wasNight = night;
                chordStep = 0;
            }
            // A new chord every two bars, from a progression picked anew every so often.
            if (beat % 8 == 0)
            {
                if (chordStep % 4 == 0) progression = night ? NightChords[Next() % NightChords.Length] : DayChords[Next() % DayChords.Length];
                int root = progression[chordStep % 4];
                chordStep++;
                PlayChord(root, night, nextBeat, beatLength * 8);
            }
            // The melody wanders over the scale, preferring the notes of the chord, with rests in between.
            int chord = progression[(chordStep + 3) % 4];
            float chance = night ? .38f : .55f;
            if (Next() % 1000 < chance * 1000) PlayMelody(chord, night, nextBeat);
            if (!night && Next() % 1000 < 180) PlayMelody(chord, night, nextBeat + beatLength * .5f);
            beat++;
            nextBeat += beatLength;
        }

        static int Pitch(int degree, int octave) => 12 * octave + Scale[((degree % 7) + 7) % 7] + 12 * Mathf.FloorToInt(degree / 7f);

        void PlayChord(int root, bool night, double at, float length)
        {
            int octave = night ? 3 : 4;
            foreach (int step in new[] { 0, 2, 4 }) Schedule(Pad(Pitch(root + step, octave)), at, .09f);
            Schedule(Piano(Pitch(root, 3 - (night ? 1 : 0))), at, .32f);
        }

        void PlayMelody(int chord, bool night, double at)
        {
            // A step or two up or down, pulled toward a note of the chord, within two octaves.
            int move = (int)(Next() % 5) - 2;
            melody = Mathf.Clamp(melody + move, night ? 5 : 7, night ? 14 : 18);
            int degreeInChord = ((melody - chord) % 7 + 7) % 7;
            if (degreeInChord != 0 && degreeInChord != 2 && degreeInChord != 4 && Next() % 3 > 0) melody += degreeInChord == 1 || degreeInChord == 3 ? -1 : 1;
            Schedule(Piano(Pitch(melody, 4)), at, (night ? .3f : .38f) * (.7f + Next() % 100 / 330f));
        }

        void Schedule(AudioClip clip, double at, float volume)
        {
            AudioSource free = null;
            foreach (var v in notes) if (!v.isPlaying) { free = v; break; }
            if (free == null) return;
            free.clip = clip;
            free.volume = volume * MusicVolume / .2f * .5f;
            free.pitch = 1;
            free.PlayScheduled(at);
        }

        AudioClip Piano(int midi)
        {
            string key = "piano_" + midi;
            if (clips.TryGetValue(key, out var clip)) return clip;
            float f = 440f * Mathf.Pow(2, (midi - 69) / 12f);
            var s = new float[(int)(Rate * 2f)];
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)Rate;
                float attack = Mathf.Min(1, t * 180);
                float v = 0;
                float[] amp = { 1, .45f, .22f, .12f, .06f };
                for (int h = 0; h < amp.Length; h++)
                    v += amp[h] * Mathf.Sin(2 * Mathf.PI * f * (h + 1) * (1 + h * .0008f) * t) * Mathf.Exp(-t * (1.6f + h * 1.1f));
                s[i] = v * attack * .35f;
            }
            return clips[key] = Make(key, s);
        }

        AudioClip Pad(int midi)
        {
            string key = "pad_" + midi;
            if (clips.TryGetValue(key, out var clip)) return clip;
            float f = 440f * Mathf.Pow(2, (midi - 69) / 12f);
            var s = new float[(int)(Rate * 7f)];
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Clamp01(t / 1.2f) * Mathf.Clamp01((7f - t) / 2.5f);
                float v = Mathf.Sin(2 * Mathf.PI * f * t) + .35f * Mathf.Sin(2 * Mathf.PI * f * 2.003f * t) + .25f * Mathf.Sin(2 * Mathf.PI * f * .998f * t);
                s[i] = v * env * .22f;
            }
            return clips[key] = Make(key, s);
        }

        // ---------------------------------------------------------------- the sounds, made here

        void Make()
        {
            Add("viento", Wind(9.3f, 1));
            Add("viento_2", Wind(13.7f, 2));
            Add("ciudad", City(8.1f, 1));
            Add("ciudad_2", City(11.9f, 2));
            Add("grillos_1", Crickets(3.1f, 4400, 1));
            Add("grillos_2", Crickets(4.7f, 4850, 2));
            Add("grillos_3", Crickets(5.9f, 3950, 3));
            for (int k = 1; k <= 8; k++) Add("pajaro_" + k, Bird(k));
            Add("hacha", Axe());
            Add("caida", Fall());
            Add("martillo", Hammer());
            Add("pitido", Beep());
        }

        void Add(string name, float[] samples)
        {
            // Older recordings named grillos or pajaro_1 still count.
            var recorded = Resources.Load<AudioClip>("Sonidos/" + name) ?? Resources.Load<AudioClip>("Sonidos/" + name.Split('_')[0]);
            clips[name] = recorded != null ? recorded : Make(name, samples);
        }

        static AudioClip Make(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

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

        float[] Wind(float seconds, int seed)
        {
            var s = new float[(int)(Rate * (seconds + 1))];
            float low = 0, lower = 0;
            for (int i = 0; i < s.Length; i++)
            {
                low += (White() - low) * (.015f + seed * .006f);
                lower += (low - lower) * .05f;
                float t = i / (float)Rate;
                float gust = .55f + .45f * Mathf.Sin(t * (.6f + seed * .17f)) * Mathf.Sin(t * (.21f + seed * .05f) + seed);
                s[i] = lower * 4f * gust;
            }
            return Seamless(s, 1);
        }

        float[] City(float seconds, int seed)
        {
            var s = new float[(int)(Rate * (seconds + 1))];
            float brown = 0, smooth = 0;
            for (int i = 0; i < s.Length; i++)
            {
                brown = Mathf.Clamp(brown + White() * .02f, -1, 1) * .998f;
                smooth += (brown - smooth) * (.06f + seed * .02f);
                float t = i / (float)Rate;
                // Far-off traffic, with a passing swell now and then.
                float swell = .6f + .4f * Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * (.7f + seed * .23f) + seed)), 6);
                s[i] = (smooth * 1.4f + Mathf.Sin(t * 2 * Mathf.PI * (88 + seed * 9)) * .012f) * swell;
            }
            return Seamless(s, 1);
        }

        float[] Crickets(float seconds, float pitch, int seed)
        {
            var s = new float[(int)(Rate * seconds)];
            float gap = .45f + seed * .07f;
            for (float chirp = seed * .13f; chirp < seconds - .2f; chirp += gap + (float)noise.NextDouble() * .12f)
            for (int pulse = 0; pulse < 2 + seed % 2; pulse++)
            {
                int start = (int)((chirp + pulse * .045f) * Rate), length = (int)(.026f * Rate);
                for (int i = 0; i < length && start + i < s.Length; i++)
                {
                    float env = Mathf.Sin(Mathf.PI * i / length);
                    s[start + i] += Mathf.Sin(2 * Mathf.PI * pitch * (start + i) / Rate) * env * .22f;
                }
            }
            return s;
        }

        /// <summary>Eight birds: different pitch, number of notes, sweep and trill.</summary>
        float[] Bird(int kind)
        {
            var r = new System.Random(kind * 31);
            int notesCount = 2 + r.Next(5);
            float noteLength = .08f + (float)r.NextDouble() * .14f, gapLength = .03f + (float)r.NextDouble() * .09f;
            float baseFreq = 2200 + (float)r.NextDouble() * 2200, sweep = -900 + (float)r.NextDouble() * 1800, trill = (float)r.NextDouble() * 60;
            float length = notesCount * (noteLength + gapLength) + .05f;
            var s = new float[(int)(Rate * length)];
            float phase = 0;
            for (int n = 0; n < notesCount; n++)
            {
                float noteFreq = baseFreq * (1 + ((float)r.NextDouble() - .5f) * .25f);
                int start = (int)(n * (noteLength + gapLength) * Rate), len = (int)(noteLength * Rate);
                for (int i = 0; i < len && start + i < s.Length; i++)
                {
                    float u = i / (float)len, t = (start + i) / (float)Rate;
                    float freq = noteFreq + sweep * u + 160 * Mathf.Sin(t * 2 * Mathf.PI * trill);
                    phase += 2 * Mathf.PI * freq / Rate;
                    s[start + i] = Mathf.Sin(phase) * Mathf.Sin(Mathf.PI * u) * .45f;
                }
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
                    phase += Mathf.Lerp(95, 45, t) / Rate;
                    float saw = (phase - Mathf.Floor(phase)) * 2 - 1;
                    s[i] += (saw * .25f + White() * .08f) * Mathf.Sin(Mathf.PI * t) * .7f;
                }
                else
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
                s[i] = Mathf.Sin(2 * Mathf.PI * 2350 * t) * env * .3f;
            }
            return s;
        }
    }
}
