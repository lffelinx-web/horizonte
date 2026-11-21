using System;
using System.Collections.Generic;

namespace Horizonte.Simulation
{
    public enum PlaceResult { Ok, OutOfBounds, Occupied, Water, NoMoney }

    /// <summary>
    /// The whole city: a grid of tiles plus money, time and statistics. One call to <see cref="Step"/> is one day.
    /// Grid coordinates: x grows east, y grows north. Index = y * Width + x.
    /// </summary>
    public sealed class City
    {
        public const int DaysPerMonth = 30;
        public const int MonthsPerYear = 12;
        public const int StartMoney = 25000;

        public readonly int Width, Height;
        public readonly Terrain[] Terrain;
        public readonly Structure[] Structure;
        public readonly byte[] Level;
        /// <summary>Direction the building faces: 0 south, 1 west, 2 north, 3 east.</summary>
        public readonly byte[] Rotation;
        /// <summary>Index of the tile that owns a multi-tile building (its south-west corner), or -1.</summary>
        public readonly int[] Anchor;
        public readonly bool[] Powered;
        /// <summary>A road connected to the map edge is within reach.</summary>
        public readonly bool[] RoadAccess;
        /// <summary>Road tiles that reach the edge of the map, where the city meets the rest of the world.</summary>
        public readonly bool[] RoadConnected;
        /// <summary>-100 to 100.</summary>
        public readonly int[] Desirability;

        public long Money;
        public int Day;
        public int TaxRate = 9;
        public uint RngState;

        public int Population { get; private set; }
        public int CommercialJobs { get; private set; }
        public int IndustrialJobs { get; private set; }
        public int Jobs => CommercialJobs + IndustrialJobs;
        public int PowerSupply { get; private set; }
        public int PowerDemand { get; private set; }
        public float DemandResidential { get; private set; }
        public float DemandCommercial { get; private set; }
        public float DemandIndustrial { get; private set; }
        public int LastIncome { get; private set; }
        public int LastExpenses { get; private set; }

        public int Month => Day / DaysPerMonth % MonthsPerYear;
        public int Year => Day / (DaysPerMonth * MonthsPerYear) + 1;

        /// <summary>Raised on the first day of every month, after the budget.</summary>
        public event Action MonthPassed;

        readonly List<int> changed = new List<int>();
        readonly HashSet<int> changedSet = new HashSet<int>();
        readonly Queue<int> queue = new Queue<int>();
        int[] reach;
        bool[] wasPowered;

        public City(int width, int height, uint seed = 1)
        {
            Width = width;
            Height = height;
            int n = width * height;
            Terrain = new Terrain[n];
            Structure = new Structure[n];
            Level = new byte[n];
            Rotation = new byte[n];
            Anchor = new int[n];
            Powered = new bool[n];
            RoadAccess = new bool[n];
            RoadConnected = new bool[n];
            Desirability = new int[n];
            reach = new int[n];
            for (int i = 0; i < n; i++) Anchor[i] = -1;
            RngState = seed == 0 ? 1u : seed;
            Money = StartMoney;
        }

        public int Index(int x, int y) => y * Width + x;
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        // ---------------------------------------------------------------- changes for the view

        void MarkChanged(int i)
        {
            if (changedSet.Add(i)) changed.Add(i);
        }

        /// <summary>Moves the indices of tiles whose look changed into <paramref name="into"/>.</summary>
        public void DrainChanges(List<int> into)
        {
            into.AddRange(changed);
            changed.Clear();
            changedSet.Clear();
        }

        // ---------------------------------------------------------------- building

        public PlaceResult CanPlace(Structure type, int x, int y)
        {
            var def = Catalog.Get(type);
            if (def == null) return PlaceResult.Occupied;
            for (int dy = 0; dy < def.Height; dy++)
            for (int dx = 0; dx < def.Width; dx++)
            {
                if (!InBounds(x + dx, y + dy)) return PlaceResult.OutOfBounds;
                int i = Index(x + dx, y + dy);
                if (Terrain[i] == Simulation.Terrain.Water) return PlaceResult.Water;
                if (Structure[i] != Simulation.Structure.None) return PlaceResult.Occupied;
            }
            return Money >= PlacementCost(type, x, y) ? PlaceResult.Ok : PlaceResult.NoMoney;
        }

