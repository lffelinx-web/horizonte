using System.Collections.Generic;

namespace Horizonte.Simulation
{
    /// <summary>Ground of a tile. Stumps and saplings are felled forest growing back; mountains cannot be built on.</summary>
    public enum Terrain : byte { Grass, Water, Forest, Mountain, Fertile, Stumps, Sapling }

    /// <summary>Groups on the building bar, in the order of the concept sheets.</summary>
    public enum Category : byte { Transport, Housing, Commerce, Industry, Energy, Water, Safety, Education, Health, Parks }

    /// <summary>What a building does in the simulation.</summary>
    public enum Kind : byte { Road, Housing, Jobs, Power, Service }

    /// <summary>City services a building can cover within its radius.</summary>
    public enum Service : byte { None, Health, Education, Safety, Fire, Water, Sewage, Waste, Culture, Transit }

    /// <summary>Fixed data for everything the player can build. One tile is 10 x 10 m.</summary>
    public sealed class BuildingDef
    {
        public ushort Id;
        /// <summary>Name of the 3D model in Resources/Buildings ("_b", "_c"… variants are picked at random).</summary>
        public string Model;
        public string Name;
        public Category Category;
        public Kind Kind;
        public int Cost;
        /// <summary>Daily upkeep (per tile for roads).</summary>
        public int Upkeep;
        public int Width = 1, Height = 1;
        /// <summary>Residents the city needs before it can be built.</summary>
        public int MinPopulation;
        /// <summary>People who can live here.</summary>
        public int Residents;
        /// <summary>Jobs it offers once it has power and a connected street.</summary>
        public int Jobs;
        /// <summary>True for factories and farms (industrial jobs), false for shops and offices.</summary>
        public bool Industrial;
        public int PowerSupply;
        /// <summary>Tiles around a plant or substation that its power lines reach.</summary>
        public int PowerReach;
        public int PowerUse;
        public Service Service;
        /// <summary>Tiles covered by the service.</summary>
        public int Radius;
        /// <summary>Water (m³), sewage (m³) or waste (kg) the building handles per day, shared among the buildings in its radius.</summary>
        public int Capacity;
        /// <summary>Desirability at the centre, fading to zero at <see cref="DesireRadius"/>.</summary>
        public int Desirability;
        public int DesireRadius;
        /// <summary>Satisfaction added to the whole city (town hall and similar).</summary>
        public int CityBonus;
        /// <summary>Must stand on the river bank (hydroelectric dam).</summary>
        public bool NeedsWater;
        /// <summary>Road that can be laid over water.</summary>
        public bool Bridge;
        /// <summary>Wood used to build it, besides the money.</summary>
        public int Wood;
        /// <summary>Tiles of forest around it its loggers reach; zero for buildings that do not log.</summary>
        public int LumberReach;
        /// <summary>Fells trees by itself every hour (sawmill); otherwise the player marks each tree (forest station).</summary>
        public bool AutoFell;
        /// <summary>Must touch the forest reserve.</summary>
        public bool NeedsForest;
        /// <summary>Stands on its own, without a street (a temporary logging camp).</summary>
        public bool Standalone;
        /// <summary>Ticks it takes to set up, when it is quicker than its price suggests; zero to use the usual time.</summary>
        public int QuickBuild;
        /// <summary>Floors, for the placeholder drawn until the model exists.</summary>
        public int Floors = 1;
        /// <summary>One line for the building menu.</summary>
        public string Summary;

        public bool IsRoad => Kind == Kind.Road;
    }

    public static class Catalog
    {
        public const ushort None = 0;
        public const int ClearForestCost = 5;
        /// <summary>Money paid for each unit of wood sold.</summary>
        public const int WoodPrice = 12;
        const int TicksPerHourForCamp = 12;
        /// <summary>Wood from one tile of forest, felled by hand or by a sawmill.</summary>
        public const int WoodPerTree = 4, WoodPerSawmillTree = 6;
        /// <summary>Upkeep in the catalog lines is per month of the old clock (20 seconds); a day now lasts as long as 24 of those.</summary>
        const int UpkeepPerDay = 24;
        public const int BulldozeCost = 5;

