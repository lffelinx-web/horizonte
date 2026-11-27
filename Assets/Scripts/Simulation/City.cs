using System;
using System.Collections.Generic;

namespace Horizonte.Simulation
{
    public enum PlaceResult { Ok, OutOfBounds, Occupied, Water, NoMoney, Locked, NoRoad, NeedsWater, Mountain, Reserve, NoWood, NeedsForest, Trees, NoFront }

    /// <summary>What happens when the player marks a tree to be felled.</summary>
    public enum FellResult { Ok, NotATree, NoStation, TooFar, AlreadyMarked, Busy }

    /// <summary>
    /// The whole city: a grid of 10 m tiles plus money, wood, time and statistics. One call to <see cref="Step"/> is five
    /// game minutes (a tick); a day has 288 and lasts eight minutes of real time.
    /// The player places every building by hand next to a street; homes fill up with residents over time, shops and
    /// factories offer jobs, plants feed the grid and services cover the homes around them.
    /// Grid coordinates: x grows east, y grows north. Index = y * Width + x.
    /// </summary>
    public sealed class City
    {
        public const int TicksPerHour = 12, HoursPerDay = 24, TicksPerDay = TicksPerHour * HoursPerDay;
        /// <summary>Taxes and upkeep are settled every day at six in the morning.</summary>
        public const int BudgetTick = 6 * TicksPerHour;
        /// <summary>New cities start at seven in the morning of the first day.</summary>
        public const int StartTick = 7 * TicksPerHour;
        public const int StartWood = 30;
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
        /// <summary>Ticks left until a building is finished, read at its anchor; it does not work meanwhile.</summary>
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
        public int Wood = StartWood;
        /// <summary>Five-minute steps since the city was founded.</summary>
        public int Tick = StartTick;
        /// <summary>Seed the map was generated from; it also shapes the mountains drawn around it.</summary>
        public uint MapSeed;
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
        /// <summary>Last month's taxes from residents and workers, and from shops and factories.</summary>
        public int LastResidentTax { get; private set; }
        public int LastBusinessTax { get; private set; }
        /// <summary>Last month's upkeep, and the loan instalment paid.</summary>
        public int LastUpkeep { get; private set; }
        public int LastLoanPayment { get; private set; }

        public const int LoanAmount = 10000, LoanDays = 10, MaxLoans = 5;
        /// <summary>Money still owed to the bank (10 % interest included), paid off in daily instalments.</summary>
        public long LoanBalance;
        public int LoanPayment;
        /// <summary>How happy residents are, 0 to 100 (0 while nobody lives in the city).</summary>
        public int Satisfaction { get; private set; }
        /// <summary>Satisfaction added by the town hall and similar buildings.</summary>
        public int CityBonus { get; private set; }
        readonly int[] serviceCoverage = new int[16];

        public int Day => Tick / TicksPerDay;
        /// <summary>Hour of the day, 0 to 24 with fractions.</summary>
        public float Hour => Tick % TicksPerDay / (float)TicksPerHour;
        public int Month => Day / DaysPerMonth % MonthsPerYear;
        public int Year => Day / (DaysPerMonth * MonthsPerYear) + 1;
        /// <summary>Whether the first daily budget has been settled.</summary>
        public bool HasBudget => LastIncome != 0 || LastExpenses != 0;

        /// <summary>Raised every game hour, and every morning after the budget.</summary>
        public event Action HourPassed, DayPassed;

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

        /// <summary>
        /// Footprint of a building on the map. A rectangular one turned to face west or east (odd rotation) lies across:
        /// its width runs north-south.
        /// </summary>
        public static (int w, int h) Footprint(BuildingDef def, bool turned) =>
            turned && def.Width != def.Height ? (def.Height, def.Width) : (def.Width, def.Height);

        /// <summary>Footprint of the building anchored at a tile.</summary>
        public (int w, int h) Size(int anchor)
        {
            var def = DefAt(anchor);
            return def == null ? (1, 1) : Footprint(def, (Rotation[anchor] & 1) == 1);
        }

        /// <summary>
        /// Where a building centred on a tile goes, and whether it should lie across. Square ones never need to;
        /// rectangular ones turn so their front (the side along their width) meets a street.
        /// </summary>
        public bool ChooseTurn(ushort id, int cx, int cy, out int ax, out int ay)
        {
            var def = Catalog.Get(id);
            bool best = false;
            int bestScore = -1;
            ax = cx; ay = cy;
            if (def == null) return false;
            for (int t = 0; t < (def.Width != def.Height ? 2 : 1); t++)
            {
                bool turned = t == 1;
                var (w, h) = Footprint(def, turned);
                int x = cx - (w - 1) / 2, y = cy - (h - 1) / 2;
                var r = CanPlace(id, x, y, turned);
                bool fits = r == PlaceResult.Ok || r == PlaceResult.NoMoney;
                bool front = FrontRoad(x, y, w, h, turned) >= 0;
                int score = (fits ? 2 : 0) + (front ? 1 : 0);
                if (score > bestScore) { bestScore = score; best = turned; ax = x; ay = y; }
            }
            return best;
        }
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

