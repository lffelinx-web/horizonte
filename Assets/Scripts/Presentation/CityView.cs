using System.Collections.Generic;
using Horizonte.Simulation;
using UnityEngine;

namespace Horizonte.Presentation
{
    /// <summary>
    /// Draws the city. Ground, roads and trees are merged into one mesh per 16 x 16 chunk; buildings are separate objects,
    /// either the artist's model from Resources/Buildings or a procedural placeholder.
    /// </summary>
    public sealed class CityView : MonoBehaviour
    {
        /// <summary>Metres per grid tile.</summary>
        public const float TileSize = 8;
        const int ChunkSize = 16;
        const float WaterLevel = -.9f;

        // Ground slots, in material order.
        const int SGrass = 0, SForest = 1, SBank = 2, SAsphalt = 3, SSidewalk = 4, SLine = 5, SZoneR = 6, SZoneC = 7, SZoneI = 8, SYard = 9, STrunk = 10, SLeaves = 11;
        static readonly string[] GroundMaterials = { "Grass", "ForestFloor", "Bank", "Asphalt", "Sidewalk", "LaneLine", "ZoneResidential", "ZoneCommercial", "ZoneIndustrial", "Yard", "Trunk", "Leaves" };

        City city;
        int chunksX, chunksY;
        MeshFilter[] chunkFilters;
        readonly MeshBuilder builder = new MeshBuilder(GroundMaterials.Length);
        readonly HashSet<int> dirtyChunks = new HashSet<int>();
        readonly List<int> changes = new List<int>();
        readonly Dictionary<int, GameObject> buildings = new Dictionary<int, GameObject>();
        Transform buildingRoot;
        BuildingFactory factory;

        MeshFilter selectionFilter;
        readonly MeshBuilder selectionMesh = new MeshBuilder(1);