        public static readonly ushort Road, Avenue, Bridge;
        static readonly List<BuildingDef> defs = new List<BuildingDef> { null };
        static readonly Dictionary<string, BuildingDef> byModel = new Dictionary<string, BuildingDef>();

        public static IReadOnlyList<BuildingDef> All => defs;
        public static BuildingDef Get(ushort id) => id < defs.Count ? defs[id] : null;
        public static BuildingDef Get(string model) => byModel.TryGetValue(model, out var d) ? d : null;

        public static IEnumerable<BuildingDef> InCategory(Category c)
        {
            foreach (var d in defs) if (d != null && d.Category == c) yield return d;
        }

        static BuildingDef Add(string model, string name, Category category, Kind kind, int cost, int upkeep, int w, int h, int floors, int minPop = 0)
        {
            var d = new BuildingDef
            {
                Id = (ushort)defs.Count, Model = model, Name = name, Category = category, Kind = kind,
                Cost = cost, Upkeep = upkeep * UpkeepPerDay, Width = w, Height = h, Floors = floors, MinPopulation = minPop,
            };
            defs.Add(d);
            byModel[model] = d;
            return d;
        }

        static BuildingDef Home(string model, string name, int cost, int w, int h, int floors, int residents, int minPop = 0, Category category = Category.Housing)
        {
            var d = Add(model, name, category, Kind.Housing, cost, 0, w, h, floors, minPop);
            d.Residents = residents;
            d.PowerUse = System.Math.Max(1, residents / 8);
            d.Summary = $"{residents} habitantes";
            return d;
        }

        static BuildingDef Work(string model, string name, Category category, int cost, int w, int h, int floors, int jobs, bool industrial, int minPop = 0, int desire = 0, int desireRadius = 0)
        {
            var d = Add(model, name, category, Kind.Jobs, cost, 0, w, h, floors, minPop);
            d.Jobs = jobs;
            d.Industrial = industrial;
            d.PowerUse = System.Math.Max(1, jobs / (industrial ? 5 : 8));
            d.Desirability = desire;
            d.DesireRadius = desireRadius;
            d.Summary = $"{jobs} empleos";
            return d;
        }

        static BuildingDef Plant(string model, string name, int cost, int upkeep, int w, int h, int floors, int supply, int reach, int minPop = 0, int desire = 0, int desireRadius = 0)
        {
            var d = Add(model, name, Category.Energy, Kind.Power, cost, upkeep, w, h, floors, minPop);
            d.PowerSupply = supply;
            d.PowerReach = reach;
            d.Desirability = desire;
            d.DesireRadius = desireRadius;
            d.Summary = supply > 0 ? $"{supply} MW" : $"Lleva la luz {reach} casillas";
            return d;
        }

        static BuildingDef Serve(string model, string name, Category category, Service service, int cost, int upkeep, int w, int h, int floors, int radius, int minPop = 0, int desire = 0)
        {
            var d = Add(model, name, category, Kind.Service, cost, upkeep, w, h, floors, minPop);
            d.Service = service;
            d.Radius = radius;
            d.Desirability = desire;
            d.DesireRadius = desire != 0 ? System.Math.Max(3, radius / 2) : 0;
            d.PowerUse = System.Math.Max(1, w * h / 2);
            d.Summary = service == Service.None ? (d.CityBonus > 0 ? "" : $"Atractivo +{desire}") : $"Alcance {radius} casillas";
            return d;
        }

