using System;
using System.Collections.Generic;

namespace Horizonte.Simulation
{
    public enum PlaceResult { Ok, OutOfBounds, Occupied, Water, NoMoney, Locked, NoRoad, NeedsWater }

    /// <summary>
    /// The whole city: a grid of 10 m tiles plus money, time and statistics. One call to <see cref="Step"/> is one day.
    /// The player places every building by hand next to a street; homes fill up with residents over time, shops and
    /// factories offer jobs, plants feed the grid and services cover the homes around them.
    /// Grid coordinates: x grows east, y grows north. Index = y * Width + x.
    /// </summary>
    public sealed class City
    {
        public const int DaysPerMonth = 30;
        public const int MonthsPerYear = 12;
        public const int StartMoney = 25000;

        public readonly int Width, Height;
        public readonly Terrain[] Terrain;
        /// <summary>Catalog id of what stands on each tile (0 for nothing).</summary>
        public readonly ushort[] Building;
        /// <summary>Index of the tile that owns a building (its south-west corner), or -1.</summary>
        public readonly int[] Anchor;
        /// <summary>Direction the building faces: 0 south, 1 west, 2 north, 3 east. Read at the anchor.</summary>
        public readonly byte[] Rotation;
        /// <summary>Days left until a building is finished, read at its anchor; it does not work meanwhile.</summary>
        public readonly byte[] Construction;
        /// <summary>Residents living in a home, read at its anchor.</summary>
        public readonly int[] Occupants;
        public readonly bool[] Powered;
        /// <summary>Road tiles that reach the edge of the map, where the city meets the rest of the world.</summary>
        public readonly bool[] RoadConnected;
        /// <summary>Buildings touching a connected road; without one they cannot work.</summary>
        public readonly bool[] Served;
        /// <summary>Tiles reached by a working plant's or substation's power lines.</summary>
        public readonly bool[] PowerCover;
        /// <summary>-100 to 100.</summary>
        public readonly int[] Desirability;
        /// <summary>Bit (1 &lt;&lt; Service) set where a working service building reaches.</summary>
        public readonly ushort[] Coverage;

        public long Money;
        public int Day;
        public string Name = "Horizonte";
        public int TaxRate = 9;
        public uint RngState;
        /// <summary>Lets every building be placed whatever the population (for showcases and tests).</summary>
        public bool IgnoreRequirements;

        public int Population { get; private set; }
        public int CommercialJobs { get; private set; }
        public int IndustrialJobs { get; private set; }
        public int Jobs => CommercialJobs + IndustrialJobs;
        /// <summary>Residents who can work (60 %).</summary>
        public int Workers => Population * 6 / 10;
        public int Employed => Math.Min(Jobs, Workers);
        public int PowerSupply { get; private set; }
        public int PowerDemand { get; private set; }
        public float DemandResidential { get; private set; }
        public float DemandCommercial { get; private set; }
        public float DemandIndustrial { get; private set; }
        public int LastIncome { get; private set; }
        public int LastExpenses { get; private set; }
        /// <summary>How happy residents are, 0 to 100 (0 while nobody lives in the city).</summary>
        public int Satisfaction { get; private set; }
        /// <summary>Satisfaction added by the town hall and similar buildings.</summary>
        public int CityBonus { get; private set; }
        readonly int[] serviceCoverage = new int[16];

        public int Month => Day / DaysPerMonth % MonthsPerYear;
        public int Year => Day / (DaysPerMonth * MonthsPerYear) + 1;

        /// <summary>Raised on the first day of every month, after the budget.</summary>
        public event Action MonthPassed;

        readonly List<int> changed = new List<int>();
        readonly HashSet<int> changedSet = new HashSet<int>();
        readonly Queue<int> queue = new Queue<int>();

        public City(int width, int height, uint seed = 1)
        {
            Width = width;
            Height = height;
            int n = width * height;
            Terrain = new Terrain[n];
            Building = new ushort[n];
            Anchor = new int[n];
            Rotation = new byte[n];
            Occupants = new int[n];
            Construction = new byte[n];
            Powered = new bool[n];
            RoadConnected = new bool[n];
            Served = new bool[n];
            PowerCover = new bool[n];
            Desirability = new int[n];
            Coverage = new ushort[n];
            for (int i = 0; i < n; i++) Anchor[i] = -1;
            RngState = seed == 0 ? 1u : seed;
            Money = StartMoney;
        }

