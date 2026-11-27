using System.Collections.Generic;
using System.Text.RegularExpressions;
using Horizonte.Simulation;
using UnityEngine;

namespace Horizonte.Presentation
{
    /// <summary>
    /// Makes the object for a building. A prepared model in Resources/Buildings named like the catalog entry
    /// ("casa_basica", "hospital_general"…) wins over the procedural placeholder; letters at the end ("casa_basica_b")
    /// are variants picked at random. Models face south (-Z) with the pivot at the centre of the footprint on the ground.
    /// </summary>
    public sealed class BuildingFactory
    {
        const int Wall = 0, Roof = 1, Glass = 2, Accent = 3;
        const float T = CityView.TileSize;

        readonly Dictionary<string, List<GameObject>> models = new Dictionary<string, List<GameObject>>();
        readonly Dictionary<ushort, Mesh> meshes = new Dictionary<ushort, Mesh>();
        // The building material with the main walls repainted: terracotta, mustard, blue, mint, pink, peach.
        readonly Material[] facades;
        static readonly Regex Variant = new Regex("_[a-z]$");

        public BuildingFactory()
        {
            foreach (var model in Resources.LoadAll<GameObject>("Buildings"))
            {
                string key = Variant.Replace(model.name.ToLowerInvariant(), "");
                if (!models.TryGetValue(key, out var list)) models[key] = list = new List<GameObject>();
                list.Add(model);
            }
            facades = Resources.LoadAll<Material>("Fachadas");
            System.Array.Sort(facades, (a, b) => string.CompareOrdinal(a.name, b.name));
        }

        public bool HasModel(BuildingDef def) => models.ContainsKey(def.Model);

        public GameObject Create(BuildingDef def, int rotation, uint seed)
        {
            GameObject go;
            if (models.TryGetValue(def.Model, out var list))
            {
                go = Object.Instantiate(list[(int)(seed % (uint)list.Count)]);
                Paint(go, def, seed);
            }
            else
            {
                if (!meshes.TryGetValue(def.Id, out var mesh)) meshes[def.Id] = mesh = Placeholder(def);
                go = new GameObject(def.Model, typeof(MeshFilter), typeof(MeshRenderer));
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                go.GetComponent<MeshRenderer>().sharedMaterials = Materials(def);
            }
            go.name = def.Model;
            go.transform.localRotation = Quaternion.Euler(0, rotation * 90, 0);
            return go;
        }

        /// <summary>
        /// Houses and shops get a wall colour of their own, picked from the lot so it stays the same between visits.
        /// About one in four keeps the colour it was modelled with.
        /// </summary>
        void Paint(GameObject go, BuildingDef def, uint seed)
        {
            if (facades.Length == 0 || (def.Category != Category.Housing && def.Category != Category.Commerce)) return;
            int pick = (int)((seed / 7) % (uint)(facades.Length + 2));
            if (pick >= facades.Length) return;
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = facades[pick];
        }

        static Material[] Materials(BuildingDef def)
        {
            if (def.ParkingSpaces > 0) return Palette.Get("Asphalt", "LaneLine", "Sidewalk", "Metal");
            if (def.Standalone) return Palette.Get("ZoneIndustrial", "Trunk", "Glass", "StumpCut");
            if (def.LumberReach > 0) return Palette.Get("Trunk", "RoofResidential", "Glass", "StumpCut");
            switch (def.Category)
            {
                case Category.Housing: return Palette.Get("WallResidential", "RoofResidential", "Glass", "Trim");
                case Category.Commerce: return Palette.Get("WallCommercial", "RoofFlat", "Glass", "AccentCommercial");
                case Category.Industry:
                case Category.Energy: return Palette.Get("WallIndustrial", "RoofIndustrial", "Glass", "Metal");
                case Category.Safety: return Palette.Get("WallPolice", "RoofFlat", "Glass", "Trim");
                case Category.Education: return Palette.Get("WallSchool", "RoofFlat", "Glass", "Trim");
                case Category.Health: return Palette.Get("Trim", "RoofFlat", "Glass", "AccentCommercial");
                case Category.Parks: return def.Floors == 0 ? Palette.Get("Leaves", "Trunk", "Sidewalk", "Yard") : Palette.Get("WallCommercial", "RoofFlat", "Glass", "Leaves");
                default: return Palette.Get("WallCommercial", "RoofFlat", "Glass", "Metal");
            }
        }

