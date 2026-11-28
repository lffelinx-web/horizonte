using Horizonte.Simulation;
using NUnit.Framework;

namespace Horizonte.Tests
{
    public sealed class LakeTests
    {
        static ushort Id(string model) => Catalog.Get(model).Id;

        /// <summary>Flat land with a street along y = 10, a power plant, a pumping station and a 3 x 3 lake south of the street.</summary>
        static City WithLake()
        {
            var city = new City(30, 30, 7) { Money = 1_000_000, IgnoreRequirements = true };
            city.PlaceMany(Catalog.Road, City.Line(0, 10, 20, 10), out _);
            for (int y = 6; y <= 8; y++)
            for (int x = 14; x <= 16; x++)
            {
                int i = city.Index(x, y);
                city.Terrain[i] = Terrain.Water;
                city.LakeOf[i] = 1;
                // One metre deep at the shore, three in the middle.
                city.LakeDepth[i] = (byte)(x == 15 && y == 7 ? 30 : 10);
            }
            city.RebuildLakes();
            city.Place(Id("central_termoelectrica"), 0, 11);
            city.Place(Id("estacion_bombeo"), 4, 9);
            return city;
        }

        [Test]
        public void ANewMapHasThreeFullLakes()
        {
            var city = CityGenerator.Create();
            Assert.AreEqual(3, city.Lakes.Count);
            foreach (var lake in city.Lakes)
            {
                Assert.GreaterOrEqual(lake.Tiles.Count, 8);
                Assert.Greater(lake.Capacity, 0);
                Assert.AreEqual(1, lake.Level, .001f);
            }
        }

        [Test]
        public void PumpsGiveWaterOnlyWithAnIntake()
        {
            var city = WithLake();
            city.Place(Id("casa_basica"), 5, 11);
            city.FinishConstruction();
            city.Occupants[city.Index(5, 11)] = 4;
            city.Refresh();
            Assert.AreEqual(0, city.UtilitySupply(Service.Water));
            Assert.IsTrue(city.MissingIntake);
            Assert.IsFalse(city.Covered(city.Index(5, 11), Service.Water));

            Assert.AreEqual(PlaceResult.NeedsWater, city.Place(Id("toma_agua"), 10, 9));
            Assert.AreEqual(PlaceResult.Ok, city.Place(Id("toma_agua"), 15, 9));
            city.FinishConstruction();
            city.Refresh();
            Assert.AreEqual(1, city.SourceOf(city.Index(15, 9)));
            Assert.AreEqual(60, city.UtilitySupply(Service.Water));
            Assert.IsFalse(city.MissingIntake);
            Assert.IsTrue(city.Covered(city.Index(5, 11), Service.Water));
        }

        [Test]
        public void DrawingMoreThanItFillsDriesALakeAndItComesBack()
        {
            var city = WithLake();
            city.Place(Id("toma_agua"), 15, 9);
            // An airport: 400 jobs, 41 m³ of water a day whatever happens in town.
            int tower = city.Index(3, 11);
            Assert.AreEqual(PlaceResult.Ok, city.Place(Id("aeropuerto"), 3, 11));
            city.FinishConstruction();
            city.Refresh();
            Assert.IsTrue(city.Covered(tower, Service.Water));
            var lake = city.Lakes[0];
            Assert.AreEqual(220, lake.Capacity, .1f);
            Assert.Greater(lake.Draw, lake.Inflow);

            city.StepDays(5);
            Assert.Less(lake.Level, .8f);
            Assert.AreEqual(Terrain.Water, city.Terrain[city.Index(14, 6)]);

            city.StepDays(12);
            Assert.IsTrue(lake.Dry);
            Assert.AreEqual(Terrain.Lakebed, city.Terrain[city.Index(15, 7)]);
            Assert.AreEqual(0, city.IntakeYield(city.Index(15, 9)));
            Assert.IsFalse(city.Covered(tower, Service.Water));

            // Left alone, it fills up again.
            city.Bulldoze(15, 9);
            city.StepDays(12);
            Assert.AreEqual(1, lake.Level, .01f);
            Assert.AreEqual(Terrain.Water, city.Terrain[city.Index(14, 6)]);
        }

        [Test]
        public void ALakeBedIsBuiltOnOnlyWhenTheLakeIsDry()
        {
            var city = WithLake();
            // Low: the shore is dry, the middle still under water.
            city.RebuildLakes(new[] { 30f });
            int shore = city.Index(14, 8);
            Assert.AreEqual(Terrain.Lakebed, city.Terrain[shore]);
            Assert.AreEqual(Terrain.Water, city.Terrain[city.Index(15, 7)]);
            Assert.AreEqual(PlaceResult.Lake, city.Place(Catalog.Road, 14, 8));

            city.RebuildLakes(new[] { 0f });
            Assert.IsTrue(city.Lakes[0].Dry);
            Assert.AreEqual(PlaceResult.Ok, city.Place(Catalog.Road, 14, 8));
            Assert.AreEqual(0, city.LakeOf[shore]);
            Assert.AreEqual(200, city.Lakes[0].Capacity, .1f);
        }
    }
}
