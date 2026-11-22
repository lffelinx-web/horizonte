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
            new Entry("Grass", "#7FA35A", .05f),
            new Entry("ForestFloor", "#5E7F42", .05f),
            new Entry("Bank", "#8C7556", .05f),
            new Entry("Water", "#3F7FA6", .92f),
            new Entry("Asphalt", "#3B3D42", .2f),
            new Entry("Sidewalk", "#B9B5AC", .1f),
            new Entry("LaneLine", "#E8E2C8", .2f),
            new Entry("ZoneResidential", "#8CCB6A", .05f),
            new Entry("ZoneCommercial", "#6FA6D8", .05f),
            new Entry("ZoneIndustrial", "#D9B85A", .05f),
            new Entry("Yard", "#86AE60", .05f),
            new Entry("Trunk", "#6B4A32", .05f),
            new Entry("Leaves", "#4C7A3A", .1f),
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
            if (name == "Grass" || name == "ForestFloor" || name == "Yard" || name == "Sidewalk" || name == "Asphalt" || name == "Bank")
            {
                m = new Material(m) { name = name };
                m.SetTexture("_BaseMap", Detail());
                m.SetTextureScale("_BaseMap", new Vector2(.125f, .125f));
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

        static float N(float x, float y) =>
            Mathf.PerlinNoise(x * .03f + 500, y * .03f + 500) * .5f + Mathf.PerlinNoise(x * .11f + 640, y * .11f + 640) * .3f + Mathf.PerlinNoise(x * .4f + 990, y * .4f + 990) * .2f;
    }
}