        /// <summary>
        /// Parking bays of a lot, in the model's space (front toward -Z): six at the front either side of the entrance and
        /// eight at the back, each with the way its car faces once parked (away from the aisle along the middle).
        /// </summary>
        public static readonly (Vector3 spot, Vector3 facing)[] ParkingSpots = Bays();

        static (Vector3, Vector3)[] Bays()
        {
            var list = new List<(Vector3, Vector3)>();
            foreach (float x in new[] { -8.4f, -6f, -3.6f, 3.6f, 6f, 8.4f }) list.Add((new Vector3(x, 0, -6.6f), Vector3.back));
            for (int k = 0; k < 8; k++) list.Add((new Vector3(-8.4f + k * 2.4f, 0, 6.6f), Vector3.forward));
            return list.ToArray();
        }

        // ---------------------------------------------------------------- placeholders (centred on the footprint, front to -Z)

        /// <summary>A simple stand-in sized to the footprint and floors of the building, until its model is ready.</summary>
        static Mesh Placeholder(BuildingDef def)
        {
            var b = new MeshBuilder(4);
            float w = def.Width * T - 3, d = def.Height * T - 3;
            if (def.ParkingSpaces > 0)
            {
                // Parking: asphalt, painted bays in two rows either side of an aisle, a ticket booth by the entrance;
                // the multi-storey one adds concrete decks on columns. Slots: 0 asphalt, 1 paint, 2 concrete, 3 metal.
                b.Flat(0, -9.7f, -9.7f, 9.7f, 9.7f, .04f);
                foreach (var (spot, facing) in ParkingSpots)
                {
                    float x0 = spot.x - 1.2f, x1 = spot.x + 1.2f, z0 = Mathf.Min(spot.z, spot.z + facing.z * 2.6f) - (facing.z > 0 ? 2.6f : 0), z1 = z0 + 5.2f;
                    b.Flat(1, x0, z0, x0 + .12f, z1, .06f);
                    b.Flat(1, x1 - .12f, z0, x1, z1, .06f);
                }
                b.Box(2, new Vector3(-3.2f, 0, -9.2f), new Vector3(1.4f, 2.4f, 1.2f));
                b.Box(3, new Vector3(-3.2f, 2.4f, -9.2f), new Vector3(1.7f, .15f, 1.5f));
                b.Box(3, new Vector3(-1.9f, .9f, -9.4f), new Vector3(2.2f, .1f, .1f));
                for (int level = 1; level < def.Floors; level++)
                {
                    float y = level * 3.2f;
                    b.Box(2, new Vector3(0, y, 0), new Vector3(19.4f, .35f, 19.4f));
                    b.Box(3, new Vector3(0, y + .35f, -9.6f), new Vector3(19.4f, .9f, .12f));
                    b.Box(3, new Vector3(0, y + .35f, 9.6f), new Vector3(19.4f, .9f, .12f));
                    b.Box(3, new Vector3(-9.6f, y + .35f, 0), new Vector3(.12f, .9f, 19.4f));
                    b.Box(3, new Vector3(9.6f, y + .35f, 0), new Vector3(.12f, .9f, 19.4f));
                }
                if (def.Floors > 1)
                    foreach (float cx in new[] { -9.3f, 0f, 9.3f })
                    foreach (float cz in new[] { -9.3f, 9.3f })
                        b.Box(2, new Vector3(cx, 0, cz), new Vector3(.5f, (def.Floors - 1) * 3.2f, .5f));
            }
            else if (def.Standalone)
            {
                // Logging camp: a canvas tent, a pile of logs with their pale ends, and a chopping block.
                b.Box(Roof, new Vector3(-1.2f, 0, 1.2f), new Vector3(.12f, 2.1f, .12f));
                b.GableRoof(Wall, new Vector3(-1.2f, 0, 1.2f), 3.2f, 3.4f, 2.3f);
                for (int r = 0; r < 3; r++)
                for (int k = 0; k < 3 - r; k++)
                {
                    var at = new Vector3(2.1f, .42f * r, -1.8f + k * .5f + r * .25f);
                    b.Box(Roof, at, new Vector3(2.6f, .42f, .42f));
                    b.Box(Accent, at + new Vector3(1.31f, .02f, 0), new Vector3(.02f, .38f, .38f));
                    b.Box(Accent, at - new Vector3(1.31f, -.02f, 0), new Vector3(.02f, .38f, .38f));
                }
                b.Cylinder(Roof, new Vector3(-.8f, 0, -2.4f), .4f, .55f, 8);
                b.Cylinder(Accent, new Vector3(-.8f, .55f, -2.4f), .38f, .02f, 8);
            }
            else if (def.Floors == 0)
            {
                // Open spaces: lawn, paths and trees. Slots: 0 leaves, 1 trunk, 2 paths, 3 lawn.
                float hw = def.Width * T * .5f - .3f, hd = def.Height * T * .5f - .3f;
                b.Flat(3, -hw, -hd, hw, hd, .05f);
                b.Flat(2, -.8f, -hd, .8f, hd, .07f);
                b.Flat(2, -hw, -.8f, hw, .8f, .07f);
                for (int k = 0; k < def.Width * def.Height * 4; k++)
                {
                    float tx = ((k * 37) % 100 / 100f - .5f) * (hw * 2 - 2), tz = ((k * 61 + 13) % 100 / 100f - .5f) * (hd * 2 - 2);
                    if (Mathf.Abs(tx) < 1.4f || Mathf.Abs(tz) < 1.4f) continue;
                    CityView.Tree(b, 0, 1, new Vector3(tx, 0, tz), .8f + (k % 3) * .15f);
                }
            }
            else if (def.Category == Category.Housing && def.Floors <= 2 && def.Width == 1)
            {
                float h = def.Floors * 3f;
                b.Box(Wall, new Vector3(0, 0, .4f), new Vector3(w * .7f, h, d * .6f));
                b.GableRoof(Roof, new Vector3(0, h, .4f), w * .7f + .5f, d * .6f + .6f, 2f);
                b.Box(Accent, new Vector3(-w * .15f, 0, .4f - d * .3f - .05f), new Vector3(.9f, 2f, .1f));
                Windows(b, new Vector3(0, 0, .4f), w * .7f, d * .6f, h, def.Floors, 1.6f);
            }
            else if (def.Kind == Kind.Power && def.PowerSupply == 0)
            {
                // Substation: a small hut and pylons.
                b.Box(Wall, new Vector3(0, 0, 1.5f), new Vector3(3, 3, 2.5f));
                for (int k = -1; k <= 1; k += 2) b.Box(Accent, new Vector3(k * 2.2f, 0, -1.5f), new Vector3(.3f, 7, .3f));
                b.Box(Accent, new Vector3(0, 6.5f, -1.5f), new Vector3(4.8f, .25f, .25f));
            }
            else
            {
                float h = def.Floors * 3.6f;
                // Big lots keep some yard: the building takes the front part of the footprint.
                float bw = w * (def.Width > 2 ? .8f : 1), bd = d * (def.Height > 2 ? .7f : 1);
                var c = new Vector3(0, 0, -(d - bd) * .5f);
                b.Box(def.Category == Category.Commerce && def.Floors >= 8 ? Glass : Wall, c, new Vector3(bw, h, bd));
                b.Box(Roof, c + new Vector3(0, h, 0), new Vector3(bw + .2f, .4f, bd + .2f));
                Windows(b, c, bw, bd, h, def.Floors, def.Category == Category.Industry ? 3f : 1.8f);
                // A coloured band over the entrance so categories read at a glance.
                b.Box(Accent, c + new Vector3(0, Mathf.Min(h - .6f, 3.2f), -bd * .5f - .3f), new Vector3(Mathf.Min(bw, 8), .5f, .6f));
                if ((def.Category == Category.Industry && def.LumberReach == 0) || (def.Kind == Kind.Power && def.Desirability < 0))
                {
                    b.Cylinder(Accent, c + new Vector3(bw * .3f, h, bd * .2f), .8f, 8 + def.Floors * 2, 8);
                    if (def.Width >= 3) b.Cylinder(Roof, new Vector3(-bw * .3f, 0, d * .5f - 2.5f), 2.2f, 6 + def.Floors * 2, 12);
                }
                if (def.Kind == Kind.Power && def.Desirability >= 0)
                {
                    // Solar, wind and batteries: rows of panels behind the hut.
                    for (int r = 0; r < def.Height * 2; r++)
                        b.Box(Glass, new Vector3(0, .6f, d * .5f - 2 - r * 3.4f), new Vector3(w - 2, .2f, 2f));
                }
            }
            var mesh = b.ToMesh();
            mesh.name = def.Model;
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
