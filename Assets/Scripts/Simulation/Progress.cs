using System;

namespace Horizonte.Simulation
{
    /// <summary>City level (from population) and the list of goals that guide a new player.</summary>
    public static class Progress
    {
        /// <summary>Population needed to reach each level; level 1 starts at zero.</summary>
        static readonly int[] LevelPopulation = { 0, 50, 200, 500, 1000, 2000, 4000, 8000, 15000, 30000 };

        public static int MaxLevel => LevelPopulation.Length;

        public static int Level(int population)
        {
            int level = 1;
            for (int i = 1; i < LevelPopulation.Length; i++) if (population >= LevelPopulation[i]) level = i + 1;
            return level;
        }

        /// <summary>How far the city is from its level towards the next one, 0 to 1 (1 at the top level).</summary>
        public static float LevelProgress(int population)
        {
            int level = Level(population);
            if (level >= MaxLevel) return 1;
            int from = LevelPopulation[level - 1], to = LevelPopulation[level];
            return (population - from) / (float)(to - from);
        }

        public static int NextLevelPopulation(int population)
        {
            int level = Level(population);
            return level >= MaxLevel ? LevelPopulation[MaxLevel - 1] : LevelPopulation[level];
        }

        public sealed class Goal
        {
            public string Text;
            public int Target;
            public Func<City, long> Value;

            public long Current(City c) => Math.Min(Value(c), Target);
            public bool Done(City c) => Value(c) >= Target;
        }

        static Goal G(string text, int target, Func<City, long> value) => new Goal { Text = text, Target = target, Value = value };

        /// <summary>Goals in the order they are shown; each is judged from the city as it is now.</summary>
        public static readonly Goal[] Goals =
        {
            G("Marca 10 lotes residenciales", 10, c => c.Count(Structure.Residential)),
            G("Construye una central eléctrica", 1, c => c.Count(Structure.PowerPlant)),
            G("Alcanza 100 habitantes", 100, c => c.Population),
            G("Marca 6 lotes comerciales", 6, c => c.Count(Structure.Commercial)),
            G("Crea 100 empleos", 100, c => c.Jobs),
            G("Construye un parque", 1, c => c.Count(Structure.Park)),
            G("Alcanza 500 habitantes", 500, c => c.Population),
            G("Construye una escuela", 1, c => c.Count(Structure.School)),
            G("Construye un consultorio", 1, c => c.Count(Structure.Doctor)),
            G("Construye una estación de policía", 1, c => c.Count(Structure.Police)),
            G("Alcanza 1 000 habitantes", 1000, c => c.Population),
            G("Ahorra $ 50 000", 50000, c => c.Money),
            G("Alcanza 2 500 habitantes", 2500, c => c.Population),
            G("Llega al nivel de ciudad 8", 8, c => Level(c.Population)),
            G("Alcanza 10 000 habitantes", 10000, c => c.Population),
            G("Alcanza 30 000 habitantes", 30000, c => c.Population),
        };

        /// <summary>Index of the first goal not yet met, or Goals.Length when all are.</summary>
        public static int FirstOpenGoal(City c)
        {
            for (int i = 0; i < Goals.Length; i++) if (!Goals[i].Done(c)) return i;
            return Goals.Length;
        }
    }
}