        /// <summary>Price of placing here, including clearing the forest under it.</summary>
        public int PlacementCost(Structure type, int x, int y)
        {
            var def = Catalog.Get(type);
            int cost = def.Cost;
            for (int dy = 0; dy < def.Height; dy++)
            for (int dx = 0; dx < def.Width; dx++)
                if (InBounds(x + dx, y + dy) && Terrain[Index(x + dx, y + dy)] == Simulation.Terrain.Forest) cost += Catalog.ClearForestCost;
            return cost;
        }

        public PlaceResult Place(Structure type, int x, int y)
        {
            var result = CanPlace(type, x, y);
            if (result != PlaceResult.Ok) return result;
            var def = Catalog.Get(type);
            Money -= PlacementCost(type, x, y);
            int anchor = Index(x, y);
            for (int dy = 0; dy < def.Height; dy++)
            for (int dx = 0; dx < def.Width; dx++)
            {
                int i = Index(x + dx, y + dy);
                Terrain[i] = Simulation.Terrain.Grass;
                Structure[i] = type;
                Level[i] = 0;
                Anchor[i] = anchor;
                MarkChanged(i);
            }
            Rotation[anchor] = FacingRoad(x, y, def.Width, def.Height);
            if (type == Simulation.Structure.Road) RefreshRoadNeighbours(x, y);
            UpdateNetworks();
            return PlaceResult.Ok;
        }

        /// <summary>Places on every tile in order; stops when money runs out. Returns how many were placed.</summary>
        public int PlaceMany(Structure type, IEnumerable<(int x, int y)> tiles, out PlaceResult firstProblem)
        {
            firstProblem = PlaceResult.Ok;
            int placed = 0;
            foreach (var (x, y) in tiles)
            {
                var r = Place(type, x, y);
                if (r == PlaceResult.Ok) placed++;
                else
                {
                    if (firstProblem == PlaceResult.Ok) firstProblem = r;
                    if (r == PlaceResult.NoMoney) break;
                }
            }
            return placed;
        }

        /// <summary>Removes whatever is on the tile (the whole building if it spans several), or clears forest.</summary>
        public bool Bulldoze(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            int i = Index(x, y);
            if (Structure[i] == Simulation.Structure.None)
            {
                if (Terrain[i] != Simulation.Terrain.Forest || Money < Catalog.ClearForestCost) return false;
                Money -= Catalog.ClearForestCost;
                Terrain[i] = Simulation.Terrain.Grass;
                MarkChanged(i);
                return true;
            }
            if (Money < Catalog.BulldozeCost) return false;
            Money -= Catalog.BulldozeCost;
            var type = Structure[i];
            var def = Catalog.Get(type);
            int anchor = Anchor[i];
            int ax = anchor % Width, ay = anchor / Width;
            for (int dy = 0; dy < def.Height; dy++)
            for (int dx = 0; dx < def.Width; dx++)
            {
                int j = Index(ax + dx, ay + dy);
                Structure[j] = Simulation.Structure.None;
                Level[j] = 0;
                Anchor[j] = -1;
                Rotation[j] = 0;
                Powered[j] = false;
                MarkChanged(j);
            }
            if (type == Simulation.Structure.Road) RefreshRoadNeighbours(x, y);
            UpdateNetworks();
            return true;
        }

        void RefreshRoadNeighbours(int x, int y)
        {
            // Road pieces change shape with their neighbours, and buildings turn to face new roads.
            for (int d = 0; d < 4; d++)
            {
                int nx = x + DX[d], ny = y + DY[d];
                if (!InBounds(nx, ny)) continue;
                int j = Index(nx, ny);
                if (Structure[j] == Simulation.Structure.None) continue;
                MarkChanged(j);
                int anchor = Anchor[j];
                if (anchor == j && Structure[j] != Simulation.Structure.Road)
                {
                    var def = Catalog.Get(Structure[j]);
                    Rotation[j] = FacingRoad(nx, ny, def.Width, def.Height);
                }
            }
        }

