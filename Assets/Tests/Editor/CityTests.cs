using System.Linq;
using Horizonte.Simulation;
using NUnit.Framework;

namespace Horizonte.Tests
{
    public sealed class CityTests
    {
        static ushort Id(string model) => Catalog.Get(model).Id;

        /// <summary>Flat land with a street along y = 10 that reaches the west edge of the map.</summary>
        static City Flat()
        {
            var city = new City(30, 30, 7) { Money = 1_000_000, IgnoreRequirements = true };
            city.PlaceMany(Catalog.Road, City.Line(0, 10, 20, 10), out _);
            return city;
        }

        static void Days(City city, int days)
        {
            for (int d = 0; d < days; d++) city.Step();
        }

        [Test]
        public void EveryBuildingHasAModelNameAndAPrice()
        {
            var defs = Catalog.All.Where(d => d != null).ToList();
            Assert.Greater(defs.Count, 70);
            Assert.AreEqual(defs.Count, defs.Select(d => d.Model).Distinct().Count());
            Assert.IsTrue(defs.All(d => d.Cost > 0 && d.Width > 0 && d.Height > 0 && !string.IsNullOrEmpty(d.Name)));
        }

        [Test]
        public void BuildingsNeedAStreetNextToThem()
        {
            var city = Flat();
            Assert.AreEqual(PlaceResult.NoRoad, city.Place(Id("casa_basica"), 5, 14));
            Assert.AreEqual(PlaceResult.Ok, city.Place(Id("casa_basica"), 5, 11));
            Assert.AreEqual(PlaceResult.Occupied, city.Place(Id("casa_basica"), 5, 11));
            Assert.AreEqual(PlaceResult.Ok, city.Place(Id("apartamentos"), 6, 8));
        }

        [Test]
        public void PlacingChargesMoney()
        {
            var city = Flat();
            long before = city.Money;
            city.Place(Id("duplex"), 2, 11);
            Assert.AreEqual(before - Catalog.Get("duplex").Cost, city.Money);
        }

        [Test]
        public void LockedBuildingsWaitForPopulation()
        {
            var city = Flat();
            city.IgnoreRequirements = false;
            Assert.AreEqual(PlaceResult.Locked, city.Place(Id("torre_residencial"), 2, 11));
            city.IgnoreRequirements = true;
            Assert.AreEqual(PlaceResult.Ok, city.Place(Id("torre_residencial"), 2, 11));
        }

        [Test]
        public void HomesFillUpWithPowerAndJobs()
        {
            var city = Flat();
            city.Place(Id("central_termoelectrica"), 0, 11);
            for (int x = 4; x < 12; x++) city.Place(Id("casa_basica"), x, 11);
            city.Place(Id("tienda_abarrotes"), 4, 8);
            city.Place(Id("granja_agricola"), 6, 6);
            city.FinishConstruction();
            Assert.IsTrue(city.Working(city.Index(4, 11)));
            Days(city, 120);
            Assert.Greater(city.Population, 10);
            Assert.Greater(city.Satisfaction, 0);
        }

        [Test]
        public void HomesWithoutPowerStayEmpty()
        {
            var city = Flat();
            for (int x = 4; x < 8; x++) city.Place(Id("casa_basica"), x, 11);
            Days(city, 60);
            Assert.AreEqual(0, city.Population);
            Assert.IsFalse(city.Powered[city.Index(4, 11)]);
        }

        [Test]
        public void PowerReachesOnlyAroundPlantsAndSubstations()
        {
            var city = new City(60, 30, 7) { Money = 1_000_000 };
            city.PlaceMany(Catalog.Road, City.Line(0, 10, 59, 10), out _);
            city.Place(Id("central_termoelectrica"), 0, 11);
            city.Place(Id("casa_basica"), 50, 11);
            Assert.IsFalse(city.Powered[city.Index(50, 11)]);
            for (int x = 12; x < 50; x += 9) city.Place(Id("subestacion"), x, 11);
            city.FinishConstruction();
            Assert.IsTrue(city.Powered[city.Index(50, 11)]);
        }

