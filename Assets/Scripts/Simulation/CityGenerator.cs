using System;

namespace Horizonte.Simulation
{
    /// <summary>
    /// Makes a new map after the territory sketch: mountains in the north-west, the river from north to south, a fenced
    /// forest reserve on the far bank to the north-east, fertile fields on the far bank to the south-east, a plain for the
    /// town in the west, all of it closed forest to be felled as the town grows. The highway comes out of a tunnel in the
    /// hills at the west edge and runs a few tiles into a small clearing, where the first logging camp and homes go; every
    /// other street, the bridge and the road to the reserve are for the player to build.
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
                    // The plain where the town will grow is closed forest.
                    else t = Terrain.Forest;
                    city.Terrain[i] = t;
                }
            }

            city.GateX = gateX;

            // The highway out of the tunnel, a few tiles into the map, and a small clearing on both sides of its end.
            int end = EntranceEnd(width);
            for (int x = 0; x <= end; x++) Lay(city, x, hy);
            foreach (int y in new[] { hy - 1, hy + 1 })
            for (int x = 1; x <= end + 1; x++)
            {
                int i = city.Index(x, y);
                if (city.Terrain[i] == Terrain.Forest) city.Terrain[i] = Terrain.Grass;
            }
            city.Terrain[city.Index(end + 1, hy)] = Terrain.Grass;

            city.Refresh();
            return city;
        }

        /// <summary>Column where the highway ends and the first block begins.</summary>
        public static int EntranceEnd(int width) => 3;

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