        // South, west, north, east: matches Rotation.
        public static readonly int[] DX = { 0, -1, 0, 1 };
        public static readonly int[] DY = { -1, 0, 1, 0 };

        public bool IsRoad(int x, int y) => InBounds(x, y) && Structure[Index(x, y)] == Simulation.Structure.Road;

        /// <summary>Picks the side of the footprint that touches a road, south first.</summary>
        byte FacingRoad(int x, int y, int w, int h)
        {
            for (int k = 0; k < w; k++) if (IsRoad(x + k, y - 1)) return 0;
            for (int k = 0; k < h; k++) if (IsRoad(x - 1, y + k)) return 1;
            for (int k = 0; k < w; k++) if (IsRoad(x + k, y + h)) return 2;
            for (int k = 0; k < h; k++) if (IsRoad(x + w, y + k)) return 3;
            return Rotation[Index(x, y)];
        }

        // ---------------------------------------------------------------- networks

        /// <summary>Recomputes road connection, access, power and desirability.</summary>
        public void UpdateNetworks()
        {
            UpdateRoads();
            UpdatePower();
            UpdateDesirability();
        }

        void UpdateRoads()
        {
            int n = Width * Height;
            Array.Clear(RoadConnected, 0, n);
            queue.Clear();
            for (int x = 0; x < Width; x++) { SeedRoad(x, 0); SeedRoad(x, Height - 1); }
            for (int y = 0; y < Height; y++) { SeedRoad(0, y); SeedRoad(Width - 1, y); }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % Width, y = i / Width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + DX[d], ny = y + DY[d];
                    if (!IsRoad(nx, ny)) continue;
                    int j = Index(nx, ny);
                    if (RoadConnected[j]) continue;
                    RoadConnected[j] = true;
                    queue.Enqueue(j);
                }
            }

