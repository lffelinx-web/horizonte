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
        public const float TileSize = 10;
        const int ChunkSize = 16;
        const float WaterLevel = -.9f;

        // Ground slots, in material order.
        const int SGrass = 0, SForest = 1, SBank = 2, SAsphalt = 3, SSidewalk = 4, SLine = 5, SZoneR = 6, SZoneC = 7, SZoneI = 8, SYard = 9, STrunk = 10, SLeaves = 11, SLamp = 12, SGlow = 13;
        const int SLeavesLight = 14, SField = 15, SFieldRow = 16, SCut = 17;
        static readonly string[] GroundMaterials = { "Grass", "ForestFloor", "Bank", "Asphalt", "Sidewalk", "LaneLine", "ZoneResidential", "ZoneCommercial", "ZoneIndustrial", "Yard", "Trunk", "Leaves", "StreetLamp", "LampGlow", "LeavesLight", "Field", "FieldRow", "StumpCut" };

        /// <summary>Terrain last drawn on each tile, to notice trees being felled.</summary>
        Simulation.Terrain[] drawnTerrain;

        City city;
        int chunksX, chunksY;
        MeshFilter[] chunkFilters;
        readonly MeshBuilder builder = new MeshBuilder(GroundMaterials.Length);
        readonly HashSet<int> dirtyChunks = new HashSet<int>();
        readonly List<int> changes = new List<int>();
        readonly Dictionary<int, GameObject> buildings = new Dictionary<int, GameObject>();
        readonly Dictionary<int, ConstructionSite> sites = new Dictionary<int, ConstructionSite>();
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

        MeshFilter gridFilter;
        readonly MeshBuilder gridMesh = new MeshBuilder(2);
        bool gridVisible;

        /// <summary>
        /// While building, faint lines mark the free tiles near the streets, and the tiles that touch a street (where
        /// buildings can go) get a soft tint, so it is easy to see how much room there is.
        /// </summary>
        public void ShowGrid(bool visible)
        {
            gridVisible = visible;
            if (visible) RebuildGrid();
            else if (gridFilter != null) gridFilter.gameObject.SetActive(false);
        }

        void RebuildGrid()
        {
            if (gridFilter == null)
            {
                var go = new GameObject("Cuadrícula", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterials = Palette.Get("Grid", "GridFront");
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                gridFilter = go.GetComponent<MeshFilter>();
                gridFilter.sharedMesh = new Mesh();
            }
            gridMesh.Clear();
            const int near = 4;
            const float line = .09f, inset = .06f, y = .1f;
            for (int ty = 0; ty < city.Height; ty++)
            for (int tx = 0; tx < city.Width; tx++)
            {
                int i = city.Index(tx, ty);
                if (city.Building[i] != Catalog.None || city.Terrain[i] == Simulation.Terrain.Water) continue;
                bool roadNear = false;
                for (int dy = -near; dy <= near && !roadNear; dy++)
                for (int dx = -near; dx <= near && !roadNear; dx++)
                    roadNear = city.IsRoad(tx + dx, ty + dy);
                if (!roadNear) continue;
                float x0 = tx * TileSize + inset, z0 = ty * TileSize + inset, x1 = (tx + 1) * TileSize - inset, z1 = (ty + 1) * TileSize - inset;
                gridMesh.Flat(0, x0, z0, x1, z0 + line, y);
                gridMesh.Flat(0, x0, z1 - line, x1, z1, y);
                gridMesh.Flat(0, x0, z0 + line, x0 + line, z1 - line, y);
                gridMesh.Flat(0, x1 - line, z0 + line, x1, z1 - line, y);
                bool front = false;
                for (int d = 0; d < 4; d++) front |= city.IsRoad(tx + City.DX[d], ty + City.DY[d]);
                if (front) gridMesh.Flat(1, x0 + .5f, z0 + .5f, x1 - .5f, z1 - .5f, y - .02f);
            }
            gridFilter.sharedMesh = gridMesh.ToMesh(gridFilter.sharedMesh);
            gridFilter.gameObject.SetActive(true);
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
            sites.Clear();
            gridFilter = null;
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
            Relief.Build(transform, city);
            BuildFence();
            drawnTerrain = (Simulation.Terrain[])city.Terrain.Clone();
            for (int i = 0; i < city.Building.Length; i++) RefreshBuilding(i);
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

        /// <summary>Applies the simulation's changes. Call once per frame; dayFraction is how far the current day has gone (0 to 1).</summary>
        public void Sync(float dayFraction = 0)
        {
            foreach (var kv in sites)
            {
                var def = city.DefAt(kv.Key);
                int total = def == null ? 1 : Mathf.Max(1, Catalog.BuildTicks(def));
                float left = Mathf.Max(0, city.Construction[kv.Key] - dayFraction);
                kv.Value.SetProgress(1 - left / total);
            }
            changes.Clear();
            city.DrainChanges(changes);
            foreach (int i in changes)
            {
                int x = i % city.Width, y = i / city.Width;
                if (drawnTerrain[i] == Simulation.Terrain.Forest && city.Terrain[i] == Simulation.Terrain.Stumps) FallingTree.Spawn(transform, TileCentre(x, y), Hash(x, y));
                drawnTerrain[i] = city.Terrain[i];
                // Neighbours matter for road shapes and river banks, which can cross a chunk border.
                MarkChunk(x, y);
                MarkChunk(x - 1, y);
                MarkChunk(x + 1, y);
                MarkChunk(x, y - 1);
                MarkChunk(x, y + 1);
                RefreshBuilding(i);
            }
            if (gridVisible && changes.Count > 0) RebuildGrid();
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
            var def = city.DefAt(i);
            if (terrain == Simulation.Terrain.Water)
            {
                builder.Flat(SBank, x0, z0, x1, z1, WaterLevel - 1.2f);
                if (def == null || !def.Bridge) return;
                // Bridge: a deck at street level on two piers.
                builder.Box(SSidewalk, new Vector3(x0 + TileSize * .5f, -.7f, z0 + TileSize * .5f), new Vector3(TileSize, .7f, TileSize));
                builder.Box(SSidewalk, new Vector3(x0 + TileSize * .5f, WaterLevel - 1.2f, z0 + TileSize * .5f), new Vector3(1.6f, -WaterLevel + .5f, 1.6f));
                BuildRoad(x, y, x0, z0, false);
                return;
            }

            if (def == null)
            {
                switch (terrain)
                {
                    // The relief mesh covers mountain tiles.
                    case Simulation.Terrain.Mountain: return;
                    case Simulation.Terrain.Fertile: BuildField(x, y, x0, z0); break;
                    case Simulation.Terrain.Forest:
                    case Simulation.Terrain.Stumps:
                    case Simulation.Terrain.Sapling:
                        builder.Flat(SForest, x0, z0, x1, z1, 0);
                        BuildTrees(x, y, terrain);
                        break;
                    default: builder.Flat(SGrass, x0, z0, x1, z1, 0); break;
                }
            }
            else if (def.IsRoad) BuildRoad(x, y, x0, z0, def.Id == Catalog.Avenue);
            else
            {
                builder.Flat(SGrass, x0, z0, x1, z1, 0);
                // Homes stand in gardens, parks on grass; the rest on a paved lot until their model brings its own.
                if (def.Category == Category.Housing) builder.Flat(SYard, x0 + .2f, z0 + .2f, x1 - .2f, z1 - .2f, .03f);
                else if (def.Floors > 0 && !factory.HasModel(def)) builder.Flat(SSidewalk, x0, z0, x1, z1, .03f);
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
            return city.IsRoad(nx, ny);
        }

        void BuildRoad(int x, int y, float x0, float z0, bool avenue)
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

            StreetFurniture(x, y, x0, z0, s, w, n, e, curb, sidewalkHeight);

            // Dashed centre lines; none inside junctions. Avenues get a planted median instead.
            if (links > 2) return;
            if (avenue)
            {
                if (s || n || links == 0) builder.Box(SYard, new Vector3(mx, 0, (s ? z0 : mz - .6f) * .5f + (n ? z1 : mz + .6f) * .5f), new Vector3(1.2f, .18f, (n ? z1 : mz + .6f) - (s ? z0 : mz - .6f)));
                if (w || e) builder.Box(SYard, new Vector3((w ? x0 : mx - .6f) * .5f + (e ? x1 : mx + .6f) * .5f, 0, mz), new Vector3((e ? x1 : mx + .6f) - (w ? x0 : mx - .6f), .18f, 1.2f));
                return;
            }
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

        /// <summary>
        /// Along straight stretches, a street lamp every other tile on one side and a small tree on the other,
        /// alternating so the street reads as a lived-in place.
        /// </summary>
        void StreetFurniture(int x, int y, float x0, float z0, bool s, bool w, bool n, bool e, float curb, float h)
        {
            bool northSouth = (s || n) && !w && !e, eastWest = (w || e) && !s && !n;
            if (!northSouth && !eastWest) return;
            float mid = TileSize * .5f, side = curb * .5f;
            int step = northSouth ? y : x;
            // Lamp on one sidewalk, tree on the opposite one, swapping every tile.
            bool lamp = (step & 1) == 0;
            if (northSouth)
            {
                if (lamp) Lamp(new Vector3(x0 + side, h, z0 + mid), Vector3.right);
                else Trees.Broadleaf(builder, SLeavesLight, STrunk, new Vector3(x0 + TileSize - side, h, z0 + mid), .42f, Hash(x, y));
            }
            else
            {
                if (lamp) Lamp(new Vector3(x0 + mid, h, z0 + side), Vector3.forward);
                else Trees.Broadleaf(builder, SLeavesLight, STrunk, new Vector3(x0 + mid, h, z0 + TileSize - side), .42f, Hash(x, y));
            }
        }

        void Lamp(Vector3 at, Vector3 towardRoad)
        {
            const float height = 5.6f, reach = 1.5f;
            builder.Box(SLamp, at, new Vector3(.2f, height, .2f));
            var arm = at + towardRoad * reach * .5f + Vector3.up * (height - .12f);
            builder.Box(SLamp, arm, new Vector3(towardRoad.x != 0 ? reach : .12f, .12f, towardRoad.z != 0 ? reach : .12f));
            var head = at + towardRoad * reach + Vector3.up * (height - .3f);
            builder.Box(SGlow, head, new Vector3(towardRoad.x != 0 ? .7f : .34f, .2f, towardRoad.z != 0 ? .7f : .34f));
        }

        void Sidewalk(float x0, float z0, float x1, float z1, float h)
        {
            builder.Box(SSidewalk, new Vector3((x0 + x1) * .5f, 0, (z0 + z1) * .5f), new Vector3(x1 - x0, h, z1 - z0));
        }

        /// <summary>
        /// Woods: mostly pines with some broadleaf trees, of different sizes. Felled tiles show stumps, and young trees
        /// when the forest grows back.
        /// </summary>
        void BuildTrees(int x, int y, Simulation.Terrain terrain)
        {
            uint h = Hash(x, y);
            int count = 2 + (int)(h % 3);
            for (int k = 0; k < count; k++)
            {
                h = h * 1664525u + 1013904223u;
                float px = (x + .15f + (h & 0xFF) / 255f * .7f) * TileSize;
                float pz = (y + .15f + ((h >> 8) & 0xFF) / 255f * .7f) * TileSize;
                float size = .8f + ((h >> 16) & 0xFF) / 255f * .5f;
                var at = new Vector3(px, 0, pz);
                switch (terrain)
                {
                    case Simulation.Terrain.Stumps: Trees.Stump(builder, STrunk, SCut, at, size); break;
                    case Simulation.Terrain.Sapling: Trees.Sapling(builder, SLeavesLight, STrunk, at, size); break;
                    default:
                        if ((h >> 24) % 10 < 3) Trees.Broadleaf(builder, SLeavesLight, STrunk, at, size * .9f, h);
                        else Trees.Conifer(builder, SLeaves, STrunk, at, size);
                        break;
                }
            }
        }

        public static void Tree(MeshBuilder b, int leaves, int trunk, Vector3 at, float size) => Trees.Conifer(b, leaves, trunk, at, size);

        /// <summary>A ploughed field: crop rows along the tile, slightly different on every tile.</summary>
        void BuildField(int x, int y, float x0, float z0)
        {
            builder.Flat(SField, x0, z0, x0 + TileSize, z0 + TileSize, 0);
            const int rows = 6;
            const float gap = TileSize / rows;
            bool across = ((x / 3 + y / 4) & 1) == 0;
            for (int r = 0; r < rows; r++)
            {
                float a = r * gap + gap * .3f, b = a + gap * .4f;
                if (across) builder.Flat(SFieldRow, x0 + a, z0, x0 + b, z0 + TileSize, .02f);
                else builder.Flat(SFieldRow, x0, z0 + a, x0 + TileSize, z0 + b, .02f);
            }
        }

        /// <summary>Wooden fence around the forest reserve, with a gate where the road from the highway arrives.</summary>
        void BuildFence()
        {
            if (city.ReserveX1 < city.ReserveX0) return;
            var b = new MeshBuilder(2);
            float x0 = city.ReserveX0 * TileSize, x1 = (city.ReserveX1 + 1) * TileSize;
            float z0 = city.ReserveY0 * TileSize, z1 = (city.ReserveY1 + 1) * TileSize;
            float gate0 = city.GateX * TileSize + 2.2f, gate1 = (city.GateX + 1) * TileSize - 2.2f;
            bool Land(Vector3 p)
            {
                int tx = Mathf.Clamp(Mathf.FloorToInt(p.x / TileSize), 0, city.Width - 1), tz = Mathf.Clamp(Mathf.FloorToInt(p.z / TileSize), 0, city.Height - 1);
                return city.Terrain[city.Index(tx, tz)] != Simulation.Terrain.Water;
            }
            void Side(Vector3 from, Vector3 to, bool withGate)
            {
                const float spacing = 2.5f;
                int n = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(from, to) / spacing));
                var dir = (to - from) / n;
                for (int k = 0; k <= n; k++)
                {
                    var p = from + dir * k;
                    bool inGate = withGate && p.x > gate0 && p.x < gate1;
                    if (!inGate && Land(p)) b.Box(0, p, new Vector3(.16f, 1.25f, .16f));
                    if (k == n) continue;
                    var q = p + dir;
                    bool railGate = withGate && q.x > gate0 && p.x < gate1;
                    if (railGate || !Land(p) || !Land(q)) continue;
                    var mid = (p + q) * .5f;
                    var size = new Vector3(Mathf.Abs(dir.x) + .06f, .09f, Mathf.Abs(dir.z) + .06f);
                    b.Box(0, mid + Vector3.up * .45f, size);
                    b.Box(0, mid + Vector3.up * .95f, size);
                }
            }
            Side(new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), true);
            Side(new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), false);
            Side(new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), false);
            Side(new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), false);
            // Gate: two tall posts and a beam with a sign.
            b.Box(0, new Vector3(gate0, 0, z0), new Vector3(.35f, 4.2f, .35f));
            b.Box(0, new Vector3(gate1, 0, z0), new Vector3(.35f, 4.2f, .35f));
            b.Box(0, new Vector3((gate0 + gate1) * .5f, 3.8f, z0), new Vector3(gate1 - gate0 + .6f, .3f, .3f));
            b.Box(1, new Vector3((gate0 + gate1) * .5f, 2.85f, z0 - .1f), new Vector3(3.4f, .85f, .12f));
            var go = new GameObject("Cerca de la reserva", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = b.ToMesh();
            go.GetComponent<MeshRenderer>().sharedMaterials = Palette.Get("Trunk", "StumpCut");
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
            if (sites.TryGetValue(i, out var site))
            {
                Destroy(site.gameObject);
                sites.Remove(i);
            }
            var def = city.DefAt(i);
            if (def == null || def.IsRoad || city.Anchor[i] != i) return;
            var (w, h) = city.Size(i);
            var go = factory.Create(def, city.Rotation[i], Hash(i % city.Width, i / city.Width));
            if (go == null) return;
            int x = i % city.Width, y = i / city.Width;
            go.transform.SetParent(buildingRoot, false);
            // A few centimetres up so the lot drawn in the model covers the ground underneath.
            go.transform.localPosition = new Vector3((x + w * .5f) * TileSize, .05f, (y + h * .5f) * TileSize);
            buildings[i] = go;
            if (city.Construction[i] > 0) sites[i] = ConstructionSite.Create(buildingRoot, go, Hash(x, y));
        }
    }
}
