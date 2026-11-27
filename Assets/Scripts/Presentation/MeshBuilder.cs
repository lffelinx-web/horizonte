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

        /// <summary>A thin bar between two points (a cable): two crossed strips, seen from both sides.</summary>
        public void Segment(int slot, Vector3 a, Vector3 b, float thickness)
        {
            var along = (b - a).normalized;
            var side = Vector3.Cross(along, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            side = side.normalized * thickness * .5f;
            var up = Vector3.Cross(side, along).normalized * thickness * .5f;
            foreach (var o in new[] { side, up })
            {
                Quad(slot, a - o, b - o, b + o, a + o);
                Quad(slot, a + o, b + o, b - o, a - o);
            }
        }

        /// <summary>Cone with smooth normals around its side (foliage that should not look faceted).</summary>
        public void SmoothCone(int slot, Vector3 baseCentre, float radius, float height, int sides)
        {
            int first = vertices.Count;
            var top = baseCentre + Vector3.up * height;
            for (int s = 0; s <= sides; s++)
            {
                float a = s * Mathf.PI * 2 / sides, mid = (s + .5f) * Mathf.PI * 2 / sides;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var dirMid = new Vector3(Mathf.Cos(mid), 0, Mathf.Sin(mid));
                vertices.Add(baseCentre + dir * radius);
                normals.Add((dir * height + Vector3.up * radius).normalized);
                uvs.Add(new Vector2(s / (float)sides, 0));
                vertices.Add(top);
                normals.Add((dirMid * height + Vector3.up * radius).normalized);
                uvs.Add(new Vector2(s / (float)sides, 1));
            }
            var t = triangles[slot];
            for (int s = 0; s < sides; s++)
            {
                int ring = first + s * 2, next = ring + 2;
                t.Add(next); t.Add(ring); t.Add(ring + 1);
            }
            // Underside, so the cone reads as solid from low angles.
            int centre = vertices.Count;
            vertices.Add(baseCentre);
            normals.Add(Vector3.down);
            uvs.Add(Vector2.zero);
            for (int s = 0; s < sides; s++)
            {
                int ring = first + s * 2, next = ring + 2;
                t.Add(ring); t.Add(next); t.Add(centre);
            }
        }

        /// <summary>Ellipsoid with smooth normals (a round crown of leaves, a rock).</summary>
        public void Ellipsoid(int slot, Vector3 centre, Vector3 radii, int segments = 7, int rings = 5)
        {
            int first = vertices.Count;
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                for (int s = 0; s <= segments; s++)
                {
                    float theta = Mathf.PI * 2 * s / segments;
                    var dir = new Vector3(Mathf.Cos(theta) * Mathf.Sin(phi), -Mathf.Cos(phi), Mathf.Sin(theta) * Mathf.Sin(phi));
                    vertices.Add(centre + Vector3.Scale(dir, radii));
                    normals.Add(new Vector3(dir.x / radii.x, dir.y / radii.y, dir.z / radii.z).normalized);
                    uvs.Add(new Vector2(s / (float)segments, r / (float)rings));
                }
            }
            var t = triangles[slot];
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = first + r * (segments + 1) + s, b = a + 1, d = a + segments + 1, c = d + 1;
                t.Add(a); t.Add(d); t.Add(c);
                t.Add(a); t.Add(c); t.Add(b);
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
