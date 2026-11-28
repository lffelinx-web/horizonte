using System.Collections.Generic;
using UnityEngine;

namespace Horizonte.Presentation
{
    /// <summary>
    /// The materials used by the procedural ground and the placeholder buildings.
    /// The editor setup saves them under Resources/Materials so the URP shaders are included in builds.
    /// </summary>
    public static class Palette
    {
        public struct Entry
        {
            public string Name;
            public Color Color;
            public float Smoothness, Metallic;
            public bool Transparent;

            public Entry(string name, string hex, float smoothness = .15f, float metallic = 0, bool transparent = false)
            {
                Name = name;
                ColorUtility.TryParseHtmlString(hex, out Color);
                Smoothness = smoothness;
                Metallic = metallic;
                Transparent = transparent;
            }
        }

        public static readonly Entry[] Entries =
        {
            new Entry("Grass", "#86AD5C", .05f),
            new Entry("ForestFloor", "#628744", .05f),
            new Entry("Bank", "#8C7556", .05f),
            new Entry("Water", "#3F7FA6", .92f),
            new Entry("Lakebed", "#A8916B", .04f),
            new Entry("Asphalt", "#3B3D42", .2f),
            new Entry("Sidewalk", "#C9C4B8", .1f),
            new Entry("LaneLine", "#E8E2C8", .2f),
            new Entry("ZoneResidential", "#8CCB6A", .05f),
            new Entry("ZoneCommercial", "#6FA6D8", .05f),
            new Entry("ZoneIndustrial", "#D9B85A", .05f),
            new Entry("Yard", "#86AE60", .05f),
            new Entry("Trunk", "#6B4A32", .05f),
            new Entry("Leaves", "#4C7A3A", .1f),
            new Entry("LeavesLight", "#6A9445", .1f),
            new Entry("Field", "#A6A861", .05f),
            new Entry("FieldRow", "#7B7F41", .05f),
            new Entry("StumpCut", "#D8B47C", .1f),
            new Entry("StreetLamp", "#4A4F57", .4f, .5f),
            new Entry("LampGlow", "#FFF1C9", .6f),
            new Entry("WallResidential", "#EDE3D1", .1f),
            new Entry("RoofResidential", "#B5553C", .2f),
            new Entry("WallCommercial", "#D7DADF", .3f),
            new Entry("WallIndustrial", "#C9B79A", .1f),
            new Entry("RoofIndustrial", "#6E747C", .35f, .4f),
            new Entry("RoofFlat", "#8E9095", .1f),
            new Entry("Glass", "#5D88A8", .9f, .2f),
            new Entry("Trim", "#F4F1EA", .2f),
            new Entry("Metal", "#9AA1A8", .5f, .7f),
            new Entry("AccentCommercial", "#D9534F", .3f),
            new Entry("WallPolice", "#4E6E9E", .2f),
            new Entry("WallSchool", "#C2703F", .1f),
            new Entry("PreviewOk", "#5BE37C", .5f, 0, true),
            new Entry("PreviewBad", "#F2554C", .5f, 0, true),
            new Entry("PreviewDemolish", "#F2A33A", .5f, 0, true),
            new Entry("Selection", "#4DB2FF", .5f, 0, true),
            new Entry("Grid", "#FFFFFF", .5f, 0, true),
            new Entry("GridFront", "#9BE38C", .5f, 0, true),
        };

        public const string Folder = "Materials";
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static Texture2D detail;

        public static Material Get(string name)
        {
            if (cache.TryGetValue(name, out var m)) return m;
            m = Resources.Load<Material>($"{Folder}/{name}");
            if (m == null)
            {
                // Only reached in the editor before the setup has run.
                foreach (var e in Entries) if (e.Name == name) m = Create(e);
                if (m == null) throw new KeyNotFoundException($"Material {name} is not in the palette.");
            }
            if (name == "Grass" || name == "ForestFloor" || name == "Yard" || name == "Sidewalk" || name == "Asphalt" || name == "Bank" || name == "Lakebed")
            {
                m = new Material(m) { name = name };
                // Grass gets broad patches of lusher and drier green; paving only a fine grain.
                bool green = name == "Grass" || name == "ForestFloor" || name == "Yard";
                m.SetTexture("_BaseMap", green ? Patches() : Detail());
                m.SetTextureScale("_BaseMap", green ? new Vector2(1 / 160f, 1 / 160f) : new Vector2(.125f, .125f));
            }
            cache[name] = m;
            return m;
        }