        static Catalog()
        {
            // Transport and mobility.
            var road = Add("calle", "Calle local", Category.Transport, Kind.Road, 10, 1, 1, 1, 0);
            road.Summary = "$ 10 por casilla";
            Road = road.Id;
            var avenue = Add("avenida", "Avenida", Category.Transport, Kind.Road, 25, 2, 1, 1, 0, 300);
            avenue.Summary = "$ 25 por casilla";
            avenue.Desirability = 3;
            avenue.DesireRadius = 2;
            Avenue = avenue.Id;
            var bridge = Add("puente", "Puente urbano", Category.Transport, Kind.Road, 120, 4, 1, 1, 0, 100);
            bridge.Bridge = true;
            bridge.Summary = "Cruza el río";
            Bridge = bridge.Id;
            Serve("terminal_autobuses", "Terminal de autobuses", Category.Transport, Service.Transit, 6000, 250, 3, 2, 1, 16, 800, 4);
            Serve("estacion_tren", "Estación de tren", Category.Transport, Service.Transit, 18000, 600, 5, 2, 2, 26, 3000, 5);
            Serve("estacion_metro", "Estación de metro", Category.Transport, Service.Transit, 12000, 450, 1, 1, 1, 14, 6000, 6);
            Work("centro_logistico", "Centro logístico", Category.Transport, 14000, 4, 3, 2, 90, true, 1500, -10, 4);
            var airport = Work("aeropuerto", "Aeropuerto", Category.Transport, 90000, 8, 5, 3, 400, false, 15000, -25, 10);
            airport.CityBonus = 4;
            airport.Summary = "400 empleos · satisfacción +4";

            // Housing.
            Home("casa_basica", "Casa básica", 300, 1, 1, 1, 4);
            Home("casa_ampliada", "Casa ampliada", 550, 1, 2, 2, 6);
            Home("duplex", "Dúplex", 800, 2, 1, 2, 10, 40);
            Home("apartamentos", "Apartamentos", 3500, 2, 2, 4, 48, 150);
            Home("conjunto_residencial", "Conjunto residencial", 9500, 3, 3, 4, 150, 500);
            Home("torre_residencial", "Torre residencial", 16000, 2, 2, 12, 320, 1200);

            // Commerce and offices.
            Work("tienda_abarrotes", "Tienda de abarrotes", Category.Commerce, 450, 2, 2, 1, 6, false);
            Work("mercado_municipal", "Mercado municipal", Category.Commerce, 2500, 3, 3, 2, 25, false, 100);
            Work("supermercado", "Supermercado", Category.Commerce, 4500, 4, 3, 1, 40, false, 250);
            Work("restaurante", "Restaurante", Category.Commerce, 1200, 3, 3, 2, 12, false, 60, 6, 3);
            Work("centro_comercial", "Centro comercial", Category.Commerce, 14000, 4, 4, 3, 130, false, 900, 5, 5);
            Work("edificio_oficinas", "Edificio de oficinas", Category.Commerce, 9000, 3, 3, 5, 120, false, 700);
            Work("torre_corporativa", "Torre corporativa", Category.Commerce, 26000, 4, 3, 18, 360, false, 2500);
            Work("centro_financiero", "Centro financiero", Category.Commerce, 45000, 5, 4, 14, 600, false, 6000, 6, 6);

            // Industry and production.
            Work("granja_agricola", "Granja agrícola", Category.Industry, 1500, 4, 4, 1, 12, true);
            Work("procesadora_alimentos", "Planta de alimentos", Category.Industry, 5000, 3, 3, 2, 40, true, 100, -15, 4);
            // Temporary logging camp: a tent and a woodpile anywhere near the trees, no street or power needed.
            var camp = Work("puesto_tala", "Puesto de tala temporal", Category.Industry, 150, 1, 1, 0, 2, true);
            camp.LumberReach = 8;
            camp.PowerUse = 0;
            camp.Standalone = true;
            camp.QuickBuild = TicksPerHourForCamp;
            camp.Summary = "Su leñador tala los árboles que marques cerca";
            var station = Work("caseta_forestal", "Caseta forestal", Category.Industry, 400, 1, 1, 1, 3, true, 0, -2, 2);
            station.LumberReach = 14;
            station.PowerUse = 0;
            station.NeedsForest = true;
            station.Summary = "Un leñador tala los árboles que marques · sin luz";
            var sawmill = Work("aserradero", "Aserradero", Category.Industry, 3500, 3, 3, 2, 30, true, 300, -15, 4);
            sawmill.LumberReach = 12;
            sawmill.AutoFell = true;
            sawmill.NeedsForest = true;
            sawmill.Summary = $"Su cuadrilla tala sola · {WoodPerSawmillTree} de madera por árbol";
            Work("fabrica_materiales", "Fábrica de materiales", Category.Industry, 7000, 4, 3, 2, 50, true, 300, -25, 5);
            Work("planta_metalurgica", "Planta metalúrgica", Category.Industry, 12000, 4, 4, 3, 90, true, 800, -40, 7);
            Work("fabrica_bienes", "Fábrica de bienes de consumo", Category.Industry, 10000, 4, 3, 2, 80, true, 600, -20, 5);
            Work("planta_electronica", "Planta electrónica", Category.Industry, 18000, 4, 3, 2, 110, true, 2000, -10, 4);
            Work("industria_automatizada", "Industria automatizada", Category.Industry, 30000, 4, 4, 2, 160, true, 5000, -10, 4);

            // Energy.
            // A small, cheap generator so a new town can afford its first lights.
            var generator = Plant("generador", "Generador eléctrico", 800, 30, 1, 1, 1, 40, 7, 0, -8, 3);
            generator.Summary = "40 MW · barato de mantener";
            Plant("central_termoelectrica", "Central termoeléctrica", 6000, 200, 2, 2, 3, 400, 12, 0, -35, 7);
            Plant("subestacion", "Subestación eléctrica", 800, 20, 2, 2, 1, 0, 10);
            Plant("planta_solar", "Planta solar", 5000, 40, 2, 2, 1, 120, 8, 200);
            Plant("parque_eolico", "Parque eólico", 8000, 60, 2, 2, 1, 220, 10, 500);
            var hydro = Plant("central_hidroelectrica", "Central hidroeléctrica", 20000, 300, 2, 2, 3, 900, 14, 2000);
            hydro.NeedsWater = true;
            hydro.Summary = "900 MW · junto al río";
            Plant("sistema_baterias", "Sistema de baterías", 4000, 50, 2, 2, 1, 100, 6, 1000);
            Plant("central_nuclear", "Central nuclear", 60000, 1500, 2, 2, 4, 3000, 20, 8000, -30, 8);
            Plant("red_inteligente", "Centro de red inteligente", 15000, 300, 2, 2, 3, 300, 16, 4000);

            // Water, sanitation and waste.
            Serve("estacion_bombeo", "Estación de bombeo", Category.Water, Service.Water, 1200, 50, 1, 1, 1, 9);
            Serve("planta_potabilizadora", "Planta potabilizadora", Category.Water, Service.Water, 8000, 280, 2, 2, 2, 22, 1000);
            Serve("tanque_agua", "Tanque de almacenamiento", Category.Water, Service.Water, 3000, 90, 2, 2, 3, 13, 400);
            Serve("control_alcantarillado", "Red de alcantarillado", Category.Water, Service.Sewage, 3000, 110, 2, 2, 2, 15, 300);
            Serve("planta_tratamiento", "Planta de tratamiento", Category.Water, Service.Sewage, 9000, 320, 2, 2, 2, 26, 1500, -10);
            Serve("recoleccion_basura", "Centro de recolección de basura", Category.Water, Service.Waste, 2500, 100, 2, 2, 1, 15, 300, -8);
            Serve("planta_reciclaje", "Planta de reciclaje", Category.Water, Service.Waste, 7000, 240, 2, 2, 2, 22, 1500, -5);
            var waste = Serve("valorizacion_energetica", "Planta de valorización energética", Category.Water, Service.Waste, 15000, 420, 2, 2, 3, 24, 3000, -15);
            waste.PowerSupply = 150;
            waste.PowerReach = 8;
            waste.Summary = "Alcance 24 · 150 MW";
            void Supplies(string model, int capacity)
            {
                var d = Get(model);
                d.Capacity = capacity;
                d.Summary = $"{capacity} {UtilityUnit(d.Service)} al día · alcance {d.Radius}";
            }
            Supplies("estacion_bombeo", 60);
            Supplies("planta_potabilizadora", 700);
            Supplies("tanque_agua", 220);
            Supplies("control_alcantarillado", 120);
            Supplies("planta_tratamiento", 900);
            Supplies("recoleccion_basura", 500);
            Supplies("planta_reciclaje", 1800);
            Supplies("valorizacion_energetica", 3500);
            waste.Summary = "3 500 kg al día · 150 MW";

            // Safety and government.
            Serve("puesto_policia", "Puesto de policía", Category.Safety, Service.Safety, 700, 60, 1, 1, 1, 9, 0, 5);
            Serve("comisaria", "Comisaría", Category.Safety, Service.Safety, 3500, 180, 2, 2, 2, 18, 800, 8);
            Serve("estacion_bomberos", "Estación de bomberos", Category.Safety, Service.Fire, 2500, 140, 2, 2, 2, 13, 200, 5);
            Serve("proteccion_civil", "Centro de protección civil", Category.Safety, Service.Fire, 4500, 200, 2, 2, 2, 18, 1500, 4);
            Serve("central_emergencias", "Central de emergencias", Category.Safety, Service.Safety, 7000, 300, 2, 2, 2, 24, 3000, 4);
            Serve("gestion_riesgos", "Centro de gestión de riesgos", Category.Safety, Service.Fire, 9000, 360, 2, 2, 3, 28, 5000, 4);
            var hall = Serve("ayuntamiento", "Ayuntamiento", Category.Safety, Service.None, 10000, 300, 3, 3, 4, 0, 500, 12);
            hall.CityBonus = 5;
            hall.DesireRadius = 6;
            hall.Summary = "Satisfacción +5 en la ciudad";
            var admin = Serve("centro_administrativo", "Centro administrativo", Category.Safety, Service.None, 20000, 500, 3, 3, 5, 0, 4000, 6);
            admin.CityBonus = 4;
            admin.DesireRadius = 5;
            admin.Summary = "Satisfacción +4 en la ciudad";

            // Education and research.
            Serve("guarderia", "Guardería", Category.Education, Service.Education, 1000, 60, 2, 2, 1, 8, 0, 6);
            Serve("escuela_primaria", "Escuela primaria", Category.Education, Service.Education, 3000, 140, 3, 3, 2, 13, 200, 10);
            Serve("escuela_secundaria", "Escuela secundaria", Category.Education, Service.Education, 6000, 240, 4, 3, 3, 17, 800, 8);
            Serve("instituto_tecnico", "Instituto técnico", Category.Education, Service.Education, 9000, 320, 3, 3, 4, 19, 2000, 8);
            Serve("universidad", "Universidad", Category.Education, Service.Education, 30000, 900, 5, 4, 5, 30, 6000, 14);
            Serve("biblioteca", "Biblioteca pública", Category.Education, Service.Culture, 3000, 120, 3, 3, 2, 13, 400, 10);
            Serve("centro_investigacion", "Centro de investigación", Category.Education, Service.Education, 40000, 1100, 5, 3, 5, 24, 10000, 10);

            // Health: footprints follow the real models.
            Serve("consultorio", "Consultorio", Category.Health, Service.Health, 1200, 60, 2, 2, 1, 8, 0, 8);
            Serve("clinica", "Clínica comunitaria", Category.Health, Service.Health, 3500, 150, 3, 2, 2, 12, 600, 10);
            Serve("urgencias", "Centro de urgencias", Category.Health, Service.Health, 6000, 260, 3, 3, 2, 15, 1200, 8);
            Serve("hospital_general", "Hospital general", Category.Health, Service.Health, 12000, 480, 4, 4, 6, 20, 2500, 12);
            Serve("hospital_especializado", "Hospital especializado", Category.Health, Service.Health, 20000, 800, 5, 4, 8, 26, 6000, 15);
            Serve("laboratorio", "Laboratorio médico", Category.Health, Service.Health, 2500, 110, 3, 2, 2, 10, 300, 6);

            // Parks, culture and the future.
            Serve("parque_vecinal", "Parque vecinal", Category.Parks, Service.None, 150, 5, 1, 1, 0, 0, 0, 25).DesireRadius = 4;
            var plaza = Serve("plaza_publica", "Plaza pública", Category.Parks, Service.None, 900, 20, 2, 2, 0, 0, 100, 30);
            plaza.DesireRadius = 6;
            Serve("centro_deportivo", "Centro deportivo", Category.Parks, Service.Culture, 5000, 200, 3, 2, 2, 14, 500, 10);
            Serve("museo", "Museo", Category.Parks, Service.Culture, 8000, 260, 3, 2, 2, 16, 1500, 12);
            Serve("teatro", "Teatro", Category.Parks, Service.Culture, 9000, 280, 3, 2, 3, 16, 2500, 12);
            Serve("estadio", "Estadio", Category.Parks, Service.Culture, 40000, 1200, 6, 5, 4, 30, 8000, 6);
            Work("granja_vertical", "Granja vertical", Category.Parks, 12000, 2, 2, 6, 60, true, 5000);
            Home("residencial_inteligente", "Edificio residencial inteligente", 40000, 3, 3, 18, 520, 8000, Category.Parks);
            Work("fabrica_robotizada", "Fábrica robotizada", Category.Parks, 35000, 4, 3, 2, 140, true, 8000, -8, 4);
            var smart = Serve("gestion_urbana", "Centro de gestión urbana inteligente", Category.Parks, Service.None, 30000, 700, 3, 2, 4, 0, 10000, 8);
            smart.CityBonus = 6;
            smart.DesireRadius = 5;
            smart.Summary = "Satisfacción +6 en la ciudad";

            // Wood: homes and small buildings are framed in timber.
            void Timber(string model, int wood) => Get(model).Wood = wood;
            Timber("casa_basica", 4);
            Timber("casa_ampliada", 6);
            Timber("duplex", 8);
            Timber("apartamentos", 16);
            Timber("conjunto_residencial", 40);
            Timber("torre_residencial", 20);
            Timber("tienda_abarrotes", 4);
            Timber("restaurante", 8);
            Timber("mercado_municipal", 12);
            Timber("granja_agricola", 10);
            Timber("guarderia", 6);
            Timber("consultorio", 6);
            Timber("escuela_primaria", 12);
            Timber("puesto_policia", 4);
            foreach (var d in defs)
                if (d != null && d.Wood > 0) d.Summary += $" · {d.Wood} de madera";
        }

