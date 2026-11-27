using System.Collections.Generic;
using Horizonte.Simulation;
using UnityEngine;

namespace Horizonte.Presentation
{
    /// <summary>
    /// The lumberjacks. The player marks whole tiles of forest; the city has one lumberjack at first and more as it grows,
    /// and each takes one marked tile at a time from a logging camp in reach. He walks to every tree of the tile in turn,
    /// chops until that very tree falls (leaving its stump), and when the tile is clear carries a log back, which is when
    /// the wood counts. Sawmills send their crew to the trees the simulation picked; those fall when it says so.
    /// Marked tiles show an orange frame on the ground and a flag.
    /// </summary>
    public sealed class Lumber : MonoBehaviour
    {
        const float Speed = 2f, ChopTime = 2.2f, WatchTime = 1.2f;

        enum State { Out, Chop, Watch, Back }

        sealed class Worker
        {
            public GameObject Go;
            public int Station, Tile, Tree;
            public bool Manual;
            public State State;
            public float Timer, Phase;
            public Vector3 Home, Target;
            public List<CityView.TreeSpot> Trees;
        }

        City city;
        CityView view;
        readonly List<Worker> workers = new List<Worker>();
        readonly Dictionary<int, GameObject> marks = new Dictionary<int, GameObject>();
        readonly List<int> stations = new List<int>();
        readonly HashSet<int> crewTiles = new HashSet<int>();
        float rescan;
        static Mesh markMesh;

        public void Init(City c, CityView v)
        {
            city = c;
            view = v;
            foreach (Transform child in transform) Destroy(child.gameObject);
            workers.Clear();
            marks.Clear();
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
            }

            // Free lumberjacks take the next marked tile in reach of a camp.
            if (dt > 0)
            {
                int busy = 0;
                foreach (var w in workers) if (w.Manual) busy++;
                foreach (int s in stations)
                {
                    while (busy < city.Lumberjacks)
                    {
                        int tile = city.TakeMarked(s);
                        if (tile < 0) break;
                        Send(s, tile, true);
                        busy++;
                    }
                }
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
            UpdateMarks();
        }

        Vector3 Centre(int anchor)
        {
            var (w, h) = city.Size(anchor);
            return new Vector3((anchor % city.Width + w * .5f) * CityView.TileSize, 0, (anchor / city.Width + h * .5f) * CityView.TileSize);
        }

        void Send(int station, int tile, bool manual)
        {
            var home = Centre(station);
            var go = Figures.Create(transform, (uint)(tile * 7 + station), Figures.Role.Lumberjack);
            go.transform.position = home;
            var w = new Worker { Go = go, Station = station, Tile = tile, Manual = manual, Home = home };
            if (manual)
            {
                // The trees of the tile, nearest to the camp first.
                w.Trees = view.BeginFelling(tile);
                w.Trees.Sort((a, b) => (a.At - home).sqrMagnitude.CompareTo((b.At - home).sqrMagnitude));
                w.Tree = 0;
                w.Target = Beside(w.Trees[0].At, home);
            }
            else w.Target = Beside(CityView.TileCentre(tile % city.Width, tile / city.Width), home);
            workers.Add(w);
        }

        /// <summary>Where to stand to chop a tree: just beside it, on the side he comes from.</summary>
        static Vector3 Beside(Vector3 tree, Vector3 from)
        {
            var d = from - tree;
            d.y = 0;
            return tree + (d.sqrMagnitude > .01f ? d.normalized : Vector3.back) * 1.1f;
        }