        public static Material[] Get(params string[] names)
        {
            var result = new Material[names.Length];
            for (int i = 0; i < names.Length; i++) result[i] = Get(names[i]);
            return result;
        }

        /// <summary>Builds a URP Lit material for an entry. The editor setup saves these as assets.</summary>
        public static Material Create(Entry e)
        {
            var shader = Shader.Find(e.Transparent ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            var m = new Material(shader) { name = e.Name };
            var c = e.Color;
            if (e.Transparent) c.a = e.Name == "Selection" ? .85f : e.Name == "Grid" ? .22f : e.Name == "GridFront" ? .16f : .45f;
            m.SetColor("_BaseColor", c);
            if (!e.Transparent)
            {
                m.SetFloat("_Smoothness", e.Smoothness);
                m.SetFloat("_Metallic", e.Metallic);
            }
            else
            {
                m.SetFloat("_Surface", 1);
                m.SetFloat("_Blend", 0);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            // Street lamps and windows light up at night (DayNight); keeping the keyword on keeps that shader in the build.
            if (e.Name == "LampGlow" || e.Name == "Glass")
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            m.enableInstancing = true;
            return m;
        }

        /// <summary>Soft grey noise multiplied over ground materials so large areas do not look flat.</summary>
        static Texture2D Detail()
        {
            if (detail != null) return detail;
            const int size = 256;
            detail = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = "GroundDetail" };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Blending four shifted copies makes the pattern repeat without seams.
                float u = x / (float)size, w = y / (float)size;
                float n = N(x, y) * (1 - u) * (1 - w) + N(x - size, y) * u * (1 - w) + N(x, y - size) * (1 - u) * w + N(x - size, y - size) * u * w;
                byte v = (byte)Mathf.Clamp(228 + (n - .5f) * 90, 0, 255);
                px[y * size + x] = new Color32(v, v, v, 255);
            }
            detail.SetPixels32(px);
            detail.Apply(true, true);
            return detail;
        }

        static Texture2D patches;

        /// <summary>
        /// 160 m of meadow: soft patches that shift between lush, dark and sun-dried green, over a fine grain.
        /// Tiles without seams.
        /// </summary>
        static Texture2D Patches()
        {
            if (patches != null) return patches;
            const int size = 512;
            patches = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = "GrassPatches" };
            var px = new Color32[size * size];
            var lush = new Color(.86f, 1f, .8f);
            var dark = new Color(.78f, .88f, .76f);
            var dry = new Color(1f, .95f, .72f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, w = y / (float)size;
                float a = Seamless(PatchA, x, y, size, u, w), b = Seamless(PatchB, x, y, size, u, w);
                var c = Color.Lerp(lush, dark, Mathf.SmoothStep(.35f, .7f, a));
                c = Color.Lerp(c, dry, Mathf.SmoothStep(.55f, .8f, b) * .8f);
                float grain = .92f + Seamless(Grain, x, y, size, u, w) * .14f;
                px[y * size + x] = new Color(c.r * grain, c.g * grain, c.b * grain);
            }
            patches.SetPixels32(px);
            patches.Apply(true, true);
            return patches;
        }

        static float PatchA(float x, float y) => Mathf.PerlinNoise(x * .012f + 40, y * .012f + 90) * .65f + Mathf.PerlinNoise(x * .04f + 300, y * .04f + 20) * .35f;
        static float Grain(float x, float y) => Mathf.PerlinNoise(x * .35f + 210, y * .35f + 870);
        static float PatchB(float x, float y) => Mathf.PerlinNoise(x * .008f + 700, y * .008f + 150) * .7f + Mathf.PerlinNoise(x * .03f + 60, y * .03f + 610) * .3f;

        /// <summary>Blends four shifted copies of a noise so the texture repeats without seams.</summary>
        static float Seamless(System.Func<float, float, float> f, int x, int y, int size, float u, float w) =>
            f(x, y) * (1 - u) * (1 - w) + f(x - size, y) * u * (1 - w) + f(x, y - size) * (1 - u) * w + f(x - size, y - size) * u * w;

        static float N(float x, float y) =>
            Mathf.PerlinNoise(x * .03f + 500, y * .03f + 500) * .5f + Mathf.PerlinNoise(x * .11f + 640, y * .11f + 640) * .3f + Mathf.PerlinNoise(x * .4f + 990, y * .4f + 990) * .2f;
    }
}
