using System.Collections.Generic;
using Horizonte.Simulation;
using UnityEngine;

namespace Horizonte.Presentation
{
    /// <summary>
    /// Life on the streets, near the camera only: people walk out of buildings along the sidewalks, and homes with a
    /// garage have a car that leaves in the morning and parks again in the afternoon, on the city clock (a day lasts
    /// eight minutes).
    /// Models named persona*.fbx and coche*.fbx in Assets/Modelos replace the simple placeholders.
    /// </summary>
    public sealed class Traffic : MonoBehaviour
    {
        const int MaxWalkers = 45;
        const float WalkSpeed = 1.4f, CarSpeed = 7.5f, ReverseSpeed = 2.2f;
        /// <summary>Distance from the middle of a street to the middle of its sidewalk, and to the middle of a lane.</summary>
        const float Kerb = 4.35f, Lane = 1.9f;
        const float SidewalkY = .12f, RoadY = .02f;
        /// <summary>Beyond this camera distance people and cars are too small to see and none are drawn.</summary>
        const float VisibleDistance = 330;

        /// <summary>Where the car of each home with a garage parks, in the model's space (front toward -Z).</summary>
        static readonly Dictionary<string, (Vector3 spot, Vector3 driveway)> Garages = new Dictionary<string, (Vector3, Vector3)>
        {
            ["casa_ampliada"] = (new Vector3(3.5f, 0, -5.6f), new Vector3(3.5f, 0, -10.4f)),
        };

        /// <summary>0 is midnight, .25 six in the morning, .5 noon; it follows the city's clock.</summary>
        public float TimeOfDay { get; private set; } = .3f;

        public enum Trip { In, Parked, Stopped, Waiting, Out }

        sealed class Mover
        {
            public GameObject Go;
            public Transform Body;
            public readonly List<Vector3> Points = new List<Vector3>();
            /// <summary>Segments driven backwards, by the index of the point they end at.</summary>
            public readonly HashSet<int> Reverse = new HashSet<int>();
            public int Next;
            public float Age, Leaving = -1, Phase;
            public bool Car, Arriving, Truck;
            public int Home = -1;
            /// <summary>Traffic lights on the way, by the index of the first point in the junction: the junction, whether the car goes north-south, the middle of the junction and the way it comes in.</summary>
            public readonly Dictionary<int, (int tile, bool northSouth, Vector3 centre, Vector3 way)> Gates = new Dictionary<int, (int, bool, Vector3, Vector3)>();
            public Trip Trip;
            /// <summary>Car park and bay, or kerb tile and side, where the car is parked; the building it came to.</summary>
            public int Lot = -1, Bay = -1, KerbTile = -1, KerbSide, Destination = -1;
            /// <summary>Hour it leaves (parked) or clock time it leaves (stopped); seconds it has been held up.</summary>
            public float Until, Stuck;
            /// <summary>The person walking back to the car before it leaves.</summary>
            public Mover Driver;
            /// <summary>The point this person was already let through the lights toward.</summary>
            public int Cleared = -1;
        }

        sealed class Garage
        {
            public Mover Car;
            public bool Home;
            public float Leaves, Returns;
        }

        City city;
        CameraRig rig;
        CityView view;
        float speed = 1;
        readonly List<Mover> walkers = new List<Mover>();
        readonly Stack<Mover> idleWalkers = new Stack<Mover>();
        readonly Stack<Mover> idleCars = new Stack<Mover>();
        readonly Stack<Mover> idleTrucks = new Stack<Mover>();
        /// <summary>Cars and vans on their way to work, home or a delivery.</summary>
        readonly List<Mover> commuters = new List<Mover>();
        float carDebt;
        readonly Dictionary<int, Garage> garages = new Dictionary<int, Garage>();
        readonly List<int> nearby = new List<int>();
        readonly List<int> scratch = new List<int>();
        float spawnDebt, rescan;
        int focusX, focusY, radius;
        uint random = 12345;

        GameObject[] carModels;
        Mesh carMesh, vanMesh;
        Material[][] carLooks;

        public void Init(City c, CameraRig r, CityView v)
        {
            city = c;
            rig = r;
            view = v;
            foreach (Transform child in transform) Destroy(child.gameObject);
            walkers.Clear();
            idleWalkers.Clear();
            idleCars.Clear();
            commuters.Clear();
            idleTrucks.Clear();
            bays.Clear();
            kerb.Clear();
            TurnedAway = 0;
            garages.Clear();
            nearby.Clear();
            if (carMesh == null) LoadLooks();
        }

        /// <summary>
        /// Moves everyone along. Speed is 0 when paused and 1 at normal speed; the time of day comes from the city. When the
        /// clock jumps (a loaded game, a screenshot set to another hour) every car is put back where it belongs.
        /// </summary>
        public void Tick(float speedFactor, float timeOfDay)
        {
            if (city == null) return;
            speed = speedFactor;
            float dt = Time.deltaTime * speed;
            float jump = Mathf.Abs(Mathf.DeltaAngle(TimeOfDay * 360, timeOfDay * 360)) / 360;
            TimeOfDay = timeOfDay;
            if (jump > .02f)
            {
                foreach (var g in garages.Values) DropCar(g);
                garages.Clear();
                rescan = 0;
            }

            bool visible = rig.Distance < VisibleDistance;
            rescan -= Time.deltaTime;
            if (rescan <= 0)
            {
                rescan = .5f;
                Scan(visible);
            }
            // Cars live all over town, seen or not, so they never pop up out of nowhere.
            UpdateCars(dt);
            if (!visible)
            {
                for (int k = walkers.Count - 1; k >= 0; k--) Release(walkers[k], k);
                return;
            }

            if (dt > 0) SpawnWalkers(dt);
            for (int k = walkers.Count - 1; k >= 0; k--)
                if (!Move(walkers[k], dt) || TooFar(walkers[k].Go.transform.position)) Release(walkers[k], k);
            UpdateGarages(dt);
        }

        // ---------------------------------------------------------------- who is around

