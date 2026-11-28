using System.Collections.Generic;
using Horizonte.Simulation;

namespace Horizonte.Presentation
{
    /// <summary>Towns built in code for screenshots and quick checks.</summary>
    public static class Demo
    {
        static readonly string[] Houses = { "casa_basica", "casa_ampliada", "duplex" };

        /// <summary>A small grown town (command line: -demo).</summary>
        public static City Build()
        {
            var city = CityGenerator.Create();
            city.Money = 1_000_000;
            city.IgnoreRequirements = true;
            int cx = city.Width / 2, cy = city.Height / 2;

            // Street grid west of the end of the highway, and a street out to the power plant.
            for (int x = cx - 18; x <= cx; x += 6) Road(city, City.Line(x, cy - 10, x, cy + 10));
            for (int y = cy - 10; y <= cy + 10; y += 5) Road(city, City.Line(cx - 18, y, cx, y));
            Road(city, City.Line(cx - 22, cy - 10, cx - 18, cy - 10));
            // From the end of the highway out of the tunnel to the town.
            Road(city, City.Line(CityGenerator.EntranceEnd(city.Width), cy, cx - 18, cy));
            Try(city, "central_termoelectrica", cx - 22, cy - 9);

            // Industry to the south, then shops, then homes; services spread among them.
            Try(city, "granja_agricola", cx - 17, cy - 9);
            Try(city, "aserradero", cx - 11, cy - 9);
            Try(city, "procesadora_alimentos", cx - 5, cy - 9);
            Try(city, "supermercado", cx - 17, cy - 4);
            Try(city, "mercado_municipal", cx - 17, cy - 2);
            Try(city, "restaurante", cx - 14, cy - 4);
            Try(city, "edificio_oficinas", cx - 5, cy - 4);
            Fill(city, new[] { "tienda_abarrotes", "restaurante" }, cx - 13, cy - 4, cx - 1, cy - 1);
            Try(city, "consultorio", cx - 14, cy + 1);
            Try(city, "escuela_primaria", cx - 11, cy + 1);
            Try(city, "puesto_policia", cx - 7, cy + 1);
            Try(city, "estacion_bomberos", cx - 17, cy + 8);
            Try(city, "estacion_bombeo", cx - 13, cy + 9);
            Try(city, "torre_residencial", cx - 5, cy + 1);
            Try(city, "apartamentos", cx - 17, cy + 1);
            Try(city, "conjunto_residencial", cx - 11, cy + 6);
            Try(city, "plaza_publica", cx - 2, cy + 8);
            Try(city, "parque_vecinal", cx - 14, cy + 4);
            Try(city, "ayuntamiento", cx - 11, cy + 11);
            Try(city, "recoleccion_basura", cx - 24, cy + 1);
            // Health care among the homes.
            foreach (var model in new[] { "hospital_general", "clinica", "urgencias", "laboratorio" })
                FirstFree(city, model, cx - 17, cy + 1, cx - 1, cy + 9);
            Fill(city, Houses, cx - 17, cy + 1, cx - 1, cy + 9);

            // A car park in the middle of town, on the first free lot by a street.
            bool parked = false;
            for (int py = cy - 9; py <= cy + 9 && !parked; py++)
            for (int px = cx - 17; px <= cx - 1 && !parked; px++)
                parked = city.Place(Catalog.Get("estacionamiento").Id, px, py) == PlaceResult.Ok;

            // Wood: a logging camp by the grove across the highway from the first block.
            int gx = cx - 23, ry = cy - 1;
            Try(city, "puesto_tala", gx, ry);

            city.IgnoreRequirements = false;
            for (int d = 0; d < 600; d++) city.Step();
            // Trees for the lumberjack, nearest first.
            var trees = new List<int>();
            for (int i = 0; i < city.Terrain.Length; i++)
                if (city.Terrain[i] == Terrain.Forest) trees.Add(i);
            trees.Sort((a, b) => Gap(city, a, gx, ry).CompareTo(Gap(city, b, gx, ry)));
            for (int k = 0; k < trees.Count && city.Marked.Count < 5; k++) city.MarkTree(trees[k] % city.Width, trees[k] / city.Width);
            city.Money = 48_250;
            return city;
        }

        /// <summary>Every building of the catalog in rows along streets, facing them (command line: -vitrina).</summary>
        public static City Showcase()
        {
            var city = new City(120, 90, 3) { Money = 100_000_000, IgnoreRequirements = true, Name = "Vitrina" };
            int street = 4, x = 1;
            Road(city, City.Line(0, street, city.Width - 1, street));
            foreach (var def in Catalog.All)
            {
                if (def == null || def.IsRoad) continue;
                if (x + def.Width >= city.Width - 1)
                {
                    street += 13;
                    x = 1;
                    Road(city, City.Line(0, street, city.Width - 1, street));
                }
                if (def.NeedsWater)
                    for (int k = 0; k < def.Width; k++) city.Terrain[city.Index(x + k, street + 1 + def.Height)] = Terrain.Water;
                city.Place(def.Id, x, street + 1);
                x += def.Width + 1;
            }
            city.FinishConstruction();
            city.Refresh();
            return city;
        }

        static int Gap(City city, int i, int x, int y) => System.Math.Abs(i % city.Width - x) + System.Math.Abs(i / city.Width - y);

        static void Road(City city, IEnumerable<(int x, int y)> tiles) => city.PlaceMany(Catalog.Road, tiles, out _);

        static void FirstFree(City city, string model, int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (city.Place(Catalog.Get(model).Id, x, y) == PlaceResult.Ok) return;
        }

        static void Try(City city, string model, int x, int y) => city.Place(Catalog.Get(model).Id, x, y);

        /// <summary>Places the models in turn on every free spot of a rectangle that touches a street.</summary>
        static void Fill(City city, string[] models, int x0, int y0, int x1, int y1)
        {
            int k = 0;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (city.Place(Catalog.Get(models[k % models.Length]).Id, x, y) == PlaceResult.Ok) k++;
        }
    }
}