            // Lots up to two tiles from a connected road have access; a building has it if any of its tiles does.
            const int reachTiles = 2;
            for (int i = 0; i < n; i++) reach[i] = RoadConnected[i] ? 0 : int.MaxValue;
            for (int i = 0; i < n; i++) if (RoadConnected[i]) queue.Enqueue(i);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                if (reach[i] >= reachTiles) continue;
                int x = i % Width, y = i / Width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + DX[d], ny = y + DY[d];
                    if (!InBounds(nx, ny)) continue;
                    int j = Index(nx, ny);
                    if (reach[j] <= reach[i] + 1 || Terrain[j] == Simulation.Terrain.Water) continue;
                    reach[j] = reach[i] + 1;
                    queue.Enqueue(j);
                }
            }
            for (int i = 0; i < n; i++) RoadAccess[i] = reach[i] > 0 && reach[i] <= reachTiles;
            for (int i = 0; i < n; i++)
            {
                int a = Anchor[i];
                if (a >= 0 && a != i && RoadAccess[i]) RoadAccess[a] = true;
            }
        }

        void SeedRoad(int x, int y)
        {
            int i = Index(x, y);
            if (Structure[i] != Simulation.Structure.Road || RoadConnected[i]) return;
            RoadConnected[i] = true;
            queue.Enqueue(i);
        }

        /// <summary>Power flows out of each plant through roads and buildings, nearest consumers first.</summary>
        void UpdatePower()
        {
            int n = Width * Height;
            if (wasPowered == null) wasPowered = new bool[n];
            Array.Copy(Powered, wasPowered, n);
            Array.Clear(Powered, 0, n);
            queue.Clear();
            int supply = 0, demand = 0;
            for (int i = 0; i < n; i++)
            {
                if (Structure[i] == Simulation.Structure.PowerPlant)
                {
                    if (Anchor[i] == i) supply += Catalog.Get(Simulation.Structure.PowerPlant).PowerSupply;
                    Powered[i] = true;
                    queue.Enqueue(i);
                }
                else if (Anchor[i] == i) demand += Catalog.PowerUse(Structure[i], Level[i]);
            }
            int remaining = supply;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % Width, y = i / Width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + DX[d], ny = y + DY[d];
                    if (!InBounds(nx, ny)) continue;
                    int j = Index(nx, ny);
                    if (Powered[j] || Structure[j] == Simulation.Structure.None) continue;
                    int anchor = Anchor[j];
                    int use = anchor == j || !Powered[anchor] ? Catalog.PowerUse(Structure[j], Level[anchor]) : 0;
                    if (use > remaining) continue;
                    remaining -= use;
                    // A multi-tile building is powered as a whole.
                    var def = Catalog.Get(Structure[j]);
                    int ax = anchor % Width, ay = anchor / Width;
                    for (int dy = 0; dy < def.Height; dy++)
                    for (int dx = 0; dx < def.Width; dx++)
                    {
                        int k = Index(ax + dx, ay + dy);
                        if (Powered[k]) continue;
                        Powered[k] = true;
                        queue.Enqueue(k);
                    }
                }
            }
            PowerSupply = supply;
            PowerDemand = demand;
            for (int i = 0; i < n; i++)
                if (Powered[i] != wasPowered[i] && Structure[i] != Simulation.Structure.Road) MarkChanged(i);
        }

        void UpdateDesirability()
        {
            int n = Width * Height;
            Array.Clear(Desirability, 0, n);
            for (int i = 0; i < n; i++)
            {
                var t = Terrain[i];
                if (t == Simulation.Terrain.Water) Stamp(i % Width, i / Width, 1, 1, 3, 15);
                else if (t == Simulation.Terrain.Forest) Stamp(i % Width, i / Width, 1, 1, 2, 6);
                if (Anchor[i] != i) continue;
                var def = Catalog.Get(Structure[i]);
                if (def == null || def.Radius == 0) continue;
                // Industry only pollutes once something is built on the lot.
                if (def.IsZone && Level[i] == 0) continue;
                Stamp(i % Width, i / Width, def.Width, def.Height, def.Radius, def.Desirability);
            }
            for (int i = 0; i < n; i++) Desirability[i] = Math.Max(-100, Math.Min(100, Desirability[i]));
        }

        void Stamp(int x, int y, int w, int h, int radius, int strength)
        {
            float cx = x + (w - 1) * .5f, cy = y + (h - 1) * .5f;
            int x0 = Math.Max(0, x - radius), x1 = Math.Min(Width - 1, x + w - 1 + radius);
            int y0 = Math.Max(0, y - radius), y1 = Math.Min(Height - 1, y + h - 1 + radius);
            float reachDistance = radius + Math.Max(w, h) * .5f;
            for (int yy = y0; yy <= y1; yy++)
            for (int xx = x0; xx <= x1; xx++)
            {
                float dx = xx - cx, dy = yy - cy;
                float d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d > reachDistance) continue;
                Desirability[Index(xx, yy)] += (int)Math.Round(strength * (1 - d / (reachDistance + 1)));
            }
        }

        // ---------------------------------------------------------------- time

        /// <summary>Advances one day.</summary>
        public void Step()
        {
            UpdateStats();
            UpdateDemand();
            Grow();
            UpdateNetworks();
            UpdateStats();
            Day++;
            if (Day % DaysPerMonth == 0)
            {
                RunBudget();
                MonthPassed?.Invoke();
            }
        }

        void UpdateStats()
        {
            int pop = 0, com = 0, ind = 0;
            for (int i = 0; i < Structure.Length; i++)
            {
                int cap = Catalog.Capacity(Structure[i], Level[i]);
                switch (Structure[i])
                {
                    case Simulation.Structure.Residential: pop += cap; break;
                    case Simulation.Structure.Commercial: com += cap; break;
                    case Simulation.Structure.Industrial: ind += cap; break;
                }
            }
            Population = pop;
            CommercialJobs = com;
            IndustrialJobs = ind;
        }

        void UpdateDemand()
        {
            // People come for jobs (plus a few pioneers); shops follow people; factories need workers and customers.
            float workers = Population * .6f;
            float targetPop = Jobs / .6f + 80;
            DemandResidential = Clamp((targetPop - Population) / Math.Max(60f, targetPop * .4f));
            float targetCom = Population * .22f;
            DemandCommercial = Clamp((targetCom - CommercialJobs + 6) / Math.Max(20f, targetCom * .4f));
            float targetInd = workers * .55f + 20;
            DemandIndustrial = Clamp((targetInd - IndustrialJobs) / Math.Max(30f, targetInd * .4f));
            // Taxes above 9 % scare people and businesses away; below it attracts them.
            float taxFactor = 1 - (TaxRate - 9) * .07f;
            DemandResidential = AdjustForTax(DemandResidential, taxFactor);
            DemandCommercial = AdjustForTax(DemandCommercial, taxFactor);
            DemandIndustrial = AdjustForTax(DemandIndustrial, taxFactor);
        }

        static float AdjustForTax(float demand, float factor) =>
            Clamp(demand >= 0 ? demand * factor : demand * (2 - factor) - Math.Max(0, 1 - factor) * .2f);

        static float Clamp(float v) => v < -1 ? -1 : v > 1 ? 1 : v;

        public float Demand(Structure zone) =>
            zone == Simulation.Structure.Residential ? DemandResidential :
            zone == Simulation.Structure.Commercial ? DemandCommercial :
            zone == Simulation.Structure.Industrial ? DemandIndustrial : 0;

        void Grow()
        {
            for (int i = 0; i < Structure.Length; i++)
            {
                var type = Structure[i];
                var zoneDef = Catalog.Get(type);
                if (zoneDef == null || !zoneDef.IsZone) continue;
                int level = Level[i];
                bool served = RoadAccess[i] && Powered[i];
                float demand = Demand(type);
                if (!served)
                {
                    // Without road or power, buildings slowly empty out.
                    if (level > 0 && Chance(.02f)) SetLevel(i, level - 1);
                    continue;
                }
                float desire = Desirability[i] / 100f;
                if (type == Simulation.Structure.Industrial) desire = Math.Max(desire, 0);
                if (level < Catalog.MaxLevel && demand > 0)
                {
                    float chance = .06f * demand * (1 + desire) / (1 + level);
                    if (level >= 1 && desire < -.15f) chance = 0;
                    if (level >= 2 && (desire < .1f || Population < 400)) chance = 0;
                    if (Chance(chance)) SetLevel(i, level + 1);
                }
                else if (level > 0 && demand < -.35f && Chance(.015f * -demand))
                {
                    SetLevel(i, level - 1);
                }
            }
        }

        void SetLevel(int i, int level)
        {
            if (Level[i] == 0 && level > 0)
            {
                var def = Catalog.Get(Structure[i]);
                Rotation[i] = FacingRoad(i % Width, i / Width, def.Width, def.Height);
            }
            Level[i] = (byte)level;
            MarkChanged(i);
        }

        void RunBudget()
        {
            int income = (int)Math.Round((Population + Jobs) * TaxRate / 10.0);
            int expenses = 0;
            for (int i = 0; i < Structure.Length; i++)
            {
                if (Anchor[i] != i) continue;
                expenses += Catalog.Get(Structure[i])?.Upkeep ?? 0;
            }
            LastIncome = income;
            LastExpenses = expenses;
            Money += income - expenses;
        }

        public float NextRandom()
        {
            uint s = RngState;
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            RngState = s;
            return (s >> 8) / 16777216f;
        }

        bool Chance(float p) => p > 0 && NextRandom() < p;

        /// <summary>Call after loading or generating so derived data and statistics are filled in.</summary>
        public void Refresh()
        {
            UpdateNetworks();
            UpdateStats();
            UpdateDemand();
            for (int i = 0; i < Structure.Length; i++) MarkChanged(i);
        }

        // ---------------------------------------------------------------- drag shapes

        /// <summary>An L-shaped path: along x first, then along y.</summary>
        public static IEnumerable<(int x, int y)> Line(int x0, int y0, int x1, int y1)
        {
            int sx = Math.Sign(x1 - x0), sy = Math.Sign(y1 - y0);
            int x = x0, y = y0;
            yield return (x, y);
            while (x != x1) { x += sx; yield return (x, y); }
            while (y != y1) { y += sy; yield return (x, y); }
        }

        public static IEnumerable<(int x, int y)> Rect(int x0, int y0, int x1, int y1)
        {
            for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
            for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
                yield return (x, y);
        }
    }
}