        [Test]
        public void ServicesCoverNearbyHomes()
        {
            var city = Flat();
            city.Place(Id("central_termoelectrica"), 0, 11);
            city.Place(Id("casa_basica"), 4, 11);
            Assert.IsFalse(city.Covered(city.Index(4, 11), Service.Health));
            city.Place(Id("consultorio"), 6, 8);
            city.FinishConstruction();
            Assert.IsTrue(city.Covered(city.Index(4, 11), Service.Health));
        }

        [Test]
        public void BuildingsWorkOnlyOnceFinished()
        {
            var city = Flat();
            city.Place(Id("central_termoelectrica"), 0, 11);
            city.Place(Id("tienda_abarrotes"), 4, 11);
            int shop = city.Index(4, 11);
            Assert.Greater(city.Construction[shop], 0);
            Assert.IsFalse(city.Working(shop));
            Days(city, Catalog.BuildDays(Catalog.Get("central_termoelectrica")));
            Assert.AreEqual(0, city.Construction[shop]);
            Assert.IsTrue(city.Working(shop));
            Assert.AreEqual(6, city.Jobs);
        }

        [Test]
        public void WaterIsSharedByCapacity()
        {
            var city = Flat();
            city.Place(Id("central_termoelectrica"), 0, 11);
            city.Place(Id("estacion_bombeo"), 4, 9);
            city.Place(Id("casa_basica"), 5, 11);
            city.Place(Id("torre_residencial"), 8, 11);
            city.FinishConstruction();
            city.Occupants[city.Index(5, 11)] = 4;
            city.Occupants[city.Index(8, 11)] = 320;
            city.Refresh();
            Assert.AreEqual(60, city.UtilitySupply(Service.Water));
            Assert.AreEqual(1 + 64 + 2, city.UtilityDemand(Service.Water));
            Assert.IsTrue(city.Covered(city.Index(5, 11), Service.Water));
            Assert.IsFalse(city.Covered(city.Index(8, 11), Service.Water));
        }

        [Test]
        public void LoansArePaidBackMonthly()
        {
            var city = Flat();
            long before = city.Money;
            for (int k = 0; k < City.MaxLoans; k++) Assert.IsTrue(city.TakeLoan());
            Assert.IsFalse(city.TakeLoan());
            Assert.AreEqual(before + City.LoanAmount * City.MaxLoans, city.Money);
            Assert.AreEqual(55000, city.LoanBalance);
            Days(city, City.DaysPerMonth);
            Assert.AreEqual(2292, city.LastLoanPayment);
            Assert.AreEqual(55000 - 2292, city.LoanBalance);
        }

        [Test]
        public void ShopsPayTaxesAndTheGeneratorIsAvailableFromTheStart()
        {
            var city = Flat();
            city.IgnoreRequirements = false;
            Assert.AreEqual(PlaceResult.Ok, city.Place(Id("generador"), 0, 11));
            city.Place(Id("tienda_abarrotes"), 2, 11);
            city.FinishConstruction();
            Assert.IsTrue(city.Working(city.Index(2, 11)));
            Days(city, City.DaysPerMonth);
            Assert.AreEqual((int)System.Math.Round(6 * 9 / 5.0), city.LastBusinessTax);
        }

        [Test]
        public void MultiTileBuildingsAreBulldozedWhole()
        {
            var city = Flat();
            city.Place(Id("apartamentos"), 3, 11);
            Assert.IsTrue(city.Bulldoze(4, 12));
            Assert.AreEqual(Catalog.None, city.Building[city.Index(3, 11)]);
            Assert.AreEqual(-1, city.Anchor[city.Index(4, 12)]);
        }