        public int Index(int x, int y) => y * Width + x;
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public BuildingDef DefAt(int i) => Catalog.Get(Building[i]);
        public bool IsRoad(int x, int y) => InBounds(x, y) && (Catalog.Get(Building[Index(x, y)])?.IsRoad ?? false);
        public bool Covered(int i, Service s) => (Coverage[i] & (1 << (int)s)) != 0;

        /// <summary>Share of residents a service reaches, 0 to 100.</summary>
        public int ServiceCoverage(Service s) => serviceCoverage[(int)s];

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

        public PlaceResult CanPlace(ushort id, int x, int y)
        {
            var def = Catalog.Get(id);
            if (def == null) return PlaceResult.Occupied;
            if (!IgnoreRequirements && Population < def.MinPopulation) return PlaceResult.Locked;
            for (int dy = 0; dy < def.Height; dy++)
            for (int dx = 0; dx < def.Width; dx++)
            {
                if (!InBounds(x + dx, y + dy)) return PlaceResult.OutOfBounds;
                int i = Index(x + dx, y + dy);
                bool water = Terrain[i] == Simulation.Terrain.Water;
                if (water != def.Bridge) return water ? PlaceResult.Water : PlaceResult.Occupied;
                if (Building[i] == Catalog.None) continue;
                // A street can be rebuilt as an avenue and back.
                var there = DefAt(i);
                bool reroad = def.IsRoad && !def.Bridge && there.IsRoad && !there.Bridge && Building[i] != id;
                if (!reroad) return PlaceResult.Occupied;
            }
            if (!def.IsRoad && !Touches(x, y, def.Width, def.Height, IsRoad)) return PlaceResult.NoRoad;
            if (def.NeedsWater && !Touches(x, y, def.Width, def.Height, (tx, ty) => InBounds(tx, ty) && Terrain[Index(tx, ty)] == Simulation.Terrain.Water))
                return PlaceResult.NeedsWater;
            return Money >= PlacementCost(id, x, y) ? PlaceResult.Ok : PlaceResult.NoMoney;
        }

        /// <summary>Whether any tile just outside the footprint passes the test.</summary>
        static bool Touches(int x, int y, int w, int h, Func<int, int, bool> test)
        {
            for (int k = 0; k < w; k++) if (test(x + k, y - 1) || test(x + k, y + h)) return true;
            for (int k = 0; k < h; k++) if (test(x - 1, y + k) || test(x + w, y + k)) return true;
            return false;
        }

        /// <summary>Price of building here, including clearing the forest under it.</summary>
        public int PlacementCost(ushort id, int x, int y)
        {
            var def = Catalog.Get(id);
            int cost = def.Cost;
            for (int dy = 0; dy < def.Height; dy++)
            for (int dx = 0; dx < def.Width; dx++)
                if (InBounds(x + dx, y + dy) && Terrain[Index(x + dx, y + dy)] == Simulation.Terrain.Forest) cost += Catalog.ClearForestCost;
            return cost;
        }

        public PlaceResult Place(ushort id, int x, int y)
        {
            var result = CanPlace(id, x, y);
            if (result != PlaceResult.Ok) return result;
            var def = Catalog.Get(id);
            Money -= PlacementCost(id, x, y);
            int anchor = Index(x, y);
            for (int dy = 0; dy < def.Height; dy++)
            for (int dx = 0; dx < def.Width; dx++)
            {
                int i = Index(x + dx, y + dy);
                if (Terrain[i] == Simulation.Terrain.Forest) Terrain[i] = Simulation.Terrain.Grass;
                Building[i] = id;
                Anchor[i] = anchor;
                Occupants[i] = 0;
                MarkChanged(i);
            }
            Rotation[anchor] = FacingRoad(x, y, def.Width, def.Height);
            Construction[anchor] = (byte)Catalog.BuildDays(def);
            if (def.IsRoad) RefreshRoadNeighbours(x, y);
            UpdateNetworks();
            UpdateStats();
            return PlaceResult.Ok;
        }

        /// <summary>Places on every tile in order; stops when money runs out. Returns how many were placed.</summary>
        public int PlaceMany(ushort id, IEnumerable<(int x, int y)> tiles, out PlaceResult firstProblem)
        {
            firstProblem = PlaceResult.Ok;
            int placed = 0;
            foreach (var (x, y) in tiles)
            {
                var r = Place(id, x, y);
                if (r == PlaceResult.Ok) placed++;
                else
                {
                    if (firstProblem == PlaceResult.Ok) firstProblem = r;
                    if (r == PlaceResult.NoMoney) break;
                }
            }
            return placed;
        }

