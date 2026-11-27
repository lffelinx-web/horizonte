using System.Collections.Generic;
using UnityEngine;

namespace Horizonte.Presentation
{
    /// <summary>
    /// A building going up, in stages, without stretching the building itself:
    /// the lot is fenced with tape and materials arrive; the foundation slab is poured; the concrete frame rises floor by
    /// floor with the scaffold climbing beside it (and a tower crane for taller or bigger buildings); then the finished
    /// building appears inside the scaffold, which comes down at the end. Workers in hard hats walk the lot all along.
    /// </summary>
    public sealed class ConstructionSite : MonoBehaviour
    {
        const float LotStage = .1f, FoundationStage = .22f, FrameStage = .82f, ScaffoldDown = .96f;
        const float FloorHeight = 3f, ScaffoldLevel = 2f;

        Transform jib, trolley, cable, hook, lift, foundation;
        GameObject building;
        readonly List<GameObject> floors = new List<GameObject>();
        readonly List<GameObject> scaffoldLevels = new List<GameObject>();
        readonly List<GameObject> lotOnly = new List<GameObject>();
        GameObject scaffoldRoot, liftRoot;
        float height, craneHeight, jibLength, shown, target, phase, hx, hz, lotX, lotZ;
        int floorsHint;

        sealed class Worker
        {
            public GameObject Go;
            public Vector3 Target;
            public float Wait, Phase;
        }
        readonly List<Worker> workers = new List<Worker>();
        uint random;

        static Mesh cube;

        /// <summary>Builds the site around a building already placed in the world and hides the building until it is nearly done.</summary>
        /// <param name="lotX">Width of the lot in metres (east-west) and <paramref name="lotZ"/> its depth; nothing of the site leaves it.</param>
        public static ConstructionSite Create(Transform parent, GameObject buildingObject, uint seed, float lotX, float lotZ, int floors)
        {
            // Measure the finished building before hiding it.
            var bounds = new Bounds(buildingObject.transform.position, Vector3.zero);
            foreach (var r in buildingObject.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);

            var go = new GameObject("Obra");
            go.transform.SetParent(parent, false);
            go.transform.position = buildingObject.transform.position;
            var site = go.AddComponent<ConstructionSite>();
            site.building = buildingObject;
            // Placeholders may carry a tall chimney; the frame follows the floors.
            site.height = Mathf.Max(2.5f, Mathf.Min(bounds.size.y, Mathf.Max(3, floors * 3.8f) + 2));
            site.phase = seed % 1000 * .37f;
            site.random = seed | 1;
            site.lotX = lotX * .5f;
            site.lotZ = lotZ * .5f;
            site.floorsHint = floors;
            // The frame and its scaffold fit inside the lot, whatever the model measures.
            site.Build(Mathf.Min(bounds.size.x, lotX - 2.1f), Mathf.Min(bounds.size.z, lotZ - 2.1f));
            site.Apply();
            return site;
        }

        /// <summary>How far the work has gone, 0 to 1. What is shown catches up smoothly.</summary>
        public void SetProgress(float progress)
        {
            target = Mathf.Clamp01(progress);
            if (shown == 0 && target > 0) shown = target;
        }