        void Scan(bool visible)
        {
            nearby.Clear();
            var f = rig.Focus;
            focusX = Mathf.FloorToInt(f.x / CityView.TileSize);
            focusY = Mathf.FloorToInt(f.z / CityView.TileSize);
            radius = Mathf.Clamp(Mathf.RoundToInt(rig.Distance * .07f) + 6, 8, 26);
            if (!visible)
            {
                foreach (var g in garages.Values) DropCar(g);
                garages.Clear();
                return;
            }

            scratch.Clear();
            for (int y = Mathf.Max(0, focusY - radius); y < Mathf.Min(city.Height, focusY + radius); y++)
            for (int x = Mathf.Max(0, focusX - radius); x < Mathf.Min(city.Width, focusX + radius); x++)
            {
                int i = city.Index(x, y);
                if (city.Anchor[i] != i) continue;
                var def = city.DefAt(i);
                if (def == null || def.IsRoad || !city.Working(i)) continue;
                if (def.Kind == Kind.Housing && city.Occupants[i] == 0) continue;
                nearby.Add(i);
                if (Garages.ContainsKey(def.Model)) scratch.Add(i);
            }

            // Cars of homes that left the area (or were demolished) go away; new ones appear where they belong.
            var gone = new List<int>();
            foreach (var kv in garages) if (!scratch.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (int i in gone)
            {
                DropCar(garages[i]);
                garages.Remove(i);
            }
            foreach (int i in scratch)
            {
                if (garages.ContainsKey(i)) continue;
                uint h = CityView.Hash(i, 77);
                var g = new Garage { Leaves = .26f + (h & 255) / 255f * .08f, Returns = .7f + ((h >> 8) & 255) / 255f * .09f };
                g.Home = !Away(g);
                if (g.Home) g.Car = Parked(i);
                garages[i] = g;
            }
        }

        void Trace(string what, int home)
        {
            if (Debug.isDebugBuild) Debug.Log($"Horizonte: coche de {home}: {what} a las {TimeOfDay * 24:0.0}");
        }

        bool Away(Garage g) => TimeOfDay >= g.Leaves && TimeOfDay < g.Returns;

        bool TooFar(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x / CityView.TileSize), y = Mathf.FloorToInt(p.z / CityView.TileSize);
            return Mathf.Abs(x - focusX) > radius + 6 || Mathf.Abs(y - focusY) > radius + 6;
        }

        // ---------------------------------------------------------------- people

        /// <summary>
        /// Share of the residents who head out in an hour: half the working people over the morning rush and back over the
        /// evening one, a few errands at midday, and nearly nobody at night.
        /// </summary>
        float OutPerHour()
        {
            float h = TimeOfDay * 24;
            if (h < 5 || h >= 22) return .002f;
            if (h < 6) return .015f;
            if (h < 9) return .1f;
            if (h < 12) return .025f;
            if (h < 15) return .045f;
            if (h < 17) return .025f;
            if (h < 20) return .1f;
            return .02f;
        }

        bool Evening => TimeOfDay * 24 >= 17 && TimeOfDay * 24 < 22;

        void SpawnWalkers(float dt)
        {
            if (nearby.Count == 0) return;
            int residents = 0;
            foreach (int i in nearby)
                if (city.DefAt(i).Kind == Kind.Housing) residents += city.Occupants[i];
            // A game hour lasts twenty seconds.
            spawnDebt += dt * residents * OutPerHour() / 20f;
            while (spawnDebt >= 1)
            {
                spawnDebt -= 1;
                if (walkers.Count >= MaxWalkers) { spawnDebt = 0; return; }
                TrySpawnWalker();
            }
        }

        /// <summary>
        /// Someone comes out of a front door and walks to another building, going in through its door. Until the evening
        /// people leave home (for work, school or shopping); in the evening they come back home.
        /// </summary>
        void TrySpawnWalker()
        {
            bool evening = Evening;
            int home = Pick(i => city.DefAt(i).Kind == Kind.Housing ? city.Occupants[i] : 0);
            if (home < 0) return;
            int other = Pick(i => i != home && city.DefAt(i).Kind != Kind.Housing ? Mathf.Min(8, 1 + city.DefAt(i).Jobs / 6f) : 0);
            if (!Front(home, out int homeRoad, out int homeDir)) return;

            int from = home, fromRoad = homeRoad, fromDir = homeDir, to = -1, toRoad = -1, toDir = 0;
            var route = new List<int>();
            if (other >= 0 && Front(other, out int otherRoad, out int otherDir) && Route(homeRoad, otherRoad, 60, route))
            {
                if (evening)
                {
                    // From work back home.
                    route.Reverse();
                    from = other; fromRoad = otherRoad; fromDir = otherDir;
                    to = home; toRoad = homeRoad; toDir = homeDir;
                }
                else { to = other; toRoad = otherRoad; toDir = otherDir; }
            }
            else
            {
                // Nowhere nearby to go: a walk round the block, out and back in the morning, or home from further away.
                Wander(homeRoad, 5 + (int)(Next() % 8), route);
                if (evening)
                {
                    route.Reverse();
                    from = -1;
                    fromRoad = route[0];
                    to = home; toRoad = homeRoad; toDir = homeDir;
                }
            }

            var m = idleWalkers.Count > 0 ? idleWalkers.Pop() : NewWalker();
            m.Points.Clear();
            m.Reverse.Clear();
            m.Gates.Clear();
            m.Cleared = -1;
            if (from >= 0)
            {
                // Out of the front door and down to the sidewalk.
                if (!view.Door(from, out var door)) door = Door(fromRoad, fromDir);
                m.Points.Add(door);
                m.Points.Add(Centre(fromRoad) - Dir(fromDir) * Kerb + Vector3.up * SidewalkY);
            }
            float side = 1;
            if (route.Count > 1)
            {
                var kerb = from >= 0 ? -Dir(fromDir) : Right(Step(route[0], route[1]));
                side = Vector3.Dot(Right(Step(route[0], route[1])), kerb) < 0 ? -1 : 1;
            }
            AddWalk(m, route, side, to >= 0 ? toDir : -1);
            if (to >= 0)
            {
                m.Points.Add(Centre(toRoad) - Dir(toDir) * Kerb + Vector3.up * SidewalkY);
                if (!view.Door(to, out var theirs)) theirs = Door(toRoad, toDir);
                m.Points.Add(theirs);
            }
            if (m.Points.Count < 2)
            {
                idleWalkers.Push(m);
                return;
            }
            m.Next = 1;
            m.Age = 0;
            m.Leaving = -1;
            m.Phase = Next() % 100 / 15f;
            m.Go.transform.position = m.Points[0];
            m.Go.transform.localScale = Vector3.one;
            var heading = m.Points[1] - m.Points[0];
            heading.y = 0;
            if (heading.sqrMagnitude > .001f) m.Go.transform.rotation = Quaternion.LookRotation(heading);
            m.Go.SetActive(true);
            walkers.Add(m);
        }

        int Pick(System.Func<int, float> weight)
        {
            float total = 0;
            foreach (int i in nearby) total += weight(i);
            if (total <= 0) return -1;
            float r = Next() % 10000 / 10000f * total;
            foreach (int i in nearby)
            {
                r -= weight(i);
                if (r < 0) return i;
            }
            return nearby[nearby.Count - 1];
        }