        /// <summary>Removes the building on the tile (all of it), or clears forest.</summary>
        public bool Bulldoze(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            int i = Index(x, y);
            if (Building[i] == Catalog.None)
            {
                if (Terrain[i] != Simulation.Terrain.Forest || Money < Catalog.ClearForestCost) return false;
                Money -= Catalog.ClearForestCost;
                Terrain[i] = Simulation.Terrain.Grass;
                MarkChanged(i);
                return true;
            }
            if (Money < Catalog.BulldozeCost) return false;
            Money -= Catalog.BulldozeCost;
            var def = DefAt(i);
            int anchor = Anchor[i], ax = anchor % Width, ay = anchor / Width;
            for (int dy = 0; dy < def.Height; dy++)
            for (int dx = 0; dx < def.Width; dx++)
            {
                int j = Index(ax + dx, ay + dy);
                Building[j] = Catalog.None;
                Anchor[j] = -1;
                Rotation[j] = 0;
                Occupants[j] = 0;
                Construction[j] = 0;
                Powered[j] = false;
                Served[j] = false;
                MarkChanged(j);
            }
            if (def.IsRoad) RefreshRoadNeighbours(x, y);
            UpdateNetworks();
            UpdateStats();
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
                if (Building[j] == Catalog.None) continue;
                MarkChanged(j);
                int anchor = Anchor[j];
                var def = DefAt(j);
                if (def.IsRoad) continue;
                byte facing = FacingRoad(anchor % Width, anchor / Width, def.Width, def.Height);
                if (facing == Rotation[anchor]) continue;
                Rotation[anchor] = facing;
                MarkChanged(anchor);
            }
        }

        // South, west, north, east: matches Rotation.
        public static readonly int[] DX = { 0, -1, 0, 1 };
        public static readonly int[] DY = { -1, 0, 1, 0 };

        /// <summary>Picks the side of the footprint that touches a road, south first.</summary>
        byte FacingRoad(int x, int y, int w, int h)
        {
            for (int k = 0; k < w; k++) if (IsRoad(x + k, y - 1)) return 0;
            for (int k = 0; k < h; k++) if (IsRoad(x - 1, y + k)) return 1;
            for (int k = 0; k < w; k++) if (IsRoad(x + k, y + h)) return 2;
            for (int k = 0; k < h; k++) if (IsRoad(x + w, y + k)) return 3;
            return Rotation[Index(x, y)];
        }

        /// <summary>Buildings of one model of the catalog (each counted once).</summary>
        public int Count(string model)
        {
            var def = Catalog.Get(model);
            if (def == null) return 0;
            int count = 0;
            for (int i = 0; i < Building.Length; i++) if (Building[i] == def.Id && Anchor[i] == i) count++;
            return count;
        }

        public int Count(Func<BuildingDef, bool> pick)
        {
            int count = 0;
            for (int i = 0; i < Building.Length; i++)
                if (Building[i] != Catalog.None && Anchor[i] == i && pick(DefAt(i))) count++;
            return count;
        }

        /// <summary>A building works when it touches a connected street and has power (plants only need the street).</summary>
        public bool Working(int anchor)
        {
            var def = DefAt(anchor);
            if (def == null || def.IsRoad) return false;
            return Served[anchor] && Construction[anchor] == 0 && (def.Kind == Kind.Power || Powered[anchor]);
        }

        // ---------------------------------------------------------------- networks

        /// <summary>Recomputes roads, power, desirability and service coverage.</summary>
        public void UpdateNetworks()
        {
            UpdateRoads();
            UpdatePower();
            UpdateDesirability();
            UpdateCoverage();
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

            Array.Clear(Served, 0, n);
            for (int i = 0; i < n; i++)
            {
                if (Anchor[i] != i) continue;
                var def = DefAt(i);
                if (def == null || def.IsRoad) continue;
                bool served = Touches(i % Width, i / Width, def.Width, def.Height, (tx, ty) => InBounds(tx, ty) && RoadConnected[Index(tx, ty)]);
                if (served) Fill(i, Served, true);
            }
        }

        void SeedRoad(int x, int y)
        {
            int i = Index(x, y);
            if (!IsRoad(x, y) || RoadConnected[i]) return;
            RoadConnected[i] = true;
            queue.Enqueue(i);
        }

