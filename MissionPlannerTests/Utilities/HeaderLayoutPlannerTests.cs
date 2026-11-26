using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using P = MissionPlanner.Utilities.HeaderLayoutPlanner;

namespace MissionPlanner.Utilities.Tests
{
    [TestClass()]
    public class HeaderLayoutPlannerTests
    {
        // the real bar: Fly, Plan, Setup, Config, Simulation, Classic Fly, About; arm + mode; GPS; 5 pinned modes
        private static P.Inputs Bar(int width, bool connected = true)
        {
            return new P.Inputs
            {
                Available = width - 24,
                NavWordWidths = new List<int> { 56, 64, 72, 74, 112, 112, 76 },
                VehicleStateWanted = connected,
                VehicleMinimal = 240,
                VehicleGps = 110,
                VehiclePinned = new List<int> { 52, 44, 54, 54, 74 }
            };
        }

        [TestMethod()]
        public void EveryCommonWidthFitsWithTheLogo()
        {
            foreach (var w in new[] { 800, 900, 1024, 1152, 1280, 1366, 1440, 1600, 1920, 2560 })
            foreach (var connected in new[] { true, false })
            {
                var plan = P.Decide(Bar(w, connected));
                Assert.IsTrue(plan.Fits, $"{w}px connected={connected}: {plan}");
                Assert.IsTrue(plan.Used + 12 <= Bar(w).Available, $"{w}px: {plan}");
            }
        }

        [TestMethod()]
        public void MonitorKeepsEverythingFull()
        {
            var plan = P.Decide(Bar(1920));
            Assert.AreEqual(0, plan.Step, plan.ToString());
            Assert.AreEqual(P.LogoStyle.Full, plan.Logo);
            Assert.AreEqual(P.NavStyle.Words, plan.Nav);
            Assert.IsTrue(plan.VehicleShowsGps);
            Assert.AreEqual(5, plan.VehiclePinnedShown);
        }

        [TestMethod()]
        public void LaptopKeepsWordsAndALogo()
        {
            // 14-inch at 150% scaling (1280) and at 100% (1366)
            foreach (var w in new[] { 1280, 1366 })
            {
                var plan = P.Decide(Bar(w));
                Assert.IsTrue(plan.Nav == P.NavStyle.Words || plan.Nav == P.NavStyle.TightWords, $"{w}: {plan}");
                Assert.AreNotEqual(P.LogoStyle.Mark, plan.Logo, $"{w}: {plan}");
            }
        }

        [TestMethod()]
        public void VehicleStripGivesWayBeforeTheLogoShrinksToTheMark()
        {
            for (int w = 700; w <= 1920; w += 8)
            {
                var plan = P.Decide(Bar(w));
                if (plan.Logo == P.LogoStyle.Mark)
                    Assert.AreEqual(P.VehicleStateStyle.Hidden, plan.Vehicle, $"{w}: {plan}");
                if (plan.Vehicle != P.VehicleStateStyle.Full)
                    Assert.AreEqual(0, plan.VehiclePinnedShown, $"{w}: {plan}");
            }
        }

        [TestMethod()]
        public void WiderNeverLooksWorse()
        {
            int previous = int.MaxValue;
            for (int w = 700; w <= 2000; w += 4)
            {
                var plan = P.Decide(Bar(w));
                Assert.IsTrue(plan.Step <= previous || previous == int.MaxValue, $"{w}: step {plan.Step} after {previous}");
                previous = plan.Step;
            }
        }

        [TestMethod()]
        public void NotConnectedGivesTheStripNothing()
        {
            var plan = P.Decide(Bar(1280, false));
            Assert.AreEqual(P.VehicleStateStyle.Hidden, plan.Vehicle);
            Assert.AreEqual(0, plan.VehicleBudget);
        }

        [TestMethod()]
        public void SpareRoomGoesToGpsThenPinnedModesInOrder()
        {
            var x = Bar(1920);
            var full = P.Decide(x);
            var spare = x.Available - x.Slack - full.Used;
            Assert.IsTrue(spare >= 0);
            Assert.AreEqual(x.VehicleMinimal + x.VehicleGps + x.VehiclePinned.Sum(), full.VehicleBudget);
        }
    }
}