        void Release(Mover m, int index)
        {
            m.Go.SetActive(false);
            walkers.RemoveAt(index);
            idleWalkers.Push(m);
        }

        // ---------------------------------------------------------------- traffic


        /// <summary>Cars at most on the streets and in the car parks of the whole town, and parked at the kerb.</summary>
        const int MaxCars = 40, StreetLimit = 10;
        /// <summary>Metres from the middle of a street to a car parked at its kerb.</summary>
        const float KerbParking = 3.05f;

        /// <summary>Which bays of each car park are taken.</summary>
        readonly Dictionary<int, Mover[]> bays = new Dictionary<int, Mover[]>();
        /// <summary>Kerb places taken, by street tile and side.</summary>
        readonly HashSet<(int tile, int side)> kerb = new HashSet<(int, int)>();
        readonly List<int> workplaces = new List<int>();
        readonly List<int> carParks = new List<int>();
        float carClock, workScan;

        /// <summary>Cars parked at the kerb right now, and drivers who found nowhere to park lately (for the alerts).</summary>
        public int StreetParked => kerb.Count;
        public int TurnedAway { get; private set; }
        float turnedAwayDecay;

        /// <summary>The highway tile at the edge of the map, where the tunnel comes out; -1 if the highway is gone.</summary>
        int TunnelRoad
        {
            get
            {
                int i = city.Index(0, Landscape.HighwayY(city.Height));
                return city.IsRoad(0, Landscape.HighwayY(city.Height)) ? i : -1;
            }
        }

        float TunnelZ => (Landscape.HighwayY(city.Height) + .5f) * CityView.TileSize;

        /// <summary>
        /// Cars come into town through the tunnel and leave through it: people who work here arrive in the morning, park
        /// (in a car park near their work if there is room, otherwise at the kerb, otherwise they are dropped off) and walk
        /// the rest; in the evening they walk back to the car and drive out. By day delivery vans pull up at shops and
        /// factories for a moment. Nothing appears out of thin air.
        /// </summary>
        void UpdateCars(float dt)
        {
            carClock += dt;
            workScan -= Time.deltaTime;
            if (workScan <= 0)
            {
                workScan = 2;
                workplaces.Clear();
                carParks.Clear();
                for (int i = 0; i < city.Building.Length; i++)
                {
                    if (city.Anchor[i] != i || !city.Working(i)) continue;
                    var def = city.DefAt(i);
                    if (def.Jobs > 0) workplaces.Add(i);
                    if (def.ParkingSpaces > 0) carParks.Add(i);
                }
                // Car parks that were demolished send their cars away.
                var gone = new List<int>();
                foreach (var kv in bays) if (!carParks.Contains(kv.Key)) gone.Add(kv.Key);
                foreach (int lot in gone)
                {
                    foreach (var c in bays[lot]) if (c != null) c.Lot = -1;
                    bays.Remove(lot);
                }
            }
            turnedAwayDecay += dt;
            if (turnedAwayDecay > 40) { turnedAwayDecay = 0; TurnedAway = Mathf.Max(0, TurnedAway - 1); }

            if (dt > 0 && TunnelRoad >= 0) SpawnCars(dt);
            for (int k = commuters.Count - 1; k >= 0; k--)
                if (!Drive(commuters[k], dt)) ReleaseCar(k);
        }

        void SpawnCars(float dt)
        {
            float h = TimeOfDay * 24;
            // Workers arrive over the morning; vans come by during the working day.
            float arrivals = h >= 6 && h < 9.5f ? city.Employed * .3f * .1f / 20f : 0;
            float vans = h >= 8 && h < 18 ? workplaces.Count * .006f : 0;
            carDebt += dt * (arrivals + vans);
            while (carDebt >= 1)
            {
                carDebt -= 1;
                if (commuters.Count >= MaxCars || workplaces.Count == 0) { carDebt = 0; return; }
                bool van = Next() % 1000 < 1000 * vans / (vans + arrivals + .0001f);
                int to = workplaces[(int)(Next() % (uint)workplaces.Count)];
                ComeIn(to, van);
            }
        }

        Mover TakeCar(bool van)
        {
            var m = van ? (idleTrucks.Count > 0 ? idleTrucks.Pop() : NewCar(true)) : (idleCars.Count > 0 ? idleCars.Pop() : NewCar());
            m.Points.Clear();
            m.Reverse.Clear();
            m.Gates.Clear();
            m.Cleared = -1;
            m.Home = -1;
            m.Lot = -1;
            m.Bay = -1;
            m.KerbSide = 0;
            m.Stuck = 0;
            m.Next = 1;
            m.Age = 0;
            m.Leaving = -1;
            m.Arriving = false;
            SetLook(m, Next());
            return m;
        }

        /// <summary>A car drives out of the tunnel to a building: to a bay of the nearest car park with room, or to the kerb.</summary>
        void ComeIn(int destination, bool van)
        {
            if (!Front(destination, out int road, out int dir)) return;
            var m = TakeCar(van);
            m.Destination = destination;
            var route = new List<int>();
            int lot = van ? -1 : FreeCarPark(destination, out m.Bay);
            if (lot >= 0 && Front(lot, out int lotRoad, out _) && Route(TunnelRoad, lotRoad, 70, route))
            {
                m.Lot = lot;
                bays[lot][m.Bay] = m;
                TunnelIn(m);
                AddPath(m, route, Lane, RoadY);
                IntoBay(m, lot, m.Bay);
            }
            else if (Route(TunnelRoad, road, 70, route))
            {
                TunnelIn(m);
                AddPath(m, route, Lane, RoadY);
                // Pull over at the kerb on its side of the street, a little before the door.
                int a = route.Count > 1 ? Step(route[route.Count - 2], route[route.Count - 1]) : 3;
                int side = KerbSideOf(a);
                if (!van && kerb.Count < StreetLimit && kerb.Add((road, side))) m.KerbSide = side;
                else if (!van) TurnedAway++;
                m.KerbTile = road;
                m.Points.Add(Centre(road) + Right(a) * KerbParking - Dir(a) * 1.5f + Vector3.up * RoadY);
            }
            else
            {
                (m.Truck ? idleTrucks : idleCars).Push(m);
                return;
            }
            m.Trip = Trip.In;
            m.Go.transform.position = m.Points[0];
            m.Go.transform.rotation = Quaternion.LookRotation(m.Points[1] - m.Points[0]);
            m.Go.transform.localScale = Vector3.one;
            m.Go.SetActive(true);
            commuters.Add(m);
        }

