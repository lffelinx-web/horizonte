using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Horizonte.EditorTools
{
    /// <summary>
    /// Turns the Blender models in Assets/Modelos into game-ready buildings in Resources/Buildings.
    /// Each model becomes one mesh drawn with a single shared material: every flat colour of the original materials is
    /// written into a small palette texture and the mesh's UVs point at it. Unity then adds simplified versions of the
    /// mesh (Mesh LOD) for when the building is far away. A whole city of buildings costs one draw call each.
    /// </summary>
    public static class BuildingBaker
    {
        const string SourceFolder = "Assets/Modelos";
        const string PrefabFolder = "Assets/Resources/Buildings";
        const string GeneratedFolder = "Assets/Generated/Buildings";
        const string PalettePath = GeneratedFolder + "/Paleta.png";
        const string GlossPath = GeneratedFolder + "/Paleta_brillo.png";
        const string MaterialPath = GeneratedFolder + "/Edificios.mat";
        const int PaletteSize = 32;

        /// <summary>
        /// Turn that makes the models face -Z (south, toward the camera at the start). The Blender exports already
        /// arrive with their front toward -Z, so no turn is needed; change this if a batch comes in backwards.
        /// </summary>
        const float FrontYaw = 0;

        struct Swatch : IEquatable<Swatch>
        {
            public Color32 Color;
            public byte Metallic, Smoothness;

            public bool Equals(Swatch o) => Color.r == o.Color.r && Color.g == o.Color.g && Color.b == o.Color.b && Metallic == o.Metallic && Smoothness == o.Smoothness;
            public override int GetHashCode() => (Color.r << 24) ^ (Color.g << 16) ^ (Color.b << 8) ^ (Metallic << 4) ^ Smoothness;
        }

        [MenuItem("Horizonte/Preparar edificios")]
        public static void Bake()
        {
            if (!AssetDatabase.IsValidFolder(SourceFolder)) return;
            Directory.CreateDirectory(PrefabFolder);
            Directory.CreateDirectory(GeneratedFolder);
            AssetDatabase.Refresh();

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
            var material = Material();

            foreach (var (name, bakedMesh) in baked)
            {
                var mesh = bakedMesh;
                // UVs can only be filled once every colour has its texel.
                var uv = mesh.uv;
                for (int k = 0; k < uv.Length; k++) uv[k] = Texel((int)uv[k].x);
                mesh.uv = uv;
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
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabFolder}/{name}.prefab");
                UnityEngine.Object.DestroyImmediate(go);
                var b = mesh.bounds;
                Debug.Log($"Horizonte: edificio {name}: {mesh.vertexCount} vértices, {b.size.x:0.0} x {b.size.z:0.0} m, {b.size.y:0.0} m de alto, {mesh.lodCount} niveles de detalle.");
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"Horizonte: {baked.Count} edificios preparados, {palette.Count} colores en la paleta.");
        }

        /// <summary>Merges every mesh of a model into one, in the model's space turned to face -Z. UV.x temporarily holds the palette index.</summary>
        static Mesh Combine(GameObject model, string name, List<Swatch> palette, Dictionary<Swatch, int> lookup)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            var turn = Matrix4x4.Rotate(Quaternion.Euler(0, FrontYaw, 0));
            var map = new Dictionary<int, int>();

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
                var materials = renderer.sharedMaterials;
                for (int s = 0; s < source.subMeshCount; s++)
                {
                    int index = PaletteIndex(s < materials.Length ? materials[s] : null, palette, lookup);
                    map.Clear();
                    var tris = source.GetTriangles(s);
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        int a = Vertex(tris[t]), b = Vertex(tris[t + 1]), c = Vertex(tris[t + 2]);
                        triangles.Add(a);
                        triangles.Add(flip ? c : b);
                        triangles.Add(flip ? b : c);
                    }

                    int Vertex(int v)
                    {
                        if (map.TryGetValue(v, out int existing)) return existing;
                        int n = vertices.Count;
                        vertices.Add(matrix.MultiplyPoint3x4(srcVertices[v]));
                        normals.Add(srcNormals.Length > v ? normalMatrix.MultiplyVector(srcNormals[v]).normalized : Vector3.up);
                        uvs.Add(new Vector2(index, 0));
                        map[v] = n;
                        return n;
                    }
                }
            }

            var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        static int PaletteIndex(Material m, List<Swatch> palette, Dictionary<Swatch, int> lookup)
        {
            var swatch = Describe(m);
            if (lookup.TryGetValue(swatch, out int i)) return i;
            i = palette.Count;
            palette.Add(swatch);
            lookup[swatch] = i;
            return i;
        }

        /// <summary>
        /// Colour and finish of an imported material. Blender writes its linear base colour into the FBX and Unity reads it
        /// as if it were sRGB, so it is converted back. The FBX carries no roughness Unity can use; glass and metal are
        /// recognised by name.
        /// </summary>
        static Swatch Describe(Material m)
        {
            var c = m == null ? Color.gray : m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
            var srgb = new Color(Mathf.LinearToGammaSpace(c.r), Mathf.LinearToGammaSpace(c.g), Mathf.LinearToGammaSpace(c.b));
            string n = m == null ? "" : m.name.ToLowerInvariant();
            float metallic = 0, smoothness = .2f;
            if (n.Contains("vidrio") || n.Contains("cristal") || n.Contains("acristal") || n.Contains("ventan"))
            {
                metallic = .1f;
                smoothness = .78f;
            }
            else if (n.Contains("aluminio") || n.Contains("herraje") || n.Contains("acero") || n.Contains("metal") || n.Contains("inox") || n.Contains("cromo"))
            {
                metallic = .55f;
                smoothness = .55f;
            }
            else if (n.Contains("follaje") || n.Contains("hoja") || n.Contains("césped") || n.Contains("cesped") || n.Contains("pasto"))
            {
                smoothness = .08f;
            }
            return new Swatch
            {
                Color = srgb,
                Metallic = (byte)Mathf.RoundToInt(metallic * 255),
                Smoothness = (byte)Mathf.RoundToInt(smoothness * 255),
            };
        }

        static Vector2 Texel(int index) =>
            new Vector2((index % PaletteSize + .5f) / PaletteSize, (index / PaletteSize + .5f) / PaletteSize);

        static void WritePalette(List<Swatch> palette)
        {
            var colors = new Color32[PaletteSize * PaletteSize];
            var gloss = new Color32[PaletteSize * PaletteSize];
            for (int i = 0; i < colors.Length; i++)
            {
                var s = i < palette.Count ? palette[i] : new Swatch { Color = new Color32(128, 128, 128, 255), Smoothness = 50 };
                colors[i] = new Color32(s.Color.r, s.Color.g, s.Color.b, 255);
                gloss[i] = new Color32(s.Metallic, s.Metallic, s.Metallic, s.Smoothness);
            }
            SavePng(PalettePath, colors, true);
            SavePng(GlossPath, gloss, false);
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

        static Material Material()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Edificios" };
                AssetDatabase.CreateAsset(m, MaterialPath);
            }
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePath));
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(GlossPath));
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Smoothness", 1);
            m.SetFloat("_SmoothnessTextureChannel", 0);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
