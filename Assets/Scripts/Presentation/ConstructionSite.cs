using UnityEngine;

namespace Horizonte.Presentation
{
    /// <summary>
    /// A building going up: scaffolding around it, the building rising from the ground as work advances, a lift that runs
    /// up and down the scaffold and, for taller or bigger buildings, a tower crane that swings its load around.
    /// </summary>
    public sealed class ConstructionSite : MonoBehaviour
    {
        Transform building, jib, trolley, cable, hook, lift;
        Vector3 buildingScale;
        float height, craneHeight, jibLength, liftMin, shown = .02f, target = .02f, phase;

        static Mesh cube;

        /// <summary>Builds the site around a building already placed in the world and shrinks the building to its foundations.</summary>
        public static ConstructionSite Create(Transform parent, GameObject buildingObject, uint seed)
        {
            // Measure the finished building before it shrinks to its foundations.
            var bounds = new Bounds(buildingObject.transform.position, Vector3.zero);
            foreach (var r in buildingObject.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);

            var go = new GameObject("Obra");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(bounds.center.x, buildingObject.transform.position.y, bounds.center.z);
            var site = go.AddComponent<ConstructionSite>();
            site.building = buildingObject.transform;
            site.buildingScale = buildingObject.transform.localScale;
            site.height = Mathf.Max(2.5f, bounds.size.y);
            site.phase = seed % 1000 * .37f;
            site.Build(bounds.size.x, bounds.size.z);
            site.Apply();
            return site;
        }

        /// <summary>How far the work has gone, 0 to 1. The building and scaffold catch up smoothly.</summary>
        public void SetProgress(float progress) => target = Mathf.Clamp(progress, .02f, 1);

        void Build(float sizeX, float sizeZ)
        {
            var metal = Palette.Get("Metal");
            var wood = Palette.Get("Trunk");
            var yellow = Palette.Get("ZoneIndustrial");
            var grey = Palette.Get("RoofFlat");

            // Scaffold: poles every couple of metres around the building and plank decks every two metres.
            const float margin = .9f, deck = .75f, poleSpacing = 2.6f, levelSpacing = 2f;
            float hx = sizeX * .5f + margin, hz = sizeZ * .5f + margin, top = height + 1;
            var b = new MeshBuilder(2);
            void Poles(Vector3 from, Vector3 to)
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(from, to) / poleSpacing));
                for (int k = 0; k <= n; k++)
                {
                    var p = Vector3.Lerp(from, to, k / (float)n);
                    b.Box(0, p, new Vector3(.12f, top, .12f));
                    b.Box(0, p + new Vector3(Mathf.Sign(p.x) * deck * (Mathf.Abs(p.x) >= hx - .01f ? 1 : 0), 0, Mathf.Sign(p.z) * deck * (Mathf.Abs(p.z) >= hz - .01f ? 1 : 0)), new Vector3(.12f, top, .12f));
                }
            }
            Poles(new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz));
            Poles(new Vector3(-hx, 0, hz), new Vector3(hx, 0, hz));
            Poles(new Vector3(-hx, 0, -hz), new Vector3(-hx, 0, hz));
            Poles(new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz));
            for (float y = levelSpacing; y <= top + .01f; y += levelSpacing)
            {
                float o = deck * .5f;
                b.Box(1, new Vector3(0, y, -hz - o), new Vector3(hx * 2 + deck * 2, .07f, deck));
                b.Box(1, new Vector3(0, y, hz + o), new Vector3(hx * 2 + deck * 2, .07f, deck));
                b.Box(1, new Vector3(-hx - o, y, 0), new Vector3(deck, .07f, hz * 2));
                b.Box(1, new Vector3(hx + o, y, 0), new Vector3(deck, .07f, hz * 2));
                // Guard rails.
                b.Box(0, new Vector3(0, y + 1, -hz - deck), new Vector3(hx * 2 + deck * 2, .05f, .05f));
                b.Box(0, new Vector3(0, y + 1, hz + deck), new Vector3(hx * 2 + deck * 2, .05f, .05f));
                b.Box(0, new Vector3(-hx - deck, y + 1, 0), new Vector3(.05f, .05f, hz * 2 + deck * 2));
                b.Box(0, new Vector3(hx + deck, y + 1, 0), new Vector3(.05f, .05f, hz * 2 + deck * 2));
            }
            var scaffold = new GameObject("Andamios", typeof(MeshFilter), typeof(MeshRenderer));
            scaffold.transform.SetParent(transform, false);
            scaffold.GetComponent<MeshFilter>().sharedMesh = b.ToMesh();
            scaffold.GetComponent<MeshRenderer>().sharedMaterials = new[] { metal, wood };

            // Lift platform on the front of the scaffold.
            liftMin = .1f;
            lift = Cube(transform, new Vector3(-hx * .4f, liftMin, -hz - deck - .9f), new Vector3(1.6f, .25f, 1.6f), yellow).transform;
            Cube(lift, new Vector3(0, .6f, 0), new Vector3(.9f, 1.2f, .9f), grey);
            Cube(transform, new Vector3(-hx * .4f, top * .5f, -hz - deck - .1f), new Vector3(.25f, top, .25f), metal);

            // Tower crane beside tall or wide buildings.
            if (height < 8 && Mathf.Max(sizeX, sizeZ) < 15) return;
            craneHeight = height + 9;
            jibLength = Mathf.Max(12, Mathf.Max(sizeX, sizeZ) * .9f);
            var towerPos = new Vector3(hx + 3, 0, hz + 3);
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
            shown = Mathf.MoveTowards(shown, target, Time.deltaTime * .35f);
            Apply();
        }

        void Apply()
        {
            if (building != null) building.localScale = new Vector3(buildingScale.x, buildingScale.y * shown, buildingScale.z);
            float t = Time.time + phase;
            // The lift climbs to the working floor and back down for more material.
            if (lift != null)
            {
                float top = Mathf.Max(liftMin, height * shown - .5f);
                lift.localPosition = new Vector3(lift.localPosition.x, Mathf.Lerp(liftMin, top, Mathf.SmoothStep(0, 1, Mathf.PingPong(t * .18f, 1))), lift.localPosition.z);
            }
            if (jib == null) return;
            jib.localRotation = Quaternion.Euler(0, 200 + Mathf.Sin(t * .23f) * 75, 0);
            trolley.localPosition = new Vector3(Mathf.Lerp(3, jibLength - 1, Mathf.SmoothStep(0, 1, Mathf.PingPong(t * .11f, 1))), -.4f, 0);
            float drop = Mathf.Lerp(2, craneHeight - Mathf.Max(2, height * shown) + 1, Mathf.SmoothStep(0, 1, Mathf.PingPong(t * .15f + .3f, 1)));
            cable.localScale = new Vector3(.06f, drop, .06f);
            cable.localPosition = new Vector3(0, -drop * .5f - .5f, 0);
            hook.localPosition = new Vector3(0, -drop - .7f, 0);
        }
    }
}