        /// <summary>Which kerb a car on a street going one way pulls over to (its right-hand side), as a sign.</summary>
        static int KerbSideOf(int dir) => dir;

        /// <summary>The nearest working car park with a free bay, no more than a short walk from the destination.</summary>
        int FreeCarPark(int destination, out int bay)
        {
            bay = -1;
            int best = -1;
            float bestDistance = 13 * 13;
            foreach (int lot in carParks)
            {
                var def = city.DefAt(lot);
                if (!bays.TryGetValue(lot, out var taken)) bays[lot] = taken = new Mover[BuildingFactory.ParkingSpots.Length];
                int free = -1;
                for (int k = 0; k < taken.Length && free < 0; k++) if (taken[k] == null) free = k;
                if (free < 0) continue;
                float dx = lot % city.Width - destination % city.Width, dy = lot / city.Width - destination / city.Width;
                float d = dx * dx + dy * dy;
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = lot;
                bay = free;
            }
            return best;
        }

        /// <summary>World position of a point of a car park given in its own space.</summary>
        Vector3 InLot(int lot, Vector3 local)
        {
            var (w, h) = city.Size(lot);
            var centre = new Vector3((lot % city.Width + w * .5f) * CityView.TileSize, RoadY + .03f, (lot / city.Width + h * .5f) * CityView.TileSize);
            return centre + Quaternion.Euler(0, city.Rotation[lot] * 90, 0) * local;
        }

        /// <summary>From the street in front, through the entrance and along the aisle into the bay.</summary>
        void IntoBay(Mover m, int lot, int bay)
        {
            var (spot, facing) = BuildingFactory.ParkingSpots[bay];
            m.Points.Add(InLot(lot, new Vector3(0, 0, -10.3f)));
            m.Points.Add(InLot(lot, new Vector3(0, 0, 0)));
            m.Points.Add(InLot(lot, new Vector3(spot.x, 0, 0)));
            m.Points.Add(InLot(lot, spot + facing * .3f));
        }

        void TunnelIn(Mover m)
        {
            float z = TunnelZ - Lane;
            m.Points.Add(new Vector3(-36, RoadY, z));
            m.Points.Add(new Vector3(-6, RoadY, z));
        }

        void TunnelOut(Mover m)
        {
            float z = TunnelZ + Lane;
            m.Points.Add(new Vector3(-6, RoadY, z));
            m.Points.Add(new Vector3(-36, RoadY, z));
        }

        /// <summary>Moves a car on its trip; false once it has gone back into the tunnel.</summary>
        bool Drive(Mover m, float dt)
        {
            switch (m.Trip)
            {
                case Trip.In:
                    if (Move(m, dt)) return true;
                    m.Points.Clear();
                    if (m.Lot >= 0)
                    {
                        m.Trip = Trip.Parked;
                        // The driver walks from the car park to work, and back in the evening.
                        m.Until = EveningLeave();
                        WalkFrom(m.Go.transform.position, m.Lot, m.Destination);
                    }
                    else if (m.KerbSide != 0)
                    {
                        m.Trip = Trip.Parked;
                        m.Until = EveningLeave();
                        WalkFromKerb(m, m.Destination);
                    }
                    else
                    {
                        // Dropped off, or a delivery: a short stop, then away.
                        m.Trip = Trip.Stopped;
                        m.Until = carClock + (m.Truck ? 12 : 4);
                        if (!m.Truck) WalkFromKerb(m, m.Destination);
                    }
                    return true;
                case Trip.Parked:
                    if (m.Lot >= 0 && !bays.ContainsKey(m.Lot)) return false;
                    if (TimeOfDay * 24 < m.Until && TimeOfDay * 24 > 5) return true;
                    // Time to go home: the driver walks back and they leave together.
                    m.Trip = Trip.Waiting;
                    m.Driver = m.Lot >= 0 ? WalkTo(m.Destination, m.Lot, m.Go.transform.position) : null;
                    return true;
                case Trip.Stopped:
                    if (carClock < m.Until) return true;
                    return GoOut(m);
                case Trip.Waiting:
                    if (m.Driver != null && m.Driver.Go.activeSelf && walkers.Contains(m.Driver)) return true;
                    m.Driver = null;
                    return GoOut(m);
                default:
                    return Move(m, dt);
            }
        }

        /// <summary>When a worker heads home: between five and half past seven in the evening.</summary>
        float EveningLeave() => 17 + Next() % 100 / 40f;

        /// <summary>Back out of the bay (or pull out from the kerb), drive to the tunnel and in.</summary>
        bool GoOut(Mover m)
        {
            int tunnel = TunnelRoad;
            var route = new List<int>();
            m.Points.Clear();
            m.Reverse.Clear();
            m.Gates.Clear();
            m.Points.Add(m.Go.transform.position);
            int start;
            if (m.Lot >= 0)
            {
                var (spot, _) = BuildingFactory.ParkingSpots[m.Bay];
                m.Points.Add(InLot(m.Lot, new Vector3(spot.x, 0, 0)));
                m.Reverse.Add(1);
                m.Points.Add(InLot(m.Lot, Vector3.zero));
                m.Points.Add(InLot(m.Lot, new Vector3(0, 0, -10.3f)));
                bays[m.Lot][m.Bay] = null;
                m.Lot = -1;
                start = LotRoad(m);
            }
            else
            {
                if (m.KerbSide != 0) kerb.Remove((m.KerbTile, m.KerbSide));
                m.KerbSide = 0;
                start = m.KerbTile;
            }
            if (start < 0 || tunnel < 0 || !Route(start, tunnel, 70, route)) return false;
            AddPath(m, route, Lane, RoadY);
            TunnelOut(m);
            m.Next = 1;
            m.Trip = Trip.Out;
            return true;
        }

        int LotRoad(Mover m)
        {
            var p = m.Points[m.Points.Count - 1];
            int x = Mathf.FloorToInt(p.x / CityView.TileSize), y = Mathf.FloorToInt(p.z / CityView.TileSize);
            for (int r = 0; r <= 1; r++)
            for (int d = 0; d < 4; d++)
            {
                int nx = x + City.DX[d] * r, ny = y + City.DY[d] * r;
                if (city.IsRoad(nx, ny)) return city.Index(nx, ny);
            }
            return -1;
        }

