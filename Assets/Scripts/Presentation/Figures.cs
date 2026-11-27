using System.Collections.Generic;
using UnityEngine;

namespace Horizonte.Presentation
{
    /// <summary>
    /// People for the streets, the forest and the building sites: simple placeholder figures 1.7 m tall, facing +Z, or the
    /// artist's persona*.fbx models once they are in Assets/Modelos. Workers get a hard hat and vest; lumberjacks an axe.
    /// </summary>
    public static class Figures
    {
        public enum Role { Walker, Worker, Lumberjack }

        static Mesh body, hat, axe, log;
        static GameObject[] models;
        static Material[][] looks;
        static readonly Dictionary<string, Material> colours = new Dictionary<string, Material>();

        static readonly string[] Shirts = { "#D9534F", "#F0AD4E", "#5BC0DE", "#5CB85C", "#F7F7F2", "#8E6CC8", "#E58AB0", "#2F6FAE" };
        static readonly string[] Trousers = { "#2E3A4F", "#5A4A3A", "#3B3B3B", "#6C7A89" };
        static readonly string[] Skins = { "#F1C7A5", "#C98E64", "#8D5A3B", "#E0AC82" };
        static readonly string[] Hair = { "#2B211C", "#5A3B22", "#9A6B3F", "#1C1C1C" };

        /// <summary>A plain coloured material that shares the shader already in the build.</summary>
        public static Material Colour(string hex, float smoothness = .2f, float metallic = 0)
        {
            string key = hex + smoothness + metallic;
            if (colours.TryGetValue(key, out var m)) return m;
            ColorUtility.TryParseHtmlString(hex, out var c);
            m = new Material(Palette.Get("StreetLamp")) { name = "Figura " + hex };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            m.enableInstancing = true;
            return colours[key] = m;
        }

        static void Load()
        {
            if (body != null) return;
            var found = new List<GameObject>();
            foreach (var model in Resources.LoadAll<GameObject>("Buildings"))
                if (model.name.ToLowerInvariant().StartsWith("persona")) found.Add(model);
            models = found.ToArray();

            var b = new MeshBuilder(4);
            // Trousers, shirt, skin, hair.
            b.Box(0, new Vector3(-.09f, 0, 0), new Vector3(.15f, .82f, .2f));
            b.Box(0, new Vector3(.09f, 0, 0), new Vector3(.15f, .82f, .2f));
            b.Box(1, new Vector3(0, .8f, 0), new Vector3(.44f, .58f, .24f));
            b.Box(1, new Vector3(-.27f, .86f, 0), new Vector3(.1f, .5f, .14f));
            b.Box(1, new Vector3(.27f, .86f, 0), new Vector3(.1f, .5f, .14f));
            b.Cylinder(2, new Vector3(0, 1.38f, 0), .12f, .25f, 8);
            b.Box(3, new Vector3(0, 1.6f, -.01f), new Vector3(.25f, .09f, .26f));
            body = b.ToMesh();

            b = new MeshBuilder(1);
            b.Cylinder(0, new Vector3(0, 1.58f, 0), .17f, .13f, 8);
            b.Box(0, new Vector3(0, 1.58f, .1f), new Vector3(.3f, .03f, .14f));
            hat = b.ToMesh();

            b = new MeshBuilder(2);
            // Handle along +Y from the hand, head at the top.
            b.Box(0, new Vector3(0, 0, 0), new Vector3(.05f, .85f, .05f));
            b.Box(1, new Vector3(.08f, .7f, 0), new Vector3(.2f, .14f, .04f));
            axe = b.ToMesh();

            b = new MeshBuilder(2);
            b.Cylinder(0, new Vector3(0, 0, 0), .14f, 1.8f, 7);
            b.Cylinder(1, new Vector3(0, 1.8f, 0), .13f, .01f, 7);
            log = b.ToMesh();

            looks = new Material[12][];
            for (int k = 0; k < looks.Length; k++)
                looks[k] = new[] { Colour(Trousers[k % 4]), Colour(Shirts[k % 8]), Colour(Skins[(k * 3) % 4]), Colour(Hair[(k * 5 + 1) % 4]) };
        }

        /// <summary>A person standing at the origin of a new object, facing +Z. Its child "Cuerpo" bobs when walking.</summary>
        public static GameObject Create(Transform parent, uint seed, Role role = Role.Walker)
        {
            Load();
            var go = new GameObject(role == Role.Walker ? "Persona" : role == Role.Worker ? "Trabajador" : "Leñador");
            go.transform.SetParent(parent, false);
            GameObject figure;
            if (models.Length > 0 && role == Role.Walker)
            {
                figure = Object.Instantiate(models[seed % (uint)models.Length]);
                figure.transform.localRotation = Quaternion.Euler(0, 180, 0);
            }
            else
            {
                figure = new GameObject("Figura", typeof(MeshFilter), typeof(MeshRenderer));
                figure.GetComponent<MeshFilter>().sharedMesh = body;
                var look = (Material[])looks[seed % (uint)looks.Length].Clone();
                if (role == Role.Worker) look[1] = Colour("#F28C28");
                if (role == Role.Lumberjack)
                {
                    look[1] = Colour("#B23A2E");
                    look[0] = Colour("#3C4A5C");
                }
                figure.GetComponent<MeshRenderer>().sharedMaterials = look;
            }
            var bodyRoot = new GameObject("Cuerpo").transform;
            bodyRoot.SetParent(go.transform, false);
            figure.transform.SetParent(bodyRoot, false);
            if (role == Role.Worker) Part(bodyRoot, "Casco", hat, Colour("#F2C230", .5f), Vector3.zero, Quaternion.identity);
            if (role == Role.Lumberjack)
            {
                Part(bodyRoot, "Gorro", hat, Colour("#2E5E3A"), Vector3.zero, Quaternion.identity);
                var tool = Part(bodyRoot, "Hacha", axe, null, new Vector3(.3f, .62f, .1f), Quaternion.Euler(-20, 0, 0));
                tool.GetComponent<MeshRenderer>().sharedMaterials = new[] { Colour("#7A5332"), Colour("#8A9096", .5f, .7f) };
                var carried = Part(bodyRoot, "Tronco", log, null, new Vector3(-.05f, 1.45f, -.9f), Quaternion.Euler(80, 0, 0));
                carried.GetComponent<MeshRenderer>().sharedMaterials = new[] { Palette.Get("Trunk"), Colour("#C9A26B") };
                carried.SetActive(false);
            }
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            return go;
        }

        /// <summary>The log a lumberjack carries on the shoulder; shown on the way back.</summary>
        public static void Carry(GameObject person, bool carrying)
        {
            var t = person.transform.Find("Cuerpo/Tronco");
            if (t != null) t.gameObject.SetActive(carrying);
        }

        /// <summary>Raises and swings the axe while chopping (0 to 1 over a stroke).</summary>
        public static void Swing(GameObject person, float stroke)
        {
            var t = person.transform.Find("Cuerpo/Hacha");
            if (t == null) return;
            float angle = stroke < .7f ? Mathf.Lerp(-20, -150, stroke / .7f) : Mathf.Lerp(-150, -20, (stroke - .7f) / .3f);
            t.localRotation = Quaternion.Euler(angle, 0, 0);
        }

        static GameObject Part(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>A light bob in step with walking, on the figure's "Cuerpo".</summary>
        public static void Bob(GameObject person, float phase)
        {
            var t = person.transform.GetChild(0);
            t.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(phase)) * .06f, 0);
        }
    }
}
