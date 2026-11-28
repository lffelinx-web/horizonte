using System;

namespace Horizonte.Simulation
{
    /// <summary>Plain data copy of a city, ready for a JSON serializer. Tile layers are stored as base64.</summary>
    [Serializable]
    public sealed class CitySave
    {
        /// <summary>Version 5: lakes. 4: the map with the tunnel entrance and closed forest (still read, without lakes); 3 had the long highway; 2 had days; 1 zones.</summary>
        public const int CurrentVersion = 5;

        public int version = CurrentVersion;
        public int width, height;
        public long money;
        public int tick, taxRate, wood;
        public uint mapSeed;
        /// <summary>Forest reserve bounds and the column of its gate.</summary>
        public int reserveX0, reserveY0, reserveX1, reserveY1, gateX;
        public uint rng;
        public string name;
        /// <summary>Terrain and rotation are one byte per tile; buildings are catalog models by name; occupants four bytes.</summary>
        public string terrain, building, rotation, occupants;
        /// <summary>Ticks left on buildings under construction (one byte per tile); missing in older saves.</summary>
        public string construction;
        public string[] models;
        /// <summary>Lake of each tile and its depth in decimetres (one byte per tile), and the water in each lake.</summary>
        public string lakes, lakeDepth;
        public float[] lakeVolume;
        public long loanBalance;
        public int loanPayment;

        public static CitySave From(City city)
        {
            int n = city.Width * city.Height;
            var terrain = new byte[n];
            var building = new byte[n * 2];
            var occupants = new byte[n * 4];
            // Buildings are saved through a table of model names so later catalog changes keep old saves readable.
            var table = new System.Collections.Generic.List<string> { "" };
            var lookup = new System.Collections.Generic.Dictionary<ushort, ushort> { [Catalog.None] = 0 };
            for (int i = 0; i < n; i++)
            {
                terrain[i] = (byte)city.Terrain[i];
                ushort id = city.Building[i];
                if (!lookup.TryGetValue(id, out ushort code))
                {
                    code = (ushort)table.Count;
                    table.Add(Catalog.Get(id).Model);
                    lookup[id] = code;
                }
                building[i * 2] = (byte)code;
                building[i * 2 + 1] = (byte)(code >> 8);
                BitConverter.GetBytes(city.Occupants[i]).CopyTo(occupants, i * 4);
            }
            return new CitySave
            {
                width = city.Width,
                height = city.Height,
                money = city.Money,
                tick = city.Tick,
                wood = city.Wood,
                mapSeed = city.MapSeed,
                reserveX0 = city.ReserveX0,
                reserveY0 = city.ReserveY0,
                reserveX1 = city.ReserveX1,
                reserveY1 = city.ReserveY1,
                gateX = city.GateX,
                taxRate = city.TaxRate,
                rng = city.RngState,
                name = city.Name,
                terrain = Convert.ToBase64String(terrain),
                building = Convert.ToBase64String(building),
                rotation = Convert.ToBase64String(city.Rotation),
                occupants = Convert.ToBase64String(occupants),
                construction = Convert.ToBase64String(city.Construction),
                models = table.ToArray(),
                loanBalance = city.LoanBalance,
                loanPayment = city.LoanPayment,
                lakes = Convert.ToBase64String(city.LakeOf),
                lakeDepth = Convert.ToBase64String(city.LakeDepth),
                lakeVolume = city.Lakes.ConvertAll(l => l.Volume).ToArray(),
            };
        }

        public City ToCity()
        {
            if (version != CurrentVersion && version != 4) throw new InvalidOperationException($"Save version {version} is from an older game.");
            var city = new City(width, height, rng) { Money = money, Tick = tick, Wood = wood, MapSeed = mapSeed, GateX = gateX, TaxRate = taxRate, LoanBalance = loanBalance, LoanPayment = loanPayment };
            city.SetReserve(reserveX0, reserveY0, reserveX1, reserveY1);
            if (!string.IsNullOrEmpty(name)) city.Name = name;
            int n = width * height;
            var t = Convert.FromBase64String(terrain);
            var b = Convert.FromBase64String(building);
            var r = Convert.FromBase64String(rotation);
            var o = Convert.FromBase64String(occupants);
            var k2 = string.IsNullOrEmpty(construction) ? new byte[n] : Convert.FromBase64String(construction);
            if (t.Length != n || b.Length != n * 2 || r.Length != n || o.Length != n * 4) throw new InvalidOperationException("Corrupt save.");
            var ids = new ushort[models.Length];
            for (int k = 1; k < models.Length; k++) ids[k] = Catalog.Get(models[k])?.Id ?? Catalog.None;
            for (int i = 0; i < n; i++)
            {
                city.Terrain[i] = (Terrain)t[i];
                int code = b[i * 2] | (b[i * 2 + 1] << 8);
                city.Building[i] = code < ids.Length ? ids[code] : Catalog.None;
                city.Rotation[i] = r[i];
                city.Occupants[i] = BitConverter.ToInt32(o, i * 4);
                city.Construction[i] = i < k2.Length ? k2[i] : (byte)0;
            }
            if (!string.IsNullOrEmpty(lakes) && !string.IsNullOrEmpty(lakeDepth))
            {
                var lo = Convert.FromBase64String(lakes);
                var ld = Convert.FromBase64String(lakeDepth);
                if (lo.Length == n && ld.Length == n)
                {
                    lo.CopyTo(city.LakeOf, 0);
                    ld.CopyTo(city.LakeDepth, 0);
                    city.RebuildLakes(lakeVolume);
                }
            }
            RebuildAnchors(city);
            city.Refresh();
            return city;
        }

        /// <summary>Buildings are rebuilt by scanning from their south-west corner; anything that no longer fits is dropped.</summary>
        static void RebuildAnchors(City city)
        {
            for (int y = 0; y < city.Height; y++)
            for (int x = 0; x < city.Width; x++)
            {
                int i = city.Index(x, y);
                if (city.Building[i] == Catalog.None || city.Anchor[i] >= 0) continue;
                var def = Catalog.Get(city.Building[i]);
                // The south-west corner is the anchor and keeps the rotation, which tells how a rectangular footprint lies.
                var (w, h) = City.Footprint(def, (city.Rotation[i] & 1) == 1);
                bool fits = true;
                for (int dy = 0; dy < h && fits; dy++)
                for (int dx = 0; dx < w && fits; dx++)
                    fits = city.InBounds(x + dx, y + dy) && city.Building[city.Index(x + dx, y + dy)] == def.Id && city.Anchor[city.Index(x + dx, y + dy)] < 0;
                if (!fits)
                {
                    city.Building[i] = Catalog.None;
                    city.Occupants[i] = 0;
                    continue;
                }
                for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                    city.Anchor[city.Index(x + dx, y + dy)] = i;
            }
        }
    }
}
