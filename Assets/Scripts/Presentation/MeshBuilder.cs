using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Horizonte.Presentation
{
    /// <summary>Collects flat-shaded geometry into one mesh with a submesh per material slot.</summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int>[] triangles;

        public MeshBuilder(int slots)
        {
            triangles = new List<int>[slots];
            for (int i = 0; i < slots; i++) triangles[i] = new List<int>();
        }

        public bool IsEmpty => vertices.Count == 0;

        public void Clear()
        {
            vertices.Clear();
            normals.Clear();
            uvs.Clear();
            foreach (var t in triangles) t.Clear();
        }

        /// <summary>Quad from its corners seen from the front: bottom-left, bottom-right, top-right, top-left.</summary>
        public void Quad(int slot, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var n = Vector3.Cross(d - a, b - a).normalized;
            int i = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            for (int k = 0; k < 4; k++) normals.Add(n);
            // World-space planar UVs keep textures continuous across tiles.
            bool flat = Mathf.Abs(n.y) > .5f;
            uvs.Add(Uv(a, flat, n)); uvs.Add(Uv(b, flat, n)); uvs.Add(Uv(c, flat, n)); uvs.Add(Uv(d, flat, n));
            var t = triangles[slot];
            t.Add(i); t.Add(i + 2); t.Add(i + 1);
            t.Add(i); t.Add(i + 3); t.Add(i + 2);
        }

        static Vector2 Uv(Vector3 p, bool flat, Vector3 n) =>
            flat ? new Vector2(p.x, p.z) * .125f : new Vector2(Mathf.Abs(n.x) > .5f ? p.z : p.x, p.y) * .125f;

        public void Triangle(int slot, Vector3 a, Vector3 b, Vector3 c)
        {
            var n = Vector3.Cross(c - a, b - a).normalized;
            int i = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            for (int k = 0; k < 3; k++) normals.Add(n);
            uvs.Add(new Vector2(a.x, a.z)); uvs.Add(new Vector2(b.x, b.z)); uvs.Add(new Vector2(c.x, c.z));
            var t = triangles[slot];
            t.Add(i); t.Add(i + 2); t.Add(i + 1);
        }

        /// <summary>Flat rectangle on the ground, facing up.</summary>
        public void Flat(int slot, float x0, float z0, float x1, float z1, float y)
        {
            Quad(slot, new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1));
        }

        /// <summary>Axis-aligned box from its bottom centre.</summary>
        public void Box(int slot, Vector3 bottomCentre, Vector3 size, bool bottom = false)
        {
            Vector3 h = size * .5f;
            float x0 = bottomCentre.x - h.x, x1 = bottomCentre.x + h.x;
            float z0 = bottomCentre.z - h.z, z1 = bottomCentre.z + h.z;
            float y0 = bottomCentre.y, y1 = bottomCentre.y + size.y;
            Quad(slot, new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1));
            Quad(slot, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0));
            Quad(slot, new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1));
            Quad(slot, new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x0, y1, z1));
            Quad(slot, new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0));
            if (bottom) Quad(slot, new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y0, z0), new Vector3(x0, y0, z0));
        }

        /// <summary>Gabled roof along x, from the top of a box.</summary>
        public void GableRoof(int slot, Vector3 baseCentre, float width, float depth, float height)
        {
            float x0 = baseCentre.x - width * .5f, x1 = baseCentre.x + width * .5f;
            float z0 = baseCentre.z - depth * .5f, z1 = baseCentre.z + depth * .5f;
            float y0 = baseCentre.y, y1 = baseCentre.y + height;
            float zm = baseCentre.z;
            Quad(slot, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, zm), new Vector3(x0, y1, zm));
            Quad(slot, new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), new Vector3(x0, y1, zm), new Vector3(x1, y1, zm));
            Triangle(slot, new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x0, y1, zm));
            Triangle(slot, new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, zm));
        }

        /// <summary>Cone (or pyramid with few sides) from its base centre.</summary>
        public void Cone(int slot, Vector3 baseCentre, float radius, float height, int sides)
        {
            var top = baseCentre + Vector3.up * height;
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * Mathf.PI * 2 / sides, a1 = (s + 1) * Mathf.PI * 2 / sides;
                var p0 = baseCentre + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius;
                var p1 = baseCentre + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                Triangle(slot, p1, top, p0);
            }
        }

        public void Cylinder(int slot, Vector3 baseCentre, float radius, float height, int sides)
        {
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * Mathf.PI * 2 / sides, a1 = (s + 1) * Mathf.PI * 2 / sides;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius;
                var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                var up = Vector3.up * height;
                Quad(slot, baseCentre + d1, baseCentre + d1 + up, baseCentre + d0 + up, baseCentre + d0);
                Triangle(slot, baseCentre + up, baseCentre + d0 + up, baseCentre + d1 + up);
            }
        }

        public Mesh ToMesh(Mesh mesh = null)
        {
            if (mesh == null) mesh = new Mesh();
            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = triangles.Length;
            for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i, false);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