        /// <summary>
        /// Ticks (five game minutes each) a building takes to go up: about three hours for a house, five for apartments,
        /// eight for a tower and up to half a day for the biggest projects. Streets are instant.
        /// </summary>
        public static int BuildTicks(BuildingDef def) =>
            def.IsRoad ? 0 : def.QuickBuild > 0 ? def.QuickBuild : (int)System.Math.Round(System.Math.Min(13, .72 * System.Math.Pow(def.Cost, .25)) * City.TicksPerHour);

        // ---------------------------------------------------------------- services

        /// <summary>Services shared out by capacity, like power: water, sewage and waste.</summary>
        public static readonly Service[] Utilities = { Service.Water, Service.Sewage, Service.Waste };

        public static bool IsUtility(Service s) => s == Service.Water || s == Service.Sewage || s == Service.Waste;

        public static string UtilityUnit(Service s) => s == Service.Waste ? "kg" : "m³";

        /// <summary>Daily use of a utility: homes by the people living there, workplaces by their jobs, the rest by size.</summary>
        public static int Use(BuildingDef d, Service s, int occupants)
        {
            if (d == null || d.IsRoad || d.Service == s) return 0;
            bool waste = s == Service.Waste;
            switch (d.Kind)
            {
                case Kind.Housing: return waste ? occupants : (occupants + 4) / 5;
                case Kind.Jobs: return waste ? d.Jobs / 2 + 1 : d.Jobs / 10 + 1;
                default: return System.Math.Max(1, d.Width * d.Height / (waste ? 1 : 2));
            }
        }

