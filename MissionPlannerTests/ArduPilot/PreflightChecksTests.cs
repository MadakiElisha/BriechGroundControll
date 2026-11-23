using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Family = MissionPlanner.ArduPilot.FlightCommandProfile.Family;

namespace MissionPlanner.ArduPilot.Tests
{
    [TestClass()]
    public class PreflightChecksTests
    {
        private static PreflightChecks.Inputs Good()
        {
            var p = new Dictionary<string, double>
            {
                { "THR_FAILSAFE", 1 }, { "FS_GCS_ENABL", 1 }, { "BATT_FS_LOW_ACT", 2 }, { "BATT_LOW_VOLT", 21.0 }, { "FENCE_ENABLE", 1 }
            };
            return new PreflightChecks.Inputs
            {
                Connected = true,
                Family = Family.QuadPlane,
                PrearmOk = true,
                GpsFix = 3, Hdop = 0.8, Sats = 18,
                EkfVariance = 0.1, CompassVariance = 0.1,
                BatteryPct = 98,
                ParamsReceived = 1412, ParamsTotal = 1412,
                HasAirspeedSensor = true, Airspeed = 0.5, Groundspeed = 0,
                WindMs = 4,
                Param = n => p.TryGetValue(n, out var v) ? v : (double?)null,
                HomeAltAmsl = 300,
                Terrain = (lat, lng) => 300,
                Mission = new List<PreflightChecks.Waypoint>
                {
                    new PreflightChecks.Waypoint { Command = 16 },                                           // home
                    new PreflightChecks.Waypoint { Command = 84, Lat = 8.9, Lng = 7.2, Alt = 30, Frame = 3 }, // VTOL takeoff: ignored
                    new PreflightChecks.Waypoint { Command = 16, Lat = 8.9, Lng = 7.3, Alt = 100, Frame = 3 },
                    new PreflightChecks.Waypoint { Command = 85, Lat = 8.9, Lng = 7.2, Alt = 0, Frame = 3 }   // VTOL land: ignored
                },
                RallyPoints = 1,
                Crew = new List<(string, bool)> { ("Props secure", true), ("Takeoff area clear", true) }
            };
        }

        [TestMethod()]
        public void AllGoodIsGo()
        {
            var r = PreflightChecks.Evaluate(Good());
            Assert.AreEqual(PreflightChecks.Verdict.Go, r.Verdict, string.Join("; ", r.Items.Where(i => i.Status != PreflightChecks.Status.Pass).Select(i => i.Text)));
            Assert.AreEqual(0, r.Attention);
        }

        [TestMethod()]
        public void NotConnectedIsNoGo()
        {
            var x = Good();
            x.Connected = false;
            Assert.AreEqual(PreflightChecks.Verdict.NoGo, PreflightChecks.Evaluate(x).Verdict);
        }

        [TestMethod()]
        public void UntickedCrewItemHolds()
        {
            var x = Good();
            x.Crew = new List<(string, bool)> { ("Pitot cover removed", false) };
            var r = PreflightChecks.Evaluate(x);
            Assert.AreEqual(PreflightChecks.Verdict.Hold, r.Verdict);
            Assert.AreEqual("1 item needs attention", r.Summary);
        }

        [TestMethod()]
        public void PrearmFailureIsNoGo()
        {
            var x = Good();
            x.PrearmOk = false;
            Assert.AreEqual(PreflightChecks.Verdict.NoGo, PreflightChecks.Evaluate(x).Verdict);
        }

        [TestMethod()]
        public void WeakGpsHoldsNoFixFails()
        {
            var x = Good();
            x.Hdop = 2.6;
            Assert.AreEqual(PreflightChecks.Verdict.Hold, PreflightChecks.Evaluate(x).Verdict);
            x.GpsFix = 1;
            Assert.AreEqual(PreflightChecks.Verdict.NoGo, PreflightChecks.Evaluate(x).Verdict);
        }

        [TestMethod()]
        public void AirspeedAtRestNeedsCheck()
        {
            var x = Good();
            x.Airspeed = 3.4;
            var r = PreflightChecks.Evaluate(x);
            Assert.AreEqual(PreflightChecks.Verdict.Hold, r.Verdict);
            Assert.IsTrue(r.Items.Any(i => i.Text.StartsWith("Airspeed") && i.Status == PreflightChecks.Status.Check));
            // a copter has no airspeed check at all
            x.Family = Family.Multirotor;
            Assert.IsFalse(PreflightChecks.Evaluate(x).Items.Any(i => i.Text.StartsWith("Airspeed")));
        }

        [TestMethod()]
        public void TerrainClearanceUsesFrames()
        {
            var x = Good();
            x.Terrain = (lat, lng) => 381; // 19 m below the 100 m relative waypoint
            Assert.AreEqual(19, PreflightChecks.TerrainClearance(x).Value, 1e-9);
            Assert.AreEqual(PreflightChecks.Verdict.Hold, PreflightChecks.Evaluate(x).Verdict);

            x.Mission[2].Frame = 0;  // AMSL 100 m over 381 m ground: below terrain
            Assert.AreEqual(PreflightChecks.Verdict.NoGo, PreflightChecks.Evaluate(x).Verdict);

            x.Mission[2].Frame = 10; // terrain-relative: 100 m clear
            Assert.AreEqual(100, PreflightChecks.TerrainClearance(x).Value, 1e-9);
        }

        [TestMethod()]
        public void FailsafesOffAreNamed()
        {
            var x = Good();
            x.Param = n => n == "THR_FAILSAFE" || n == "BATT_FS_LOW_ACT" ? 0 : n == "FS_GCS_ENABL" ? 1 : (double?)null;
            CollectionAssert.AreEqual(new[] { "RC", "battery" }, PreflightChecks.MissingFailsafes(x).ToArray());
        }

        [TestMethod()]
        public void DefaultCrewListFitsAirframe()
        {
            CollectionAssert.Contains(PreflightChecks.DefaultCrewList(Family.QuadPlane), "VTOL motor test done");
            CollectionAssert.DoesNotContain(PreflightChecks.DefaultCrewList(Family.Multirotor), "Pitot cover removed");
        }
    }
}