        public PlaceResult CanPlace(ushort id, int x, int y, bool turned = false)
        {
            var def = Catalog.Get(id);
            if (def == null) return PlaceResult.Occupied;
            var (fw, fh) = Footprint(def, turned);
            if (!IgnoreRequirements && Population < def.MinPopulation) return PlaceResult.Locked;
            bool mountain = false, reserve = false, trees = false;
            for (int dy = 0; dy < fh; dy++)
            for (int dx = 0; dx < fw; dx++)
            {
                if (!InBounds(x + dx, y + dy)) return PlaceResult.OutOfBounds;
                int i = Index(x + dx, y + dy);
                bool water = Terrain[i] == Simulation.Terrain.Water;
                if (water != def.Bridge) return water ? PlaceResult.Water : PlaceResult.Occupied;
                mountain |= Terrain[i] == Simulation.Terrain.Mountain;
                reserve |= InReserve(x + dx, y + dy);
                trees |= IsTree(Terrain[i]);
                if (Building[i] == Catalog.None) continue;
                // A street can be rebuilt as an avenue and back.
                var there = DefAt(i);
                bool reroad = def.IsRoad && !def.Bridge && there.IsRoad && !there.Bridge && Building[i] != id;
                if (!reroad) return PlaceResult.Occupied;
            }
            if (mountain) return PlaceResult.Mountain;
            if (reserve) return PlaceResult.Reserve;
            // Trees have to be felled first (which gives their wood); showcases and tests clear them.
            if (trees && !IgnoreRequirements) return PlaceResult.Trees;
            if (!def.IsRoad && !def.Standalone && !Touches(x, y, fw, fh, IsRoad)) return PlaceResult.NoRoad;
            // A rectangular building only goes where its front (the side along its width) meets the street.
            if (!def.IsRoad && !def.Standalone && def.Width != def.Height && FrontRoad(x, y, fw, fh, turned) < 0) return PlaceResult.NoFront;
            if (def.NeedsForest && !Touches(x, y, fw, fh, InReserve)) return PlaceResult.NeedsForest;
            if (!IgnoreRequirements && Wood < def.Wood) return PlaceResult.NoWood;
            if (def.NeedsWater && !Touches(x, y, fw, fh, (tx, ty) => InBounds(tx, ty) && Terrain[Index(tx, ty)] == Simulation.Terrain.Water))
                return PlaceResult.NeedsWater;
            return Money >= PlacementCost(id, x, y, turned) ? PlaceResult.Ok : PlaceResult.NoMoney;
        }

        /// <summary>Whether any tile just outside the footprint passes the test.</summary>
        static bool Touches(int x, int y, int w, int h, Func<int, int, bool> test)
        {
            for (int k = 0; k < w; k++) if (test(x + k, y - 1) || test(x + k, y + h)) return true;
            for (int k = 0; k < h; k++) if (test(x - 1, y + k) || test(x + w, y + k)) return true;
            return false;
        }

        /// <summary>Price of building here, including clearing the forest under it.</summary>
        public int PlacementCost(ushort id, int x, int y, bool turned = false)
        {
            var def = Catalog.Get(id);
            int cost = def.Cost;
            var (fw, fh) = Footprint(def, turned);
            for (int dy = 0; dy < fh; dy++)
            for (int dx = 0; dx < fw; dx++)
                if (InBounds(x + dx, y + dy) && IsTree(Terrain[Index(x + dx, y + dy)])) cost += Catalog.ClearForestCost;
            return cost;
        }

        public PlaceResult Place(ushort id, int x, int y, bool turned = false)
        {
            var result = CanPlace(id, x, y, turned);
            if (result != PlaceResult.Ok) return result;
            var def = Catalog.Get(id);
            Money -= PlacementCost(id, x, y, turned);
            if (!IgnoreRequirements) Wood -= def.Wood;
            int anchor = Index(x, y);
            var (fw, fh) = Footprint(def, turned);
            turned &= def.Width != def.Height;
            for (int dy = 0; dy < fh; dy++)
            for (int dx = 0; dx < fw; dx++)
            {
                int i = Index(x + dx, y + dy);
                if (IsTree(Terrain[i]) || Terrain[i] == Simulation.Terrain.Stumps) Terrain[i] = Simulation.Terrain.Grass;
                Building[i] = id;
                Anchor[i] = anchor;
                Occupants[i] = 0;
                MarkChanged(i);
            }
            Rotation[anchor] = Facing(x, y, fw, fh, def.Width != def.Height, turned);
            Construction[anchor] = (byte)Catalog.BuildTicks(def);
            if (def.IsRoad) RefreshRoadNeighbours(x, y);
            UpdateNetworks();
            UpdateStats();
            return PlaceResult.Ok;
        }

