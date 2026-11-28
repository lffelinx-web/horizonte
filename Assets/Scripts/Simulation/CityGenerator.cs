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
            AddLakes(city, seed);

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

        /// <summary>
        /// Three lakes in natural shapes: a big one in the forest south-west of the entrance, near where the town grows,
        /// a small one at the foot of the mountains and another among the fields across the river. Deep in the middle,
        /// shallow at the shore; never on the mountain, the reserve, the river banks or the highway.
        /// </summary>
        static void AddLakes(City city, uint seed)
        {
            int w = city.Width, h = city.Height, hy = Landscape.HighwayY(h);
            var lakes = new[] { (x: w * .2f, y: h * .22f, r: 5.6f), (x: w * .47f, y: h * .74f, r: 3.1f), (x: w * .84f, y: h * .2f, r: 3.3f) };
            byte id = 0;
            foreach (var (cx0, cy0, r) in lakes)
            {
                id++;
                // A little different on every map.
                float cx = cx0 + (Landscape.Noise(id * 3.1f, 0, seed + 41) - .5f) * 4, cy = cy0 + (Landscape.Noise(0, id * 3.1f, seed + 43) - .5f) * 4;
                for (int y = (int)(cy - r * 1.5f); y <= cy + r * 1.5f; y++)
                for (int x = (int)(cx - r * 1.5f); x <= cx + r * 1.5f; x++)
                {
                    if (x < 6 || y < 2 || x >= w - 2 || y >= h - 2 || Math.Abs(y - hy) <= 2) continue;
                    int i = city.Index(x, y);
                    var t = city.Terrain[i];
                    if (t == Terrain.Water || t == Terrain.Mountain || city.InReserve(x, y)) continue;
                    if (Math.Abs(x + .5f - Landscape.RiverX(seed, w, y + .5f)) < Landscape.RiverHalfWidth(seed, y + .5f) + 3) continue;
                    float dx = x + .5f - cx, dy = y + .5f - cy;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy) / r;
                    float edge = 1 + (Landscape.Noise(x * .35f, y * .35f, seed + 50 + id) - .5f) * .7f;
                    if (d >= edge) continue;
                    float depth = .4f + 3.2f * (float)Math.Pow(1 - d / edge, .8);
                    city.Terrain[i] = Terrain.Water;
                    city.LakeOf[i] = id;
                    city.LakeDepth[i] = (byte)Math.Round(depth * 10);
                }
            }
            city.RebuildLakes();
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
