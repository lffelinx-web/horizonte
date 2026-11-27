using System.Collections.Generic;
using Horizonte.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Horizonte.Presentation
{
    /// <summary>
    /// The land beyond the flat tiles: the mountains in the north-west and the hills around the map, as one smooth mesh
    /// (a vertex every 10 m, only where the ground rises) painted with a colour map of meadow, dry grass, rock and snow
    /// by height and slope. Also the far woods on the hillsides, and the river and highway running on outside the map.
    /// </summary>
    public static class Relief
    {
        /// <summary>Tiles of land drawn around the map.</summary>
        public const int Margin = 36;
        /// <summary>Review aid: where to save the colour map of the relief (set from the command line).</summary>
        public static string DumpFolder;
        const int TextureSize = 512;

        public static void Build(Transform parent, City city)
        {
            uint seed = city.MapSeed;
            int w = city.Width, h = city.Height;
            int n = w + Margin * 2 + 1, m = h + Margin * 2 + 1;
            const float T = CityView.TileSize;

            // Heights at tile corners; flat wherever a corner touches a buildable tile of the map.
            var height = new float[n * m];
            for (int j = 0; j < m; j++)
            for (int i = 0; i < n; i++)
            {
                int vx = i - Margin, vy = j - Margin;
                bool flat = false;
                for (int dy = -1; dy <= 0 && !flat; dy++)
                for (int dx = -1; dx <= 0 && !flat; dx++)
                {
                    int tx = vx + dx, ty = vy + dy;
                    flat = city.InBounds(tx, ty) && city.Terrain[city.Index(tx, ty)] != Simulation.Terrain.Mountain;
                }
                height[j * n + i] = flat ? 0 : Landscape.Height(seed, w, h, vx, vy);
            }
            float H(int i, int j) => height[Mathf.Clamp(j, 0, m - 1) * n + Mathf.Clamp(i, 0, n - 1)];

            // Shared vertices with smooth normals, only for cells that rise above the ground.
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            var index = new int[n * m];
            for (int k = 0; k < index.Length; k++) index[k] = -1;
            int Vertex(int i, int j)
            {
                int k = j * n + i;
                if (index[k] >= 0) return index[k];
                index[k] = vertices.Count;
                vertices.Add(new Vector3((i - Margin) * T, H(i, j), (j - Margin) * T));
                normals.Add(new Vector3(H(i - 1, j) - H(i + 1, j), 2 * T, H(i, j - 1) - H(i, j + 1)).normalized);
                uvs.Add(new Vector2(i / (float)(n - 1), j / (float)(m - 1)));
                return index[k];
            }
            for (int j = 0; j < m - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                // Every mountain tile of the map is covered, even where all its corners sit at ground level.
                int tx = i - Margin, ty = j - Margin;
                bool mountainTile = city.InBounds(tx, ty) && city.Terrain[city.Index(tx, ty)] == Simulation.Terrain.Mountain;
                if (!mountainTile && H(i, j) + H(i + 1, j) + H(i, j + 1) + H(i + 1, j + 1) < .05f) continue;
                int a = Vertex(i, j), b = Vertex(i + 1, j), c = Vertex(i + 1, j + 1), d = Vertex(i, j + 1);
                triangles.Add(a); triangles.Add(d); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
            }
            var mesh = new Mesh { name = "Relieve", indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            if (Debug.isDebugBuild) Debug.Log($"Horizonte: relieve con {vertices.Count} vértices, {triangles.Count / 3} triángulos, altura máxima {mesh.bounds.max.y:0} m");

            var material = new Material(Palette.Get("StreetLamp")) { name = "Relieve" };
            material.SetTexture("_BaseMap", Paint(city, n, m, H));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", .04f);
            material.SetFloat("_Metallic", 0);
            var go = new GameObject("Relieve", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;

            Woods(parent, city, n, m, H);
            Beyond(parent, city);
        }

        /// <summary>Colour map over the whole relief: meadow, then dry grass, rock on steep or high ground and snow on the peaks.</summary>
        static Texture2D Paint(City city, int n, int m, System.Func<int, int, float> H)
        {
            int size = TextureSize;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 2, name = "Relieve" };
            var px = new Color32[size * size];
            var meadow = new Color(.525f, .678f, .361f);
            var lush = new Color(.45f, .62f, .32f);
            var dry = new Color(.66f, .64f, .42f);
            var rock = new Color(.6f, .58f, .54f);
            var darkRock = new Color(.46f, .45f, .43f);
            var snow = new Color(.93f, .95f, .98f);
            float Sample(float u, float v)
            {
                float fx = u * (n - 1), fy = v * (m - 1);
                int i = (int)fx, j = (int)fy;
                float tx = fx - i, ty = fy - j;
                return Mathf.Lerp(Mathf.Lerp(H(i, j), H(i + 1, j), tx), Mathf.Lerp(H(i, j + 1), H(i + 1, j + 1), tx), ty);
            }
            float step = 1f / size, metres = (n - 1) * CityView.TileSize / size;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + .5f) * step, v = (y + .5f) * step;
                float hgt = Sample(u, v);
                float gx = (Sample(u + step, v) - Sample(u - step, v)) / (2 * metres);
                float gy = (Sample(u, v + step) - Sample(u, v - step)) / (2 * metres);
                float slope = Mathf.Sqrt(gx * gx + gy * gy);
                float wx = u * size * 2.6f, wy = v * size * 2.6f;
                float patch = Mathf.PerlinNoise(wx * .02f + 30, wy * .02f + 70);
                float grain = Mathf.PerlinNoise(wx * .3f + 5, wy * .3f + 9);

                var c = Color.Lerp(meadow, lush, Step(.3f, .75f, patch));
                c = Color.Lerp(c, dry, Step(40, 95, hgt + (patch - .5f) * 25) * .75f);
                // Rock where it is steep or high, in bands like strata.
                float strata = Mathf.PerlinNoise(wx * .01f, hgt * .08f);
                var stone = Color.Lerp(rock, darkRock, strata);
                float rocky = Mathf.Max(Step(.6f, 1.15f, slope + (grain - .5f) * .3f), Step(120, 175, hgt + (patch - .5f) * 30));
                c = Color.Lerp(c, stone, rocky);
                float snowy = Step(185, 215, hgt + (patch - .5f) * 25) * (1 - Step(1.2f, 1.9f, slope));
                c = Color.Lerp(c, snow, snowy);
                // Shading from a fixed light, as on a relief map, so slopes read whatever the sun does. It comes from the
                // south-west, the side the camera usually looks from, so the slopes in view are the lit ones.
                var normal = new Vector3(-gx, 1, -gy).normalized;
                float shade = Mathf.Clamp01(Vector3.Dot(normal, new Vector3(-.4f, .72f, -.56f).normalized));
                c *= (.93f + grain * .12f) * Mathf.Lerp(.74f, 1.1f, shade);
                px[y * size + x] = c;
            }
            tex.SetPixels32(px);
            if (!string.IsNullOrEmpty(DumpFolder)) System.IO.File.WriteAllBytes(System.IO.Path.Combine(DumpFolder, "relieve.png"), tex.EncodeToPNG());
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>0 below a, 1 above b, smooth in between (like a shader's smoothstep).</summary>
        static float Step(float a, float b, float v)
        {
            float t = Mathf.Clamp01((v - a) / (b - a));
            return t * t * (3 - 2 * t);
        }

        /// <summary>Conifers scattered on the gentler, lower slopes, as one mesh.</summary>
        static void Woods(Transform parent, City city, int n, int m, System.Func<int, int, float> H)
        {
            var b = new MeshBuilder(2);
            uint seed = city.MapSeed;
            int trees = 0;
            for (int j = 1; j < m - 2; j++)
            for (int i = 1; i < n - 2; i++)
            {
                float hgt = H(i, j);
                if (hgt < 2.5f || hgt > 75) continue;
                float slope = Mathf.Abs(H(i + 1, j) - H(i - 1, j)) + Mathf.Abs(H(i, j + 1) - H(i, j - 1));
                if (slope > 14) continue;
                float wood = Landscape.Noise(i * .13f, j * .13f, seed + 41);
                if (wood < .5f) continue;
                uint r = CityView.Hash(i, j + 1000);
                int count = wood > .65f ? 3 : 1;
                for (int k = 0; k < count; k++)
                {
                    r = r * 1664525u + 1013904223u;
                    float fx = (r & 255) / 255f, fy = ((r >> 8) & 255) / 255f;
                    float x = i + fx, y = j + fy;
                    int i0 = (int)x, j0 = (int)y;
                    float ground = Mathf.Lerp(Mathf.Lerp(H(i0, j0), H(i0 + 1, j0), x - i0), Mathf.Lerp(H(i0, j0 + 1), H(i0 + 1, j0 + 1), x - i0), y - j0);
                    if (ground < 1) continue;
                    var at = new Vector3((x - Margin) * CityView.TileSize, ground - .3f, (y - Margin) * CityView.TileSize);
                    float size = 1.1f + ((r >> 16) & 255) / 255f * .8f;
                    Trees.Conifer(b, 0, 1, at, size, false);
                    trees++;
                }
            }
            if (trees == 0) return;
            var go = new GameObject("Bosque lejano", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = b.ToMesh();
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterials = Palette.Get("Leaves", "Trunk");
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>The river carries on past the edge of the map, and the highway arrives from the west over the countryside.</summary>
        static void Beyond(Transform parent, City city)
        {
            uint seed = city.MapSeed;
            int w = city.Width, h = city.Height;
            const float T = CityView.TileSize;
            float reach = Margin * T;

            var water = new MeshBuilder(1);
            foreach (var (from, to) in new[] { (-reach, 0f), (h * T, h * T + reach) })
            {
                const float step = 10;
                for (float z = from; z < to; z += step)
                {
                    float y0 = z / T, y1 = (z + step) / T;
                    float c0 = Landscape.RiverX(seed, w, y0) * T, c1 = Landscape.RiverX(seed, w, y1) * T;
                    float h0 = Landscape.RiverHalfWidth(seed, y0) * T, h1 = Landscape.RiverHalfWidth(seed, y1) * T;
                    water.Quad(0, new Vector3(c0 - h0, .04f, z), new Vector3(c0 + h0, .04f, z), new Vector3(c1 + h1, .04f, z + step), new Vector3(c1 - h1, .04f, z + step));
                }
            }
            var river = new GameObject("Río lejano", typeof(MeshFilter), typeof(MeshRenderer));
            river.transform.SetParent(parent, false);
            river.GetComponent<MeshFilter>().sharedMesh = water.ToMesh();
            river.GetComponent<MeshRenderer>().sharedMaterial = Palette.Get("Water");

            Tunnel(parent, (Landscape.HighwayY(h) + .5f) * T);
        }

        /// <summary>
        /// Where the highway comes into the map: out of a tunnel in the ridge along the west edge, through a cutting with
        /// retaining walls. The concrete portal frames a dark mouth.
        /// </summary>
        static void Tunnel(Transform parent, float zc)
        {
            const float T = CityView.TileSize;
            float portal = -Landscape.TunnelPortal * T;
            // Slots: asphalt, lane line, concrete, the dark inside.
            var b = new MeshBuilder(4);
            b.Flat(0, portal - 1, zc - 3.7f, 0, zc + 3.7f, .03f);
            for (float x = portal + .3f; x + 1.4f < 0; x += 2.8f) b.Flat(1, x, zc - .14f, x + 1.4f, zc + .14f, .045f);
            // Retaining walls along the cutting, lower toward the town.
            for (int k = 0; k < 6; k++)
            {
                float x0 = portal + k * 2.5f, height = Mathf.Lerp(7.5f, 1.2f, k / 5f);
                b.Box(2, new Vector3(x0 + 1.25f, 0, zc - 5.2f), new Vector3(2.5f, height, .7f));
                b.Box(2, new Vector3(x0 + 1.25f, 0, zc + 5.2f), new Vector3(2.5f, height, .7f));
            }
            // Portal: two piers and a deep lintel around the mouth, and the dark tunnel behind.
            b.Box(2, new Vector3(portal - .6f, 0, zc - 5f), new Vector3(1.6f, 8.6f, 1.4f));
            b.Box(2, new Vector3(portal - .6f, 0, zc + 5f), new Vector3(1.6f, 8.6f, 1.4f));
            b.Box(2, new Vector3(portal - .6f, 6.2f, zc), new Vector3(1.6f, 2.4f, 11.4f));
            b.Box(2, new Vector3(portal - .2f, 8.6f, zc), new Vector3(.8f, .5f, 12f));
            b.Box(3, new Vector3(portal - 9, 0, zc), new Vector3(16, 6.4f, 8.6f));
            var go = new GameObject("Túnel", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = b.ToMesh();
            go.GetComponent<MeshRenderer>().sharedMaterials = new[] { Palette.Get("Asphalt"), Palette.Get("LaneLine"), Palette.Get("Sidewalk"), Figures.Colour("#0B0D10", 0) };
        }
    }
}
