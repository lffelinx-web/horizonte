namespace Horizonte.Simulation
{
    public enum Terrain : byte { Grass, Water, Forest }

    public enum Structure : byte
    {
        None,
        Road,
        Residential,
        Commercial,
        Industrial,
        PowerPlant,
        Park,
        Police,
        School,
        Doctor,
        Lab,
        Clinic,
        Emergency,
        Hospital,
        SpecialistHospital,
    }

    /// <summary>Fixed data for everything the player can place.</summary>
    public sealed class StructureDef
    {
        public Structure Type;
        /// <summary>Base name of the 3D model in Resources/Buildings (zones add the level, and the size when bigger than one lot).</summary>
        public string ModelId;
        public string Name;
        public int Cost;
        /// <summary>Monthly upkeep, per tile for roads.</summary>
        public int Upkeep;
        public int Width = 1, Height = 1;
        public bool IsZone;
        /// <summary>Power the building supplies to the grid.</summary>
        public int PowerSupply;
        /// <summary>Power a service building draws from the grid.</summary>
        public int PowerUse = 2;
        /// <summary>Tiles around the building that feel its effect.</summary>
        public int Radius;
        /// <summary>Desirability added at the centre of the radius, fading to zero at its edge.</summary>
        public int Desirability;
        /// <summary>Gives medical care to homes within its radius.</summary>
        public bool IsHealth;
        /// <summary>Residents the city needs before it can be built.</summary>
        public int MinPopulation;
    }

    public static class Catalog
    {
        public const int ClearForestCost = 5;
        public const int BulldozeCost = 5;

        static readonly StructureDef[] defs =
        {
            null,
            new StructureDef { Type = Structure.Road, ModelId = "calle", Name = "Calle", Cost = 10, Upkeep = 1 },
            new StructureDef { Type = Structure.Residential, ModelId = "residencial", Name = "Residencial", Cost = 20, IsZone = true },
            new StructureDef { Type = Structure.Commercial, ModelId = "comercial", Name = "Comercial", Cost = 25, IsZone = true },
            new StructureDef { Type = Structure.Industrial, ModelId = "industrial", Name = "Industrial", Cost = 25, IsZone = true, Radius = 5, Desirability = -35 },
            new StructureDef { Type = Structure.PowerPlant, ModelId = "central_electrica", Name = "Central eléctrica", Cost = 3000, Upkeep = 120, Width = 2, Height = 2, PowerSupply = 400, Radius = 6, Desirability = -30 },
            new StructureDef { Type = Structure.Park, ModelId = "parque", Name = "Parque", Cost = 150, Upkeep = 5, Radius = 4, Desirability = 30 },
            new StructureDef { Type = Structure.Police, ModelId = "policia", Name = "Policía", Cost = 700, Upkeep = 60, Radius = 9, Desirability = 15 },
            new StructureDef { Type = Structure.School, ModelId = "escuela", Name = "Escuela", Cost = 900, Upkeep = 80, Width = 2, Height = 1, Radius = 9, Desirability = 20 },
            // Health: footprints follow the real size of the models (one tile is 10 m).
            new StructureDef { Type = Structure.Doctor, ModelId = "consultorio", Name = "Consultorio", Cost = 1200, Upkeep = 60, Width = 2, Height = 2, PowerUse = 3, Radius = 8, Desirability = 8, IsHealth = true },
            new StructureDef { Type = Structure.Lab, ModelId = "laboratorio", Name = "Laboratorio médico", Cost = 2500, Upkeep = 110, Width = 3, Height = 2, PowerUse = 4, Radius = 10, Desirability = 6, IsHealth = true, MinPopulation = 300 },
            new StructureDef { Type = Structure.Clinic, ModelId = "clinica", Name = "Clínica comunitaria", Cost = 3500, Upkeep = 150, Width = 3, Height = 2, PowerUse = 4, Radius = 12, Desirability = 10, IsHealth = true, MinPopulation = 600 },
            new StructureDef { Type = Structure.Emergency, ModelId = "urgencias", Name = "Centro de urgencias", Cost = 6000, Upkeep = 260, Width = 3, Height = 3, PowerUse = 6, Radius = 15, Desirability = 8, IsHealth = true, MinPopulation = 1200 },
            new StructureDef { Type = Structure.Hospital, ModelId = "hospital_general", Name = "Hospital general", Cost = 12000, Upkeep = 480, Width = 4, Height = 4, PowerUse = 10, Radius = 20, Desirability = 12, IsHealth = true, MinPopulation = 2500 },
            new StructureDef { Type = Structure.SpecialistHospital, ModelId = "hospital_especializado", Name = "Hospital especializado", Cost = 20000, Upkeep = 800, Width = 5, Height = 4, PowerUse = 14, Radius = 26, Desirability = 15, IsHealth = true, MinPopulation = 6000 },
        };

        public static StructureDef Get(Structure type) => defs[(int)type];

        public const int MaxLevel = 3;

        /// <summary>Residents or jobs a zone tile holds at each level (a building on several lots holds this on each one).</summary>
        public static int Capacity(Structure zone, int level)
        {
            if (level <= 0) return 0;
            switch (zone)
            {
                case Structure.Residential: return level == 1 ? 8 : level == 2 ? 30 : 90;
                case Structure.Commercial: return level == 1 ? 6 : level == 2 ? 20 : 55;
                case Structure.Industrial: return level == 1 ? 10 : level == 2 ? 30 : 60;
                default: return 0;
            }
        }

        static readonly int[] Single = { 1 };
        static readonly int[] ResidentialLevel2 = { 2, 4 };
        static readonly int[] ResidentialLevel3 = { 3 };

        /// <summary>
        /// Sides, in lots, of the square buildings a zone can grow into at a level. Homes start as houses on one lot;
        /// apartments need 2 x 2 lots (or a 4 x 4 complex) and towers 3 x 3.
        /// </summary>
        public static int[] Footprints(Structure zone, int level)
        {
            if (zone != Structure.Residential || level <= 1) return Single;
            return level == 2 ? ResidentialLevel2 : ResidentialLevel3;
        }

        /// <summary>Power one tile of a zone, or a whole service building, draws from the grid.</summary>
        public static int PowerUse(Structure type, int level)
        {
            var def = Get(type);
            if (def == null || type == Structure.Road || type == Structure.PowerPlant) return 0;
            if (def.IsZone) return level * (type == Structure.Industrial ? 3 : 2);
            return def.PowerUse;
        }
    }
}
