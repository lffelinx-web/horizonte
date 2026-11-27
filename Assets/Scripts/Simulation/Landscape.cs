using System;

namespace Horizonte.Simulation
{
    /// <summary>
    /// The shape of the land, the same for the simulation (which tiles are mountain) and for the view (the relief it
    /// draws, also beyond the edge of the map): mountains in the north-west corner, rolling hills all around outside the
    /// map, and the river that crosses it from north to south. Coordinates are in tiles; heights in metres.
    /// </summary>
    public static class Landscape
    {
        /// <summary>Tiles whose centre stands higher than this are mountain.</summary>
        public const float MountainLine = 2f;

        static float Phase(uint seed) => seed % 1000 / 1000f * 6.28f;

        /// <summary>Middle of the river at a row (also for rows outside the map).</summary>
        public static float RiverX(uint seed, int width, float y)
        {
            float p = Phase(seed);
            return width * .66f + (float)Math.Sin(y * .11f + p) * 4f + (float)Math.Sin(y * .047f + p * 2) * 2.5f;
        }

        public static float RiverHalfWidth(uint seed, float y) => 1.6f + (float)Math.Sin(y * .21f + Phase(seed)) * .45f;

        /// <summary>Row of the highway that crosses the map from west to east.</summary>
        public static int HighwayY(int height) => height / 2;

        public static float Height(uint seed, int width, int height, float x, float y)
        {
            // Keep clear of the river and the highway, inside the map and beyond it.
            float river = SmoothStep(3, 10, Math.Abs(x - RiverX(seed, width, y)));
            float road = SmoothStep(2, 9, Math.Abs(y + .5f - (HighwayY(height) + .5f)));

            // Mountains: a range in the north-west corner that runs on outside the map.
            float dx = x + 8, dy = y - (height + 8);
            float d = (float)Math.Sqrt(dx * dx + dy * dy) / (width * .52f);
            float m = 1 - d + (Noise(x * .07f, y * .07f, seed + 11) - .5f) * .4f;
            // Never into the plain where the town starts.
            m *= SmoothStep(HighwayY(height) + 7, HighwayY(height) + 15, y) * (1 - SmoothStep(width * .3f, width * .46f, x));
            float mountain = 0;
            if (m > 0)
            {
                // One big massif that rises smoothly from the foothills, with ridges and valleys carved into it.
                float body = SmoothStep(0, .85f, m);
                float massif = (float)Math.Pow(body, 1.5) * 250;
                float ridges = (Ridge(x * .055f, y * .055f, seed) - .45f) * 90 + (Noise(x * .16f, y * .16f, seed + 3) - .5f) * 22;
                mountain = massif + ridges * body + (Noise(x * .35f, y * .35f, seed + 5) - .5f) * 4 * Math.Min(1, m * 4);
            }

            // Rolling hills outside the map, so the world does not end in a flat plain.
            float outX = Math.Max(-x, x - width), outY = Math.Max(-y, y - height);
            float outside = Math.Max(outX, outY);
            float hills = 0;
            if (outside > 0)
                hills = SmoothStep(3, 16, outside) * (5 + 26 * Noise(x * .05f, y * .05f, seed + 17) + 8 * Noise(x * .16f, y * .16f, seed + 23));
            // A ridge along the west edge; the highway goes through it in a tunnel, in a cutting up to the portal.
            float ridge = 0;
            if (x < 0)
            {
                ridge = SmoothStep(1.6f, 5.5f, -x) * (34 + 16 * Noise(x * .08f, y * .08f, seed + 29));
                if (x > -TunnelPortal - .3f && Math.Abs(y - (HighwayY(height) + .5f)) < 1.2f) ridge = 0;
            }
            return Math.Max(0, Math.Max(Math.Max(mountain, hills) * river * road, ridge * river));
        }

        /// <summary>How far outside the west edge (in tiles) the tunnel the highway comes through opens.</summary>
        public const float TunnelPortal = 2f;

        public static bool IsMountain(uint seed, int width, int height, int x, int y) =>
            Height(seed, width, height, x + .5f, y + .5f) > MountainLine;

        static float SmoothStep(float a, float b, float v)
        {
            float t = Math.Max(0, Math.Min(1, (v - a) / (b - a)));
            return t * t * (3 - 2 * t);
        }

        /// <summary>Sharp-crested noise for mountain ridges, 0..1.</summary>
        static float Ridge(float x, float y, uint seed)
        {
            float n = 1 - Math.Abs(Noise(x, y, seed) * 2 - 1);
            float n2 = 1 - Math.Abs(Noise(x * 2.1f, y * 2.1f, seed + 1) * 2 - 1);
            return n * n * .7f + n2 * n2 * .3f;
        }

        /// <summary>Smooth value noise in 0..1.</summary>
        public static float Noise(float x, float y, uint seed)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            float a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed);
            float c = Hash(xi, yi + 1, seed), e = Hash(xi + 1, yi + 1, seed);
            return a + (b - a) * fx + (c - a) * fy + (a - b - c + e) * fx * fy;
        }

        static float Hash(int x, int y, uint seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }
}