        /// <summary>Draws a glowing frame on the ground around a building footprint, in tiles.</summary>
        public void SetSelection(int x, int y, int w, int h)
        {
            if (selectionFilter == null)
            {
                var go = new GameObject("Selección", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = Palette.Get("Selection");
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                selectionFilter = go.GetComponent<MeshFilter>();
                selectionFilter.sharedMesh = new Mesh();
            }
            const float inset = .35f, width = .7f, lift = .32f;
            float x0 = x * TileSize + inset, z0 = y * TileSize + inset;
            float x1 = (x + w) * TileSize - inset, z1 = (y + h) * TileSize - inset;
            selectionMesh.Clear();
            selectionMesh.Flat(0, x0, z0, x1, z0 + width, lift);
            selectionMesh.Flat(0, x0, z1 - width, x1, z1, lift);
            selectionMesh.Flat(0, x0, z0 + width, x0 + width, z1 - width, lift);
            selectionMesh.Flat(0, x1 - width, z0 + width, x1, z1 - width, lift);
            selectionFilter.sharedMesh = selectionMesh.ToMesh(selectionFilter.sharedMesh);
            selectionFilter.gameObject.SetActive(true);
        }

        public void ClearSelection()
        {
            if (selectionFilter != null) selectionFilter.gameObject.SetActive(false);
        }

        public static Vector3 TileCentre(int x, int y) => new Vector3((x + .5f) * TileSize, 0, (y + .5f) * TileSize);

        public void Init(City c)
        {
            city = c;
            factory = new BuildingFactory();
            foreach (Transform child in transform) Destroy(child.gameObject);
            selectionFilter = null;
            buildings.Clear();
            buildingRoot = new GameObject("Edificios").transform;
            buildingRoot.SetParent(transform, false);

            chunksX = Mathf.CeilToInt(city.Width / (float)ChunkSize);
            chunksY = Mathf.CeilToInt(city.Height / (float)ChunkSize);
            chunkFilters = new MeshFilter[chunksX * chunksY];
            var mats = Palette.Get(GroundMaterials);
            for (int i = 0; i < chunkFilters.Length; i++)
            {
                var go = new GameObject($"Terreno {i % chunksX},{i / chunksX}", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                go.GetComponent<MeshRenderer>().sharedMaterials = mats;
                chunkFilters[i] = go.GetComponent<MeshFilter>();
                dirtyChunks.Add(i);
            }
            BuildSurroundings();
            for (int i = 0; i < city.Structure.Length; i++) RefreshBuilding(i);
            RebuildDirty();
        }

        /// <summary>Water plane under the map and countryside around it, so the edge of the world is never visible.</summary>
        void BuildSurroundings()
        {
            float w = city.Width * TileSize, h = city.Height * TileSize;
            const float margin = 1200;
            var water = new MeshBuilder(1);
            water.Flat(0, -2, -2, w + 2, h + 2, WaterLevel);
            AddStatic("Agua", water, Palette.Get("Water"));

            var land = new MeshBuilder(1);
            land.Flat(0, -margin, -margin, w + margin, 0, 0);
            land.Flat(0, -margin, h, w + margin, h + margin, 0);
            land.Flat(0, -margin, 0, 0, h, 0);
            land.Flat(0, w, 0, w + margin, h, 0);
            AddStatic("Campo", land, Palette.Get("Grass"));
        }

        void AddStatic(string name, MeshBuilder b, Material m)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = b.ToMesh();
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        /// <summary>Applies the simulation's changes. Call once per frame.</summary>
        public void Sync()
        {
            changes.Clear();
            city.DrainChanges(changes);
            foreach (int i in changes)
            {
                int x = i % city.Width, y = i / city.Width;
                // Neighbours matter for road shapes and river banks, which can cross a chunk border.
                MarkChunk(x, y);
                MarkChunk(x - 1, y);
                MarkChunk(x + 1, y);
                MarkChunk(x, y - 1);
                MarkChunk(x, y + 1);
                RefreshBuilding(i);
            }
            RebuildDirty();
        }

        void MarkChunk(int x, int y)
        {
            if (!city.InBounds(x, y)) return;
            dirtyChunks.Add(y / ChunkSize * chunksX + x / ChunkSize);
        }

        void RebuildDirty()
        {
            foreach (int c in dirtyChunks) BuildChunk(c);
            dirtyChunks.Clear();
        }

        // ---------------------------------------------------------------- ground

        void BuildChunk(int c)
        {
            builder.Clear();
            int cx = c % chunksX, cy = c / chunksX;
            for (int y = cy * ChunkSize; y < Mathf.Min(city.Height, (cy + 1) * ChunkSize); y++)
            for (int x = cx * ChunkSize; x < Mathf.Min(city.Width, (cx + 1) * ChunkSize); x++)
                BuildTile(x, y);
            var mf = chunkFilters[c];
            mf.sharedMesh = builder.ToMesh(mf.sharedMesh);
        }

        void BuildTile(int x, int y)
        {
            int i = city.Index(x, y);
            float x0 = x * TileSize, z0 = y * TileSize, x1 = x0 + TileSize, z1 = z0 + TileSize;
            var terrain = city.Terrain[i];
            if (terrain == Simulation.Terrain.Water)
            {
                builder.Flat(SBank, x0, z0, x1, z1, WaterLevel - 1.2f);
                return;
            }

            var s = city.Structure[i];
            switch (s)
            {
                case Structure.Road: BuildRoad(x, y, x0, z0); break;
                case Structure.Residential:
                case Structure.Commercial:
                case Structure.Industrial:
                    builder.Flat(SGrass, x0, z0, x1, z1, 0);
                    if (city.Level[i] == 0)
                    {
                        int slot = s == Structure.Residential ? SZoneR : s == Structure.Commercial ? SZoneC : SZoneI;
                        builder.Flat(slot, x0 + .5f, z0 + .5f, x1 - .5f, z1 - .5f, .03f);
                    }
                    else builder.Flat(s == Structure.Residential ? SYard : SSidewalk, x0 + .2f, z0 + .2f, x1 - .2f, z1 - .2f, .03f);
                    break;
                case Structure.None:
                    builder.Flat(terrain == Simulation.Terrain.Forest ? SForest : SGrass, x0, z0, x1, z1, 0);
                    if (terrain == Simulation.Terrain.Forest) BuildTrees(x, y);
                    break;
                default:
                    // Service buildings stand on a paved lot.
                    builder.Flat(SGrass, x0, z0, x1, z1, 0);
                    if (s != Structure.Park) builder.Flat(SSidewalk, x0, z0, x1, z1, .03f);
                    break;
            }

            // River banks: earth walls down to the water where land meets it.
            for (int d = 0; d < 4; d++)
            {
                int nx = x + City.DX[d], ny = y + City.DY[d];
                if (!city.InBounds(nx, ny) || city.Terrain[city.Index(nx, ny)] != Simulation.Terrain.Water) continue;
                float bottom = WaterLevel - 1.2f;
                switch (d)
                {
                    case 0: builder.Quad(SBank, new Vector3(x0, bottom, z0), new Vector3(x1, bottom, z0), new Vector3(x1, 0, z0), new Vector3(x0, 0, z0)); break;
                    case 1: builder.Quad(SBank, new Vector3(x0, bottom, z1), new Vector3(x0, bottom, z0), new Vector3(x0, 0, z0), new Vector3(x0, 0, z1)); break;
                    case 2: builder.Quad(SBank, new Vector3(x1, bottom, z1), new Vector3(x0, bottom, z1), new Vector3(x0, 0, z1), new Vector3(x1, 0, z1)); break;
                    case 3: builder.Quad(SBank, new Vector3(x1, bottom, z0), new Vector3(x1, bottom, z1), new Vector3(x1, 0, z1), new Vector3(x1, 0, z0)); break;
                }
            }
        }

        bool RoadLinks(int x, int y, int d)
        {
            int nx = x + City.DX[d], ny = y + City.DY[d];
            // Roads on the edge of the map continue into the outside world.
            if (!city.InBounds(nx, ny)) return true;
            return city.Structure[city.Index(nx, ny)] == Structure.Road;
        }

        void BuildRoad(int x, int y, float x0, float z0)
        {
            const float curb = 1.3f, sidewalkHeight = .12f, lineWidth = .14f;
            float x1 = x0 + TileSize, z1 = z0 + TileSize;
            float mx = x0 + TileSize * .5f, mz = z0 + TileSize * .5f;
            bool s = RoadLinks(x, y, 0), w = RoadLinks(x, y, 1), n = RoadLinks(x, y, 2), e = RoadLinks(x, y, 3);
            int links = (s ? 1 : 0) + (w ? 1 : 0) + (n ? 1 : 0) + (e ? 1 : 0);

            // Asphalt: the centre square plus an arm to each linked side.
            builder.Flat(SAsphalt, x0 + curb, z0 + curb, x1 - curb, z1 - curb, .02f);
            if (s) builder.Flat(SAsphalt, x0 + curb, z0, x1 - curb, z0 + curb, .02f);
            if (n) builder.Flat(SAsphalt, x0 + curb, z1 - curb, x1 - curb, z1, .02f);
            if (w) builder.Flat(SAsphalt, x0, z0 + curb, x0 + curb, z1 - curb, .02f);
            if (e) builder.Flat(SAsphalt, x1 - curb, z0 + curb, x1, z1 - curb, .02f);

            // Raised sidewalks in the corners, and along every side without a link.
            Sidewalk(x0, z0, x0 + curb, z0 + curb, sidewalkHeight);
            Sidewalk(x1 - curb, z0, x1, z0 + curb, sidewalkHeight);
            Sidewalk(x0, z1 - curb, x0 + curb, z1, sidewalkHeight);
            Sidewalk(x1 - curb, z1 - curb, x1, z1, sidewalkHeight);
            if (!s) Sidewalk(x0 + curb, z0, x1 - curb, z0 + curb, sidewalkHeight);
            if (!n) Sidewalk(x0 + curb, z1 - curb, x1 - curb, z1, sidewalkHeight);
            if (!w) Sidewalk(x0, z0 + curb, x0 + curb, z1 - curb, sidewalkHeight);
            if (!e) Sidewalk(x1 - curb, z0 + curb, x1, z1 - curb, sidewalkHeight);

            // Dashed centre lines; none inside junctions.
            if (links > 2) return;
            const float dash = 1.4f, lineY = .04f;
            if (s || n || links == 0)
            {
                float from = s ? z0 : mz, to = n ? z1 : mz;
                if (links == 0) { from = z0 + curb; to = z1 - curb; }
                for (float z = from + .3f; z + dash <= to + .01f; z += dash * 2) builder.Flat(SLine, mx - lineWidth, z, mx + lineWidth, z + dash, lineY);
            }
            if (w || e)
            {
                float from = w ? x0 : mx, to = e ? x1 : mx;
                for (float xx = from + .3f; xx + dash <= to + .01f; xx += dash * 2) builder.Flat(SLine, xx, mz - lineWidth, xx + dash, mz + lineWidth, lineY);
            }
        }

        void Sidewalk(float x0, float z0, float x1, float z1, float h)
        {
            builder.Box(SSidewalk, new Vector3((x0 + x1) * .5f, 0, (z0 + z1) * .5f), new Vector3(x1 - x0, h, z1 - z0));
        }

        void BuildTrees(int x, int y)
        {
            uint h = Hash(x, y);
            int count = 2 + (int)(h % 3);
            for (int k = 0; k < count; k++)
            {
                h = h * 1664525u + 1013904223u;
                float px = (x + .15f + (h & 0xFF) / 255f * .7f) * TileSize;
                float pz = (y + .15f + ((h >> 8) & 0xFF) / 255f * .7f) * TileSize;
                float size = .8f + ((h >> 16) & 0xFF) / 255f * .5f;
                Tree(builder, SLeaves, STrunk, new Vector3(px, 0, pz), size);
            }
        }

        public static void Tree(MeshBuilder b, int leaves, int trunk, Vector3 at, float size)
        {
            b.Cylinder(trunk, at, .22f * size, 1.6f * size, 5);
            b.Cone(leaves, at + Vector3.up * 1.2f * size, 1.9f * size, 3.6f * size, 7);
            b.Cone(leaves, at + Vector3.up * 3f * size, 1.4f * size, 2.8f * size, 7);
        }

        public static uint Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)x * 73856093u ^ (uint)y * 19349663u;
                h ^= h >> 13;
                h *= 0x5bd1e995u;
                h ^= h >> 15;
                return h;
            }
        }

        // ---------------------------------------------------------------- buildings

        void RefreshBuilding(int i)
        {
            if (buildings.TryGetValue(i, out var old))
            {
                Destroy(old);
                buildings.Remove(i);
            }
            var s = city.Structure[i];
            if (s == Structure.None || s == Structure.Road || city.Anchor[i] != i) return;
            var def = Catalog.Get(s);
            if (def.IsZone && city.Level[i] == 0) return;
            var go = factory.Create(s, city.Level[i], city.Rotation[i], Hash(i % city.Width, i / city.Width));
            if (go == null) return;
            int x = i % city.Width, y = i / city.Width;
            go.transform.SetParent(buildingRoot, false);
            go.transform.localPosition = new Vector3((x + def.Width * .5f) * TileSize, 0, (y + def.Height * .5f) * TileSize);
            buildings[i] = go;
        }
    }
}