        /// <summary>Places on every tile in order; stops when money runs out. Returns how many were placed.</summary>
        public int PlaceMany(ushort id, IEnumerable<(int x, int y)> tiles, out PlaceResult firstProblem, bool turned = false)
        {
            firstProblem = PlaceResult.Ok;
            int placed = 0;
            foreach (var (x, y) in tiles)
            {
                var r = Place(id, x, y, turned);
                if (r == PlaceResult.Ok) placed++;
                else
                {
                    if (firstProblem == PlaceResult.Ok) firstProblem = r;
                    if (r == PlaceResult.NoMoney || r == PlaceResult.NoWood) break;
                }
            }
            return placed;
        }

        /// <summary>Removes the building on the tile (all of it). Trees are felled by a lumberjack, not bulldozed.</summary>
        public bool Bulldoze(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            int i = Index(x, y);
            if (Building[i] == Catalog.None) return false;
            if (Money < Catalog.BulldozeCost) return false;
            Money -= Catalog.BulldozeCost;
            var def = DefAt(i);
            int anchor = Anchor[i], ax = anchor % Width, ay = anchor / Width;
            var (fw, fh) = Size(anchor);
            for (int dy = 0; dy < fh; dy++)
            for (int dx = 0; dx < fw; dx++)
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
                var (fw, fh) = Size(anchor);
                bool rectangular = def.Width != def.Height;
                byte facing = Facing(anchor % Width, anchor / Width, fw, fh, rectangular, (Rotation[anchor] & 1) == 1);
                if (facing == Rotation[anchor]) continue;
                Rotation[anchor] = facing;
                MarkChanged(anchor);
            }
        }

        // South, west, north, east: matches Rotation.
        public static readonly int[] DX = { 0, -1, 0, 1 };
        public static readonly int[] DY = { -1, 0, 1, 0 };

        /// <summary>
        /// Side of the footprint that touches a road, south first, or -1. A rectangular building may only face along
        /// its front and back: south or north when it lies along, west or east when it lies across.
        /// </summary>
        int FrontRoad(int x, int y, int w, int h, bool? across = null)
        {
            bool ns = across != true, we = across != false;
            if (ns) for (int k = 0; k < w; k++) if (IsRoad(x + k, y - 1)) return 0;
            if (we) for (int k = 0; k < h; k++) if (IsRoad(x - 1, y + k)) return 1;
            if (ns) for (int k = 0; k < w; k++) if (IsRoad(x + k, y + h)) return 2;
            if (we) for (int k = 0; k < h; k++) if (IsRoad(x + w, y + k)) return 3;
            return -1;
        }