        /// <summary>Moves a lumberjack along; false once he is back home (or his camp is gone).</summary>
        bool Step(Worker w, float dt)
        {
            var def = city.DefAt(w.Station);
            if (def == null || def.LumberReach == 0 || city.Anchor[w.Station] != w.Station)
            {
                // His camp was demolished: the tile goes back to the forest, to be marked again.
                if (w.Manual && w.State != State.Back)
                {
                    city.StopFelling(w.Tile);
                    view.EndFelling(w.Tile);
                }
                return false;
            }
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
                    var aim = (w.Manual ? w.Trees[w.Tree].At : CityView.TileCentre(w.Tile % city.Width, w.Tile / city.Width)) - t.position;
                    aim.y = 0;
                    if (aim.sqrMagnitude > .01f) t.rotation = Quaternion.LookRotation(aim);
                    Figures.Swing(w.Go, w.Timer % .7f / .7f);
                    if (w.Manual)
                    {
                        if (w.Timer < ChopTime) break;
                        view.FellOne(w.Tile, TreeIndex(w), t.position);
                        Figures.Swing(w.Go, 0);
                        w.State = State.Watch;
                        w.Timer = 0;
                    }
                    // A sawmill's tree falls when the simulation says; he gives up if the job was dropped.
                    else if (city.Terrain[w.Tile] != Simulation.Terrain.Forest || w.Timer > 90)
                    {
                        Figures.Swing(w.Go, 0);
                        w.State = State.Watch;
                        w.Timer = 0;
                    }
                    break;
                case State.Watch:
                    w.Timer += dt;
                    if (w.Timer < WatchTime) break;
                    if (w.Manual && ++w.Tree < w.Trees.Count)
                    {
                        w.Target = Beside(w.Trees[w.Tree].At, t.position);
                        w.State = State.Out;
                        break;
                    }
                    if (w.Manual)
                    {
                        // The tile is clear: the map shows its stumps.
                        city.FellTree(w.Tile);
                        view.EndFelling(w.Tile);
                    }
                    w.State = State.Back;
                    Figures.Carry(w.Go, true);
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

        /// <summary>Index in the tile's own order of the tree he is on (his list is sorted by distance).</summary>
        int TreeIndex(Worker w)
        {
            var spots = CityView.TreesOn(w.Tile % city.Width, w.Tile / city.Width);
            var at = w.Trees[w.Tree].At;
            for (int k = 0; k < spots.Count; k++)
                if ((spots[k].At - at).sqrMagnitude < .001f) return k;
            return -1;
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

        /// <summary>An orange frame on the ground and a flag on every tile waiting to be felled or being felled.</summary>
        void UpdateMarks()
        {
            if (markMesh == null)
            {
                var b = new MeshBuilder(2);
                const float T = CityView.TileSize, line = .35f, y = .25f;
                b.Flat(1, .2f, .2f, T - .2f, .2f + line, y);
                b.Flat(1, .2f, T - .2f - line, T - .2f, T - .2f, y);
                b.Flat(1, .2f, .2f + line, .2f + line, T - .2f - line, y);
                b.Flat(1, T - .2f - line, .2f + line, T - .2f, T - .2f - line, y);
                b.Box(0, new Vector3(.6f, 0, .6f), new Vector3(.12f, 8.2f, .12f));
                b.Box(1, new Vector3(1.05f, 7.3f, .6f), new Vector3(.9f, .65f, .05f));
                markMesh = b.ToMesh();
            }
            var wanted = new HashSet<int>(city.Marked);
            foreach (var w in workers) if (w.Manual && w.State != State.Back) wanted.Add(w.Tile);
            var gone = new List<int>();
            foreach (var kv in marks) if (!wanted.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (int i in gone)
            {
                Destroy(marks[i]);
                marks.Remove(i);
            }
            foreach (int i in wanted)
            {
                if (marks.ContainsKey(i)) continue;
                var go = new GameObject("Casilla marcada", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(i % city.Width * CityView.TileSize, 0, i / city.Width * CityView.TileSize);
                go.GetComponent<MeshFilter>().sharedMesh = markMesh;
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { Figures.Colour("#F2EEE2"), Figures.Colour("#FF7A1A", .3f) };
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                marks[i] = go;
            }
        }
    }
}
