using System;

namespace Horizonte.Simulation
{
    /// <summary>
    /// Makes a new map after the territory sketch: mountains in the north-west, the river from north to south, a fenced
    /// forest reserve on the far bank to the north-east, fertile fields on the far bank to the south-east, a plain for the
    /// town in the middle of the west, and a highway from the west edge to the east edge that crosses the river on a bridge.
    /// A short road leads from the highway to the gate of the reserve.
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

            // Highway from the west edge to the east edge, on a bridge over the river.
            for (int x = 0; x < width; x++) Lay(city, x, hy);
            // Road to the gate of the reserve.
            for (int y = hy + 1; y < city.ReserveY0; y++) Lay(city, gateX, y);
            city.GateX = gateX;

            // Keep the plain where the town starts clear, so the first blocks are easy to lay out.
            for (int y = hy - 12; y <= hy + 12; y++)
            for (int x = width / 2 - 26; x < width / 2 + 3; x++)
            {
                if (!city.InBounds(x, y)) continue;
                int i = city.Index(x, y);
                if (city.Terrain[i] == Terrain.Forest) city.Terrain[i] = Terrain.Grass;
            }

            city.Refresh();
            return city;
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
