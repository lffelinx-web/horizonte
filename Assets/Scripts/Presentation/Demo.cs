using Horizonte.Simulation;

namespace Horizonte.Presentation
{
    /// <summary>A small grown town for screenshots and quick checks (command line: -demo).</summary>
    public static class Demo
    {
        public static City Build()
        {
            var city = CityGenerator.Create();
            city.Money = 1_000_000;
            int cx = city.Width / 2, cy = city.Height / 2;

            // Street grid west of the end of the highway.
            for (int x = cx - 18; x <= cx; x += 6) Place(city, Structure.Road, City.Line(x, cy - 10, x, cy + 10));
            for (int y = cy - 10; y <= cy + 10; y += 5) Place(city, Structure.Road, City.Line(cx - 18, y, cx, y));

            for (int k = 0; k < 4; k++) city.Place(Structure.PowerPlant, cx - 22, cy - 9 + k * 2);
            Place(city, Structure.Road, City.Line(cx - 22, cy - 10, cx - 18, cy - 10));
            for (int y = cy - 9; y <= cy + 9; y++)
            for (int x = cx - 17; x < cx; x++)
            {
                var type = y < cy - 5 ? Structure.Industrial : y <= cy + 1 ? Structure.Commercial : Structure.Residential;
                city.Place(type, x, y);
            }
            // Bulldoze a few lots for services so the town has parks, a school and police.
            Replace(city, Structure.Park, cx - 8, cy + 3);
            Replace(city, Structure.Park, cx - 14, cy + 7);
            Replace(city, Structure.Park, cx - 3, cy + 8);
            Replace(city, Structure.Police, cx - 9, cy - 3);
            ReplaceWide(city, Structure.School, cx - 5, cy + 3);
            ReplaceArea(city, Structure.Doctor, cx - 17, cy + 3);

            for (int d = 0; d < 500; d++) city.Step();
            // Bigger health buildings unlock as the town grows.
            ReplaceArea(city, Structure.Clinic, cx - 11, cy + 6);
            for (int d = 0; d < 400; d++) city.Step();
            city.Money = 48_250;
            return city;
        }

        /// <summary>Every model in a row along one street, facing it (command line: -vitrina).</summary>
        public static City Showcase()
        {
            var city = new City(64, 64, 3) { Money = 10_000_000, IgnoreRequirements = true, Name = "Vitrina" };
            Place(city, Structure.Road, City.Line(0, 10, 63, 10));
            city.Place(Structure.PowerPlant, 60, 8);
            int x = 1;
            for (int k = 0; k < 3; k++, x += 2) Lots(city, x, 11, 1, 1);
            Lots(city, x, 11, 2, 1);
            city.Upgrade(x, 11, 2);
            x += 3;
            Lots(city, x, 11, 3, 2);
            city.Upgrade(x, 11, 3);
            x += 4;
            Lots(city, x, 11, 4, 1);
            city.Upgrade(x, 11, 4);
            x += 5;
            foreach (var type in new[] { Structure.Doctor, Structure.Lab, Structure.Clinic, Structure.Emergency, Structure.Hospital, Structure.SpecialistHospital })
            {
                city.Place(type, x, 11);
                x += Catalog.Get(type).Width + 1;
            }
            city.Refresh();
            return city;
        }

        /// <summary>A square of residential lots already built to a level, each its own building.</summary>
        static void Lots(City city, int x, int y, int size, int level)
        {
            foreach (var (tx, ty) in City.Rect(x, y, x + size - 1, y + size - 1))
            {
                city.Place(Structure.Residential, tx, ty);
                city.Level[city.Index(tx, ty)] = (byte)level;
            }
            city.Refresh();
        }

        static void Place(City city, Structure type, System.Collections.Generic.IEnumerable<(int x, int y)> tiles) => city.PlaceMany(type, tiles, out _);

        static void Replace(City city, Structure type, int x, int y)
        {
            city.Bulldoze(x, y);
            city.Place(type, x, y);
        }

        static void ReplaceArea(City city, Structure type, int x, int y)
        {
            var def = Catalog.Get(type);
            foreach (var (tx, ty) in City.Rect(x, y, x + def.Width - 1, y + def.Height - 1)) city.Bulldoze(tx, ty);
            city.Place(type, x, y);
        }

        static void ReplaceWide(City city, Structure type, int x, int y)
        {
            city.Bulldoze(x, y);
            city.Bulldoze(x + 1, y);
            city.Place(type, x, y);
        }
    }
}
