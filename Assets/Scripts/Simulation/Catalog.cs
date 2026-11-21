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
    }

    /// <summary>Fixed data for everything the player can place.</summary>
    public sealed class StructureDef
    {
        public Structure Type;
        /// <summary>Base name of the 3D model in Resources/Buildings (zones add "_1".."_3" for the level).</summary>
        public string ModelId;
        public string Name;
        public int Cost;
        /// <summary>Monthly upkeep, per tile for roads.</summary>
        public int Upkeep;
        public int Width = 1, Height = 1;
        public bool IsZone;
        /// <summary>Power the building supplies to the grid.</summary>
        public int PowerSupply;
        /// <summary>Tiles around the building that feel its effect.</summary>
        public int Radius;
        /// <summary>Desirability added at the centre of the radius, fading to zero at its edge.</summary>
        public int Desirability;
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
        };

        public static StructureDef Get(Structure type) => defs[(int)type];

        public const int MaxLevel = 3;

        /// <summary>Residents or jobs a zone tile holds at each level.</summary>
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

        /// <summary>Power a tile draws from the grid.</summary>
        public static int PowerUse(Structure type, int level)
        {
            var def = Get(type);
            if (def == null || type == Structure.Road || type == Structure.PowerPlant) return 0;
            if (def.IsZone) return level * (type == Structure.Industrial ? 3 : 2);
            return 2;
        }
    }
}
