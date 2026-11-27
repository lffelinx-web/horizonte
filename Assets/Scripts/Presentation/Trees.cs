using UnityEngine;

namespace Horizonte.Presentation
{
    /// <summary>Tree shapes with smooth crowns, added to a MeshBuilder: tall conifers and round broadleaf trees.</summary>
    public static class Trees
    {
        /// <summary>A pine: a short trunk under three stacked crowns. About 80 vertices.</summary>
        public static void Conifer(MeshBuilder b, int leaves, int trunk, Vector3 at, float size, bool withTrunk = true)
        {
            if (withTrunk) b.Cylinder(trunk, at, .2f * size, 1.4f * size, 5);
            b.SmoothCone(leaves, at + Vector3.up * .9f * size, 1.9f * size, 3.3f * size, 8);
            b.SmoothCone(leaves, at + Vector3.up * 2.3f * size, 1.45f * size, 2.8f * size, 8);
            b.SmoothCone(leaves, at + Vector3.up * 3.6f * size, .95f * size, 2.3f * size, 7);
        }

        /// <summary>A broadleaf tree: trunk and a crown of two overlapping rounded masses. About 110 vertices.</summary>
        public static void Broadleaf(MeshBuilder b, int leaves, int trunk, Vector3 at, float size, uint seed)
        {
            b.Cylinder(trunk, at, .2f * size, 2.4f * size, 5);
            float turn = seed % 360 * Mathf.Deg2Rad;
            var side = new Vector3(Mathf.Cos(turn), 0, Mathf.Sin(turn));
            b.Ellipsoid(leaves, at + Vector3.up * 3.3f * size, new Vector3(2f, 1.7f, 2f) * size);
            b.Ellipsoid(leaves, at + (Vector3.up * 4.1f + side * .8f) * size, new Vector3(1.4f, 1.25f, 1.4f) * size, 6, 4);
        }

        /// <summary>A tree as seen from far away: one smooth crown and no trunk, a fraction of the vertices.</summary>
        public static void Far(MeshBuilder b, int leaves, Vector3 at, float size, bool broad)
        {
            if (broad) b.Ellipsoid(leaves, at + Vector3.up * 3.3f * size, new Vector3(2.1f, 1.9f, 2.1f) * size, 6, 3);
            else b.SmoothCone(leaves, at + Vector3.up * .5f * size, 1.8f * size, 6.2f * size, 6);
        }

        /// <summary>A young tree growing back where the forest was felled.</summary>
        public static void Sapling(MeshBuilder b, int leaves, int trunk, Vector3 at, float size)
        {
            b.Cylinder(trunk, at, .08f * size, .7f * size, 4);
            b.SmoothCone(leaves, at + Vector3.up * .4f * size, .75f * size, 1.6f * size, 6);
        }

        /// <summary>What is left after felling: a stump with its pale cut on top.</summary>
        public static void Stump(MeshBuilder b, int bark, int cut, Vector3 at, float size)
        {
            b.Cylinder(bark, at, .28f * size, .45f * size, 6);
            b.Cylinder(cut, at + Vector3.up * .45f * size, .26f * size, .02f, 6);
        }
    }
}
