using UnityEngine;

namespace Horizonte.Presentation
{
    /// <summary>
    /// A tree coming down where the forest is felled: it leans slowly, falls faster and faster, bounces once on the
    /// ground, rests a moment and sinks away, leaving the stumps drawn by the map.
    /// </summary>
    public sealed class FallingTree : MonoBehaviour
    {
        const float FallTime = 1.7f, Rest = 3.5f, Sink = 1.2f;
        static Mesh mesh;
        Vector3 axis;
        float age;

        public static void Spawn(Transform parent, Vector3 at, uint seed)
        {
            if (mesh == null)
            {
                var b = new MeshBuilder(2);
                Trees.Conifer(b, 0, 1, Vector3.zero, 1.15f);
                mesh = b.ToMesh();
                mesh.name = "Árbol talado";
            }
            var go = new GameObject("Árbol cayendo", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.position = at + new Vector3((seed & 7) * .3f - 1, 0, ((seed >> 3) & 7) * .3f - 1);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = Palette.Get("Leaves", "Trunk");
            Sound.At("caida", at, .8f);
            var tree = go.AddComponent<FallingTree>();
            float angle = seed % 360 * Mathf.Deg2Rad;
            tree.axis = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
        }

        /// <summary>Brings down a tree already standing in the world, falling toward a direction on the ground.</summary>
        public static void Topple(GameObject tree, Vector3 toward)
        {
            Sound.At("caida", tree.transform.position, .8f);
            var fall = tree.AddComponent<FallingTree>();
            fall.axis = Vector3.Cross(Vector3.up, toward);
        }

        void Update()
        {
            age += Time.deltaTime;
            float tilt;
            if (age < FallTime)
            {
                float t = age / FallTime;
                tilt = 86 * t * t * t;
            }
            else
            {
                // A small bounce as it hits the ground.
                float t = age - FallTime;
                tilt = 86 - Mathf.Abs(Mathf.Sin(t * 9)) * 6 * Mathf.Exp(-t * 5);
            }
            transform.localRotation = Quaternion.AngleAxis(tilt, axis);
            if (age > FallTime + Rest)
            {
                float t = (age - FallTime - Rest) / Sink;
                transform.localScale = Vector3.one * Mathf.Max(0, 1 - t);
                if (t >= 1) Destroy(gameObject);
            }
        }
    }
}