        void Build(float sizeX, float sizeZ)
        {
            var metal = Palette.Get("Metal");
            var wood = Palette.Get("Trunk");
            var yellow = Palette.Get("ZoneIndustrial");
            var grey = Palette.Get("RoofFlat");
            var concrete = Palette.Get("Sidewalk");
            var tape = Figures.Colour("#E8442E", .3f);
            var bricks = Figures.Colour("#B8653A");

            hx = sizeX * .5f;
            hz = sizeZ * .5f;

            // Lot: tape on posts just inside its edge, and materials stacked by the front.
            float tx = lotX - .3f, tz = lotZ - .3f;
            foreach (var (a, b) in new[] { (new Vector3(-tx, 0, -tz), new Vector3(tx, 0, -tz)), (new Vector3(tx, 0, -tz), new Vector3(tx, 0, tz)), (new Vector3(tx, 0, tz), new Vector3(-tx, 0, tz)), (new Vector3(-tx, 0, tz), new Vector3(-tx, 0, -tz)) })
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(a, b) / 3));
                for (int k = 0; k < n; k++)
                {
                    var p = Vector3.Lerp(a, b, k / (float)n);
                    lotOnly.Add(Cube(transform, p + Vector3.up * .5f, new Vector3(.08f, 1, .08f), wood));
                }
                var mid = (a + b) * .5f;
                lotOnly.Add(Cube(transform, mid + Vector3.up * .85f, new Vector3(Mathf.Abs(b.x - a.x) + .05f, .08f, Mathf.Abs(b.z - a.z) + .05f), tape));
            }
            Cube(transform, new Vector3(-tx * .55f, .35f, -tz + .7f), new Vector3(1.2f, .7f, .9f), bricks);
            Cube(transform, new Vector3(tx * .45f, .2f, -tz + .7f), new Vector3(Mathf.Min(3.2f, tx * .7f), .4f, .8f), wood);

            foundation = Cube(transform, new Vector3(0, .2f, 0), new Vector3(sizeX + .4f, .4f, sizeZ + .4f), concrete).transform;

            // Concrete frame: a slab and its columns per floor.
            int floorCount = Mathf.Max(1, Mathf.RoundToInt(height / FloorHeight));
            float floorH = height / floorCount;
            int cols = Mathf.Max(2, Mathf.RoundToInt(sizeX / 5) + 1), rows = Mathf.Max(2, Mathf.RoundToInt(sizeZ / 5) + 1);
            for (int f = 0; f < floorCount; f++)
            {
                var level = new GameObject("Piso " + (f + 1));
                level.transform.SetParent(transform, false);
                var b = new MeshBuilder(1);
                float y0 = .4f + f * floorH;
                for (int c = 0; c < cols; c++)
                for (int r = 0; r < rows; r++)
                {
                    if (c > 0 && c < cols - 1 && r > 0 && r < rows - 1) continue;
                    float x = Mathf.Lerp(-hx + .3f, hx - .3f, c / (float)(cols - 1)), z = Mathf.Lerp(-hz + .3f, hz - .3f, r / (float)(rows - 1));
                    b.Box(0, new Vector3(x, y0, z), new Vector3(.4f, floorH - .25f, .4f));
                }
                b.Box(0, new Vector3(0, y0 + floorH - .25f, 0), new Vector3(sizeX, .25f, sizeZ));
                var mf = level.AddComponent<MeshFilter>();
                mf.sharedMesh = b.ToMesh();
                level.AddComponent<MeshRenderer>().sharedMaterial = grey;
                floors.Add(level);
            }

            // Scaffold in levels of two metres, shown as the frame rises.
            const float margin = .35f, deck = .5f, poleSpacing = 2.6f;
            float sx = hx + margin, sz = hz + margin, top = height + 1;
            scaffoldRoot = new GameObject("Andamios");
            scaffoldRoot.transform.SetParent(transform, false);
            for (float y = 0; y < top; y += ScaffoldLevel)
            {
                var b = new MeshBuilder(2);
                void Poles(Vector3 from, Vector3 to)
                {
                    int n = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(from, to) / poleSpacing));
                    for (int k = 0; k <= n; k++)
                    {
                        var p = Vector3.Lerp(from, to, k / (float)n) + Vector3.up * y;
                        b.Box(0, p, new Vector3(.12f, ScaffoldLevel, .12f));
                        b.Box(0, p + new Vector3(Mathf.Sign(p.x) * deck * (Mathf.Abs(p.x) >= sx - .01f ? 1 : 0), 0, Mathf.Sign(p.z) * deck * (Mathf.Abs(p.z) >= sz - .01f ? 1 : 0)), new Vector3(.12f, ScaffoldLevel, .12f));
                    }
                }
                Poles(new Vector3(-sx, 0, -sz), new Vector3(sx, 0, -sz));
                Poles(new Vector3(-sx, 0, sz), new Vector3(sx, 0, sz));
                Poles(new Vector3(-sx, 0, -sz), new Vector3(-sx, 0, sz));
                Poles(new Vector3(sx, 0, -sz), new Vector3(sx, 0, sz));
                float yd = y + ScaffoldLevel, o = deck * .5f;
                b.Box(1, new Vector3(0, yd, -sz - o), new Vector3(sx * 2 + deck * 2, .07f, deck));
                b.Box(1, new Vector3(0, yd, sz + o), new Vector3(sx * 2 + deck * 2, .07f, deck));
                b.Box(1, new Vector3(-sx - o, yd, 0), new Vector3(deck, .07f, sz * 2));
                b.Box(1, new Vector3(sx + o, yd, 0), new Vector3(deck, .07f, sz * 2));
                b.Box(0, new Vector3(0, yd + 1, -sz - deck), new Vector3(sx * 2 + deck * 2, .05f, .05f));
                b.Box(0, new Vector3(0, yd + 1, sz + deck), new Vector3(sx * 2 + deck * 2, .05f, .05f));
                b.Box(0, new Vector3(-sx - deck, yd + 1, 0), new Vector3(.05f, .05f, sz * 2 + deck * 2));
                b.Box(0, new Vector3(sx + deck, yd + 1, 0), new Vector3(.05f, .05f, sz * 2 + deck * 2));
                var level = new GameObject("Nivel", typeof(MeshFilter), typeof(MeshRenderer));
                level.transform.SetParent(scaffoldRoot.transform, false);
                level.GetComponent<MeshFilter>().sharedMesh = b.ToMesh();
                level.GetComponent<MeshRenderer>().sharedMaterials = new[] { metal, wood };
                scaffoldLevels.Add(level);
            }

            // Lift on the front of the scaffold.
            liftRoot = new GameObject("Elevador");
            liftRoot.transform.SetParent(transform, false);
            lift = Cube(liftRoot.transform, new Vector3(-sx * .4f, .1f, -sz - deck - .9f), new Vector3(1.6f, .25f, 1.6f), yellow).transform;
            Cube(lift, new Vector3(0, .6f, 0), new Vector3(.9f, 1.2f, .9f), grey);
            Cube(liftRoot.transform, new Vector3(-sx * .4f, top * .5f, -sz - deck - .1f), new Vector3(.25f, top, .25f), metal);

            // Workers walk the lot.
            int crew = Mathf.Clamp(Mathf.RoundToInt(sizeX * sizeZ / 70), 2, 5);
            for (int k = 0; k < crew; k++)
            {
                var person = Figures.Create(transform, Next(), Figures.Role.Worker);
                person.transform.localPosition = RandomSpot();
                workers.Add(new Worker { Go = person, Target = RandomSpot(), Wait = Next() % 100 / 40f });
            }

            // Tower crane in a back corner of the lot, for buildings of three floors or more on a lot of 2 x 2 or more.
            if (floorsHint < 3 || Mathf.Min(lotX, lotZ) < 9.5f) return;
            craneHeight = height + 5;
            jibLength = Mathf.Clamp(Mathf.Max(lotX, lotZ) * 1.4f, 9, 22);
            var towerPos = new Vector3(lotX - 1, 0, lotZ - 1);
            for (float y = 0; y < craneHeight; y += 3)
            {
                Cube(transform, towerPos + new Vector3(0, y + 1.5f, 0), new Vector3(1.2f, 2.6f, 1.2f), yellow);
                Cube(transform, towerPos + new Vector3(0, y + 2.85f, 0), new Vector3(1.5f, .3f, 1.5f), yellow);
            }
            Cube(transform, towerPos + new Vector3(0, craneHeight + .8f, 0), new Vector3(1.6f, 1.6f, 1.6f), grey);
            jib = new GameObject("Pluma").transform;
            jib.SetParent(transform, false);
            jib.localPosition = towerPos + new Vector3(0, craneHeight + 1.8f, 0);
            Cube(jib, new Vector3(jibLength * .5f - 1, 0, 0), new Vector3(jibLength + 2, .8f, .8f), yellow);
            Cube(jib, new Vector3(-4, 0, 0), new Vector3(7, .7f, .7f), yellow);
            Cube(jib, new Vector3(-6.5f, -.9f, 0), new Vector3(2, 1.6f, 1.4f), grey);
            Cube(jib, new Vector3(0, 2.5f, 0), new Vector3(.4f, 4.4f, .4f), yellow);
            trolley = new GameObject("Carro").transform;
            trolley.SetParent(jib, false);
            Cube(trolley, new Vector3(0, -.55f, 0), new Vector3(1.2f, .3f, 1f), grey);
            cable = Cube(trolley, new Vector3(0, -1, 0), new Vector3(.06f, 1, .06f), metal).transform;
            hook = new GameObject("Gancho").transform;
            hook.SetParent(trolley, false);
            Cube(hook, Vector3.zero, new Vector3(.5f, .5f, .5f), yellow);
            // The load: a bundle of beams.
            Cube(hook, new Vector3(0, -.9f, 0), new Vector3(3.2f, .5f, .9f), wood);
        }

        Vector3 RandomSpot()
        {
            uint r = Next();
            return new Vector3(((r & 1023) / 1023f * 2 - 1) * Mathf.Min(hx + .6f, lotX - .7f), 0, (((r >> 10) & 1023) / 1023f * 2 - 1) * Mathf.Min(hz + .6f, lotZ - .7f));
        }

        uint Next()
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            return random;
        }

        static GameObject Cube(Transform parent, Vector3 position, Vector3 size, Material material)
        {
            if (cube == null)
            {
                var b = new MeshBuilder(1);
                b.Box(0, new Vector3(0, -.5f, 0), Vector3.one, true);
                cube = b.ToMesh();
                cube.name = "Cubo";
            }
            var go = new GameObject("Pieza", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.GetComponent<MeshFilter>().sharedMesh = cube;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        void Update()
        {
            shown = Mathf.MoveTowards(shown, target, Time.deltaTime * .2f);
            Apply();
            MoveWorkers(Time.deltaTime);
        }

        void Apply()
        {
            float p = shown;
            // How high the frame has risen, in metres.
            float frame = height * Mathf.Clamp01(Mathf.InverseLerp(FoundationStage, FrameStage, p));
            bool done = p >= FrameStage;

            if (building != null) building.SetActive(done);
            foreach (var go in lotOnly) go.SetActive(p < FrameStage);
            foundation.gameObject.SetActive(p >= LotStage && !done);
            foundation.localScale = new Vector3(foundation.localScale.x, .4f * Mathf.Clamp01(Mathf.InverseLerp(LotStage, FoundationStage, p)) + .01f, foundation.localScale.z);
            float floorH = height / floors.Count;
            for (int f = 0; f < floors.Count; f++) floors[f].SetActive(!done && frame >= (f + .35f) * floorH);
            for (int k = 0; k < scaffoldLevels.Count; k++) scaffoldLevels[k].SetActive(p >= FoundationStage && p < ScaffoldDown && k * ScaffoldLevel < Mathf.Max(frame, done ? height : 0) + .5f);
            liftRoot.SetActive(p >= FoundationStage && p < ScaffoldDown);
            foreach (var w in workers) w.Go.SetActive(p < ScaffoldDown);

            float t = Time.time + phase;
            if (lift != null)
            {
                float top = Mathf.Max(.1f, frame - .5f);
                lift.localPosition = new Vector3(lift.localPosition.x, Mathf.Lerp(.1f, top, Mathf.SmoothStep(0, 1, Mathf.PingPong(t * .18f, 1))), lift.localPosition.z);
            }
            if (jib == null) return;
            jib.localRotation = Quaternion.Euler(0, 200 + Mathf.Sin(t * .23f) * 75, 0);
            trolley.localPosition = new Vector3(Mathf.Lerp(3, jibLength - 1, Mathf.SmoothStep(0, 1, Mathf.PingPong(t * .11f, 1))), -.4f, 0);
            float drop = Mathf.Lerp(2, craneHeight - Mathf.Max(2, frame) + 1, Mathf.SmoothStep(0, 1, Mathf.PingPong(t * .15f + .3f, 1)));
            cable.localScale = new Vector3(.06f, drop, .06f);
            cable.localPosition = new Vector3(0, -drop * .5f - .5f, 0);
            hook.localPosition = new Vector3(0, -drop - .7f, 0);
        }

        /// <summary>Each worker walks to a spot on the lot, stops there a while working, then picks another.</summary>
        void MoveWorkers(float dt)
        {
            foreach (var w in workers)
            {
                if (!w.Go.activeSelf) continue;
                var t = w.Go.transform;
                if (w.Wait > 0)
                {
                    w.Wait -= dt;
                    continue;
                }
                var to = w.Target - t.localPosition;
                to.y = 0;
                if (to.magnitude < .1f)
                {
                    w.Target = RandomSpot();
                    w.Wait = 1.5f + Next() % 100 / 30f;
                    continue;
                }
                t.localPosition += to.normalized * Mathf.Min(to.magnitude, 1.2f * dt);
                t.localRotation = Quaternion.RotateTowards(t.localRotation, Quaternion.LookRotation(to), 360 * dt);
                w.Phase += dt * 8;
                Figures.Bob(w.Go, w.Phase);
            }
        }

        void OnDestroy()
        {
            if (building != null) building.SetActive(true);
        }
    }
}