        /// <summary>Spanish name of a service, for the interface.</summary>
        public static string ServiceName(Service s)
        {
            switch (s)
            {
                case Service.Health: return "Salud";
                case Service.Education: return "Educación";
                case Service.Safety: return "Seguridad";
                case Service.Fire: return "Bomberos";
                case Service.Water: return "Agua potable";
                case Service.Sewage: return "Drenaje";
                case Service.Waste: return "Recolección de basura";
                case Service.Culture: return "Cultura y deporte";
                case Service.Transit: return "Transporte público";
                default: return "";
            }
        }

        /// <summary>Population from which residents start to miss a service they do not have.</summary>
        public static int ServiceThreshold(Service s)
        {
            switch (s)
            {
                case Service.Water: return 150;
                case Service.Health: return 300;
                case Service.Safety: return 300;
                case Service.Education: return 400;
                case Service.Fire: return 500;
                case Service.Waste: return 600;
                case Service.Sewage: return 900;
                case Service.Culture: return 1500;
                case Service.Transit: return 3000;
                default: return int.MaxValue;
            }
        }

        public static readonly Service[] Services =
        {
            Service.Water, Service.Health, Service.Safety, Service.Education, Service.Fire,
            Service.Waste, Service.Sewage, Service.Culture, Service.Transit,
        };
    }
}
