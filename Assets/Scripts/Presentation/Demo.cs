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

            for (int d = 0; d < 900; d++) city.Step();
            city.Money = 48_250;
            return city;
        }

        static void Place(City city, Structure type, System.Collections.Generic.IEnumerable<(int x, int y)> tiles) => city.PlaceMany(type, tiles, out _);

        static void Replace(City city, Structure type, int x, int y)
        {
            city.Bulldoze(x, y);
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
