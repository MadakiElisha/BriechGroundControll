using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using Family = MissionPlanner.ArduPilot.FlightCommandProfile.Family;
using TakeoffMethod = MissionPlanner.ArduPilot.FlightCommandProfile.TakeoffMethod;

namespace MissionPlanner.ArduPilot.Tests
{
    [TestClass()]
    public class FlightCommandProfileTests
    {
        [TestMethod()]
        public void ClassifiesFleet()
        {
            Assert.AreEqual(Family.Multirotor, FlightCommandProfile.Classify(MAVLink.MAV_TYPE.QUADROTOR, false));
            Assert.AreEqual(Family.Multirotor, FlightCommandProfile.Classify(MAVLink.MAV_TYPE.HEXAROTOR, false));
            Assert.AreEqual(Family.Helicopter, FlightCommandProfile.Classify(MAVLink.MAV_TYPE.HELICOPTER, false));
            Assert.AreEqual(Family.FixedWing, FlightCommandProfile.Classify(MAVLink.MAV_TYPE.FIXED_WING, false));
            Assert.AreEqual(Family.QuadPlane, FlightCommandProfile.Classify(MAVLink.MAV_TYPE.FIXED_WING, true));
            Assert.AreEqual(Family.QuadPlane, FlightCommandProfile.Classify(MAVLink.MAV_TYPE.VTOL_QUADROTOR, false));
            Assert.AreEqual(Family.Rover, FlightCommandProfile.Classify(MAVLink.MAV_TYPE.GROUND_ROVER, false));
            Assert.AreEqual(Family.Boat, FlightCommandProfile.Classify(MAVLink.MAV_TYPE.SURFACE_BOAT, false));
            Assert.AreEqual(Family.Sub, FlightCommandProfile.Classify(MAVLink.MAV_TYPE.SUBMARINE, false));
            Assert.AreEqual(Family.Unknown, FlightCommandProfile.Classify(MAVLink.MAV_TYPE.GCS, false));
        }

        [TestMethod()]
        public void PlaneHasNoLandMode()
        {
            Assert.IsNull(FlightCommandProfile.LandMode(Family.FixedWing));
            Assert.AreEqual("QLAND", FlightCommandProfile.LandMode(Family.QuadPlane));
            Assert.AreEqual("LAND", FlightCommandProfile.LandMode(Family.Multirotor));
            Assert.AreEqual("HOLD", FlightCommandProfile.LandMode(Family.Rover));
            Assert.AreEqual("SURFACE", FlightCommandProfile.LandMode(Family.Sub));
            Assert.AreEqual("RTL", FlightCommandProfile.ReturnMode(Family.QuadPlane));
            Assert.AreEqual("SURFACE", FlightCommandProfile.ReturnMode(Family.Sub));
        }

        [TestMethod()]
        public void FlyModesPerVehicle()
        {
            foreach (Family f in System.Enum.GetValues(typeof(Family)))
            {
                var modes = FlightCommandProfile.FlyModes(f);
                if (f == Family.Unknown)
                {
                    Assert.AreEqual(0, modes.Count);
                    continue;
                }

                Assert.AreEqual(6, modes.Count, f.ToString());
                Assert.IsTrue(modes.Count(m => m.Danger) <= 1, f + " has at most one danger mode");
            }

            Assert.IsFalse(FlightCommandProfile.FlyModes(Family.FixedWing).Any(m => m.Mode == "LAND"), "ArduPlane has no LAND mode");
            Assert.IsTrue(FlightCommandProfile.FlyModes(Family.QuadPlane).Any(m => m.Mode == "QLAND" && m.Danger));
            Assert.AreEqual("BRAKE", FlightCommandProfile.PauseMode(Family.Multirotor));
            Assert.AreEqual("LOITER", FlightCommandProfile.PauseMode(Family.QuadPlane));
            Assert.AreEqual("HOLD", FlightCommandProfile.PauseMode(Family.Boat));
        }

        [TestMethod()]
        public void TakeoffPerVehicle()
        {
            Assert.AreEqual(TakeoffMethod.GuidedNavTakeoff, FlightCommandProfile.Takeoff(Family.Multirotor));
            Assert.AreEqual(TakeoffMethod.GuidedNavTakeoff, FlightCommandProfile.Takeoff(Family.QuadPlane));
            Assert.AreEqual(TakeoffMethod.PlaneTakeoffMode, FlightCommandProfile.Takeoff(Family.FixedWing));
            Assert.AreEqual(TakeoffMethod.None, FlightCommandProfile.Takeoff(Family.Boat));
        }

        [TestMethod()]
        public void Airborne()
        {
            Assert.IsFalse(FlightCommandProfile.LooksAirborne(false, 2, 100, 20), "disarmed is never airborne");
            Assert.IsTrue(FlightCommandProfile.LooksAirborne(true, 2, 0, 0), "landed_state in air wins");
            Assert.IsFalse(FlightCommandProfile.LooksAirborne(true, 1, 30, 0), "landed_state on ground wins");
            Assert.IsTrue(FlightCommandProfile.LooksAirborne(true, 0, 10, 0), "no landed_state: height");
            Assert.IsTrue(FlightCommandProfile.LooksAirborne(true, 0, 0, 15), "no landed_state: speed");
            Assert.IsFalse(FlightCommandProfile.LooksAirborne(true, 0, 0.5, 0.2), "armed on the pad");
        }

        [TestMethod()]
        public void ResumeKeepsTakeoffAndDoCommandsOnly()
        {
            var ids = new List<ushort>
            {
                16,  // 0 home
                84,  // 1 VTOL_TAKEOFF
                178, // 2 DO_CHANGE_SPEED
                16,  // 3 WAYPOINT (flown)
                177, // 4 DO_JUMP
                112, // 5 CONDITION_DELAY
                206, // 6 DO_SET_CAM_TRIGG_DIST
                189, // 7 DO_LAND_START
                16,  // 8 WAYPOINT <- resume here
                21,  // 9 LAND
            };

            var keep = FlightCommandProfile.ResumeMissionItems(ids, 8);
            CollectionAssert.AreEqual(new List<int> { 0, 1, 2, 6, 8, 9 }, keep);
        }

        [TestMethod()]
        public void ResumeAtFirstItemKeepsEverything()
        {
            var ids = new List<ushort> { 16, 22, 16, 16 };
            CollectionAssert.AreEqual(new List<int> { 0, 1, 2, 3 }, FlightCommandProfile.ResumeMissionItems(ids, 1));
        }
    }
}
