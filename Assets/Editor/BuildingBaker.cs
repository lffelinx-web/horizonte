using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Horizonte.Simulation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Horizonte.EditorTools
{
    /// <summary>
    /// Turns the Blender models in Assets/Modelos into game-ready buildings in Resources/Buildings.
    /// Each model becomes one mesh drawn with one shared material (Horizonte/Edificios). The real textures of the models
    /// (Assets/Modelos/Texturas~, the textures/ folders of the deliveries, one copy of each) are packed into two texture
    /// arrays, colour and relief; each vertex says which layer it uses and where its flat colour, finish and glow are in
    /// small palette textures. Unity then adds simplified versions of the mesh (Mesh LOD) for when the building is far
    /// away. Shops and services also get a second, see-through material for their glass, so their interiors show.
    /// </summary>
    public static class BuildingBaker
    {
        const string SourceFolder = "Assets/Modelos";
        /// <summary>Not imported by Unity (the ~): read straight from disk while baking.</summary>
        const string TextureFolder = "Assets/Modelos/Texturas~";
        const string PrefabFolder = "Assets/Resources/Buildings";
        const string GeneratedFolder = "Assets/Generated/Buildings";
        const string PalettePath = GeneratedFolder + "/Paleta.png";
        const string GlossPath = GeneratedFolder + "/Paleta_brillo.png";
        const string GlowPath = GeneratedFolder + "/Paleta_luz.png";
        const string AlbedoPath = GeneratedFolder + "/Texturas.png";
        const string NormalsPath = GeneratedFolder + "/Relieves.png";
        const string GlassPath = GeneratedFolder + "/Vidrio.mat";
        /// <summary>Materials with the facades repainted, loaded by the game to give each house and shop its colour.</summary>
        const string FacadeFolder = "Assets/Resources/Fachadas";
        const string ShaderName = "Horizonte/Edificios";

        /// <summary>Facade colours (sRGB): terracotta, mustard, blue, mint, Mexican pink and peach.</summary>
        static readonly (string name, Color colour)[] FacadeColours =
        {
            ("terracota", new Color(.82f, .47f, .34f)),
            ("mostaza", new Color(.92f, .74f, .36f)),
            ("azul", new Color(.47f, .64f, .82f)),
            ("menta", new Color(.56f, .79f, .67f)),
            ("rosa", new Color(.89f, .53f, .62f)),
            ("durazno", new Color(.95f, .72f, .56f)),
        };
        const string MaterialPath = GeneratedFolder + "/Edificios.mat";
        const int PaletteSize = 32;
        const int Layer = 512, Columns = 8;

        /// <summary>
        /// Turn that makes the models face -Z (south, toward the camera at the start). The Blender exports already
        /// arrive with their front toward -Z, so no turn is needed; change this if a batch comes in backwards.
        /// </summary>
        const float FrontYaw = 0;

        struct Swatch : IEquatable<Swatch>
        {
            public Color32 Color;
            public byte Metallic, Smoothness;
            /// <summary>Lamps and lit interiors give off their own light.</summary>
            public bool Glows;
            /// <summary>Glass, lit from inside at night (when it is not see-through).</summary>
            public bool Window;
            /// <summary>Main wall, repainted per building with one of the facade colours.</summary>
            public bool Facade;

            public bool Equals(Swatch o) => Color.r == o.Color.r && Color.g == o.Color.g && Color.b == o.Color.b && Metallic == o.Metallic && Smoothness == o.Smoothness && Glows == o.Glows && Facade == o.Facade && Window == o.Window;
            public override int GetHashCode() => (Color.r << 24) ^ (Color.g << 16) ^ (Color.b << 8) ^ (Metallic << 4) ^ Smoothness ^ (Glows ? 1 << 30 : 0) ^ (Facade ? 1 << 29 : 0) ^ (Window ? 1 << 28 : 0);
        }

        /// <summary>A layer of the texture arrays: its colour and its relief (flat if the material has none).</summary>
        sealed class Slice
        {
            public Color32[] Albedo, Normal;
        }

        static readonly List<Slice> slices = new List<Slice>();
        static readonly Dictionary<string, int> sliceOf = new Dictionary<string, int>();
        static readonly Dictionary<string, Color32[]> loaded = new Dictionary<string, Color32[]>();

        [MenuItem("Horizonte/Preparar edificios")]
        public static void Bake()
        {
            if (!AssetDatabase.IsValidFolder(SourceFolder)) return;
            Directory.CreateDirectory(PrefabFolder);
            Directory.CreateDirectory(GeneratedFolder);
            AssetDatabase.Refresh();

            doors.Clear();
            slices.Clear();
            sliceOf.Clear();
            loaded.Clear();
            // Layer 0: white with flat relief, for the materials that are just a colour.
            slices.Add(new Slice { Albedo = Fill(new Color32(255, 255, 255, 255)), Normal = Fill(new Color32(128, 128, 255, 255)) });
            var palette = new List<Swatch>();
            var lookup = new Dictionary<Swatch, int>();
            var baked = new List<(string name, Mesh mesh)>();

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { SourceFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) continue;
                string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                baked.Add((name, Combine(model, name, palette, lookup)));
            }

            if (palette.Count > PaletteSize * PaletteSize) throw new InvalidOperationException($"Too many colours for the palette: {palette.Count}.");
            WritePalette(palette);
            WriteArrays();
            var material = Material();
            var glass = GlassMaterial();

            foreach (var (name, bakedMesh) in baked)
            {
                var mesh = bakedMesh;
                // The palette part of the look can only be filled once every colour has its texel.
                var look = new List<Vector4>();
                mesh.GetUVs(1, look);
                for (int k = 0; k < look.Count; k++)
                {
                    var t = Texel((int)look[k].x);
                    look[k] = new Vector4(t.x, t.y, look[k].z, look[k].w);
                }
                mesh.SetUVs(1, look);
                MeshLodUtility.GenerateMeshLods(mesh);

                string meshPath = $"{GeneratedFolder}/{name}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(mesh, existing);
                    mesh = existing;
                }
                else AssetDatabase.CreateAsset(mesh, meshPath);

                var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                if (doors.TryGetValue(name, out var door))
                {
                    var mark = new GameObject("Puerta").transform;
                    mark.SetParent(go.transform, false);
                    mark.localPosition = door;
                }
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = mesh.subMeshCount > 1 ? new[] { material, glass } : new[] { material };
                renderer.shadowCastingMode = ShadowCastingMode.On;
                PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabFolder}/{name}.prefab");
                UnityEngine.Object.DestroyImmediate(go);
                var b = mesh.bounds;
                Debug.Log($"Horizonte: edificio {name}: {mesh.vertexCount} vértices, {b.size.x:0.0} x {b.size.z:0.0} m, {b.size.y:0.0} m de alto, {mesh.lodCount} niveles de detalle{(doors.ContainsKey(name) ? ", con puerta" : ", sin puerta")}{(mesh.subMeshCount > 1 ? ", vidrio transparente" : "")}.");
            }
            RemoveStale(baked.Select(b => b.name).ToHashSet());
            AssetDatabase.SaveAssets();
            Debug.Log($"Horizonte: {baked.Count} edificios preparados, {palette.Count} colores en la paleta, {slices.Count} texturas.");
        }

        /// <summary>Deletes prepared buildings whose model left Assets/Modelos, so they fall back to the placeholder.</summary>
        static void RemoveStale(HashSet<string> names)
        {
            foreach (var (folder, filter) in new[] { (PrefabFolder, "t:Prefab"), (GeneratedFolder, "t:Mesh") })
                foreach (var guid in AssetDatabase.FindAssets(filter, new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (names.Contains(Path.GetFileNameWithoutExtension(path).ToLowerInvariant())) continue;
                    AssetDatabase.DeleteAsset(path);
                    Debug.Log($"Horizonte: {path} ya no tiene modelo y se quitó.");
                }
        }

        /// <summary>Homes keep opaque windows (there are hundreds of them); every other building gets see-through glass.</summary>
        static bool ClearGlass(string name)
        {
            var def = Catalog.All.FirstOrDefault(d => d != null && d.Model == name);
            return def != null && def.Category != Category.Housing;
        }

        /// <summary>
        /// Merges every mesh of a model into one, in the model's space turned to face -Z. UV0 keeps the model's own texture
        /// coordinates; UV1 holds (palette index, 0, texture layer, colour scale), the index turned into a texel later.
        /// See-through glass goes to a second submesh.
        /// </summary>
        static Mesh Combine(GameObject model, string name, List<Swatch> palette, Dictionary<Swatch, int> lookup)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var looks = new List<Vector4>();
            var triangles = new List<int>();
            var glassTriangles = new List<int>();
            var turn = Matrix4x4.Rotate(Quaternion.Euler(0, FrontYaw, 0));
            var map = new Dictionary<int, int>();
            bool clearGlass = ClearGlass(name);

            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                var source = filter.sharedMesh;
                if (renderer == null || source == null) continue;
                // Includes the root, which carries the turn from Blender's Z-up axes to Unity's Y-up ones.
                var matrix = turn * filter.transform.localToWorldMatrix;
                var normalMatrix = matrix.inverse.transpose;
                bool flip = matrix.determinant < 0;
                var srcVertices = source.vertices;
                var srcNormals = source.normals;
                var srcUv = source.uv;
                var materials = renderer.sharedMaterials;
                for (int s = 0; s < source.subMeshCount; s++)
                {
                    var material = s < materials.Length ? materials[s] : null;
                    var (swatch, layer, scale) = Describe(material, name);
                    int index = PaletteIndex(swatch, palette, lookup);
                    bool seeThrough = clearGlass && swatch.Window;
                    var into = seeThrough ? glassTriangles : triangles;
                    map.Clear();
                    var tris = source.GetTriangles(s);
                    if (material != null && IsDoor(material.name)) FindDoor(name, tris, srcVertices, matrix);
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        int a = Vertex(tris[t]), b = Vertex(tris[t + 1]), c = Vertex(tris[t + 2]);
                        into.Add(a);
                        into.Add(flip ? c : b);
                        into.Add(flip ? b : c);
                    }

                    int Vertex(int v)
                    {
                        if (map.TryGetValue(v, out int existing)) return existing;
                        int n = vertices.Count;
                        vertices.Add(matrix.MultiplyPoint3x4(srcVertices[v]));
                        normals.Add(srcNormals.Length > v ? normalMatrix.MultiplyVector(srcNormals[v]).normalized : Vector3.up);
                        uvs.Add(srcUv.Length > v ? srcUv[v] : Vector2.zero);
                        looks.Add(new Vector4(index, 0, layer, scale));
                        map[v] = n;
                        return n;
                    }
                }
            }

            var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, looks);
            mesh.subMeshCount = glassTriangles.Count > 0 ? 2 : 1;
            mesh.SetTriangles(triangles, 0);
            if (glassTriangles.Count > 0) mesh.SetTriangles(glassTriangles, 1);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>Front door of each model (the bottom middle of its "puerta" faces nearest the front), where people come out.</summary>
        static readonly Dictionary<string, Vector3> doors = new Dictionary<string, Vector3>();

        static bool IsDoor(string material)
        {
            string n = material.ToLowerInvariant();
            return n.Contains("puerta") || n.Contains("door") || n.Contains("acceso");
        }

        static void FindDoor(string name, int[] tris, Vector3[] source, Matrix4x4 matrix)
        {
            if (tris.Length == 0) return;
            var lo = Vector3.one * float.MaxValue;
            var hi = Vector3.one * float.MinValue;
            foreach (int v in tris)
            {
                var p = matrix.MultiplyPoint3x4(source[v]);
                lo = Vector3.Min(lo, p);
                hi = Vector3.Max(hi, p);
            }
            var door = new Vector3((lo.x + hi.x) * .5f, lo.y, lo.z);
            // The front faces -Z: of several doors, the one furthest forward.
            if (!doors.TryGetValue(name, out var best) || door.z < best.z) doors[name] = door;
        }

        static int PaletteIndex(Swatch swatch, List<Swatch> palette, Dictionary<Swatch, int> lookup)
        {
            if (lookup.TryGetValue(swatch, out int i)) return i;
            i = palette.Count;
            palette.Add(swatch);
            lookup[swatch] = i;
            return i;
        }

        /// <summary>
        /// Colour, finish, texture layer and colour scale of an imported material. Blender writes its linear base colour
        /// into the FBX and Unity reads it as if it were sRGB, so it is converted back. A material with a texture in the
        /// deliveries (by name: 19_Madera_roble → 19_Madera_roble_BaseColor.png) shows that texture as it is; a main wall
        /// gets a grey copy of its texture and its colour from the palette, so it can be repainted and keep its grain.
        /// Glass and metal are recognised by name.
        /// </summary>
        static (Swatch swatch, int layer, float scale) Describe(Material m, string model)
        {
            var c = m == null ? Color.gray : m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
            var srgb = new Color(Mathf.LinearToGammaSpace(c.r), Mathf.LinearToGammaSpace(c.g), Mathf.LinearToGammaSpace(c.b));
            string n = m == null ? "" : m.name.ToLowerInvariant();
            int layer = 0;
            float scale = 1;
            var texture = m == null ? null : TextureOf(model, m.name);
            Color average = srgb;
            if (texture != null) average = Average(Load(texture));
            float metallic = 0, smoothness = .2f;
            bool glass = n.Contains("vidrio") || n.Contains("cristal") || n.Contains("acristal") || n.Contains("ventan");
            bool metal = !glass && (n.Contains("aluminio") || n.Contains("herraje") || n.Contains("acero") || n.Contains("metal") || n.Contains("inox") || n.Contains("cromo") || n.Contains("latón") || n.Contains("laton") || n.Contains("zinc"));
            bool lawn = n.Contains("césped") || n.Contains("cesped") || n.Contains("pasto");
            bool foliage = lawn || n.Contains("follaje") || n.Contains("hoja");
            bool interior = n.Contains("interior") || n.Contains("habitacion") || n.Contains("cortina");
            if (glass)
            {
                // Deep blue-grey panes: they contrast with the walls instead of mirroring the pale sky.
                srgb = Color.Lerp(srgb, GlassColour, .8f);
                metallic = .05f;
                smoothness = .85f;
            }
            else if (metal)
            {
                metallic = .55f;
                smoothness = .55f;
            }
            else if (foliage)
            {
                smoothness = .08f;
            }
            bool facade = !glass && !metal && !foliage && IsFacade(average, n);
            if (texture != null && !glass)
            {
                if (facade)
                {
                    // The palette holds the wall's own colour (or a facade colour); the texture only its light and shade.
                    layer = SliceFor(texture, true);
                    srgb = average;
                    scale = 2;
                }
                else
                {
                    layer = SliceFor(texture, false);
                    srgb = Color.white;
                }
            }
            if (lawn) { srgb = LawnColour; layer = 0; }
            var swatch = new Swatch
            {
                Color = srgb,
                Metallic = (byte)Mathf.RoundToInt(metallic * 255),
                Smoothness = (byte)Mathf.RoundToInt(smoothness * 255),
                Glows = interior || (!glass && IsLight(m, n)),
                Window = glass,
                Facade = facade,
            };
            return (swatch, layer, scale);
        }

        /// <summary>The colour texture of a material: the model's own version if it differs from the shared one.</summary>
        static string TextureOf(string model, string material)
        {
            foreach (var folder in new[] { $"{TextureFolder}/{model}", TextureFolder })
            {
                string path = $"{folder}/{material}_BaseColor.png";
                if (File.Exists(path)) return path;
            }
            return null;
        }

        /// <summary>The layer of a texture in the arrays, adding it the first time. A wall's copy is grey, averaging .5.</summary>
        static int SliceFor(string texture, bool grey)
        {
            string key = grey ? texture + "#gris" : texture;
            if (sliceOf.TryGetValue(key, out int layer)) return layer;
            var albedo = (Color32[])Load(texture).Clone();
            if (grey)
            {
                float mean = Average(albedo).grayscale;
                for (int i = 0; i < albedo.Length; i++)
                {
                    float v = Mathf.Clamp01(((Color)albedo[i]).grayscale / Mathf.Max(mean, .05f) * .5f);
                    byte b = (byte)Mathf.RoundToInt(v * 255);
                    albedo[i] = new Color32(b, b, b, 255);
                }
            }
            string normalPath = texture.Replace("_BaseColor.png", "_Normal.png");
            var normal = File.Exists(normalPath) ? Load(normalPath) : Fill(new Color32(128, 128, 255, 255));
            layer = slices.Count;
            slices.Add(new Slice { Albedo = albedo, Normal = normal });
            sliceOf[key] = layer;
            return layer;
        }

        /// <summary>The pixels of a PNG at 512 x 512, bottom row first.</summary>
        static Color32[] Load(string path)
        {
            if (loaded.TryGetValue(path, out var pixels)) return pixels;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(path));
            if (tex.width != Layer || tex.height != Layer)
            {
                var rt = RenderTexture.GetTemporary(Layer, Layer, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(tex, rt);
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var resized = new Texture2D(Layer, Layer, TextureFormat.RGBA32, false);
                resized.ReadPixels(new Rect(0, 0, Layer, Layer), 0, 0);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(tex);
                tex = resized;
            }
            pixels = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            loaded[path] = pixels;
            return pixels;
        }

        static Color32[] Fill(Color32 c)
        {
            var p = new Color32[Layer * Layer];
            for (int i = 0; i < p.Length; i++) p[i] = c;
            return p;
        }

        static Color Average(Color32[] pixels)
        {
            long r = 0, g = 0, b = 0;
            for (int i = 0; i < pixels.Length; i += 7) { r += pixels[i].r; g += pixels[i].g; b += pixels[i].b; }
            float n = (pixels.Length + 6) / 7 * 255f;
            return new Color(r / n, g / n, b / n, 1);
        }

        /// <summary>Lawns in the models match the grass of the map, so a lot blends into the ground around it.</summary>
        static readonly Color LawnColour = new Color(.525f, .678f, .361f);

        static readonly Color GlassColour = new Color(.12f, .19f, .27f);

        /// <summary>
        /// The main wall of a building: pale plaster or paint. Each house and shop gets its own colour for it in the game,
        /// so a street is not a row of identical white boxes. Accents (terracotta, wood, stone) keep theirs.
        /// </summary>
        static bool IsFacade(Color c, string name)
        {
            bool wall = name.Contains("estuco") || name.Contains("aplanado") || name.Contains("fachada") || name.Contains("pintura") || name.Contains("muro") || name.Contains("pared");
            Color.RGBToHSV(c, out _, out float s, out float v);
            return wall && v > .55f && s < .3f;
        }

        /// <summary>Lamps and lit windows: an emissive material in the FBX, or one named like a light.</summary>
        static bool IsLight(Material m, string name)
        {
            if (name.Contains("lámpara") || name.Contains("lampara") || name.Contains("luz") || name.Contains("ilumin") || name.Contains("foco") || name.Contains("emis"))
                return true;
            if (m == null || !m.HasProperty("_EmissionColor") || !m.IsKeywordEnabled("_EMISSION")) return false;
            return m.GetColor("_EmissionColor").maxColorComponent > .05f;
        }

        static Vector2 Texel(int index) =>
            new Vector2((index % PaletteSize + .5f) / PaletteSize, (index / PaletteSize + .5f) / PaletteSize);

        static void WritePalette(List<Swatch> palette)
        {
            var colors = new Color32[PaletteSize * PaletteSize];
            var gloss = new Color32[PaletteSize * PaletteSize];
            var glow = new Color32[PaletteSize * PaletteSize];
            for (int i = 0; i < colors.Length; i++)
            {
                var s = i < palette.Count ? palette[i] : new Swatch { Color = new Color32(128, 128, 128, 255), Smoothness = 50 };
                colors[i] = new Color32(s.Color.r, s.Color.g, s.Color.b, 255);
                gloss[i] = new Color32(s.Metallic, s.Metallic, s.Metallic, s.Smoothness);
                // Lamps glow in their own colour, interiors in a warm light over their picture; opaque windows a warm
                // glow of their own. The game turns all of them up at night.
                glow[i] = s.Glows ? (s.Color.r == 255 && s.Color.g == 255 && s.Color.b == 255 ? new Color32(150, 120, 80, 255) : new Color32(s.Color.r, s.Color.g, s.Color.b, 255))
                    : s.Window ? new Color32(150, 112, 62, 255) : new Color32(0, 0, 0, 255);
            }
            SavePng(PalettePath, colors, true);
            for (int k = 0; k < FacadeColours.Length; k++)
            {
                var painted = (Color32[])colors.Clone();
                for (int i = 0; i < palette.Count; i++)
                {
                    if (!palette[i].Facade) continue;
                    // Keep how light or shaded the wall was drawn.
                    Color original = palette[i].Color;
                    float shade = Mathf.Clamp(original.grayscale / .72f, .8f, 1.15f);
                    var c = FacadeColours[k].colour * shade;
                    painted[i] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1);
                }
                SavePng(FacadePalettePath(k), painted, true);
            }
            SavePng(GlossPath, gloss, false);
            SavePng(GlowPath, glow, true);
        }

        static void SavePng(string path, Color32[] pixels, bool srgb)
        {
            var tex = new Texture2D(PaletteSize, PaletteSize, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = srgb;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = false;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Both texture arrays, written as one big picture each (eight layers per row, top row first) that Unity imports
        /// as a 2D array and compresses for each platform.
        /// </summary>
        static void WriteArrays()
        {
            int rows = (slices.Count + Columns - 1) / Columns;
            WriteArray(AlbedoPath, rows, s => s.Albedo, true);
            WriteArray(NormalsPath, rows, s => s.Normal, false);
        }

        static void WriteArray(string path, int rows, Func<Slice, Color32[]> pick, bool srgb)
        {
            int width = Columns * Layer, height = rows * Layer;
            var all = new Color32[width * height];
            for (int k = 0; k < slices.Count; k++)
            {
                var src = pick(slices[k]);
                int x0 = k % Columns * Layer, y0 = (rows - 1 - k / Columns) * Layer;
                for (int y = 0; y < Layer; y++)
                    Array.Copy(src, y * Layer, all, (y0 + y) * width + x0, Layer);
            }
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.SetPixels32(all);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.textureShape = TextureImporterShape.Texture2DArray;
            settings.flipbookColumns = Columns;
            settings.flipbookRows = rows;
            importer.SetTextureSettings(settings);
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2DArray;
            importer.sRGBTexture = srgb;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = 8192;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.alphaIsTransparency = false;
            importer.SaveAndReimport();
        }

        static string FacadePalettePath(int k) => $"{GeneratedFolder}/Paleta_{FacadeColours[k].name}.png";

        /// <summary>The shared building material, plus one per facade colour that only swaps the palette.</summary>
        static Material Material()
        {
            Directory.CreateDirectory(FacadeFolder);
            AssetDatabase.Refresh();
            for (int k = 0; k < FacadeColours.Length; k++)
                Material($"{FacadeFolder}/Edificios_{k}_{FacadeColours[k].name}.mat", FacadePalettePath(k));
            return Material(MaterialPath, PalettePath);
        }

        static Material Material(string path, string palettePath)
        {
            var shader = Shader.Find(ShaderName);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(palettePath));
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(GlossPath));
            m.SetTexture("_Albedo", AssetDatabase.LoadAssetAtPath<Texture2DArray>(AlbedoPath));
            m.SetTexture("_Normals", AssetDatabase.LoadAssetAtPath<Texture2DArray>(NormalsPath));
            m.SetFloat("_BumpScale", 1);
            m.SetFloat("_Alpha", 1);
            // Lamps and lit windows glow softly by day; the bloom picks up the brightest.
            m.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(GlowPath));
            m.SetColor("_EmissionColor", new Color(1.4f, 1.4f, 1.4f));
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>See-through glass for shops and services: blended over the interior behind it, no glow of its own.</summary>
        static Material GlassMaterial()
        {
            var m = Material(GlassPath, PalettePath);
            m.DisableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            m.SetFloat("_Alpha", .32f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
