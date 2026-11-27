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

        sealed class Mover
        {
            public GameObject Go;
            public Transform Body;
            public readonly List<Vector3> Points = new List<Vector3>();
            /// <summary>Segments driven backwards, by the index of the point they end at.</summary>
            public readonly HashSet<int> Reverse = new HashSet<int>();
            public int Next;
            public float Age, Leaving = -1, Phase;
            public bool Car, Arriving;
            public int Home = -1;
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
        readonly Dictionary<int, Garage> garages = new Dictionary<int, Garage>();
        readonly List<int> nearby = new List<int>();
        readonly List<int> scratch = new List<int>();
        float spawnDebt, rescan;
        int focusX, focusY, radius;
        uint random = 12345;

        GameObject[] carModels;
        Mesh carMesh;
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
            var route = new List<int>();
            Wander(road, 16 + (int)(Next() % 10), route);
            if (g.Car == null) g.Car = Parked(home);
            var m = g.Car;
            var (spot, driveway) = Spots(home);
            m.Points.Clear();
            m.Reverse.Clear();
            m.Points.Add(spot);
            // Backs out of the garage into the street, then drives off.
            m.Points.Add(driveway);
            m.Reverse.Add(1);
            var next = route.Count > 1 ? Step(route[0], route[1]) : 0;
            m.Points.Add(Centre(road) + Right(next) * Lane + Vector3.up * RoadY);
            m.Reverse.Add(2);
            AddPath(m, route, Lane, RoadY, 1);
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
            // Comes from a few blocks away, along the streets, and turns into the garage.
            var route = new List<int>();
            Wander(road, 8 + (int)(Next() % 5), route);
            route.Reverse();
            var m = idleCars.Count > 0 ? idleCars.Pop() : NewCar();
            m.Points.Clear();
            m.Reverse.Clear();
            m.Home = home;
            SetLook(m, CityView.Hash(home, 5));
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
            while (step > 0 && m.Next < m.Points.Count)
            {
                var target = m.Points[m.Next];
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
                t.rotation = Quaternion.RotateTowards(t.rotation, Quaternion.LookRotation(heading), (m.Car ? 160 : 420) * dt);
            if (!m.Car && m.Body != null)
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

        /// <summary>A street tile where three or four streets meet; its crossings are painted.</summary>
        bool Junction(int i)
        {
            int x = i % city.Width, y = i / city.Width, links = 0;
            for (int d = 0; d < 4; d++) if (city.IsRoad(x + City.DX[d], y + City.DY[d])) links++;
            return links >= 3;
        }

        void AddPath(Mover m, List<int> route, float offset, float y, int skip = 0)
        {
            for (int k = skip; k < route.Count; k++)
            {
                int a = k > 0 ? Step(route[k - 1], route[k]) : (route.Count > 1 ? Step(route[0], route[1]) : 0);
                int b = k + 1 < route.Count ? Step(route[k], route[k + 1]) : a;
                var side = a == b ? Right(a) : Right(a) + Right(b);
                m.Points.Add(Centre(route[k]) + side * offset + Vector3.up * y);
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

        Mover NewCar()
        {
            var go = new GameObject("Coche");
            go.transform.SetParent(transform, false);
            var m = new Mover { Go = go, Car = true };
            GameObject body;
            if (carModels.Length > 0)
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
            m.Body = body.transform;
            return m;
        }

        void SetLook(Mover m, uint seed)
        {
            if (carModels.Length > 0) return;
            m.Body.GetComponent<MeshRenderer>().sharedMaterials = carLooks[seed % (uint)carLooks.Length];
        }
    }
}
