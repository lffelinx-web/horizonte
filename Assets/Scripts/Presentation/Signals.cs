using System.Collections.Generic;
using Horizonte.Simulation;
using UnityEngine;

namespace Horizonte.Presentation
{
    /// <summary>
    /// Traffic lights at every junction of three or four streets: a post on the right-hand corner of each street that
    /// arrives, facing the cars coming in, with red, amber and green lamps. North-south and east-west take turns: green,
    /// amber, a moment of red both ways. Cars stop at the line on red; people cross on the zebra only while the traffic
    /// going their way has green. Each junction runs a little out of step with the others.
    /// </summary>
    public sealed class Signals : MonoBehaviour
    {
        public enum Light { Green, Amber, Red }

        const float Green = 10, Amber = 2.5f, Clear = .5f, Half = Green + Amber + Clear, Cycle = Half * 2;

        public static Signals Instance { get; private set; }

        sealed class Junction
        {
            public GameObject Go;
            public float Offset;
            /// <summary>Lamps of each head: red, amber, green; and whether the head faces north-south traffic.</summary>
            public readonly List<(MeshRenderer red, MeshRenderer amber, MeshRenderer green, bool northSouth)> Heads = new List<(MeshRenderer, MeshRenderer, MeshRenderer, bool)>();
        }

        City city;
        float clock, rescan, beepAt;
        readonly Dictionary<int, Junction> junctions = new Dictionary<int, Junction>();
        Material red, amber, green, off, post;
        static Mesh cube;

        void Awake() => Instance = this;

        public void Init(City c)
        {
            city = c;
            foreach (var j in junctions.Values) if (j.Go != null) Destroy(j.Go);
            junctions.Clear();
            rescan = 0;
        }

        public bool IsJunction(int tile) => junctions.ContainsKey(tile);

        /// <summary>The light for traffic travelling along an axis through a junction (north-south or east-west).</summary>
        public Light State(int tile, bool northSouth)
        {
            if (!junctions.TryGetValue(tile, out var j)) return Light.Green;
            float t = Mathf.Repeat(clock + j.Offset, Cycle);
            bool nsTurn = t < Half;
            if (nsTurn != northSouth) return Light.Red;
            float into = nsTurn ? t : t - Half;
            return into < Green ? Light.Green : into < Green + Amber ? Light.Amber : Light.Red;
        }

        /// <summary>Whether people walking along an axis may cross now (the traffic going their way has green).</summary>
        public bool CanWalk(int tile, bool northSouth) => State(tile, northSouth) == Light.Green;

        public void Tick(float speedFactor)
        {
            if (city == null) return;
            clock += Time.deltaTime * speedFactor;
            rescan -= Time.deltaTime;
            if (rescan <= 0)
            {
                rescan = 2;
                Rescan();
            }
            // The pedestrian beep, a little over once a second, where people may cross.
            beepAt -= Time.deltaTime;
            bool beep = beepAt <= 0 && speedFactor > 0;
            if (beep) beepAt = 1.1f;
            foreach (var kv in junctions)
                if (beep) Sound.At("pitido", CityView.TileCentre(kv.Key % city.Width, kv.Key / city.Width), .35f);
            foreach (var kv in junctions)
                foreach (var (r, a, g, ns) in kv.Value.Heads)
                {
                    var s = State(kv.Key, ns);
                    Set(r, s == Light.Red ? red : off);
                    Set(a, s == Light.Amber ? amber : off);
                    Set(g, s == Light.Green ? green : off);
                }
        }

        static void Set(MeshRenderer r, Material m)
        {
            if (r.sharedMaterial != m) r.sharedMaterial = m;
        }

        void Rescan()
        {
            if (red == null)
            {
                Material Lamp(Color c)
                {
                    var m = new Material(Palette.Get("Selection")) { name = "Semáforo" };
                    m.SetColor("_BaseColor", c);
                    return m;
                }
                red = Lamp(new Color(1, .16f, .1f, 1));
                amber = Lamp(new Color(1, .72f, .1f, 1));
                green = Lamp(new Color(.25f, 1, .4f, 1));
                off = Figures.Colour("#23262B", .4f);
                post = Figures.Colour("#3A3F46", .5f, .5f);
            }
            var now = new HashSet<int>();
            for (int i = 0; i < city.Building.Length; i++)
            {
                int x = i % city.Width, y = i / city.Width;
                if (!city.IsRoad(x, y)) continue;
                int links = 0;
                for (int d = 0; d < 4; d++) if (city.IsRoad(x + City.DX[d], y + City.DY[d])) links++;
                if (links >= 3) now.Add(i);
            }
            var gone = new List<int>();
            foreach (var kv in junctions) if (!now.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (int i in gone)
            {
                Destroy(junctions[i].Go);
                junctions.Remove(i);
            }
            foreach (int i in now)
            {
                if (junctions.TryGetValue(i, out var existing) && existing.Heads.Count == Arms(i)) continue;
                if (existing != null) Destroy(existing.Go);
                junctions[i] = Build(i);
            }
        }

        int Arms(int i)
        {
            int x = i % city.Width, y = i / city.Width, n = 0;
            for (int d = 0; d < 4; d++) if (city.IsRoad(x + City.DX[d], y + City.DY[d])) n++;
            return n;
        }

        Junction Build(int i)
        {
            int x = i % city.Width, y = i / city.Width;
            var j = new Junction { Go = new GameObject("Semáforos"), Offset = CityView.Hash(x, y) % 1000 / 1000f * Cycle };
            j.Go.transform.SetParent(transform, false);
            var centre = CityView.TileCentre(x, y);
            for (int d = 0; d < 4; d++)
            {
                if (!city.IsRoad(x + City.DX[d], y + City.DY[d])) continue;
                // Cars come in from this arm; the post stands on their right-hand corner, facing them.
                var toward = new Vector3(City.DX[d], 0, City.DY[d]);
                var travel = -toward;
                var right = new Vector3(travel.z, 0, -travel.x);
                var foot = centre + toward * 4.55f + right * 4.55f + Vector3.up * .12f;
                var head = new GameObject("Semáforo").transform;
                head.SetParent(j.Go.transform, false);
                head.position = foot;
                head.rotation = Quaternion.LookRotation(toward);
                Part(head, new Vector3(0, 1.6f, 0), new Vector3(.14f, 3.2f, .14f), post);
                Part(head, new Vector3(0, 3.75f, .05f), new Vector3(.42f, 1.15f, .3f), post);
                var r = Part(head, new Vector3(0, 4.1f, .21f), new Vector3(.24f, .24f, .05f), off);
                var a = Part(head, new Vector3(0, 3.75f, .21f), new Vector3(.24f, .24f, .05f), off);
                var g = Part(head, new Vector3(0, 3.4f, .21f), new Vector3(.24f, .24f, .05f), off);
                j.Heads.Add((r, a, g, d == 0 || d == 2));
            }
            return j;
        }

        static MeshRenderer Part(Transform parent, Vector3 centre, Vector3 size, Material m)
        {
            if (cube == null)
            {
                var b = new MeshBuilder(1);
                b.Box(0, new Vector3(0, -.5f, 0), Vector3.one, true);
                cube = b.ToMesh();
            }
            var go = new GameObject("Pieza", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localScale = size;
            go.GetComponent<MeshFilter>().sharedMesh = cube;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return mr;
        }
    }
}