        /// <summary>A driver gets out in the car park and walks to the building.</summary>
        void WalkFrom(Vector3 car, int lot, int destination)
        {
            if (TooFar(car) || !Front(lot, out int lotRoad, out int lotDir) || !Front(destination, out int road, out int dir)) return;
            var route = new List<int>();
            if (!Route(lotRoad, road, 40, route)) return;
            var m = SendWalker();
            m.Points.Add(car + Vector3.up * .05f);
            m.Points.Add(InLot(lot, new Vector3(1.8f, 0, -9.6f)) + Vector3.up * .05f);
            m.Points.Add(Centre(lotRoad) - Dir(lotDir) * Kerb + Vector3.up * SidewalkY);
            AddWalk(m, route, SideFor(route, -Dir(lotDir)), dir);
            m.Points.Add(Centre(road) - Dir(dir) * Kerb + Vector3.up * SidewalkY);
            if (view.Door(destination, out var door)) m.Points.Add(door);
            Launch(m);
        }

        /// <summary>A driver gets out at the kerb and walks to the building.</summary>
        void WalkFromKerb(Mover car, int destination)
        {
            var p = car.Go.transform.position;
            if (TooFar(p) || !Front(destination, out int road, out int dir)) return;
            var m = SendWalker();
            m.Points.Add(p + Vector3.up * .05f);
            m.Points.Add(Centre(road) - Dir(dir) * Kerb + Vector3.up * SidewalkY);
            if (view.Door(destination, out var door)) m.Points.Add(door);
            Launch(m);
        }

        /// <summary>In the evening the driver walks from work back to the car in the car park; null if nobody shows.</summary>
        Mover WalkTo(int from, int lot, Vector3 car)
        {
            if (TooFar(car) || !Front(lot, out int lotRoad, out int lotDir) || !Front(from, out int road, out int dir)) return null;
            var route = new List<int>();
            if (!Route(road, lotRoad, 40, route)) return null;
            var m = SendWalker();
            if (view.Door(from, out var door)) m.Points.Add(door);
            m.Points.Add(Centre(road) - Dir(dir) * Kerb + Vector3.up * SidewalkY);
            AddWalk(m, route, SideFor(route, -Dir(dir)), lotDir);
            m.Points.Add(Centre(lotRoad) - Dir(lotDir) * Kerb + Vector3.up * SidewalkY);
            m.Points.Add(InLot(lot, new Vector3(1.8f, 0, -9.6f)) + Vector3.up * .05f);
            m.Points.Add(car + Vector3.up * .05f);
            return Launch(m) ? m : null;
        }

        float SideFor(List<int> route, Vector3 kerbSide) =>
            route.Count > 1 && Vector3.Dot(Right(Step(route[0], route[1])), kerbSide) < 0 ? -1 : 1;

        Mover SendWalker()
        {
            var m = idleWalkers.Count > 0 ? idleWalkers.Pop() : NewWalker();
            m.Points.Clear();
            m.Reverse.Clear();
            m.Gates.Clear();
            m.Cleared = -1;
            return m;
        }

        bool Launch(Mover m)
        {
            if (m.Points.Count < 2 || walkers.Count >= MaxWalkers)
            {
                idleWalkers.Push(m);
                return false;
            }
            m.Next = 1;
            m.Age = 0;
            m.Leaving = -1;
            m.Phase = Next() % 100 / 15f;
            m.Go.transform.position = m.Points[0];
            m.Go.transform.localScale = Vector3.one;
            m.Go.SetActive(true);
            walkers.Add(m);
            return true;
        }

        void ReleaseCar(int index)
        {
            var m = commuters[index];
            if (m.Lot >= 0 && bays.TryGetValue(m.Lot, out var taken) && m.Bay >= 0 && taken[m.Bay] == m) taken[m.Bay] = null;
            if (m.KerbSide != 0) kerb.Remove((m.KerbTile, m.KerbSide));
            m.Go.SetActive(false);
            m.Points.Clear();
            commuters.RemoveAt(index);
            (m.Truck ? idleTrucks : idleCars).Push(m);
        }

        // ---------------------------------------------------------------- cars

        void UpdateGarages(float dt)
        {
            foreach (var kv in garages)
            {
                var g = kv.Value;
                if (g.Car != null && !g.Car.Go.activeSelf) g.Car = null;
                bool away = Away(g);
                if (g.Car != null && g.Car.Points.Count > 0)
                {
                    // On its way: when it arrives home it stays parked; when it reaches the end of the trip out it is gone.
                    if (!Move(g.Car, dt))
                    {
                        if (g.Car.Arriving) { g.Car.Points.Clear(); g.Home = true; Trace("estacionó", kv.Key); }
                        else { DropCar(g); g.Home = false; }
                    }
                    continue;
                }
                if (dt <= 0) continue;
                if (g.Home && away) Leave(kv.Key, g);
                else if (!g.Home && !away && g.Car == null) Arrive(kv.Key, g);
            }
        }

        Mover Parked(int home)
        {
            Trace("estacionado", home);
            var (spot, driveway) = Spots(home);
            var m = idleCars.Count > 0 ? idleCars.Pop() : NewCar();
            m.Points.Clear();
            m.Reverse.Clear();
            m.Gates.Clear();
            m.Cleared = -1;
            m.Home = home;
            m.Go.transform.position = spot;
            m.Go.transform.rotation = Quaternion.LookRotation(spot - driveway);
            m.Go.transform.localScale = Vector3.one;
            m.Go.SetActive(true);
            SetLook(m, CityView.Hash(home, 5));
            return m;
        }

        void Leave(int home, Garage g)
        {
            Trace("sale", home);
            if (!Front(home, out int road, out _)) return;
            // Off to work: out of town through the tunnel.
            var route = new List<int>();
            bool tunnel = TunnelRoad >= 0 && Route(road, TunnelRoad, 70, route);
            if (!tunnel) Wander(road, 16 + (int)(Next() % 10), route);
            if (g.Car == null) g.Car = Parked(home);
            var m = g.Car;
            var (spot, driveway) = Spots(home);
            m.Points.Clear();
            m.Reverse.Clear();
            m.Gates.Clear();
            m.Cleared = -1;
            m.Points.Add(spot);
            // Backs out of the garage into the street, then drives off.
            m.Points.Add(driveway);
            m.Reverse.Add(1);
            var next = route.Count > 1 ? Step(route[0], route[1]) : 0;
            m.Points.Add(Centre(road) + Right(next) * Lane + Vector3.up * RoadY);
            m.Reverse.Add(2);
            AddPath(m, route, Lane, RoadY, 1);
            if (tunnel) TunnelOut(m);
            m.Next = 1;
            m.Age = 1;
            m.Leaving = -1;
            m.Arriving = false;
            g.Home = false;
        }

