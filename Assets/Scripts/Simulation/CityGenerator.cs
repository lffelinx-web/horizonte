using System;

namespace Horizonte.Simulation
{
    /// <summary>
    /// Makes a new map after the territory sketch: mountains in the north-west, the river from north to south, a fenced
    /// forest reserve on the far bank to the north-east, fertile fields on the far bank to the south-east, a plain for the
    /// town in the middle of the west with groves of trees here and there. The highway comes in from the west edge and ends
    /// at the first block of the town, a loop of street beside a grove where the first logging camp can go; the rest
    /// (the bridge, the way east, the road to the reserve) is for the player to build.
    /// </summary>
    public static class CityGenerator
    {
        public const int DefaultSize = 64;

        public static City Create(int width = DefaultSize, int height = DefaultSize, uint seed = 20261121)
        {
            var city = new City(width, height, seed) { MapSeed = seed };
            int hy = Landscape.HighwayY(height);

            // The reserve: everything east of the river, from a few tiles north of the highway to the north edge.
            int riverEast = 0;
            for (int y = 0; y < height; y++)
                riverEast = Math.Max(riverEast, (int)Math.Ceiling(Landscape.RiverX(seed, width, y + .5f) + Landscape.RiverHalfWidth(seed, y + .5f)));
            city.SetReserve(Math.Min(width - 8, riverEast + 2), hy + 4, width - 2, height - 2);
            int gateX = (city.ReserveX0 + city.ReserveX1) / 2;

            for (int y = 0; y < height; y++)
            {
                float riverX = Landscape.RiverX(seed, width, y + .5f), half = Landscape.RiverHalfWidth(seed, y + .5f);
                for (int x = 0; x < width; x++)
                {
                    int i = city.Index(x, y);
                    var t = Terrain.Grass;
                    float n = Landscape.Noise(x * .11f, y * .11f, seed) * .7f + Landscape.Noise(x * .27f, y * .27f, seed + 7) * .3f;
                    if (Math.Abs(x + .5f - riverX) < half) t = Terrain.Water;
                    else if (Landscape.IsMountain(seed, width, height, x, y)) t = Terrain.Mountain;
                    else if (city.InReserve(x, y)) t = n > .2f ? Terrain.Forest : Terrain.Grass;
                    else if (x + .5f > riverX)
                    {
                        // Far bank: fields to the south, meadow with a few groves to the north.
                        if (y < hy - 1 && x > riverX + 2.5f) t = Landscape.Noise(x * .2f, y * .2f, seed + 31) > .22f ? Terrain.Fertile : Terrain.Grass;
                        else if (n > .66f) t = Terrain.Forest;
                    }
                    else if (n > .64f) t = Terrain.Forest;
                    city.Terrain[i] = t;
                }
            }

            city.GateX = gateX;

            // Groves by the entrance: one beside the first block, for the first logging camp, and one across the highway.
            int end = EntranceEnd(width);
            Grove(city, end + 5, hy + 5, 3.6f);
            Grove(city, end - 9, hy - 5, 2.8f);

            // The highway from the west edge, and the first block: a loop of street on its north side.
            for (int x = 0; x <= end; x++) Lay(city, x, hy);
            for (int y = hy + 1; y <= hy + 5; y++) { Lay(city, end, y); Lay(city, end - 6, y); }
            for (int x = end - 5; x < end; x++) Lay(city, x, hy + 5);
            // Room to build along the highway and inside the block.
            for (int y = hy - 1; y <= hy + 4; y++)
            for (int x = 0; x <= end; x++)
            {
                int i = city.Index(x, y);
                if (city.Building[i] == Catalog.None && city.Terrain[i] == Terrain.Forest) city.Terrain[i] = Terrain.Grass;
            }

            city.Refresh();
            return city;
        }

        /// <summary>Column where the highway ends and the first block begins.</summary>
        public static int EntranceEnd(int width) => width / 2 - 12;

        /// <summary>A round patch of forest with a ragged edge.</summary>
        static void Grove(City city, int cx, int cy, float radius)
        {
            for (int y = cy - 5; y <= cy + 5; y++)
            for (int x = cx - 5; x <= cx + 5; x++)
            {
                if (!city.InBounds(x, y)) continue;
                int i = city.Index(x, y);
                if (city.Terrain[i] != Terrain.Grass) continue;
                float d = (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) + (Landscape.Noise(x * .5f, y * .5f, 77) - .5f) * 2;
                if (d < radius) city.Terrain[i] = Terrain.Forest;
            }
        }

        static void Lay(City city, int x, int y)
        {
            int i = city.Index(x, y);
            bool water = city.Terrain[i] == Terrain.Water;
            if (!water) city.Terrain[i] = Terrain.Grass;
            city.Building[i] = water ? Catalog.Bridge : Catalog.Road;
            city.Anchor[i] = i;
        }
    }
}
