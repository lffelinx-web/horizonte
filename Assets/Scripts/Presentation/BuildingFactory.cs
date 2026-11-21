using System.Collections.Generic;
using System.Text.RegularExpressions;
using Horizonte.Simulation;
using UnityEngine;

namespace Horizonte.Presentation
{
    /// <summary>
    /// Makes the object for a building. A model in Resources/Buildings wins over the procedural placeholder.
    /// Model names: zones are "residencial_1".."_3", "comercial_1".., "industrial_1"..; services use their id
    /// ("central_electrica", "parque", "policia", "escuela"). Extra variants add a letter: "residencial_1_b".
    /// Models face south (-Z, toward the camera at the start), with the pivot at the centre of the footprint on the ground.
    /// </summary>
    public sealed class BuildingFactory
    {
        const int Wall = 0, Roof = 1, Glass = 2, Accent = 3;
        const float T = CityView.TileSize;

        readonly Dictionary<string, List<GameObject>> models = new Dictionary<string, List<GameObject>>();
        readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        static readonly Regex Variant = new Regex("_[a-z]$");

        public BuildingFactory()
        {
            foreach (var model in Resources.LoadAll<GameObject>("Buildings"))
            {
                string key = Variant.Replace(model.name.ToLowerInvariant(), "");
                if (!models.TryGetValue(key, out var list)) models[key] = list = new List<GameObject>();
                list.Add(model);
            }
        }

        public int ModelCount
        {
            get
            {
                int n = 0;
                foreach (var l in models.Values) n += l.Count;
                return n;
            }
        }

        public static string Key(Structure type, int level, int size = 1)
        {
            var def = Catalog.Get(type);
            if (!def.IsZone) return def.ModelId;
            return size > 1 ? $"{def.ModelId}_{level}_{size}" : $"{def.ModelId}_{level}";
        }

        public bool HasModel(string key) => models.ContainsKey(key);