        void Arrive(int home, Garage g)
        {
            Trace("llega", home);
            if (!Front(home, out int road, out _)) return;
            // Home from work: in through the tunnel, along the streets and into the garage.
            var route = new List<int>();
            bool tunnel = TunnelRoad >= 0 && Route(TunnelRoad, road, 70, route);
            if (!tunnel)
            {
                Wander(road, 8 + (int)(Next() % 5), route);
                route.Reverse();
            }
            var m = idleCars.Count > 0 ? idleCars.Pop() : NewCar();
            m.Points.Clear();
            m.Reverse.Clear();
            m.Gates.Clear();
            m.Cleared = -1;
            m.Home = home;
            SetLook(m, CityView.Hash(home, 5));
            if (tunnel) TunnelIn(m);
            AddPath(m, route, Lane, RoadY);
            var (spot, driveway) = Spots(home);
            m.Points.Add(driveway);
            m.Points.Add(spot);
            m.Next = 1;
            m.Age = 1;
            m.Leaving = -1;
            m.Arriving = true;
            m.Go.transform.position = m.Points[0];
            m.Go.transform.rotation = Quaternion.LookRotation(m.Points[1] - m.Points[0]);
            m.Go.transform.localScale = Vector3.one;
            m.Go.SetActive(true);
            g.Car = m;
        }

        void DropCar(Garage g)
        {
            if (g.Car == null) return;
            Trace("quita", g.Car.Home);
            g.Car.Go.SetActive(false);
            g.Car.Points.Clear();
            idleCars.Push(g.Car);
            g.Car = null;
        }

        (Vector3 spot, Vector3 driveway) Spots(int home)
        {
            var def = city.DefAt(home);
            var (w, h) = city.Size(home);
            var centre = new Vector3((home % city.Width + w * .5f) * CityView.TileSize, .05f, (home / city.Width + h * .5f) * CityView.TileSize);
            var turn = Quaternion.Euler(0, city.Rotation[home] * 90, 0);
            var g = Garages[def.Model];
            return (centre + turn * g.spot, centre + turn * g.driveway + Vector3.up * -.03f);
        }

        // ---------------------------------------------------------------- moving along a path