        /// <summary>Sets a flag on every tile of the building anchored at a tile.</summary>
        void Fill(int anchor, bool[] layer, bool value)
        {
            var def = DefAt(anchor);
            int ax = anchor % Width, ay = anchor / Width;
            for (int dy = 0; dy < def.Height; dy++)
            for (int dx = 0; dx < def.Width; dx++)
                layer[Index(ax + dx, ay + dy)] = value;
        }

        /// <summary>
        /// Power lines reach a radius around each working plant; substations within reach of the grid pass it on further.
        /// Buildings under the lines are powered nearest first until the supply runs out.
        /// </summary>
        void UpdatePower()
        {
            int n = Width * Height;
            var wasPowered = (bool[])Powered.Clone();
            Array.Clear(Powered, 0, n);
            Array.Clear(PowerCover, 0, n);

            var nodes = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (Anchor[i] != i || !Served[i] || Construction[i] > 0) continue;
                if (DefAt(i).PowerReach > 0) nodes.Add(i);
            }
            // Generators are live; substations join when the grid reaches them.
            var live = new bool[nodes.Count];
            var frontier = new Queue<int>();
            int supply = 0;
            for (int k = 0; k < nodes.Count; k++)
            {
                if (DefAt(nodes[k]).PowerSupply <= 0) continue;
                live[k] = true;
                frontier.Enqueue(k);
                supply += DefAt(nodes[k]).PowerSupply;
            }
            while (frontier.Count > 0)
            {
                int a = frontier.Dequeue();
                for (int b = 0; b < nodes.Count; b++)
                {
                    if (live[b]) continue;
                    if (Distance(nodes[a], nodes[b]) > Math.Max(Reach(nodes[a]), Reach(nodes[b]))) continue;
                    live[b] = true;
                    frontier.Enqueue(b);
                }
            }
            var liveNodes = new List<int>();
            for (int k = 0; k < nodes.Count; k++)
            {
                if (!live[k]) continue;
                liveNodes.Add(nodes[k]);
                Fill(nodes[k], Powered, true);
                StampCircle(nodes[k], Reach(nodes[k]), i => PowerCover[i] = true);
            }