        /// <summary>Object for a building; size is the side in lots of a zone building that grew over several lots.</summary>
        public GameObject Create(Structure type, int level, int size, int rotation, uint seed)
        {
            var def = Catalog.Get(type);
            // Footprints that are not square can only turn half way round.
            if (def.Width != def.Height) rotation &= 2;
            string key = Key(type, level, size);
            GameObject go;
            if (models.TryGetValue(key, out var list))
            {
                // Prepared models (Horizonte > Preparar edificios) already face -Z like the placeholders.
                go = Object.Instantiate(list[(int)(seed % (uint)list.Count)]);
                go.name = key;
                go.transform.localRotation = Quaternion.Euler(0, rotation * 90, 0);
                return go;
            }
            string baseKey = Key(type, level);
            int variant = (int)(seed % 3);
            string meshKey = $"{baseKey}#{variant}";
            if (!meshes.TryGetValue(meshKey, out var mesh)) meshes[meshKey] = mesh = Placeholder(type, level, variant);
            go = new GameObject(key, typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = Materials(type);
            go.transform.localRotation = Quaternion.Euler(0, rotation * 90, 0);
            // Placeholders were drawn for 8 m lots; stretch them to the lot and to buildings that span several lots.
            float scale = T / 8f * size;
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        static Material[] Materials(Structure type)
        {
            switch (type)
            {
                case Structure.Residential: return Palette.Get("WallResidential", "RoofResidential", "Glass", "Trim");
                case Structure.Commercial: return Palette.Get("WallCommercial", "RoofFlat", "Glass", "AccentCommercial");
                case Structure.Industrial: return Palette.Get("WallIndustrial", "RoofIndustrial", "Glass", "Metal");
                case Structure.PowerPlant: return Palette.Get("WallIndustrial", "RoofIndustrial", "Glass", "Metal");
                case Structure.Park: return Palette.Get("Leaves", "Trunk", "Sidewalk", "Yard");
                case Structure.Police: return Palette.Get("WallPolice", "RoofFlat", "Glass", "Trim");
                default: return Palette.Get("WallSchool", "RoofResidential", "Glass", "Trim");
            }
        }

        // ---------------------------------------------------------------- placeholders (centred on the footprint, front to -Z)

        static Mesh Placeholder(Structure type, int level, int variant)
        {
            var b = new MeshBuilder(4);
            float v = variant * .5f;
            switch (type)
            {
                case Structure.Residential:
                    if (level == 1)
                    {
                        float w = 4.6f + v, d = 4.2f, h = 3f;
                        b.Box(Wall, new Vector3(0, 0, .4f), new Vector3(w, h, d));
                        b.GableRoof(Roof, new Vector3(0, h, .4f), w + .5f, d + .6f, 2f + v * .4f);
                        b.Box(Accent, new Vector3(-w * .2f, 0, .4f - d * .5f - .05f), new Vector3(.9f, 2f, .1f));
                        Windows(b, new Vector3(0, 0, .4f), w, d, h, 1, 1.6f);
                        if (variant == 2) b.Box(Wall, new Vector3(w * .5f + .6f, 0, 1.2f), new Vector3(1.6f, 2.4f, 2.6f));
                    }
                    else if (level == 2)
                    {
                        float w = 6f, d = 5.4f, h = 9 + variant * 3;
                        b.Box(Wall, Vector3.zero, new Vector3(w, h, d));
                        b.Box(Roof, new Vector3(0, h, 0), new Vector3(w + .2f, .5f, d + .2f));
                        Windows(b, Vector3.zero, w, d, h, (int)(h / 3), 1.8f);
                        b.Box(Accent, new Vector3(0, 0, -d * .5f - .3f), new Vector3(2f, .3f, .6f));
                    }
                    else
                    {
                        float w = 5.6f, h = 24 + variant * 6;
                        b.Box(Wall, Vector3.zero, new Vector3(w, h, w));
                        b.Box(Roof, new Vector3(0, h, 0), new Vector3(w - 1f, 1.5f, w - 1f));
                        Windows(b, Vector3.zero, w, w, h, (int)(h / 3), 1.4f);
                        b.Box(Accent, new Vector3(0, 0, 0), new Vector3(w + .4f, 3, w + .4f));
                    }
                    break;

                case Structure.Commercial:
                    if (level == 1)
                    {
                        float w = 6.2f, d = 5f, h = 4f + v;
                        b.Box(Wall, new Vector3(0, 0, .3f), new Vector3(w, h, d));
                        b.Box(Glass, new Vector3(0, .2f, .3f - d * .5f - .02f), new Vector3(w - 1f, 2.4f, .05f));
                        b.Box(Accent, new Vector3(0, 2.8f, .3f - d * .5f - .6f), new Vector3(w - .4f, .2f, 1.2f));
                        b.Box(Roof, new Vector3(0, h, .3f), new Vector3(w + .1f, .3f, d + .1f));
                    }
                    else if (level == 2)
                    {
                        float w = 6.4f, d = 6f, h = 12 + variant * 4;
                        b.Box(Wall, Vector3.zero, new Vector3(w, h, d));
                        Windows(b, Vector3.zero, w, d, h, (int)(h / 3.2f), 1.2f);
                        b.Box(Glass, new Vector3(0, 0, -d * .5f - .02f), new Vector3(w - .8f, 3, .05f));
                        b.Box(Roof, new Vector3(0, h, 0), new Vector3(w - .6f, 1, d - .6f));
                    }
                    else
                    {
                        float w = 6f, h = 38 + variant * 10;
                        b.Box(Glass, Vector3.zero, new Vector3(w, h, w));
                        for (float y = 3.6f; y < h; y += 3.6f) b.Box(Wall, new Vector3(0, y, 0), new Vector3(w + .08f, .35f, w + .08f));
                        b.Box(Wall, new Vector3(0, h, 0), new Vector3(w - 1.5f, 2.5f, w - 1.5f));
                        b.Cylinder(Accent, new Vector3(0, h + 2.5f, 0), .15f, 6, 5);
                    }
                    break;

                case Structure.Industrial:
                    if (level == 1)
                    {
                        float w = 6.4f, d = 5f, h = 4f;
                        b.Box(Wall, new Vector3(0, 0, .5f), new Vector3(w, h, d));
                        b.GableRoof(Roof, new Vector3(0, h, .5f), w + .3f, d + .3f, 1.4f);
                        b.Box(Accent, new Vector3(0, 0, .5f - d * .5f - .05f), new Vector3(2.6f, 3f, .1f));
                    }
                    else if (level == 2)
                    {
                        float w = 6.6f, d = 6f, h = 6f;
                        b.Box(Wall, Vector3.zero, new Vector3(w, h, d));
                        b.Box(Roof, new Vector3(0, h, 0), new Vector3(w + .1f, .4f, d + .1f));
                        b.Cylinder(Accent, new Vector3(w * .3f, h, d * .25f), .55f, 9 + v * 2, 8);
                        b.Box(Accent, new Vector3(0, 0, -d * .5f - .05f), new Vector3(3f, 4f, .1f));
                    }
                    else
                    {
                        float w = 7f, d = 7f, h = 8f;
                        b.Box(Wall, Vector3.zero, new Vector3(w, h, d));
                        b.Box(Roof, new Vector3(0, h, 0), new Vector3(w + .1f, .5f, d + .1f));
                        b.Cylinder(Accent, new Vector3(-w * .3f, h, d * .25f), .7f, 14, 8);
                        b.Cylinder(Accent, new Vector3(w * .05f, h, d * .25f), .7f, 12, 8);
                        b.Cylinder(Roof, new Vector3(w * .3f, h, -d * .2f), 1.3f, 3, 10);
                    }
                    break;

                case Structure.PowerPlant:
                    b.Box(Wall, new Vector3(-3, 0, -2), new Vector3(8, 8, 8));
                    b.Box(Roof, new Vector3(-3, 8, -2), new Vector3(8.2f, .5f, 8.2f));
                    Windows(b, new Vector3(-3, 0, -2), 8, 8, 8, 2, 2f);
                    b.Cylinder(Roof, new Vector3(3.5f, 0, 3.5f), 3.2f, 14, 14);
                    b.Cylinder(Roof, new Vector3(3.5f, 0, -3.5f), 2.6f, 11, 14);
                    b.Cylinder(Accent, new Vector3(-5.5f, 8, 1.5f), .6f, 12, 8);
                    break;

                case Structure.Park:
                    // Slots: 0 leaves, 1 trunk, 2 paths, 3 lawn.
                    b.Flat(3, -T * .5f + .2f, -T * .5f + .2f, T * .5f - .2f, T * .5f - .2f, .05f);
                    b.Flat(2, -.6f, -T * .5f + .2f, .6f, T * .5f - .2f, .07f);
                    b.Flat(2, -T * .5f + .2f, -.6f, T * .5f - .2f, .6f, .07f);
                    CityView.Tree(b, 0, 1, new Vector3(-2.2f, 0, -2.2f), 1);
                    CityView.Tree(b, 0, 1, new Vector3(2.3f, 0, 2.1f), 1.1f);
                    CityView.Tree(b, 0, 1, new Vector3(-2.1f, 0, 2.4f), .8f);
                    CityView.Tree(b, 0, 1, new Vector3(2.2f, 0, -2.3f), .9f);
                    b.Box(1, new Vector3(1.4f, 0, -1.2f), new Vector3(1.4f, .45f, .45f));
                    break;

                case Structure.Police:
                {
                    float w = 6.4f, d = 5.6f, h = 5f;
                    b.Box(Wall, Vector3.zero, new Vector3(w, h, d));
                    b.Box(Accent, new Vector3(0, h - .9f, 0), new Vector3(w + .08f, .5f, d + .08f));
                    b.Box(Roof, new Vector3(0, h, 0), new Vector3(w + .2f, .3f, d + .2f));
                    Windows(b, Vector3.zero, w, d, h - 1, 1, 1.4f);
                    b.Cylinder(Accent, new Vector3(w * .35f, h, d * .3f), .08f, 4, 4);
                    break;
                }

                default: // School, 2 x 1 tiles.
                {
                    float w = 14f, d = 6f, h = 6f;
                    b.Box(Wall, new Vector3(0, 0, .6f), new Vector3(w, h, d));
                    b.GableRoof(Roof, new Vector3(0, h, .6f), w + .5f, d + .6f, 2.2f);
                    Windows(b, new Vector3(0, 0, .6f), w, d, h, 2, 2.2f);
                    b.Box(Accent, new Vector3(0, 0, .6f - d * .5f - .6f), new Vector3(3f, .25f, 1.2f));
                    b.Cylinder(Accent, new Vector3(-w * .5f + .5f, 0, -3.2f), .07f, 7, 4);
                    b.Box(Roof, new Vector3(-w * .5f + 1.1f, 6f, -3.2f), new Vector3(1.1f, .7f, .03f));
                    break;
                }
            }
            var mesh = b.ToMesh();
            mesh.name = $"{type} {level}";
            return mesh;
        }

        /// <summary>Rows of glass panes on all four facades of a box.</summary>
        static void Windows(MeshBuilder b, Vector3 bottomCentre, float w, float d, float h, int floors, float spacing)
        {
            if (floors < 1) return;
            float floorHeight = h / floors;
            const float paneW = .9f, paneH = 1.2f, off = .03f;
            for (int f = 0; f < floors; f++)
            {
                float y0 = bottomCentre.y + f * floorHeight + floorHeight * .35f, y1 = y0 + Mathf.Min(paneH, floorHeight * .5f);
                // South and north.
                int cols = Mathf.Max(1, (int)((w - .8f) / spacing));
                for (int c = 0; c < cols; c++)
                {
                    float cx = bottomCentre.x - (cols - 1) * spacing * .5f + c * spacing;
                    float zs = bottomCentre.z - d * .5f - off, zn = bottomCentre.z + d * .5f + off;
                    b.Quad(Glass, new Vector3(cx - paneW * .5f, y0, zs), new Vector3(cx + paneW * .5f, y0, zs), new Vector3(cx + paneW * .5f, y1, zs), new Vector3(cx - paneW * .5f, y1, zs));
                    b.Quad(Glass, new Vector3(cx + paneW * .5f, y0, zn), new Vector3(cx - paneW * .5f, y0, zn), new Vector3(cx - paneW * .5f, y1, zn), new Vector3(cx + paneW * .5f, y1, zn));
                }
                // West and east.
                int rows = Mathf.Max(1, (int)((d - .8f) / spacing));
                for (int r = 0; r < rows; r++)
                {
                    float cz = bottomCentre.z - (rows - 1) * spacing * .5f + r * spacing;
                    float xw = bottomCentre.x - w * .5f - off, xe = bottomCentre.x + w * .5f + off;
                    b.Quad(Glass, new Vector3(xw, y0, cz + paneW * .5f), new Vector3(xw, y0, cz - paneW * .5f), new Vector3(xw, y1, cz - paneW * .5f), new Vector3(xw, y1, cz + paneW * .5f));
                    b.Quad(Glass, new Vector3(xe, y0, cz - paneW * .5f), new Vector3(xe, y0, cz + paneW * .5f), new Vector3(xe, y1, cz + paneW * .5f), new Vector3(xe, y1, cz - paneW * .5f));
                }
            }
        }
    }
}
