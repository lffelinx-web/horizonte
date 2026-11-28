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
        bool gridVisible, gridForest;

        /// <summary>
        /// While building, faint lines mark the free tiles near the streets, and the tiles that touch a street (where
        /// buildings can go) get a soft tint, so it is easy to see how much room there is.
        /// </summary>
        public void ShowGrid(bool visible, bool forest = false)
        {
            gridVisible = visible;
            gridForest = forest;
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
            var stations = gridForest ? city.Stations() : null;
            void GridTile(int gx, int gy, bool tint)
            {
                float gx0 = gx * TileSize + inset, gz0 = gy * TileSize + inset, gx1 = (gx + 1) * TileSize - inset, gz1 = (gy + 1) * TileSize - inset;
                gridMesh.Flat(0, gx0, gz0, gx1, gz0 + line, y);
                gridMesh.Flat(0, gx0, gz1 - line, gx1, gz1, y);
                gridMesh.Flat(0, gx0, gz0 + line, gx0 + line, gz1 - line, y);
                gridMesh.Flat(0, gx1 - line, gz0 + line, gx1, gz1 - line, y);
                if (tint) gridMesh.Flat(1, gx0 + .5f, gz0 + .5f, gx1 - .5f, gz1 - .5f, y - .02f);
            }
            for (int ty = 0; ty < city.Height; ty++)
            for (int tx = 0; tx < city.Width; tx++)
            {
                int i = city.Index(tx, ty);
                if (gridForest && stations.Count > 0)
                {
                    // Felling: the forest tiles, tinted where a logging camp reaches.
                    if (city.Terrain[i] != Simulation.Terrain.Forest) continue;
                    bool reach = false;
                    foreach (int s in stations)
                    {
                        var (sw, sh) = city.Size(s);
                        float dx = tx + .5f - (s % city.Width + sw * .5f), dy = ty + .5f - (s / city.Width + sh * .5f);
                        reach |= dx * dx + dy * dy <= city.DefAt(s).LumberReach * city.DefAt(s).LumberReach;
                    }
                    if (!reach) continue;
                    GridTile(tx, ty, reach);
                    continue;
                }
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

        /// <summary>
        /// Where people come out of a building: its front door when the model marks one ("Puerta", from the material
        /// named puerta), otherwise the middle of the front a little inside the lot.
        /// </summary>
        public bool Door(int anchor, out Vector3 door)
        {
            door = default;
            if (!buildings.TryGetValue(anchor, out var go) || go == null) return false;
            var mark = go.transform.Find("Puerta");
            int r = city.Rotation[anchor] & 3;
            var front = new Vector3(City.DX[r], 0, City.DY[r]);
            if (mark != null)
            {
                // Just outside the doorway, on the ground.
                door = mark.position + front * .35f;
                door.y = .05f;
                return true;
            }
            var (w, h) = city.Size(anchor);
            float half = (r == 0 || r == 2 ? h : w) * TileSize * .5f;
            door = go.transform.position + front * (half - 2.2f);
            door.y = .05f;
            return true;
        }

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
            chunkNear = new Mesh[chunkFilters.Length];
            chunkFar = new Mesh[chunkFilters.Length];
            felling.Clear();
            warnings.Clear();
            layerFilter = null;
            dropsFilter = null;
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

        /// <summary>Building the simplified version of a chunk, for when it is far from the camera.</summary>
        bool low;
        Mesh[] chunkNear, chunkFar;
        /// <summary>Chunks further than this from the camera use their simplified version.</summary>
        const float FarDistance = 230;

        /// <summary>Each chunk in two versions: full detail, and one with simple trees for when it is far away.</summary>
        void BuildChunk(int c)
        {
            int cx = c % chunksX, cy = c / chunksX;
            for (int pass = 0; pass < 2; pass++)
            {
                low = pass == 1;
                builder.Clear();
                for (int y = cy * ChunkSize; y < Mathf.Min(city.Height, (cy + 1) * ChunkSize); y++)
                for (int x = cx * ChunkSize; x < Mathf.Min(city.Width, (cx + 1) * ChunkSize); x++)
                    BuildTile(x, y);
                if (low) chunkFar[c] = builder.ToMesh(chunkFar[c]);
                else chunkNear[c] = builder.ToMesh(chunkNear[c]);
            }
            low = false;
            chunkFilters[c].sharedMesh = null;
            ChooseDetail(c);
        }

        void ChooseDetail(int c)
        {
            var cam = Camera.main;
            float half = ChunkSize * TileSize * .5f;
            var centre = new Vector3((c % chunksX) * ChunkSize * TileSize + half, 0, (c / chunksX) * ChunkSize * TileSize + half);
            // Distance to the nearest point of the chunk, not its centre, so a chunk right under the camera stays detailed.
            float d = cam == null ? 0 : Mathf.Max(0, Vector3.Distance(cam.transform.position, centre) - half * 1.2f);
            var mesh = d > FarDistance ? chunkFar[c] : chunkNear[c];
            if (chunkFilters[c].sharedMesh != mesh) chunkFilters[c].sharedMesh = mesh;
        }

        void LateUpdate()
        {
            if (chunkFilters == null) return;
            for (int c = 0; c < chunkFilters.Length; c++) ChooseDetail(c);
            // Warning icons face the camera.
            var cam = Camera.main;
            if (cam != null)
                foreach (var w in warnings.Values)
                {
                    if (w == null) continue;
                    w.transform.rotation = cam.transform.rotation;
                    // About the same size on screen whatever the zoom.
                    w.transform.localScale = Vector3.one * Mathf.Clamp(Vector3.Distance(cam.transform.position, w.transform.position) * .028f, 1.6f, 7f);
                }
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

            // Raised sidewalks in the corners, rounded where two streets meet, and along every side without a link.
            Corner(x0, z0, curb, sidewalkHeight, 0, s && w);
            Corner(x1, z0, curb, sidewalkHeight, 1, s && e);
            Corner(x1, z1, curb, sidewalkHeight, 2, n && e);
            Corner(x0, z1, curb, sidewalkHeight, 3, n && w);
            if (!s) Sidewalk(x0 + curb, z0, x1 - curb, z0 + curb, sidewalkHeight);
            if (!n) Sidewalk(x0 + curb, z1 - curb, x1 - curb, z1, sidewalkHeight);
            if (!w) Sidewalk(x0, z0 + curb, x0 + curb, z1 - curb, sidewalkHeight);
            if (!e) Sidewalk(x1 - curb, z0 + curb, x1, z1 - curb, sidewalkHeight);

            StreetFurniture(x, y, x0, z0, s, w, n, e, curb, sidewalkHeight);
            if (links >= 3) Crossings(mx, mz, s, w, n, e);

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
                else StreetTree(new Vector3(x0 + TileSize - side, h, z0 + mid), Hash(x, y));
            }
            else
            {
                if (lamp) Lamp(new Vector3(x0 + mid, h, z0 + side), Vector3.forward);
                else StreetTree(new Vector3(x0 + mid, h, z0 + TileSize - side), Hash(x, y));
            }
        }

        void StreetTree(Vector3 at, uint seed)
        {
            if (low) Trees.Far(builder, SLeavesLight, at, .42f, true);
            else Trees.Broadleaf(builder, SLeavesLight, STrunk, at, .42f, seed);
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

        /// <summary>
        /// Distance from the middle of a junction to the middle of its crossings: the same line people walk along the
        /// sidewalks (Traffic.Kerb), so they cross on the stripes.
        /// </summary>
        public const float CrossingLine = 4.35f;

        /// <summary>Zebra crossings over every street that leaves a junction, at the edge of the junction.</summary>
        void Crossings(float mx, float mz, bool s, bool w, bool n, bool e)
        {
            const float half = 3.7f - .45f, stripe = .5f, step = 1f, inner = CrossingLine - 1.4f, outer = CrossingLine + .35f, y = .045f;
            for (float a = -half; a + stripe <= half + .01f; a += step)
            {
                if (s) builder.Flat(SLine, mx + a, mz - outer, mx + a + stripe, mz - inner, y);
                if (n) builder.Flat(SLine, mx + a, mz + inner, mx + a + stripe, mz + outer, y);
                if (w) builder.Flat(SLine, mx - outer, mz + a, mx - inner, mz + a + stripe, y);
                if (e) builder.Flat(SLine, mx + inner, mz + a, mx + outer, mz + a + stripe, y);
            }
        }

        /// <summary>
        /// A corner of sidewalk. Where the streets on both sides of the corner continue, the kerb is rounded (a quarter
        /// circle) with asphalt filling what the curve leaves.
        /// </summary>
        void Corner(float cx, float cz, float curb, float h, int quadrant, bool rounded)
        {
            // Quadrant: 0 south-west corner of the tile, 1 south-east, 2 north-east, 3 north-west.
            float sx = quadrant == 0 || quadrant == 3 ? 1 : -1, sz = quadrant <= 1 ? 1 : -1;
            float x0 = Mathf.Min(cx, cx + sx * curb), x1 = Mathf.Max(cx, cx + sx * curb);
            float z0 = Mathf.Min(cz, cz + sz * curb), z1 = Mathf.Max(cz, cz + sz * curb);
            if (!rounded)
            {
                Sidewalk(x0, z0, x1, z1, h);
                return;
            }
            builder.Flat(SAsphalt, x0, z0, x1, z1, .02f);
            const int steps = 6;
            var centre = new Vector3(cx, h, cz);
            for (int k = 0; k < steps; k++)
            {
                float a0 = k * Mathf.PI * .5f / steps, a1 = (k + 1) * Mathf.PI * .5f / steps;
                var p0 = centre + new Vector3(sx * Mathf.Cos(a0), 0, sz * Mathf.Sin(a0)) * curb;
                var p1 = centre + new Vector3(sx * Mathf.Cos(a1), 0, sz * Mathf.Sin(a1)) * curb;
                // Top, wound to face up whichever way the quadrant turns.
                if (sx * sz > 0) builder.Triangle(SSidewalk, centre, p0, p1);
                else builder.Triangle(SSidewalk, centre, p1, p0);
                // The kerb face.
                var d0 = p0 - Vector3.up * h;
                var d1 = p1 - Vector3.up * h;
                if (sx * sz > 0) builder.Quad(SSidewalk, d0, d1, p1, p0);
                else builder.Quad(SSidewalk, d1, d0, p0, p1);
            }
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
            // While a lumberjack works the tile its trees stand as separate objects (see BeginFelling).
            if (felling.ContainsKey(city.Index(x, y))) return;
            foreach (var t in TreesOn(x, y))
            {
                switch (terrain)
                {
                    case Simulation.Terrain.Stumps: if (!low) Trees.Stump(builder, STrunk, SCut, t.At, t.Size); break;
                    case Simulation.Terrain.Sapling: Trees.Sapling(builder, SLeavesLight, STrunk, t.At, t.Size); break;
                    default:
                        if (low) Trees.Far(builder, t.Broad ? SLeavesLight : SLeaves, t.At, t.Broad ? t.Size * .9f : t.Size, t.Broad);
                        else if (t.Broad) Trees.Broadleaf(builder, SLeavesLight, STrunk, t.At, t.Size * .9f, t.Seed);
                        else Trees.Conifer(builder, SLeaves, STrunk, t.At, t.Size);
                        break;
                }
            }
        }

        /// <summary>One tree of a forest tile: where it stands, how big it is, and whether it is broadleaf.</summary>
        public struct TreeSpot
        {
            public Vector3 At;
            public float Size;
            public bool Broad;
            public uint Seed;
        }

        /// <summary>The two to four trees of a forest tile, always the same for the same tile.</summary>
        public static List<TreeSpot> TreesOn(int x, int y)
        {
            var list = new List<TreeSpot>(4);
            uint h = Hash(x, y);
            int count = 2 + (int)(h % 3);
            for (int k = 0; k < count; k++)
            {
                h = h * 1664525u + 1013904223u;
                float px = (x + .15f + (h & 0xFF) / 255f * .7f) * TileSize;
                float pz = (y + .15f + ((h >> 8) & 0xFF) / 255f * .7f) * TileSize;
                list.Add(new TreeSpot { At = new Vector3(px, 0, pz), Size = .8f + ((h >> 16) & 0xFF) / 255f * .5f, Broad = (h >> 24) % 10 < 3, Seed = h });
            }
            return list;
        }

        // ---------------------------------------------------------------- felling, tree by tree

        /// <summary>Tiles a lumberjack is working, with their trees as separate objects (null once felled).</summary>
        readonly Dictionary<int, GameObject[]> felling = new Dictionary<int, GameObject[]>();

        /// <summary>Takes the trees of a tile out of the ground mesh so the lumberjack can fell them one by one.</summary>
        public List<TreeSpot> BeginFelling(int i)
        {
            int x = i % city.Width, y = i / city.Width;
            var spots = TreesOn(x, y);
            if (felling.ContainsKey(i)) return spots;
            var trees = new GameObject[spots.Count];
            for (int k = 0; k < spots.Count; k++) trees[k] = StandingTree(spots[k]);
            felling[i] = trees;
            MarkChunk(x, y);
            RebuildDirty();
            return spots;
        }

        /// <summary>Brings one tree of the tile down, falling away from the lumberjack, and leaves its stump.</summary>
        public void FellOne(int i, int k, Vector3 from)
        {
            if (!felling.TryGetValue(i, out var trees) || k < 0 || k >= trees.Length || trees[k] == null) return;
            var tree = trees[k];
            var away = tree.transform.position - from;
            away.y = 0;
            FallingTree.Topple(tree, away.sqrMagnitude > .01f ? away.normalized : Vector3.forward);
            var spot = TreesOn(i % city.Width, i / city.Width)[k];
            var b = new MeshBuilder(2);
            Trees.Stump(b, 0, 1, Vector3.zero, spot.Size);
            var stump = new GameObject("Tocón", typeof(MeshFilter), typeof(MeshRenderer));
            stump.transform.SetParent(tree.transform.parent, false);
            stump.transform.position = spot.At;
            stump.GetComponent<MeshFilter>().sharedMesh = b.ToMesh();
            stump.GetComponent<MeshRenderer>().sharedMaterials = Palette.Get("Trunk", "StumpCut");
            trees[k] = stump;
        }

        /// <summary>The tile is done (or abandoned): the ground mesh draws it again, as stumps or as forest.</summary>
        public void EndFelling(int i)
        {
            if (!felling.TryGetValue(i, out var trees)) return;
            foreach (var t in trees)
                if (t != null && t.GetComponent<FallingTree>() == null) Destroy(t);
            felling.Remove(i);
            // Felled tree by tree already: no extra falling tree when the map notices the stumps.
            drawnTerrain[i] = city.Terrain[i];
            MarkChunk(i % city.Width, i / city.Width);
            RebuildDirty();
        }

        GameObject StandingTree(TreeSpot spot)
        {
            var b = new MeshBuilder(2);
            if (spot.Broad) Trees.Broadleaf(b, 0, 1, Vector3.zero, spot.Size * .9f, spot.Seed);
            else Trees.Conifer(b, 0, 1, Vector3.zero, spot.Size);
            var go = new GameObject("Árbol", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.transform.position = spot.At;
            go.GetComponent<MeshFilter>().sharedMesh = b.ToMesh();
            go.GetComponent<MeshRenderer>().sharedMaterials = Palette.Get(spot.Broad ? "LeavesLight" : "Leaves", "Trunk");
            return go;
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
            if (city.Construction[i] > 0) sites[i] = ConstructionSite.Create(buildingRoot, go, Hash(x, y), w * TileSize, h * TileSize, def.Floors);
            if (layer != Layer.None) Tint(i, go);
        }

        // ---------------------------------------------------------------- information layers

        public enum Layer { None, Power, Water, Sewage, Waste, Services }

        Layer layer;
        float layerRefreshAt;
        MeshFilter layerFilter;
        readonly MeshBuilder layerMesh = new MeshBuilder(1);
        MaterialPropertyBlock block;
        static readonly Color Has = new Color(.45f, 1f, .45f), Lacks = new Color(1f, .38f, .34f), Source = new Color(.45f, .7f, 1f), Partly = new Color(1f, .85f, .35f), Neutral = new Color(.62f, .62f, .66f);

        public Layer CurrentLayer => layer;

        /// <summary>
        /// Shows the city by one service: buildings turn green where they have it, red where they lack it and blue where
        /// it comes from; the streets carrying the network get a coloured line.
        /// </summary>
        public void ShowLayer(Layer l)
        {
            layer = l;
            RefreshLayer();
        }

        void Update()
        {
            if (layer != Layer.None && Time.unscaledTime >= layerRefreshAt) RefreshLayer();
            if (Time.unscaledTime >= warningsAt) RefreshWarnings();
            if (Time.unscaledTime >= dropsAt) BuildDrops();
        }

        // ---------------------------------------------------------------- power poles and service drops

        MeshFilter dropsFilter;
        readonly MeshBuilder dropsMesh = new MeshBuilder(3);
        readonly List<Vector3> poleTops = new List<Vector3>();
        /// <summary>The street wires, each from one pole to the next, with how low it hangs in the middle.</summary>
        readonly List<(Vector3 a, Vector3 b, float sag)> wires = new List<(Vector3, Vector3, float)>();
        float dropsAt;
        const int PoleSpacing = 3;
        const float PoleHeight = 7.4f;

        /// <summary>
        /// The visible grid: a wooden pole every third tile along the straight streets that have power, cables slung
        /// between them, and from the nearest pole a cable to every building with power, ending at a meter box on its
        /// front by the door. Streets without power have no poles and buildings without power no cable.
        /// </summary>
        void BuildDrops()
        {
            dropsAt = Time.unscaledTime + 1.5f;
            if (dropsFilter == null)
            {
                var go = new GameObject("Red eléctrica", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                var mr = go.GetComponent<MeshRenderer>();
                // Wood, cable, metal boxes.
                mr.sharedMaterials = Palette.Get("Trunk", "StreetLamp", "Metal");
                dropsFilter = go.GetComponent<MeshFilter>();
                dropsFilter.sharedMesh = new Mesh();
            }
            dropsMesh.Clear();
            poleTops.Clear();
            wires.Clear();
            for (int y = 0; y < city.Height; y++)
            for (int x = 0; x < city.Width; x++)
            {
                if (!Pole(x, y, out var foot, out bool northSouth)) continue;
                var along = northSouth ? Vector3.forward : Vector3.right;
                var across = northSouth ? Vector3.right : Vector3.forward;
                dropsMesh.Box(0, foot, new Vector3(.24f, PoleHeight, .24f));
                dropsMesh.Box(0, foot + Vector3.up * (PoleHeight - .45f), across * 1.6f + along * .14f + Vector3.up * .13f);
                var top = foot + Vector3.up * (PoleHeight - .3f);
                poleTops.Add(top);
                // Cables to the next pole along the street, sagging between them.
                int nx = x + (northSouth ? 0 : PoleSpacing), ny = y + (northSouth ? PoleSpacing : 0);
                if (!Pole(nx, ny, out var nextFoot, out bool nextNs) || nextNs != northSouth || !StraightBetween(x, y, nx, ny)) continue;
                var nextTop = nextFoot + Vector3.up * (PoleHeight - .3f);
                foreach (float o in new[] { -.65f, .65f })
                {
                    Cable(top + across * o, nextTop + across * o, 1.1f);
                    wires.Add((top + across * o, nextTop + across * o, 1.1f));
                }
            }

            foreach (var kv in buildings)
            {
                int a = kv.Key;
                var def = city.DefAt(a);
                var go = kv.Value;
                if (def == null || go == null || !go.activeInHierarchy || def.PowerUse <= 0 || def.PowerSupply > 0 || !city.Powered[a]) continue;
                if (!Door(a, out var door)) continue;
                int rot = city.Rotation[a] & 3;
                var front = new Vector3(City.DX[rot], 0, City.DY[rot]);
                var right = new Vector3(front.z, 0, -front.x);
                // The meter box on the wall beside the door, and the pipe up to where the cable arrives.
                var wall = door - front * .35f + right * 1.4f;
                float attachHeight = 3.3f;
                dropsMesh.Box(2, wall + Vector3.up * 1.1f + front * .08f, new Vector3(.42f, .55f, .42f) - Abs(front) * .26f);
                dropsMesh.Box(2, wall + Vector3.up * 1.65f + front * .05f, new Vector3(.07f, attachHeight - 1.65f + .3f, .07f));
                dropsMesh.Box(2, wall + Vector3.up * attachHeight + front * .2f, new Vector3(.06f, .06f, .06f) + Abs(front) * .4f);
                var attach = wall + Vector3.up * (attachHeight + .03f) + front * .4f;
                // Straight out from the street wire facing the house, so drops run side by side instead of fanning out
                // from one pole across the neighbours' roofs; the nearest pole only where no wire runs past.
                Vector3 best = default;
                float bestDistance = float.MaxValue;
                foreach (var (wa, wb, sag) in wires)
                {
                    var ab = new Vector2(wb.x - wa.x, wb.z - wa.z);
                    var ap = new Vector2(attach.x - wa.x, attach.z - wa.z);
                    float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(ab.sqrMagnitude, .01f));
                    var p = Vector3.Lerp(wa, wb, t) + Vector3.down * sag * 4 * t * (1 - t);
                    // Only wires the house faces: in front of it, not behind or off to the side.
                    var to = new Vector2(p.x - attach.x, p.z - attach.z);
                    if (Vector2.Dot(to, new Vector2(front.x, front.z)) < 0) continue;
                    float d = to.sqrMagnitude + 4 * Mathf.Pow(Vector2.Dot(to, new Vector2(right.x, right.z)), 2);
                    if (d < bestDistance) { bestDistance = d; best = p; }
                }
                if (bestDistance > 22 * 22)
                    foreach (var p in poleTops)
                    {
                        float d = (p - attach).sqrMagnitude;
                        if (d < bestDistance) { bestDistance = d; best = p; }
                    }
                if (bestDistance > 40 * 40) continue;
                Cable(best, attach, .5f);
            }
            dropsFilter.sharedMesh = dropsMesh.ToMesh(dropsFilter.sharedMesh);
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>A cable between two points, hanging lower in the middle.</summary>
        void Cable(Vector3 a, Vector3 b, float sag)
        {
            const int pieces = 4;
            var previous = a;
            for (int k = 1; k <= pieces; k++)
            {
                float t = k / (float)pieces;
                var p = Vector3.Lerp(a, b, t) + Vector3.down * sag * 4 * t * (1 - t);
                dropsMesh.Segment(1, previous, p, .045f);
                previous = p;
            }
        }

        /// <summary>Whether a pole stands on this tile: every third tile of a straight street that has power.</summary>
        bool Pole(int x, int y, out Vector3 foot, out bool northSouth)
        {
            foot = default;
            northSouth = false;
            if (!city.IsRoad(x, y) || !city.PowerCover[city.Index(x, y)]) return false;
            bool s = RoadLinks(x, y, 0), w = RoadLinks(x, y, 1), n = RoadLinks(x, y, 2), e = RoadLinks(x, y, 3);
            northSouth = (s || n) && !w && !e;
            bool eastWest = (w || e) && !s && !n;
            if (!northSouth && !eastWest) return false;
            if ((northSouth ? y : x) % PoleSpacing != 0) return false;
            const float side = .65f - .35f;
            foot = new Vector3(x * TileSize, .12f, y * TileSize) + (northSouth ? new Vector3(side, 0, 1.2f) : new Vector3(1.2f, 0, side));
            return true;
        }

        /// <summary>Whether every tile from one pole to the next is straight street, so the cable follows it.</summary>
        bool StraightBetween(int x0, int y0, int x1, int y1)
        {
            int dx = System.Math.Sign(x1 - x0), dy = System.Math.Sign(y1 - y0);
            for (int x = x0, y = y0; x != x1 || y != y1; x += dx, y += dy)
            {
                if (!city.IsRoad(x, y)) return false;
                bool s = RoadLinks(x, y, 0), w = RoadLinks(x, y, 1), n = RoadLinks(x, y, 2), e = RoadLinks(x, y, 3);
                if (dx != 0 ? (s || n) : (w || e)) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- warning icons

        /// <summary>Icons floating over buildings that lack something: a street, power, or water once the city needs it.</summary>
        readonly Dictionary<int, GameObject> warnings = new Dictionary<int, GameObject>();
        readonly Dictionary<string, Material> warningLooks = new Dictionary<string, Material>();
        static Mesh quad;
        float warningsAt;

        void RefreshWarnings()
        {
            warningsAt = Time.unscaledTime + .7f;
            var cam = Camera.main;
            bool show = layer == Layer.None && cam != null;
            var wanted = new Dictionary<int, string>();
            if (show)
                foreach (var kv in buildings)
                {
                    int a = kv.Key;
                    var def = city.DefAt(a);
                    if (def == null || city.Construction[a] > 0 || kv.Value == null || !kv.Value.activeInHierarchy) continue;
                    string icon = null;
                    if (!city.Served[a] && !def.Standalone) icon = "road";
                    else if (def.PowerUse > 0 && def.PowerSupply <= 0 && !city.Powered[a]) icon = "bolt";
                    else if (def.Kind == Kind.Housing && city.Population >= Catalog.ServiceThreshold(Service.Water) && !city.Covered(a, Service.Water)) icon = "water";
                    if (icon != null) wanted[a] = icon;
                }
            var gone = new List<int>();
            foreach (var kv in warnings)
                if (!wanted.TryGetValue(kv.Key, out var icon) || kv.Value.name != icon) gone.Add(kv.Key);
            foreach (int a in gone)
            {
                Destroy(warnings[a]);
                warnings.Remove(a);
            }
            foreach (var kv in wanted)
            {
                if (warnings.ContainsKey(kv.Key)) continue;
                var b = buildings[kv.Key];
                var bounds = new Bounds(b.transform.position, Vector3.zero);
                foreach (var r in b.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                warnings[kv.Key] = Warning(kv.Value, new Vector3(bounds.center.x, bounds.max.y + 3, bounds.center.z));
            }
        }

        GameObject Warning(string icon, Vector3 at)
        {
            if (quad == null)
            {
                quad = new Mesh { name = "Aviso" };
                quad.SetVertices(new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) });
                quad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
                quad.SetTriangles(new[] { 0, 3, 2, 0, 2, 1 }, 0);
                quad.RecalculateNormals();
                quad.RecalculateBounds();
            }
            var go = new GameObject(icon);
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            go.transform.localScale = Vector3.one * 4.5f;
            Part(go.transform, "Fondo", WarningLook("circulo"), 0);
            Part(go.transform, "Icono", WarningLook(icon), -.01f).localScale = Vector3.one * .62f;
            return go;
        }

        static Transform Part(Transform parent, string name, Material material, float z)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, 0, z);
            go.GetComponent<MeshFilter>().sharedMesh = quad;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go.transform;
        }

        Material WarningLook(string icon)
        {
            if (warningLooks.TryGetValue(icon, out var m)) return m;
            m = new Material(Palette.Get("Selection")) { name = "Aviso " + icon };
            var sprite = icon == "circulo" ? UiKit.Circle : Icons.Get(icon);
            m.SetTexture("_BaseMap", sprite.texture);
            m.SetColor("_BaseColor", icon == "circulo" ? new Color(.72f, .12f, .1f, .92f) : Color.white);
            m.renderQueue = 3100;
            return warningLooks[icon] = m;
        }

        void RefreshLayer()
        {
            layerRefreshAt = Time.unscaledTime + 1;
            foreach (var kv in buildings) Tint(kv.Key, kv.Value);
            if (layerFilter == null)
            {
                var go = new GameObject("Capa", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                var mr = go.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                layerFilter = go.GetComponent<MeshFilter>();
                layerFilter.sharedMesh = new Mesh();
            }
            layerMesh.Clear();
            var service = LayerService(layer);
            if (layer != Layer.None && layer != Layer.Services)
            {
                // The cables or pipes under the streets that are part of a working network.
                for (int i = 0; i < city.Building.Length; i++)
                {
                    int x = i % city.Width, y = i / city.Width;
                    if (!city.IsRoad(x, y)) continue;
                    bool live = layer == Layer.Power ? city.PowerCover[i] : city.UtilityReaches(service, city.RoadNetwork(i));
                    if (!live) continue;
                    float x0 = x * TileSize, z0 = y * TileSize, m = TileSize * .5f, half = .55f;
                    for (int d = 0; d < 4; d++)
                    {
                        if (!city.IsRoad(x + City.DX[d], y + City.DY[d])) continue;
                        float ex = x0 + m + City.DX[d] * m, ez = z0 + m + City.DY[d] * m;
                        layerMesh.Flat(0, Mathf.Min(x0 + m, ex) - half, Mathf.Min(z0 + m, ez) - half, Mathf.Max(x0 + m, ex) + half, Mathf.Max(z0 + m, ez) + half, .09f);
                    }
                    layerMesh.Flat(0, x0 + m - half, z0 + m - half, x0 + m + half, z0 + m + half, .09f);
                }
            }
            layerFilter.sharedMesh = layerMesh.ToMesh(layerFilter.sharedMesh);
            var colour = layer == Layer.Power ? new Color(1f, .85f, .2f, .9f) : layer == Layer.Water ? new Color(.3f, .7f, 1f, .9f) : layer == Layer.Sewage ? new Color(.7f, .5f, .3f, .9f) : new Color(.5f, .85f, .4f, .9f);
            var mat = layerFilter.GetComponent<MeshRenderer>().sharedMaterial;
            if (mat == null) layerFilter.GetComponent<MeshRenderer>().sharedMaterial = mat = new Material(Palette.Get("Selection")) { name = "Capa" };
            mat.SetColor("_BaseColor", colour);
            layerFilter.gameObject.SetActive(layer != Layer.None);
        }

        static Service LayerService(Layer l) => l == Layer.Water ? Service.Water : l == Layer.Sewage ? Service.Sewage : l == Layer.Waste ? Service.Waste : Service.None;

        void Tint(int anchor, GameObject go)
        {
            if (block == null) block = new MaterialPropertyBlock();
            bool off = layer == Layer.None;
            if (!off) block.SetColor("_BaseColor", StateColour(anchor));
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true)) r.SetPropertyBlock(off ? null : block);
        }

        /// <summary>Colour of a building in the current layer.</summary>
        Color StateColour(int a)
        {
            var def = city.DefAt(a);
            if (def == null) return Neutral;
            switch (layer)
            {
                case Layer.Power:
                    if (def.PowerSupply > 0) return Source;
                    if (def.PowerUse <= 0) return Neutral;
                    return city.Powered[a] ? Has : Lacks;
                case Layer.Water:
                case Layer.Sewage:
                case Layer.Waste:
                    var s = LayerService(layer);
                    if (def.Service == s) return Source;
                    if (Catalog.Use(def, s, city.Occupants[a]) <= 0) return Neutral;
                    return city.Covered(a, s) ? Has : Lacks;
                default:
                    if (def.Kind == Kind.Service && def.Service != Service.None && !Catalog.IsUtility(def.Service)) return Source;
                    if (def.Kind != Kind.Housing) return Neutral;
                    int needed = 0, have = 0;
                    foreach (var service in Catalog.Services)
                    {
                        if (Catalog.IsUtility(service) || city.Population < Catalog.ServiceThreshold(service)) continue;
                        needed++;
                        if (city.Covered(a, service)) have++;
                    }
                    return needed == 0 || have == needed ? Has : have > 0 ? Partly : Lacks;
            }
        }
    }
}
