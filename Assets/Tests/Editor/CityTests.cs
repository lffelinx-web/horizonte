using System.Linq;
using Horizonte.Simulation;
using NUnit.Framework;

namespace Horizonte.Tests
{
    public sealed class CityTests
    {
        /// <summary>A flat 24 x 24 map with a road from the west edge along y = 10.</summary>
        static City Flat()
        {
            var city = new City(24, 24, 7);
            city.Money = 1_000_000;
            foreach (var (x, y) in City.Line(0, 10, 15, 10)) city.Place(Structure.Road, x, y);
            return city;
        }

        [Test]
        public void PlacingChargesMoneyAndBlocksTheTile()
        {
            var city = new City(10, 10);
            long before = city.Money;
            Assert.AreEqual(PlaceResult.Ok, city.Place(Structure.Road, 2, 2));
            Assert.AreEqual(before - Catalog.Get(Structure.Road).Cost, city.Money);
            Assert.AreEqual(PlaceResult.Occupied, city.Place(Structure.Park, 2, 2));
            Assert.AreEqual(PlaceResult.OutOfBounds, city.Place(Structure.PowerPlant, 9, 9));
        }

        [Test]
        public void NoMoneyStopsADrag()
        {
            var city = new City(30, 3) { Money = 35 };
            int placed = city.PlaceMany(Structure.Road, City.Line(0, 1, 29, 1), out var problem);
            Assert.AreEqual(3, placed);
            Assert.AreEqual(PlaceResult.NoMoney, problem);
        }

        [Test]
        public void MultiTileBuildingsAreBulldozedWhole()
        {
            var city = new City(10, 10);
            city.Place(Structure.PowerPlant, 3, 3);
            Assert.IsTrue(city.Bulldoze(4, 4));
            Assert.IsTrue(Enumerable.Range(0, 100).All(i => city.Structure[i] == Structure.None));
        }

        [Test]
        public void PowerTravelsAlongRoadsAndRunsOut()
        {
            var city = Flat();
            city.Place(Structure.PowerPlant, 16, 10);
            city.Place(Structure.Residential, 3, 11);
            Assert.IsTrue(city.Powered[city.Index(3, 11)]);
            Assert.IsFalse(city.Powered[city.Index(3, 15)]);
            Assert.AreEqual(Catalog.Get(Structure.PowerPlant).PowerSupply, city.PowerSupply);
        }

        [Test]
        public void RoadAccessNeedsAConnectionToTheEdge()
        {
            var city = Flat();
            city.Place(Structure.Residential, 5, 11);
            city.Place(Structure.Road, 5, 20);
            city.Place(Structure.Residential, 5, 21);
            Assert.IsTrue(city.RoadAccess[city.Index(5, 11)]);
            Assert.IsTrue(city.RoadAccess[city.Index(5, 12)], "Lots two tiles away are served too.");
            Assert.IsFalse(city.RoadAccess[city.Index(5, 13)]);
            Assert.IsFalse(city.RoadAccess[city.Index(5, 21)], "An island road does not count.");
        }

        [Test]
        public void ServedZonesGrowIntoACity()
        {
            var city = Flat();
            city.Place(Structure.PowerPlant, 16, 10);
            city.PlaceMany(Structure.Residential, City.Rect(1, 11, 14, 12), out _);
            city.PlaceMany(Structure.Industrial, City.Rect(1, 8, 7, 9), out _);
            city.PlaceMany(Structure.Commercial, City.Rect(8, 8, 14, 9), out _);
            for (int d = 0; d < 360; d++) city.Step();
            Assert.Greater(city.Population, 150);
            Assert.Greater(city.Jobs, 50);
            Assert.AreEqual(2, city.Year);
        }

        [Test]
        public void UnservedZonesStayEmpty()
        {
            var city = new City(20, 20);
            city.PlaceMany(Structure.Residential, City.Rect(2, 2, 6, 6), out _);
            for (int d = 0; d < 200; d++) city.Step();
            Assert.AreEqual(0, city.Population);
        }

        [Test]
        public void BudgetTaxesThePopulationEveryMonth()
        {
            var city = Flat();
            city.Place(Structure.PowerPlant, 16, 10);
            city.PlaceMany(Structure.Residential, City.Rect(1, 11, 14, 12), out _);
            for (int d = 0; d < 120; d++) city.Step();
            int months = 0;
            city.MonthPassed += () => months++;
            long before = city.Money;
            for (int d = 0; d < City.DaysPerMonth; d++) city.Step();
            Assert.AreEqual(1, months);
            Assert.AreEqual(before + city.LastIncome - city.LastExpenses, city.Money);
            Assert.Greater(city.LastIncome, 0);
        }

        [Test]
        public void SaveRoundTripKeepsTheCity()
        {
            var city = CityGenerator.Create(32, 32, 99);
            city.Place(Structure.PowerPlant, 3, 3);
            city.Place(Structure.School, 8, 3);
            for (int d = 0; d < 45; d++) city.Step();
            var copy = CitySave.From(city).ToCity();
            CollectionAssert.AreEqual(city.Structure, copy.Structure);
            CollectionAssert.AreEqual(city.Terrain, copy.Terrain);
            CollectionAssert.AreEqual(city.Level, copy.Level);
            CollectionAssert.AreEqual(city.Anchor, copy.Anchor);
            Assert.AreEqual(city.Money, copy.Money);
            Assert.AreEqual(city.Day, copy.Day);
            Assert.AreEqual(city.Population, copy.Population);
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
