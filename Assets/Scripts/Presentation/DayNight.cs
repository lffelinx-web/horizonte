using System.Collections.Generic;
using UnityEngine;

namespace Horizonte.Presentation
{
    /// <summary>
    /// Light through the day: the sun rises in the east at six, crosses the sky and sets in the west at seven, warm and
    /// low at dawn and dusk; at night a cool moonlight keeps the city readable while street lamps and windows light up.
    /// The daytime look set up by ProjectSetup is the reference; this only shifts it with the hour.
    /// </summary>
    public sealed class DayNight : MonoBehaviour
    {
        const float Sunrise = .25f, Sunset = .79f;

        Light sun;
        Camera cam;
        bool ready;
        Color sunColour, sky, equator, ground, background;
        float sunIntensity, shadowStrength;
        readonly List<Material> buildings = new List<Material>();
        Material lamps, glass;
        Color lampsBase, glassBase;

        /// <summary>Sun height from -1 (midnight) to 1 (noon), zero at sunrise and sunset.</summary>
        public static float SunHeight(float t)
        {
            if (t >= Sunrise && t <= Sunset) return Mathf.Sin(Mathf.PI * (t - Sunrise) / (Sunset - Sunrise));
            float night = t > Sunset ? t - Sunset : t + 1 - Sunset;
            return -Mathf.Sin(Mathf.PI * night / (1 - (Sunset - Sunrise)));
        }

        void Capture()
        {
            ready = true;
            sun = RenderSettings.sun;
            cam = Camera.main;
            if (sun != null)
            {
                sunColour = sun.color;
                sunIntensity = sun.intensity;
                shadowStrength = sun.shadowStrength;
            }
            sky = RenderSettings.ambientSkyColor;
            equator = RenderSettings.ambientEquatorColor;
            ground = RenderSettings.ambientGroundColor;
            background = cam != null ? cam.backgroundColor : RenderSettings.fogColor;

            var seen = new HashSet<Material>();
            foreach (var model in Resources.LoadAll<GameObject>("Buildings"))
            foreach (var r in model.GetComponentsInChildren<Renderer>())
                if (r.sharedMaterial != null && r.sharedMaterial.IsKeywordEnabled("_EMISSION") && seen.Add(r.sharedMaterial)) buildings.Add(r.sharedMaterial);
            foreach (var m in Resources.LoadAll<Material>("Fachadas"))
                if (seen.Add(m)) buildings.Add(m);

            lamps = Palette.Get("LampGlow");
            glass = Palette.Get("Glass");
            lampsBase = lamps.GetColor("_BaseColor");
            glassBase = glass.GetColor("_BaseColor");
            lamps.EnableKeyword("_EMISSION");
            glass.EnableKeyword("_EMISSION");
        }

        /// <summary>
        /// The camera keeps the sun beside the view so the sides we look at are lit; the hour sets how high it stands and
        /// swings it from one side (morning) to the other (evening).
        /// </summary>
        public void Apply(float t, CameraRig rig)
        {
            if (!ready) Capture();
            float h = SunHeight(t);
            if (h > -.06f)
            {
                rig.SunPitch = Mathf.Lerp(7, 52, Mathf.Max(0, h));
                rig.SunTurn = Mathf.Lerp(-55, 55, Mathf.InverseLerp(Sunrise, Sunset, t));
            }
            else
            {
                rig.SunPitch = 46;
                rig.SunTurn = 0;
            }
            Apply(t);
        }

        void Apply(float t)
        {
            float height = SunHeight(t);
            // 1 in full day, 0 at night, with a short twilight around sunrise and sunset.
            float day = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.18f, .22f, height));
            // Warm, low light near the horizon.
            float golden = (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.05f, .45f, height))) * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.2f, .02f, height));
            var warm = new Color(1, .6f, .36f);

            if (sun != null)
            {
                var dayColour = Color.Lerp(sunColour, warm, golden * .75f);
                sun.color = Color.Lerp(new Color(.55f, .66f, 1f), dayColour, day);
                sun.intensity = Mathf.Lerp(.32f, sunIntensity * Mathf.Lerp(.55f, 1, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .5f, height))), day);
                sun.shadowStrength = Mathf.Lerp(.35f, shadowStrength, day);
            }

            var tint = Color.Lerp(Color.white, new Color(1.08f, .9f, .8f), golden);
            RenderSettings.ambientSkyColor = Color.Lerp(new Color(.15f, .19f, .33f), sky * tint, day);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(.12f, .14f, .23f), equator * tint, day);
            RenderSettings.ambientGroundColor = Color.Lerp(new Color(.06f, .06f, .09f), ground * tint, day);

            var backdrop = Color.Lerp(Color.Lerp(background, new Color(.97f, .7f, .55f), golden * .8f), new Color(.05f, .08f, .16f), 1 - day);
            RenderSettings.fogColor = backdrop;
            if (cam != null) cam.backgroundColor = backdrop;

            // Lamps and windows: barely by day, bright at night.
            float glow = Mathf.Lerp(2.2f, .12f, day);
            foreach (var m in buildings) m.SetColor("_EmissionColor", new Color(glow, glow, glow));
            lamps.SetColor("_EmissionColor", new Color(1, .9f, .7f) * Mathf.Lerp(2.6f, 0, day));
            lamps.SetColor("_BaseColor", Color.Lerp(new Color(1, .95f, .8f), lampsBase, day));
            glass.SetColor("_EmissionColor", new Color(1, .78f, .45f) * Mathf.Lerp(.9f, 0, day));
            glass.SetColor("_BaseColor", glassBase);
        }

        void OnDestroy()
        {
            // In the editor these are the material assets: leave them as they were.
            if (!ready) return;
            foreach (var m in buildings) m.SetColor("_EmissionColor", new Color(1.4f, 1.4f, 1.4f));
            if (lamps != null) { lamps.SetColor("_EmissionColor", Color.black); lamps.SetColor("_BaseColor", lampsBase); lamps.DisableKeyword("_EMISSION"); }
            if (glass != null) { glass.SetColor("_EmissionColor", Color.black); glass.DisableKeyword("_EMISSION"); }
        }
    }
}