        /// <summary>Moves along the points; false once the path is finished (after shrinking away, for people).</summary>
        bool Move(Mover m, float dt)
        {
            var t = m.Go.transform;
            m.Age += dt;
            if (m.Leaving >= 0)
            {
                m.Leaving += dt;
                return m.Leaving < .35f;
            }
            if (dt <= 0) return true;

            float step = (m.Car ? (m.Reverse.Contains(m.Next) ? ReverseSpeed : CarSpeed) : WalkSpeed) * dt;
            var p = t.position;
            // Keep a distance from the car ahead.
            if (m.Car && Blocked(m, p, dt)) step = 0;
            bool waiting = false;
            while (step > 0 && m.Next < m.Points.Count)
            {
                var target = m.Points[m.Next];
                var lights = Signals.Instance;
                if (m.Car && lights != null && m.Gates.TryGetValue(m.Next, out var gate) && lights.State(gate.tile, gate.northSouth) != Signals.Light.Green)
                {
                    // Stop at the line before the zebra crossing, measured to the middle of the junction along the way
                    // the car comes in; a car already past the line carries on.
                    float ahead = Vector3.Dot(gate.centre - p, gate.way);
                    if (ahead > StopBack - .6f)
                    {
                        float room = ahead - StopBack;
                        if (room <= .02f) { waiting = true; break; }
                        step = Mathf.Min(step, room);
                    }
                }
                if (!m.Car && lights != null && m.Cleared != m.Next)
                {
                    // People wait at the kerb until the traffic going their way has green.
                    if (CrossingAhead(m, out int tile, out bool northSouth) && !lights.CanWalk(tile, northSouth)) { waiting = true; break; }
                    m.Cleared = m.Next;
                }
                float d = Vector3.Distance(p, target);
                if (d > step)
                {
                    p = Vector3.MoveTowards(p, target, step);
                    step = 0;
                }
                else
                {
                    p = target;
                    step -= d;
                    m.Next++;
                }
            }
            t.position = p;
            // People go in through the door at the end of their walk; cars finish their trip.
            if (m.Next >= m.Points.Count) return false;
            var heading = m.Points[m.Next] - m.Points[m.Next - 1];
            heading.y = 0;
            if (m.Reverse.Contains(m.Next)) heading = -heading;
            if (heading.sqrMagnitude > .001f)
                t.rotation = Quaternion.RotateTowards(t.rotation, Quaternion.LookRotation(heading), (m.Car ? 240 : 420) * dt);
            if (!m.Car && m.Body != null && !waiting)
            {
                // A light bob in step with walking.
                m.Phase += dt * 9;
                m.Body.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(m.Phase)) * .06f, 0);
            }
            return true;
        }

        /// <summary>
        /// Adds a point per street tile, kept to one side: offset is the sidewalk (people) or the lane (cars) to the right
        /// of the direction of travel. Turns cut through the corner, which also gives people their crossings.
        /// </summary>
        /// <summary>
        /// People keep to the sidewalk: they follow it round the corners and change sides only at a junction, on its zebra
        /// crossing, never in the middle of a street; at a dead end they come back on the same sidewalk. When the place
        /// they are going is across the street, they cross at the last junction on the way.
        /// </summary>
        void AddWalk(Mover m, List<int> route, float side, int endDir)
        {
            int n = route.Count;
            if (n == 0) return;
            int Into(int k) => k > 0 ? Step(route[k - 1], route[k]) : n > 1 ? Step(route[0], route[1]) : (endDir >= 0 ? (endDir + 1) & 3 : 0);
            int Out(int k) => k + 1 < n ? Step(route[k], route[k + 1]) : Into(k);
            bool Back(int k) => Out(k) == ((Into(k) + 2) & 3);

            // The sidewalk the walk would end on; it has to be the one beside the destination.
            float end = side;
            for (int k = 0; k < n; k++) if (Back(k)) end = -end;
            int crossAt = -1;
            if (endDir >= 0 && Vector3.Dot(Right(Into(n - 1)) * end, -Dir(endDir)) < -.5f)
                for (int k = n - 1; k >= 0 && crossAt < 0; k--)
                    if (Junction(route[k])) crossAt = k;

            float s = side;
            for (int k = 0; k < n; k++)
            {
                int a = Into(k), b = Out(k);
                var c = Centre(route[k]) + Vector3.up * SidewalkY;
                if (Back(k))
                {
                    // Round the end of the street on the same sidewalk.
                    m.Points.Add(c + Right(a) * s * Kerb);
                    s = -s;
                    continue;
                }
                m.Points.Add(c + (a == b ? Right(a) : Right(a) + Right(b)) * s * Kerb);
                if (k != crossAt) continue;
                // Over the zebra crossing on the street ahead, to the other sidewalk.
                m.Points.Add(c + Right(b) * s * Kerb + Dir(b) * CityView.CrossingLine);
                m.Points.Add(c - Right(b) * s * Kerb + Dir(b) * CityView.CrossingLine);
                s = -s;
            }
        }

        /// <summary>Metres before the middle of a junction where cars stop on red: just short of its edge.</summary>
        const float StopBack = 6.6f;

        /// <summary>
        /// Whether another car is just ahead in the same lane, going the same way. A car held up for several seconds
        /// with no red light creeps on, so two cars can never wait for each other forever.
        /// </summary>
        bool Blocked(Mover m, Vector3 p, float dt)
        {
            if (m.Reverse.Contains(m.Next)) return false;
            if (m.Stuck > 4)
            {
                m.Stuck = m.Stuck > 5.5f ? 0 : m.Stuck + dt;
                return false;
            }
            var ahead = m.Go.transform.forward;
            ahead.y = 0;
            ahead.Normalize();
            foreach (var other in Driving())
            {
                if (other == m) continue;
                var v = other.Go.transform.position - p;
                v.y = 0;
                float along = Vector3.Dot(v, ahead);
                if (along < .5f || along > 6.5f) continue;
                float aside = Mathf.Abs(Vector3.Dot(v, new Vector3(ahead.z, 0, -ahead.x)));
                if (aside > .9f) continue;
                var otherWay = other.Go.transform.forward;
                otherWay.y = 0;
                if (Vector3.Dot(otherWay.normalized, ahead) < .5f) continue;
                m.Stuck += dt;
                return true;
            }
            m.Stuck = 0;
            return false;
        }

        IEnumerable<Mover> Driving()
        {
            foreach (var c in commuters) if (c.Trip == Trip.In || c.Trip == Trip.Out || c.Trip == Trip.Stopped) yield return c;
            foreach (var g in garages.Values) if (g.Car != null && g.Car.Points.Count > 0) yield return g.Car;
        }

        /// <summary>
        /// Whether the next bit of a walk crosses a street at a junction with lights, and which way: people go when the
        /// traffic along their own direction has green.
        /// </summary>
        bool CrossingAhead(Mover m, out int tile, out bool northSouth)
        {
            tile = -1;
            northSouth = false;
            if (m.Next < 1 || m.Next >= m.Points.Count) return false;
            var a = m.Points[m.Next - 1];
            var b = m.Points[m.Next];
            var move = b - a;
            move.y = 0;
            if (move.magnitude < 2) return false;
            foreach (var p in new[] { a, b })
            {
                int x = Mathf.FloorToInt(p.x / CityView.TileSize), y = Mathf.FloorToInt(p.z / CityView.TileSize);
                if (!city.InBounds(x, y)) continue;
                int i = city.Index(x, y);
                if (!Signals.Instance.IsJunction(i)) continue;
                tile = i;
                northSouth = Mathf.Abs(move.z) > Mathf.Abs(move.x);
                return true;
            }
            return false;
        }

        /// <summary>A street tile where three or four streets meet; its crossings are painted.</summary>
        bool Junction(int i)
        {
            int x = i % city.Width, y = i / city.Width, links = 0;
            for (int d = 0; d < 4; d++) if (city.IsRoad(x + City.DX[d], y + City.DY[d])) links++;
            return links >= 3;
        }

        /// <summary>
        /// A car's way along the streets, in its lane on the right. Straight on it passes the middle of each tile; at a
        /// turn it follows a curve from where it enters the tile to where it leaves. Junctions with lights on the way are
        /// noted so the car stops before them on red.
        /// </summary>
        void AddPath(Mover m, List<int> route, float offset, float y, int skip = 0)
        {
            const float half = CityView.TileSize * .5f;
            for (int k = skip; k < route.Count; k++)
            {
                int a = k > 0 ? Step(route[k - 1], route[k]) : (route.Count > 1 ? Step(route[0], route[1]) : 0);
                int b = k + 1 < route.Count ? Step(route[k], route[k + 1]) : a;
                var c = Centre(route[k]) + Vector3.up * y;
                if (k > 0 && Signals.Instance != null && Signals.Instance.IsJunction(route[k]))
                    m.Gates[m.Points.Count] = (route[k], a == 0 || a == 2, c, Dir(a));
                if (a == b || b == ((a + 2) & 3))
                {
                    m.Points.Add(c + (a == b ? Right(a) : Vector3.zero) * offset);
                    continue;
                }
                var from = c - Dir(a) * half + Right(a) * offset;
                var to = c + Dir(b) * half + Right(b) * offset;
                var corner = c + (Right(a) + Right(b)) * offset;
                for (int s = 0; s <= 4; s++)
                {
                    float t = s / 4f;
                    m.Points.Add((1 - t) * (1 - t) * from + 2 * t * (1 - t) * corner + t * t * to);
                }
            }
        }

        // ---------------------------------------------------------------- streets

        /// <summary>The street tile a building opens onto, preferring the side its front faces.</summary>
        bool Front(int anchor, out int road, out int dir)
        {
            int ax = anchor % city.Width, ay = anchor / city.Width;
            var (w, h) = city.Size(anchor);
            int facing = city.Rotation[anchor] & 3;
            for (int k = 0; k < 4; k++)
            {
                int d = (facing + k) & 3;
                int count = d == 0 || d == 2 ? w : h;
                for (int j = 0; j < count; j++)
                {
                    // From the middle of the side outward.
                    int o = count / 2 + ((j & 1) == 0 ? j / 2 : -(j + 1) / 2);
                    if (o < 0 || o >= count) continue;
                    int x, y;
                    switch (d)
                    {
                        case 0: x = ax + o; y = ay - 1; break;
                        case 1: x = ax - 1; y = ay + o; break;
                        case 2: x = ax + o; y = ay + h; break;
                        default: x = ax + w; y = ay + o; break;
                    }
                    if (!city.IsRoad(x, y)) continue;
                    road = city.Index(x, y);
                    dir = d;
                    return true;
                }
            }
            road = -1;
            dir = 0;
            return false;
        }

        /// <summary>Shortest street path between two street tiles, up to a length.</summary>
        bool Route(int from, int to, int maxLength, List<int> route)
        {
            route.Clear();
            var previous = new Dictionary<int, int> { [from] = -1 };
            var queue = new Queue<int>();
            queue.Enqueue(from);
            while (queue.Count > 0 && previous.Count < maxLength * maxLength)
            {
                int i = queue.Dequeue();
                if (i == to)
                {
                    for (int k = to; k >= 0; k = previous[k]) route.Add(k);
                    route.Reverse();
                    return true;
                }
                int x = i % city.Width, y = i / city.Width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + City.DX[d], ny = y + City.DY[d];
                    if (!city.IsRoad(nx, ny)) continue;
                    int j = city.Index(nx, ny);
                    if (previous.ContainsKey(j)) continue;
                    previous[j] = i;
                    queue.Enqueue(j);
                }
            }
            return false;
        }

        /// <summary>A stroll along the streets: mostly straight on, sometimes turning, never straight back unless at a dead end.</summary>
        void Wander(int from, int length, List<int> route)
        {
            route.Clear();
            route.Add(from);
            int dir = (int)(Next() % 4);
            int i = from;
            for (int k = 0; k < length; k++)
            {
                int x = i % city.Width, y = i / city.Width;
                int options = 0, pick = -1;
                for (int d = 0; d < 4; d++)
                {
                    if (d == ((dir + 2) & 3) && k > 0) continue;
                    if (!city.IsRoad(x + City.DX[d], y + City.DY[d])) continue;
                    // Straight on is three times as likely as a turn.
                    int weight = d == dir ? 3 : 1;
                    options += weight;
                    if (Next() % (uint)options < weight) pick = d;
                }
                if (pick < 0)
                {
                    int back = (dir + 2) & 3;
                    if (!city.IsRoad(x + City.DX[back], y + City.DY[back])) break;
                    pick = back;
                }
                dir = pick;
                i = city.Index(x + City.DX[dir], y + City.DY[dir]);
                route.Add(i);
            }
        }

        Vector3 Centre(int i) => CityView.TileCentre(i % city.Width, i / city.Width);

        /// <summary>Unit vector for a grid direction: 0 south, 1 west, 2 north, 3 east.</summary>
        static Vector3 Dir(int d) => new Vector3(City.DX[d], 0, City.DY[d]);

        /// <summary>Right-hand side when travelling in a direction.</summary>
        static Vector3 Right(int d) => Dir((d + 3) & 3) * -1;

        int Step(int from, int to)
        {
            int dx = to % city.Width - from % city.Width, dy = to / city.Width - from / city.Width;
            for (int d = 0; d < 4; d++) if (City.DX[d] == dx && City.DY[d] == dy) return d;
            return 0;
        }

        /// <summary>Middle of the edge between a building and the street tile in front of it.</summary>
        Vector3 Door(int road, int dir) => Centre(road) - Dir(dir) * (CityView.TileSize * .5f);

        uint Next()
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            return random;
        }

        // ---------------------------------------------------------------- looks

        void LoadLooks()
        {
            var cars = new List<GameObject>();
            foreach (var model in Resources.LoadAll<GameObject>("Buildings"))
                if (model.name.ToLowerInvariant().StartsWith("coche")) cars.Add(model);
            carModels = cars.ToArray();

            var b = new MeshBuilder(4);
            // Body, cabin glass, wheels, lights; front toward +Z.
            b.Box(0, new Vector3(0, .28f, 0), new Vector3(1.78f, .62f, 4.2f));
            b.Box(1, new Vector3(0, .9f, -.25f), new Vector3(1.56f, .52f, 2.2f));
            b.Box(0, new Vector3(0, 1.42f, -.25f), new Vector3(1.6f, .06f, 2.1f));
            foreach (float x in new[] { -.82f, .82f })
            foreach (float z in new[] { -1.3f, 1.3f })
                b.Box(2, new Vector3(x, 0, z), new Vector3(.26f, .62f, .62f));
            b.Box(3, new Vector3(-.6f, .55f, 2.08f), new Vector3(.36f, .16f, .06f));
            b.Box(3, new Vector3(.6f, .55f, 2.08f), new Vector3(.36f, .16f, .06f));
            carMesh = b.ToMesh();

            b = new MeshBuilder(4);
            // Delivery van: cab in front, a tall white box behind.
            b.Box(0, new Vector3(0, .32f, 1.75f), new Vector3(1.9f, 1.35f, 1.5f));
            b.Box(1, new Vector3(0, 1.1f, 2.36f), new Vector3(1.7f, .5f, .3f));
            b.Box(3, new Vector3(0, .32f, -.75f), new Vector3(2f, 2.1f, 3.5f));
            foreach (float x in new[] { -.86f, .86f })
            foreach (float z in new[] { -1.8f, 1.6f })
                b.Box(2, new Vector3(x, 0, z), new Vector3(.28f, .7f, .7f));
            vanMesh = b.ToMesh();

            var bodies = new[] { "#C8302E", "#F2F2EE", "#2B5C9E", "#3A3D42", "#E2B13C", "#7FA7C9" };
            var glass = Colour("#1E2B38", .85f);
            var tyre = Colour("#1A1A1A");
            var lights = Colour("#FFF3CF", .6f);
            carLooks = new Material[bodies.Length][];
            for (int k = 0; k < bodies.Length; k++) carLooks[k] = new[] { Colour(bodies[k], .7f), glass, tyre, lights };
        }

        static Material Colour(string hex, float smoothness = .2f) => Figures.Colour(hex, smoothness);

        Mover NewWalker()
        {
            var go = Figures.Create(transform, Next());
            return new Mover { Go = go, Body = go.transform.GetChild(0) };
        }

        Mover NewCar(bool van = false)
        {
            var go = new GameObject(van ? "Camioneta" : "Coche");
            go.transform.SetParent(transform, false);
            var m = new Mover { Go = go, Car = true, Truck = van };
            GameObject body;
            if (van)
            {
                body = new GameObject("Carrocería", typeof(MeshFilter), typeof(MeshRenderer));
                body.GetComponent<MeshFilter>().sharedMesh = vanMesh;
            }
            else if (carModels.Length > 0)
            {
                body = Instantiate(carModels[Next() % (uint)carModels.Length]);
                // Models face -Z like buildings; cars here drive toward +Z.
                body.transform.localRotation = Quaternion.Euler(0, 180, 0);
            }
            else
            {
                body = new GameObject("Carrocería", typeof(MeshFilter), typeof(MeshRenderer));
                body.GetComponent<MeshFilter>().sharedMesh = carMesh;
            }
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = Vector3.one * .85f;
            m.Body = body.transform;
            return m;
        }

        void SetLook(Mover m, uint seed)
        {
            if (m.Truck)
            {
                var look = carLooks[seed % (uint)carLooks.Length];
                m.Body.GetComponent<MeshRenderer>().sharedMaterials = new[] { look[0], look[1], look[2], Colour("#F4F4F0", .3f) };
                return;
            }
            if (carModels.Length > 0) return;
            m.Body.GetComponent<MeshRenderer>().sharedMaterials = carLooks[seed % (uint)carLooks.Length];
        }
    }
}
