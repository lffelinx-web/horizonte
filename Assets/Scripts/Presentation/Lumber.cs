using System.Collections.Generic;
using Horizonte.Simulation;
using UnityEngine;

namespace Horizonte.Presentation
{
    /// <summary>
    /// The lumberjacks. Each forest station sends its lumberjack to the trees the player marks: he walks there with his
    /// axe, chops until the tree falls, and carries a log back, which is when the wood counts. Sawmills send their crew to
    /// the trees the simulation picked for them; those trees fall when the simulation says so. Marked trees carry an
    /// orange flag until they are felled.
    /// </summary>
    public sealed class Lumber : MonoBehaviour
    {
        const float Speed = 2f, ChopTime = 2.6f, WatchTime = 2f;

        enum State { Out, Chop, Back }

        sealed class Worker
        {
            public GameObject Go;
            public int Station, Tile;
            public bool Manual, Felled;
            public State State;
            public float Timer, Phase;
            public Vector3 Home, Target;
        }

        City city;
        CityView view;
        readonly List<Worker> workers = new List<Worker>();
        readonly Dictionary<int, GameObject> flags = new Dictionary<int, GameObject>();
        readonly List<int> stations = new List<int>();
        readonly HashSet<int> crewTiles = new HashSet<int>();
        float rescan;
        static Mesh flagMesh;

        public void Init(City c, CityView v)
        {
            city = c;
            view = v;
            foreach (Transform child in transform) Destroy(child.gameObject);
            workers.Clear();
            flags.Clear();
            stations.Clear();
            crewTiles.Clear();
            rescan = 0;
        }

        public void Tick(float speedFactor)
        {
            if (city == null) return;
            float dt = Time.deltaTime * speedFactor;
            rescan -= Time.deltaTime;
            if (rescan <= 0)
            {
                rescan = .5f;
                stations.Clear();
                stations.AddRange(city.Stations());
                UpdateFlags();
            }

            // A station with nobody out takes the next marked tree in its reach.
            if (dt > 0)
                foreach (int s in stations)
                {
                    if (workers.Exists(w => w.Manual && w.Station == s)) continue;
                    int tile = city.TakeMarked(s);
                    if (tile >= 0) Send(s, tile, true);
                }
            // Sawmill crews go to the trees the simulation picked.
            foreach (var job in city.SawmillJobs)
                if (crewTiles.Add(job.Tile)) Send(job.Station, job.Tile, false);

            for (int k = workers.Count - 1; k >= 0; k--)
                if (!Step(workers[k], dt))
                {
                    crewTiles.Remove(workers[k].Tile);
                    Destroy(workers[k].Go);
                    workers.RemoveAt(k);
                }
        }

        Vector3 Centre(int anchor)
        {
            var (w, h) = city.Size(anchor);
            return new Vector3((anchor % city.Width + w * .5f) * CityView.TileSize, 0, (anchor / city.Width + h * .5f) * CityView.TileSize);
        }

        void Send(int station, int tile, bool manual)
        {
            var home = Centre(station);
            var tree = CityView.TileCentre(tile % city.Width, tile / city.Width);
            var go = Figures.Create(transform, (uint)(tile * 7 + station), Figures.Role.Lumberjack);
            go.transform.position = home;
            workers.Add(new Worker
            {
                Go = go, Station = station, Tile = tile, Manual = manual, Home = home,
                // Stands beside the trees, on the side facing the station.
                Target = tree + (home - tree).normalized * 1.8f,
            });
        }

        /// <summary>Moves a lumberjack along; false once he is back home (or his station is gone).</summary>
        bool Step(Worker w, float dt)
        {
            var def = city.DefAt(w.Station);
            if (def == null || def.LumberReach == 0 || city.Anchor[w.Station] != w.Station) return false;
            var t = w.Go.transform;
            switch (w.State)
            {
                case State.Out:
                    if (Walk(w, w.Target, dt))
                    {
                        w.State = State.Chop;
                        w.Timer = 0;
                    }
                    break;
                case State.Chop:
                    w.Timer += dt;
                    var tree = CityView.TileCentre(w.Tile % city.Width, w.Tile / city.Width) - t.position;
                    tree.y = 0;
                    if (tree.sqrMagnitude > .01f) t.rotation = Quaternion.LookRotation(tree);
                    bool standing = city.Terrain[w.Tile] == Simulation.Terrain.Forest;
                    if (!w.Felled)
                    {
                        Figures.Swing(w.Go, w.Timer % .8f / .8f);
                        if (w.Manual && w.Timer >= ChopTime && standing) city.FellTree(w.Tile);
                        // The sawmill's tree falls when the simulation says; give up if the job was dropped.
                        if (!city.Marked.Contains(w.Tile) && (city.Terrain[w.Tile] != Simulation.Terrain.Forest || w.Timer > 90))
                        {
                            w.Felled = true;
                            w.Timer = 0;
                            Figures.Swing(w.Go, 0);
                        }
                    }
                    else if (w.Timer > WatchTime)
                    {
                        w.State = State.Back;
                        Figures.Carry(w.Go, true);
                    }
                    break;
                case State.Back:
                    if (Walk(w, w.Home, dt))
                    {
                        if (w.Manual) city.DeliverWood(Catalog.WoodPerTree);
                        return false;
                    }
                    break;
            }
            return true;
        }

        bool Walk(Worker w, Vector3 to, float dt)
        {
            var t = w.Go.transform;
            var p = Vector3.MoveTowards(t.position, to, Speed * dt);
            var heading = to - t.position;
            heading.y = 0;
            if (heading.sqrMagnitude > .001f) t.rotation = Quaternion.RotateTowards(t.rotation, Quaternion.LookRotation(heading), 400 * dt);
            t.position = p;
            w.Phase += dt * 8;
            Figures.Bob(w.Go, w.Phase);
            return (p - to).sqrMagnitude < .01f;
        }

        /// <summary>An orange flag above every tree waiting to be felled.</summary>
        void UpdateFlags()
        {
            if (flagMesh == null)
            {
                var b = new MeshBuilder(2);
                b.Box(0, Vector3.zero, new Vector3(.12f, 8.2f, .12f));
                b.Box(1, new Vector3(.45f, 7.3f, 0), new Vector3(.9f, .65f, .05f));
                flagMesh = b.ToMesh();
            }
            // Marked trees, and those a lumberjack is on his way to.
            var wanted = new HashSet<int>(city.Marked);
            foreach (var w in workers) if (w.Manual && !w.Felled) wanted.Add(w.Tile);
            var gone = new List<int>();
            foreach (var kv in flags) if (!wanted.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (int i in gone)
            {
                Destroy(flags[i]);
                flags.Remove(i);
            }
            foreach (int i in wanted)
            {
                if (flags.ContainsKey(i)) continue;
                var go = new GameObject("Árbol marcado", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                go.transform.position = CityView.TileCentre(i % city.Width, i / city.Width) + new Vector3(.8f, 0, .8f);
                go.GetComponent<MeshFilter>().sharedMesh = flagMesh;
                go.GetComponent<MeshRenderer>().sharedMaterials = new[] { Figures.Colour("#F2EEE2"), Figures.Colour("#FF7A1A", .3f) };
                flags[i] = go;
            }
        }
    }
}
