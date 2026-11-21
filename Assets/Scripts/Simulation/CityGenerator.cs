using System;

namespace Horizonte.Simulation
{
    /// <summary>Makes a new map: grassland with a river, patches of forest and a highway that links the city to the world.</summary>
    public static class CityGenerator
    {
        public const int DefaultSize = 64;

        public static City Create(int width = DefaultSize, int height = DefaultSize, uint seed = 20261121)
        {
            var city = new City(width, height, seed);
            float phase = (seed % 1000) / 1000f * 6.28f;

            for (int y = 0; y < height; y++)
            {
                // The river runs north to south on the east side.
                float riverX = width * .74f + (float)Math.Sin(y * .13f + phase) * 4.5f + (float)Math.Sin(y * .05f + phase * 2) * 2.5f;
                float riverHalf = 1.6f + (float)Math.Sin(y * .21f + phase) * .5f;
                for (int x = 0; x < width; x++)
                {
                    int i = city.Index(x, y);
                    if (Math.Abs(x + .5f - riverX) < riverHalf) { city.Terrain[i] = Terrain.Water; continue; }
                    float forest = Noise(x * .11f, y * .11f, seed) * .7f + Noise(x * .27f, y * .27f, seed + 7) * .3f;
                    city.Terrain[i] = forest > .62f ? Terrain.Forest : Terrain.Grass;
                }
            }

            // Highway from the west edge to the middle of the map.
            int hy = height / 2;
            for (int x = 0; x < width / 2; x++)
            {
                int i = city.Index(x, hy);
                city.Terrain[i] = Terrain.Grass;
                city.Building[i] = Catalog.Road;
                city.Anchor[i] = i;
            }
            // Keep the starting area clear so the first blocks are easy to lay out.
            for (int y = hy - 6; y <= hy + 6; y++)
            for (int x = width / 2 - 14; x < width / 2 + 4; x++)
            {
                int i = city.Index(x, y);
                if (city.Terrain[i] == Terrain.Forest) city.Terrain[i] = Terrain.Grass;
            }

            city.Refresh();
            return city;
        }

        /// <summary>Smooth value noise in 0..1.</summary>
        static float Noise(float x, float y, uint seed)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            float a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed);
            float c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
            return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
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