        /// <summary>Which way a building turns: toward a road it touches, keeping its current way if there is none.</summary>
        byte Facing(int x, int y, int w, int h, bool rectangular, bool across)
        {
            int side = FrontRoad(x, y, w, h, rectangular ? across : (bool?)null);
            if (side >= 0) return (byte)side;
            byte current = Rotation[Index(x, y)];
            if (!rectangular) return current;
            // Keep the parity that matches how the footprint lies.
            return (byte)(((current & 1) == 1) == across ? current : across ? 1 : 0);
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

        /// <summary>A building works when it touches a connected street and has power (plants and buildings that use none only need the street).</summary>
        public bool Working(int anchor)
        {
            var def = DefAt(anchor);
            if (def == null || def.IsRoad) return false;
            return (Served[anchor] || def.Standalone) && Construction[anchor] == 0 && (def.Kind == Kind.Power || def.PowerUse == 0 || Powered[anchor]);
        }

        // ---------------------------------------------------------------- forest and wood

        /// <summary>The fenced forest reserve (inclusive tile bounds), where wood comes from. Nothing is built inside.</summary>
        public int ReserveX0 = -1, ReserveY0 = -1, ReserveX1 = -2, ReserveY1 = -2;
        /// <summary>Column of the gate in the south fence of the reserve, where the road from the highway arrives.</summary>
        public int GateX = -1;

        public void SetReserve(int x0, int y0, int x1, int y1)
        {
            ReserveX0 = x0;
            ReserveY0 = y0;
            ReserveX1 = x1;
            ReserveY1 = y1;
        }

        public bool InReserve(int x, int y) => x >= ReserveX0 && x <= ReserveX1 && y >= ReserveY0 && y <= ReserveY1;

        public static bool IsTree(Terrain t) => t == Simulation.Terrain.Forest || t == Simulation.Terrain.Sapling;

        /// <summary>Trees the player marked for felling, in the order they were marked.</summary>
        public readonly List<int> Marked = new List<int>();
        /// <summary>Tiles a lumberjack is felling right now (taken off <see cref="Marked"/>, not yet felled).</summary>
        public readonly List<int> Felling = new List<int>();

        /// <summary>Lumberjacks the city has: one at first, more as it grows.</summary>
        public int Lumberjacks => Population >= 300 ? 5 : Population >= 150 ? 4 : Population >= 80 ? 3 : Population >= 30 ? 2 : 1;

        /// <summary>Population that brings the next lumberjack, or 0 when there are already five.</summary>
        public int NextLumberjackAt => Population < 30 ? 30 : Population < 80 ? 80 : Population < 150 ? 150 : Population < 300 ? 300 : 0;

        /// <summary>Marks a tree of the reserve for the lumberjack of a forest station in reach.</summary>
        public FellResult MarkTree(int x, int y)
        {
            if (!InBounds(x, y) || Terrain[Index(x, y)] != Simulation.Terrain.Forest) return FellResult.NotATree;
            int i = Index(x, y);
            if (Marked.Contains(i) || Felling.Contains(i)) return FellResult.AlreadyMarked;
            bool any = false;
            foreach (int s in Stations())
            {
                any = true;
                if (Distance(s, i) <= DefAt(s).LumberReach)
                {
                    // One tile per lumberjack at a time.
                    if (Marked.Count + Felling.Count >= Lumberjacks) return FellResult.Busy;
                    Marked.Add(i);
                    return FellResult.Ok;
                }
            }
            return any ? FellResult.TooFar : FellResult.NoStation;
        }

        /// <summary>Working forest stations whose lumberjack fells marked trees.</summary>
        public List<int> Stations()
        {
            var list = new List<int>();
            for (int i = 0; i < Building.Length; i++)
            {
                if (Anchor[i] != i) continue;
                var def = DefAt(i);
                if (def != null && def.LumberReach > 0 && !def.AutoFell && Working(i)) list.Add(i);
            }
            return list;
        }

        /// <summary>The next marked tree in reach of a station, taken off the list; -1 when there is none.</summary>
        public int TakeMarked(int station)
        {
            var def = DefAt(station);
            if (def == null) return -1;
            for (int k = 0; k < Marked.Count; k++)
            {
                int i = Marked[k];
                if (Terrain[i] != Simulation.Terrain.Forest)
                {
                    Marked.RemoveAt(k--);
                    continue;
                }
                if (Distance(station, i) > def.LumberReach) continue;
                Marked.RemoveAt(k);
                Felling.Add(i);
                return i;
            }
            return -1;
        }

        /// <summary>Cuts down the trees of a tile, leaving stumps that grow back. False if there was no tree.</summary>
        public bool FellTree(int i)
        {
            Felling.Remove(i);
            if (Terrain[i] != Simulation.Terrain.Forest) return false;
            Terrain[i] = Simulation.Terrain.Stumps;
            MarkChanged(i);
            return true;
        }

        /// <summary>A lumberjack gave up on a tile (his station was demolished): it can be marked again.</summary>
        public void StopFelling(int i) => Felling.Remove(i);

        /// <summary>Wood carried into a station by its lumberjack.</summary>
        public void DeliverWood(int amount) => Wood += amount;

        /// <summary>Sells wood for <see cref="Catalog.WoodPrice"/> each. Returns how much was sold.</summary>
        public int SellWood(int amount)
        {
            amount = Math.Max(0, Math.Min(amount, Wood));
            Wood -= amount;
            Money += amount * Catalog.WoodPrice;
            return amount;
        }

        /// <summary>A tree a sawmill's crew is on its way to fell, and the tick it comes down.</summary>
        public struct SawmillJob
        {
            public int Station, Tile, Due;
        }

        /// <summary>Trees the sawmills' crews are working on (the view sends a lumberjack to each).</summary>
        public readonly List<SawmillJob> SawmillJobs = new List<SawmillJob>();

        /// <summary>Hours between a sawmill choosing a tree and felling it: the walk there and the work.</summary>
        public const int SawmillJobTicks = 2 * TicksPerHour;

        /// <summary>Every hour each working sawmill sends its crew to the nearest standing trees of the reserve, two at a time.</summary>
        void RunSawmills()
        {
            for (int s = 0; s < Building.Length; s++)
            {
                if (Anchor[s] != s) continue;
                var def = DefAt(s);
                if (def == null || !def.AutoFell || !Working(s)) continue;
                int busy = 0;
                foreach (var job in SawmillJobs) if (job.Station == s) busy++;
                if (busy >= 2) continue;
                int best = -1;
                float bestDistance = float.MaxValue;
                for (int y = Math.Max(ReserveY0, 0); y <= Math.Min(ReserveY1, Height - 1); y++)
                for (int x = Math.Max(ReserveX0, 0); x <= Math.Min(ReserveX1, Width - 1); x++)
                {
                    int i = Index(x, y);
                    if (Terrain[i] != Simulation.Terrain.Forest || Marked.Contains(i) || SawmillJobs.Exists(j => j.Tile == i)) continue;
                    float d = Distance(s, i);
                    if (d <= def.LumberReach && d < bestDistance)
                    {
                        bestDistance = d;
                        best = i;
                    }
                }
                if (best >= 0) SawmillJobs.Add(new SawmillJob { Station = s, Tile = best, Due = Tick + SawmillJobTicks });
            }
        }

        /// <summary>Trees whose time has come fall, and their wood goes to the city.</summary>
        void FinishSawmillJobs()
        {
            for (int k = SawmillJobs.Count - 1; k >= 0; k--)
            {
                var job = SawmillJobs[k];
                if (Tick < job.Due) continue;
                SawmillJobs.RemoveAt(k);
                if (DefAt(job.Station)?.AutoFell == true && FellTree(job.Tile)) Wood += Catalog.WoodPerSawmillTree;
            }
        }

        /// <summary>
        /// Once an hour: stumps next to standing forest sprout saplings, and saplings grow into forest, each in about a day
        /// and a quarter.
        /// </summary>
        void Regrow()
        {
            for (int i = 0; i < Terrain.Length; i++)
            {
                var t = Terrain[i];
                if (t == Simulation.Terrain.Sapling)
                {
                    if (!Chance(1 / 30f)) continue;
                    Terrain[i] = Simulation.Terrain.Forest;
                    MarkChanged(i);
                }
                // Only the reserve grows back; trees felled elsewhere are gone.
                else if (t == Simulation.Terrain.Stumps && Building[i] == Catalog.None && InReserve(i % Width, i / Width))
                {
                    int x = i % Width, y = i / Width;
                    bool seeds = false;
                    for (int d = 0; d < 4 && !seeds; d++)
                        seeds = InBounds(x + DX[d], y + DY[d]) && IsTree(Terrain[Index(x + DX[d], y + DY[d])]);
                    if (!seeds || !Chance(1 / 30f)) continue;
                    Terrain[i] = Simulation.Terrain.Sapling;
                    MarkChanged(i);
                }
            }
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
                var (sw, sh) = Size(i);
                bool served = Touches(i % Width, i / Width, sw, sh, (tx, ty) => InBounds(tx, ty) && RoadConnected[Index(tx, ty)]);
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
            var (w, h) = Size(anchor);
            int ax = anchor % Width, ay = anchor / Width;
            for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
                layer[Index(ax + dx, ay + dy)] = value;
        }

        // ---------------------------------------------------------------- street networks

        /// <summary>Network of each road tile: streets joined to each other, and through buildings that touch two of them. -1 elsewhere.</summary>
        int[] roadNet = new int[0];
        /// <summary>Network of each building, read at its anchor (the network of the streets it touches), or -1.</summary>
        int[] buildingNet = new int[0];
        readonly List<int> around = new List<int>();

        /// <summary>Network a building belongs to (the streets around it and everything joined to them), or -1.</summary>
        public int NetworkOf(int anchor) => anchor >= 0 && anchor < buildingNet.Length ? buildingNet[anchor] : -1;

        /// <summary>Network of a street tile, or -1.</summary>
        public int RoadNetwork(int i) => i >= 0 && i < roadNet.Length ? roadNet[i] : -1;

        /// <summary>Street tiles touching the footprint of a building.</summary>
        void RoadsAround(int anchor, List<int> into)
        {
            into.Clear();
            var (w, h) = Size(anchor);
            int x = anchor % Width, y = anchor / Width;
            for (int k = 0; k < w; k++) { AddRoad(x + k, y - 1, into); AddRoad(x + k, y + h, into); }
            for (int k = 0; k < h; k++) { AddRoad(x - 1, y + k, into); AddRoad(x + w, y + k, into); }
        }

        void AddRoad(int x, int y, List<int> into)
        {
            if (IsRoad(x, y)) into.Add(Index(x, y));
        }

        /// <summary>
        /// Cables and pipes run under the streets: each group of connected streets is a network, and a building that
        /// touches two groups joins them.
        /// </summary>
        void UpdateStreetNetworks()
        {
            int n = Width * Height;
            if (roadNet.Length != n) { roadNet = new int[n]; buildingNet = new int[n]; }
            for (int i = 0; i < n; i++) { roadNet[i] = -1; buildingNet[i] = -1; }
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                if (roadNet[i] >= 0 || !IsRoad(i % Width, i / Width)) continue;
                roadNet[i] = count;
                queue.Clear();
                queue.Enqueue(i);
                while (queue.Count > 0)
                {
                    int j = queue.Dequeue();
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = j % Width + DX[d], ny = j / Width + DY[d];
                        if (!IsRoad(nx, ny)) continue;
                        int k = Index(nx, ny);
                        if (roadNet[k] >= 0) continue;
                        roadNet[k] = count;
                        queue.Enqueue(k);
                    }
                }
                count++;
            }
            // Buildings that touch two networks join them.
            var parent = new int[count];
            for (int k = 0; k < count; k++) parent[k] = k;
            int Find(int a)
            {
                while (parent[a] != a) a = parent[a] = parent[parent[a]];
                return a;
            }
            for (int i = 0; i < n; i++)
            {
                if (Anchor[i] != i || DefAt(i) == null || DefAt(i).IsRoad) continue;
                RoadsAround(i, around);
                for (int k = 1; k < around.Count; k++)
                {
                    int a = Find(roadNet[around[0]]), b = Find(roadNet[around[k]]);
                    if (a != b) parent[b] = a;
                }
            }
            for (int i = 0; i < n; i++) if (roadNet[i] >= 0) roadNet[i] = Find(roadNet[i]);
            for (int i = 0; i < n; i++)
            {
                if (Anchor[i] != i || DefAt(i) == null || DefAt(i).IsRoad) continue;
                RoadsAround(i, around);
                if (around.Count > 0) buildingNet[i] = roadNet[around[0]];
            }
        }

        /// <summary>Steps along the streets from the given street tiles to every street tile (-1 where it cannot reach).</summary>
        int[] StreetDistance(List<int> sources, int max = int.MaxValue)
        {
            var dist = new int[Width * Height];
            for (int i = 0; i < dist.Length; i++) dist[i] = -1;
            queue.Clear();
            foreach (int s in sources)
            {
                if (dist[s] == 0) continue;
                dist[s] = 0;
                queue.Enqueue(s);
            }
            while (queue.Count > 0)
            {
                int j = queue.Dequeue();
                if (dist[j] >= max) continue;
                for (int d = 0; d < 4; d++)
                {
                    int nx = j % Width + DX[d], ny = j / Width + DY[d];
                    if (!IsRoad(nx, ny)) continue;
                    int k = Index(nx, ny);
                    if (dist[k] >= 0) continue;
                    dist[k] = dist[j] + 1;
                    queue.Enqueue(k);
                }
            }
            return dist;
        }

        /// <summary>Distance along the streets from a building to the nearest street reached, or -1.</summary>
        int BuildingDistance(int anchor, int[] dist)
        {
            RoadsAround(anchor, around);
            int best = -1;
            foreach (int r in around)
                if (dist[r] >= 0 && (best < 0 || dist[r] < best)) best = dist[r];
            return best;
        }

        /// <summary>
        /// Power runs along the streets: the plants feed the buildings of their network, nearest first along the streets,
        /// until the supply runs out. A network without a plant has no power.
        /// </summary>
        void UpdatePower()
        {
            int n = Width * Height;
            var wasPowered = (bool[])Powered.Clone();
            Array.Clear(Powered, 0, n);
            Array.Clear(PowerCover, 0, n);
            UpdateStreetNetworks();

            var supplyOf = new Dictionary<int, int>();
            var sources = new List<int>();
            int supply = 0;
            for (int i = 0; i < n; i++)
            {
                if (Anchor[i] != i || !Served[i] || Construction[i] > 0) continue;
                var def = DefAt(i);
                if (def.PowerSupply <= 0 || buildingNet[i] < 0) continue;
                supply += def.PowerSupply;
                supplyOf[buildingNet[i]] = (supplyOf.TryGetValue(buildingNet[i], out int s) ? s : 0) + def.PowerSupply;
                Fill(i, Powered, true);
                RoadsAround(i, around);
                sources.AddRange(around);
            }
            var dist = StreetDistance(sources);

            var consumers = new List<(int d, int anchor)>();
            int demand = 0;
            for (int i = 0; i < n; i++)
            {
                if (Anchor[i] != i || !Served[i] || Construction[i] > 0) continue;
                var def = DefAt(i);
                if (def.PowerSupply > 0 || def.PowerUse <= 0) continue;
                demand += def.PowerUse;
                if (buildingNet[i] < 0 || !supplyOf.ContainsKey(buildingNet[i])) continue;
                int d = BuildingDistance(i, dist);
                consumers.Add((d < 0 ? int.MaxValue : d, i));
            }
            consumers.Sort((p, q) => p.d.CompareTo(q.d));
            foreach (var (_, a) in consumers)
            {
                int use = DefAt(a).PowerUse, net = buildingNet[a];
                if (use > supplyOf[net]) continue;
                supplyOf[net] -= use;
                Fill(a, Powered, true);
            }

            // Where the grid reaches: its streets, the buildings on them, and the free tiles beside them.
            for (int i = 0; i < n; i++)
            {
                int net = roadNet[i];
                if (net < 0 || !supplyOf.ContainsKey(net)) continue;
                PowerCover[i] = true;
                for (int d = 0; d < 4; d++)
                {
                    int nx = i % Width + DX[d], ny = i / Width + DY[d];
                    if (InBounds(nx, ny)) PowerCover[Index(nx, ny)] = true;
                }
            }
            for (int i = 0; i < n; i++)
                if (Anchor[i] == i && buildingNet[i] >= 0 && supplyOf.ContainsKey(buildingNet[i])) Fill(i, PowerCover, true);

            PowerSupply = supply;
            PowerDemand = demand;
            for (int i = 0; i < n; i++)
                if (Powered[i] != wasPowered[i] && !IsRoad(i % Width, i / Width)) MarkChanged(i);
        }


        bool Any(int anchor, bool[] layer)
        {
            var (w, h) = Size(anchor);
            int ax = anchor % Width, ay = anchor / Width;
            for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
                if (layer[Index(ax + dx, ay + dy)]) return true;
            return false;
        }

        /// <summary>Distance in tiles between the centres of two buildings.</summary>
        float Distance(int a, int b)
        {
            var (aw, ah) = Size(a);
            var (bw, bh) = Size(b);
            float ax = a % Width + aw * .5f, ay = a / Width + ah * .5f;
            float bx = b % Width + bw * .5f, by = b / Width + bh * .5f;
            return (float)Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
        }

        /// <summary>Calls back for every tile within a radius of the building's edge.</summary>
        void StampCircle(int anchor, int radius, Action<int> stamp)
        {
            var (w, h) = Size(anchor);
            float cx = anchor % Width + (w - 1) * .5f, cy = anchor / Width + (h - 1) * .5f;
            float reach = radius + Math.Max(w, h) * .5f;
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
                else if (t == Simulation.Terrain.Mountain) Stamp(i % Width, i / Width, 1, 1, 3, 4);
                if (Anchor[i] != i) continue;
                var def = DefAt(i);
                if (def == null || def.Desirability == 0 || def.DesireRadius == 0) continue;
                var (dw, dh) = Size(i);
                Stamp(i % Width, i / Width, dw, dh, def.DesireRadius, def.Desirability);
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

        /// <summary>
        /// Working service buildings (clinics, schools, police…) reach the buildings within their radius counted in steps
        /// along the streets, not in a straight line: a school across the river without a bridge does not serve you.
        /// </summary>
        void UpdateCoverage()
        {
            Array.Clear(Coverage, 0, Coverage.Length);
            int bonus = 0;
            var anchors = new List<int>();
            for (int i = 0; i < Building.Length; i++)
                if (Anchor[i] == i && DefAt(i) != null && !DefAt(i).IsRoad) anchors.Add(i);
            var sources = new List<int>();
            foreach (int i in anchors)
            {
                if (!Working(i)) continue;
                var def = DefAt(i);
                bonus += def.CityBonus;
                if (def.Service == Service.None || def.Radius <= 0 || Catalog.IsUtility(def.Service)) continue;
                ushort bit = (ushort)(1 << (int)def.Service);
                RoadsAround(i, sources);
                var dist = StreetDistance(new List<int>(sources), def.Radius);
                foreach (int a in anchors)
                {
                    if (a != i && BuildingDistance(a, dist) < 0) continue;
                    var (w, h) = Size(a);
                    int ax = a % Width, ay = a / Width;
                    for (int dy = 0; dy < h; dy++)
                    for (int dx = 0; dx < w; dx++)
                        Coverage[Index(ax + dx, ay + dy)] |= bit;
                }
            }
            foreach (var s in Catalog.Utilities) ShareUtility(s);
            CityBonus = Math.Min(15, bonus);
        }

        readonly int[] utilitySupply = new int[16], utilityDemand = new int[16];
        readonly HashSet<int>[] utilityNets = new HashSet<int>[16];

        /// <summary>Whether a street network has a working water, sewage or waste plant on it.</summary>
        public bool UtilityReaches(Service s, int network) => network >= 0 && utilityNets[(int)s] != null && utilityNets[(int)s].Contains(network);

        /// <summary>Daily capacity of the working water, sewage or waste buildings.</summary>
        public int UtilitySupply(Service s) => utilitySupply[(int)s];

        /// <summary>Daily use of a utility by the buildings that have a connected street.</summary>
        public int UtilityDemand(Service s) => utilityDemand[(int)s];

        /// <summary>
        /// Like power: water pipes and sewers run under the streets and rubbish trucks drive along them, so a plant serves
        /// the buildings of its network, nearest first along the streets, until its capacity runs out.
        /// </summary>
        void ShareUtility(Service s)
        {
            int n = Width * Height;
            var capacityOf = new Dictionary<int, int>();
            var sources = new List<int>();
            int supply = 0, demand = 0;
            for (int i = 0; i < n; i++)
            {
                if (Anchor[i] != i || !Working(i)) continue;
                var def = DefAt(i);
                if (def.Service != s || def.Capacity <= 0 || buildingNet[i] < 0) continue;
                supply += def.Capacity;
                capacityOf[buildingNet[i]] = (capacityOf.TryGetValue(buildingNet[i], out int c) ? c : 0) + def.Capacity;
                RoadsAround(i, around);
                sources.AddRange(around);
            }
            var dist = StreetDistance(sources);
            var consumers = new List<(int d, int anchor, int use)>();
            for (int i = 0; i < n; i++)
            {
                if (Anchor[i] != i || !Served[i] || Construction[i] > 0) continue;
                var def = DefAt(i);
                if (def.IsRoad || def.Service == s) continue;
                int use = Catalog.Use(def, s, Occupants[i]);
                demand += use;
                if (buildingNet[i] < 0 || !capacityOf.ContainsKey(buildingNet[i])) continue;
                int d = BuildingDistance(i, dist);
                consumers.Add((d < 0 ? int.MaxValue : d, i, use));
            }
            consumers.Sort((p, q) => p.d.CompareTo(q.d));
            ushort bit = (ushort)(1 << (int)s);
            foreach (var (_, a, use) in consumers)
            {
                int net = buildingNet[a];
                if (use > capacityOf[net]) continue;
                capacityOf[net] -= use;
                var (cw, ch) = Size(a);
                int ax = a % Width, ay = a / Width;
                for (int dy = 0; dy < ch; dy++)
                for (int dx = 0; dx < cw; dx++)
                    Coverage[Index(ax + dx, ay + dy)] |= bit;
            }
            utilityNets[(int)s] = new HashSet<int>(capacityOf.Keys);
            utilitySupply[(int)s] = supply;
            utilityDemand[(int)s] = demand;
        }

        // ---------------------------------------------------------------- time

        /// <summary>Advances one tick (five game minutes).</summary>
        public void Step()
        {
            UpdateStats();
            UpdateDemand();
            MoveResidents();
            AdvanceConstruction();
            UpdateNetworks();
            UpdateStats();
            Tick++;
            FinishSawmillJobs();
            if (Tick % TicksPerHour == 0)
            {
                Regrow();
                RunSawmills();
                HourPassed?.Invoke();
            }
            if (Tick % TicksPerDay == BudgetTick)
            {
                RunBudget();
                DayPassed?.Invoke();
            }
        }

        /// <summary>Runs whole days at once (tests and showcases).</summary>
        public void StepDays(int days)
        {
            for (int k = 0; k < days * TicksPerDay; k++) Step();
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

        /// <summary>
        /// Homes that work fill up while people want to move in, and empty out when they do not. The rates are per tick and
        /// keep the pace of the old clock, which stepped every two fifths as often.
        /// </summary>
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
                    if (now > 0 && Chance(.59f)) Occupants[i] = Math.Max(0, now - Math.Max(1, cap / 20));
                    continue;
                }
                double mood = Math.Max(0, Math.Min(1.2, HomeSatisfaction(i) / 100));
                if (DemandResidential > 0 && now < cap)
                {
                    double expected = cap * .075 * DemandResidential * (.4 + mood);
                    int arrive = (int)expected + (Chance((float)(expected - Math.Floor(expected))) ? 1 : 0);
                    Occupants[i] = Math.Min(cap, now + arrive);
                }
                else if (now > 0 && (DemandResidential < -.3f || mood < .25) && Chance(.43f))
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
            // Residents and workers pay; every open shop, office and factory pays for the jobs it offers. A day lasts as long
            // as 24 months of the old clock, so the rates are 24 times those.
            int residents = (Population + Employed) * TaxRate * 6;
            int business = (int)Math.Round(Jobs * TaxRate * 4.8);
            int upkeep = 0;
            for (int i = 0; i < Building.Length; i++)
            {
                if (Anchor[i] != i) continue;
                upkeep += DefAt(i)?.Upkeep ?? 0;
            }
            int loan = (int)Math.Min(LoanPayment, LoanBalance);
            LoanBalance -= loan;
            if (LoanBalance <= 0) LoanPayment = 0;
            LastResidentTax = residents;
            LastBusinessTax = business;
            LastUpkeep = upkeep;
            LastLoanPayment = loan;
            LastIncome = residents + business;
            LastExpenses = upkeep + loan;
            Money += LastIncome - LastExpenses;
        }

        /// <summary>Whether the bank will lend another <see cref="LoanAmount"/>.</summary>
        public bool CanBorrow => LoanBalance + LoanAmount * 11 / 10 <= (long)LoanAmount * 11 / 10 * MaxLoans;

        /// <summary>Borrows <see cref="LoanAmount"/> now, to be paid back with 10 % interest over <see cref="LoanDays"/> days.</summary>
        public bool TakeLoan()
        {
            if (!CanBorrow) return false;
            Money += LoanAmount;
            LoanBalance += LoanAmount * 11 / 10;
            LoanPayment = (int)Math.Ceiling(LoanBalance / (double)LoanDays);
            return true;
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
