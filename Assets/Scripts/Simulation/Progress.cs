using System;

namespace Horizonte.Simulation
{
    /// <summary>City level (from population) and the list of goals that guide a new player.</summary>
    public static class Progress
    {
        /// <summary>Population needed to reach each level; level 1 starts at zero.</summary>
        static readonly int[] LevelPopulation = { 0, 30, 100, 300, 700, 1500, 3000, 6000, 12000, 25000 };

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

        static int Homes(City c) => c.Count(d => d.Kind == Kind.Housing);
        static int Plants(City c) => c.Count(d => d.Kind == Kind.Power && d.PowerSupply > 0);

        /// <summary>Goals in the order they are shown; each is judged from the city as it is now.</summary>
        public static readonly Goal[] Goals =
        {
            G("Construye 5 viviendas junto a una calle", 5, c => Homes(c)),
            G("Construye una central eléctrica", 1, c => Plants(c)),
            G("Abre una tienda de abarrotes", 1, c => c.Count("tienda_abarrotes")),
            G("Alcanza 50 habitantes", 50, c => c.Population),
            G("Construye una granja agrícola", 1, c => c.Count("granja_agricola")),
            G("Construye una toma de agua y una estación de bombeo", 2, c => System.Math.Min(1, c.Count("toma_agua")) + System.Math.Min(1, c.Count("estacion_bombeo"))),
            G("Crea 60 empleos", 60, c => c.Jobs),
            G("Construye un consultorio", 1, c => c.Count("consultorio")),
            G("Construye un puesto de policía", 1, c => c.Count("puesto_policia")),
            G("Alcanza 200 habitantes", 200, c => c.Population),
            G("Construye una escuela primaria", 1, c => c.Count("escuela_primaria")),
            G("Construye una estación de bomberos", 1, c => c.Count("estacion_bomberos")),
            G("Construye el ayuntamiento", 1, c => c.Count("ayuntamiento")),
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