        [Test]
        public void BridgesGoOnlyOverWaterAndHydroNeedsTheRiver()
        {
            var city = Flat();
            city.IgnoreRequirements = true;
            city.Terrain[city.Index(21, 10)] = Terrain.Water;
            Assert.AreEqual(PlaceResult.Water, city.Place(Catalog.Road, 21, 10));
            Assert.AreEqual(PlaceResult.Ok, city.Place(Catalog.Bridge, 21, 10));
            Assert.AreEqual(PlaceResult.Occupied, city.Place(Catalog.Bridge, 22, 10));
            Assert.AreEqual(PlaceResult.NeedsWater, city.Place(Id("central_hidroelectrica"), 2, 11));
        }

        [Test]
        public void StreetsCanBeRebuiltAsAvenues()
        {
            var city = Flat();
            city.IgnoreRequirements = true;
            Assert.AreEqual(PlaceResult.Ok, city.Place(Catalog.Avenue, 3, 10));
            Assert.AreEqual(Catalog.Avenue, city.Building[city.Index(3, 10)]);
            Assert.IsTrue(city.RoadConnected[city.Index(5, 10)]);
        }

        [Test]
        public void SaveRoundTripKeepsTheCity()
        {
            var city = Flat();
            city.Name = "Nueva Ardelia";
            city.Place(Id("central_termoelectrica"), 0, 11);
            city.Place(Id("apartamentos"), 4, 11);
            city.Place(Id("tienda_abarrotes"), 4, 8);
            Days(city, 60);
            var copy = CitySave.From(city).ToCity();
            CollectionAssert.AreEqual(city.Building, copy.Building);
            CollectionAssert.AreEqual(city.Terrain, copy.Terrain);
            CollectionAssert.AreEqual(city.Anchor, copy.Anchor);
            CollectionAssert.AreEqual(city.Occupants, copy.Occupants);
            Assert.AreEqual(city.Money, copy.Money);
            Assert.AreEqual(city.Day, copy.Day);
            Assert.AreEqual(city.Population, copy.Population);
            Assert.AreEqual(city.Name, copy.Name);
        }

        [Test]
        public void RectangularBuildingsTurnToFaceAStreetRunningNorth()
        {
            var city = Flat();
            city.PlaceMany(Catalog.Road, City.Line(15, 11, 15, 20), out _);
            ushort duplex = Id("duplex");
            bool turned = city.ChooseTurn(duplex, 16, 14, out int x, out int y);
            Assert.IsTrue(turned);
            Assert.AreEqual(PlaceResult.Ok, city.Place(duplex, x, y, turned));
            int anchor = city.Index(x, y);
            Assert.AreEqual(1, city.Rotation[anchor], "faces west, toward the street");
            Assert.AreEqual((1, 2), city.Size(anchor));
            Assert.AreEqual(anchor, city.Anchor[city.Index(x, y + 1)]);
            Assert.AreEqual(Catalog.None, city.Building[city.Index(x + 1, y)]);

            var copy = CitySave.From(city).ToCity();
            CollectionAssert.AreEqual(city.Anchor, copy.Anchor);

            Assert.IsTrue(city.Bulldoze(x, y + 1));
            Assert.AreEqual(Catalog.None, city.Building[city.Index(x, y)]);
            Assert.AreEqual(Catalog.None, city.Building[city.Index(x, y + 1)]);
        }

        [Test]
        public void LevelsFollowPopulation()
        {
            Assert.AreEqual(1, Progress.Level(0));
            Assert.AreEqual(2, Progress.Level(30));
            Assert.AreEqual(Progress.MaxLevel, Progress.Level(1000000));
            Assert.AreEqual(1f, Progress.LevelProgress(1000000));
        }

        [Test]
        public void GeneratedMapHasRiverForestAndHighway()
        {
            var city = CityGenerator.Create();
            Assert.IsTrue(city.Terrain.Contains(Terrain.Water));
            Assert.IsTrue(city.Terrain.Contains(Terrain.Forest));
            Assert.IsTrue(city.RoadConnected[city.Index(city.Width / 2 - 1, city.Height / 2)]);
        }
    }
}
