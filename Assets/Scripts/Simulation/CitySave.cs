using System;

namespace Horizonte.Simulation
{
    /// <summary>Plain data copy of a city, ready for a JSON serializer. Tile layers are stored as base64.</summary>
    [Serializable]
    public sealed class CitySave
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public int width, height;
        public long money;
        public int day, taxRate;
        public uint rng;
        public string name;
        public string terrain, structure, level, rotation;
        /// <summary>Side of grown zone buildings; missing in saves from before lots could merge.</summary>
        public string size;

        public static CitySave From(City city)
        {
            int n = city.Width * city.Height;
            var terrain = new byte[n];
            var structure = new byte[n];
            for (int i = 0; i < n; i++)
            {
                terrain[i] = (byte)city.Terrain[i];
                structure[i] = (byte)city.Structure[i];
            }
            return new CitySave
            {
                width = city.Width,
                height = city.Height,
                money = city.Money,
                day = city.Day,
                taxRate = city.TaxRate,
                rng = city.RngState,
                name = city.Name,
                terrain = Convert.ToBase64String(terrain),
                structure = Convert.ToBase64String(structure),
                level = Convert.ToBase64String(city.Level),
                rotation = Convert.ToBase64String(city.Rotation),
                size = Convert.ToBase64String(city.Size),
            };
        }

        public City ToCity()
        {
            if (version != CurrentVersion) throw new InvalidOperationException($"Unknown save version {version}.");
            var city = new City(width, height, rng) { Money = money, Day = day, TaxRate = taxRate };
            if (!string.IsNullOrEmpty(name)) city.Name = name;
            int n = width * height;
            var t = Convert.FromBase64String(terrain);
            var s = Convert.FromBase64String(structure);
            var l = Convert.FromBase64String(level);
            var r = Convert.FromBase64String(rotation);
            var z = string.IsNullOrEmpty(size) ? null : Convert.FromBase64String(size);
            if (z != null && z.Length != n) throw new InvalidOperationException("Corrupt save.");
            if (t.Length != n || s.Length != n || l.Length != n || r.Length != n) throw new InvalidOperationException("Corrupt save.");
            for (int i = 0; i < n; i++)
            {
                city.Terrain[i] = (Terrain)t[i];
                city.Structure[i] = (Structure)s[i];
                city.Level[i] = l[i];
                city.Rotation[i] = r[i];
                city.Size[i] = z == null || z[i] == 0 ? (byte)1 : z[i];
            }
            RebuildAnchors(city);
            city.Refresh();
            return city;
        }

        /// <summary>Multi-tile buildings are rebuilt by scanning from their south-west corner.</summary>
        static void RebuildAnchors(City city)
        {
            for (int y = 0; y < city.Height; y++)
            for (int x = 0; x < city.Width; x++)
            {
                int i = city.Index(x, y);
                if (city.Structure[i] == Structure.None || city.Anchor[i] >= 0) continue;
                int w = city.FootprintWidth(i), h = city.FootprintHeight(i);
                for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                    if (city.InBounds(x + dx, y + dy)) city.Anchor[city.Index(x + dx, y + dy)] = i;
            }
        }
    }
}