            // Consumers under the lines, nearest first.
            var consumers = new List<(float d, int anchor)>();
            int demand = 0;
            for (int i = 0; i < n; i++)
            {
                if (Anchor[i] != i || !Served[i] || Construction[i] > 0) continue;
                var def = DefAt(i);
                if (def.PowerReach > 0 || def.PowerUse <= 0) continue;
                demand += def.PowerUse;
                if (!Any(i, PowerCover)) continue;
                float best = float.MaxValue;
                foreach (int node in liveNodes) best = Math.Min(best, Distance(node, i));
                consumers.Add((best, i));
            }
            consumers.Sort((p, q) => p.d.CompareTo(q.d));
            int remaining = supply;
            foreach (var (_, a) in consumers)
            {
                int use = DefAt(a).PowerUse;
                if (use > remaining) continue;
                remaining -= use;
                Fill(a, Powered, true);
            }
            PowerSupply = supply;
            PowerDemand = demand;
            for (int i = 0; i < n; i++)
                if (Powered[i] != wasPowered[i] && !IsRoad(i % Width, i / Width)) MarkChanged(i);
        }

        int Reach(int anchor) => DefAt(anchor).PowerReach;

        bool Any(int anchor, bool[] layer)
        {
            var def = DefAt(anchor);
            int ax = anchor % Width, ay = anchor / Width;
            for (int dy = 0; dy < def.Height; dy++)
            for (int dx = 0; dx < def.Width; dx++)
                if (layer[Index(ax + dx, ay + dy)]) return true;
            return false;
        }

        /// <summary>Distance in tiles between the centres of two buildings.</summary>
        float Distance(int a, int b)
        {
            var da = DefAt(a);
            var db = DefAt(b);
            float ax = a % Width + da.Width * .5f, ay = a / Width + da.Height * .5f;
            float bx = b % Width + db.Width * .5f, by = b / Width + db.Height * .5f;
            return (float)Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
        }

        /// <summary>Calls back for every tile within a radius of the building's edge.</summary>
        void StampCircle(int anchor, int radius, Action<int> stamp)
        {
            var def = DefAt(anchor);
            float cx = anchor % Width + (def.Width - 1) * .5f, cy = anchor / Width + (def.Height - 1) * .5f;
            float reach = radius + Math.Max(def.Width, def.Height) * .5f;
            int x0 = Math.Max(0, (int)Math.Floor(cx - reach)), x1 = Math.Min(Width - 1, (int)Math.Ceiling(cx + reach));
            int y0 = Math.Max(0, (int)Math.Floor(cy - reach)), y1 = Math.Min(Height - 1, (int)Math.Ceiling(cy + reach));
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy <= reach * reach) stamp(Index(x, y));
            }
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
                var def = DefAt(i);
                if (def == null || def.Desirability == 0 || def.DesireRadius == 0) continue;
                Stamp(i % Width, i / Width, def.Width, def.Height, def.DesireRadius, def.Desirability);
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

        /// <summary>Working service buildings cover the tiles within their radius.</summary>
        void UpdateCoverage()
        {
            Array.Clear(Coverage, 0, Coverage.Length);
            int bonus = 0;
            for (int i = 0; i < Building.Length; i++)
            {
                if (Anchor[i] != i || !Working(i)) continue;
                var def = DefAt(i);
                bonus += def.CityBonus;
                if (def.Service == Service.None || def.Radius <= 0 || Catalog.IsUtility(def.Service)) continue;
                ushort bit = (ushort)(1 << (int)def.Service);
                StampCircle(i, def.Radius, t => Coverage[t] |= bit);
            }
            foreach (var s in Catalog.Utilities) ShareUtility(s);
            CityBonus = Math.Min(15, bonus);
        }

        readonly int[] utilitySupply = new int[16], utilityDemand = new int[16];

        /// <summary>Daily capacity of the working water, sewage or waste buildings.</summary>
        public int UtilitySupply(Service s) => utilitySupply[(int)s];

        /// <summary>Daily use of a utility by the buildings that have a connected street.</summary>
        public int UtilityDemand(Service s) => utilityDemand[(int)s];

        /// <summary>
        /// Like power: pipes and trucks reach a radius around each working plant, and the buildings in reach are served
        /// nearest first until the capacity runs out.
        /// </summary>
        void ShareUtility(Service s)
        {
            int n = Width * Height;
            var reach = new bool[n];
            var providers = new List<int>();
            int supply = 0, demand = 0;
            for (int i = 0; i < n; i++)
            {
                if (Anchor[i] != i || !Working(i)) continue;
                var def = DefAt(i);
                if (def.Service != s || def.Capacity <= 0) continue;
                providers.Add(i);
                supply += def.Capacity;
                StampCircle(i, def.Radius, t => reach[t] = true);
            }
            var consumers = new List<(float d, int anchor, int use)>();
            for (int i = 0; i < n; i++)
            {
                if (Anchor[i] != i || !Served[i] || Construction[i] > 0) continue;
                var def = DefAt(i);
                if (def.IsRoad || def.Service == s) continue;
                int use = Catalog.Use(def, s, Occupants[i]);
                demand += use;
                if (!Any(i, reach)) continue;
                float best = float.MaxValue;
                foreach (int p in providers) best = Math.Min(best, Distance(p, i));
                consumers.Add((best, i, use));
            }
            consumers.Sort((p, q) => p.d.CompareTo(q.d));
            ushort bit = (ushort)(1 << (int)s);
            int remaining = supply;
            foreach (var (_, a, use) in consumers)
            {
                if (use > remaining) continue;
                remaining -= use;
                var def = DefAt(a);
                int ax = a % Width, ay = a / Width;
                for (int dy = 0; dy < def.Height; dy++)
                for (int dx = 0; dx < def.Width; dx++)
                    Coverage[Index(ax + dx, ay + dy)] |= bit;
            }
            utilitySupply[(int)s] = supply;
            utilityDemand[(int)s] = demand;
        }

        // ---------------------------------------------------------------- time

        /// <summary>Advances one day.</summary>
        public void Step()
        {
            UpdateStats();
            UpdateDemand();
            MoveResidents();
            AdvanceConstruction();
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
            var covered = new int[serviceCoverage.Length];
            for (int i = 0; i < Building.Length; i++)
            {
                if (Anchor[i] != i) continue;
                var def = DefAt(i);
                if (def == null) continue;
                if (def.Kind == Kind.Housing)
                {
                    pop += Occupants[i];
                    foreach (var s in Catalog.Services) if (Covered(i, s)) covered[(int)s] += Occupants[i];
                }
                else if (def.Kind == Kind.Jobs && Working(i))
                {
                    if (def.Industrial) ind += def.Jobs;
                    else com += def.Jobs;
                }
            }
            Population = pop;
            CommercialJobs = com;
            IndustrialJobs = ind;
            for (int k = 0; k < serviceCoverage.Length; k++) serviceCoverage[k] = pop == 0 ? 0 : covered[k] * 100 / pop;
            Satisfaction = ComputeSatisfaction();
        }

        /// <summary>Satisfaction of the residents of one home, before city-wide effects.</summary>
        public double HomeSatisfaction(int anchor)
        {
            double s = 60 + Desirability[anchor] * .35;
            if (!Served[anchor]) s -= 20;
            if (!Powered[anchor]) s -= 30;
            foreach (var service in Catalog.Services)
            {
                if (Covered(anchor, service)) s += 3;
                else if (Population >= Catalog.ServiceThreshold(service)) s -= 7;
            }
            return s;
        }

        int ComputeSatisfaction()
        {
            double score = 0, weight = 0;
            for (int i = 0; i < Building.Length; i++)
            {
                if (Anchor[i] != i || Occupants[i] == 0 || DefAt(i).Kind != Kind.Housing) continue;
                score += HomeSatisfaction(i) * Occupants[i];
                weight += Occupants[i];
            }
            if (weight == 0) return 0;
            double avg = score / weight;
            double workers = Workers;
            if (workers > 0 && Jobs < workers) avg -= (1 - Jobs / workers) * 20;
            avg -= (TaxRate - 9) * 2.5;
            avg += CityBonus;
            return (int)Math.Round(Math.Max(1, Math.Min(100, avg)));
        }

        void UpdateDemand()
        {
            // People come for jobs (plus a few pioneers); shops follow people; factories need workers and customers.
            float targetPop = Jobs / .6f + 60;
            DemandResidential = Clamp((targetPop - Population) / Math.Max(40f, targetPop * .4f));
            float targetCom = Population * .25f + 4;
            DemandCommercial = Clamp((targetCom - CommercialJobs) / Math.Max(15f, targetCom * .4f));
            float targetInd = Workers * .5f + 10;
            DemandIndustrial = Clamp((targetInd - IndustrialJobs) / Math.Max(20f, targetInd * .4f));
            // Taxes above 9 % scare people away; below it attracts them.
            float taxFactor = 1 - (TaxRate - 9) * .07f;
            DemandResidential = Clamp(DemandResidential >= 0 ? DemandResidential * taxFactor : DemandResidential * (2 - taxFactor));
        }

        static float Clamp(float v) => v < -1 ? -1 : v > 1 ? 1 : v;

        /// <summary>Homes that work fill up while people want to move in, and empty out when they do not.</summary>
        void MoveResidents()
        {
            for (int i = 0; i < Building.Length; i++)
            {
                if (Anchor[i] != i) continue;
                var def = DefAt(i);
                if (def == null || def.Kind != Kind.Housing) continue;
                int now = Occupants[i], cap = def.Residents;
                if (!Working(i))
                {
                    if (now > 0 && Chance(.3f)) Occupants[i] = Math.Max(0, now - Math.Max(1, cap / 20));
                    continue;
                }
                double mood = Math.Max(0, Math.Min(1.2, HomeSatisfaction(i) / 100));
                if (DemandResidential > 0 && now < cap)
                {
                    double expected = cap * .03 * DemandResidential * (.4 + mood);
                    int arrive = (int)expected + (Chance((float)(expected - Math.Floor(expected))) ? 1 : 0);
                    Occupants[i] = Math.Min(cap, now + arrive);
                }
                else if (now > 0 && (DemandResidential < -.3f || mood < .25) && Chance(.2f))
                {
                    Occupants[i] = Math.Max(0, now - Math.Max(1, cap / 25));
                }
            }
        }

        void AdvanceConstruction()
        {
            for (int i = 0; i < Construction.Length; i++)
            {
                if (Construction[i] == 0) continue;
                Construction[i]--;
                if (Construction[i] == 0) MarkChanged(i);
            }
        }

        /// <summary>Completes every building at once (showcases and tests).</summary>
        public void FinishConstruction()
        {
            for (int i = 0; i < Construction.Length; i++)
            {
                if (Construction[i] == 0) continue;
                Construction[i] = 0;
                MarkChanged(i);
            }
            UpdateNetworks();
            UpdateStats();
        }

        void RunBudget()
        {
            int income = (int)Math.Round((Population + Employed) * TaxRate / 4.0);
            int expenses = 0;
            for (int i = 0; i < Building.Length; i++)
            {
                if (Anchor[i] != i) continue;
                expenses += DefAt(i)?.Upkeep ?? 0;
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
            for (int i = 0; i < Building.Length; i++) MarkChanged(i);
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
