using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MissionPlanner.ArduPilot.Tests
{
    [TestClass()]
    public class ParamAliasesTests
    {
        private static Func<string, double?> Vehicle(params (string name, double value)[] p)
        {
            var d = p.ToDictionary(x => x.name, x => x.value);
            return n => d.TryGetValue(n, out var v) ? v : (double?)null;
        }

        [TestMethod()]
        public void TableHasNoDuplicateOrChainedNames()
        {
            var current = ParamAliases.All.Select(a => a.Current).ToList();
            var old = ParamAliases.All.Select(a => a.Old).ToList();
            CollectionAssert.AllItemsAreUnique(current);
            CollectionAssert.AllItemsAreUnique(old);
            Assert.AreEqual(0, current.Intersect(old).Count(), "a name is both current and old");
            Assert.IsTrue(ParamAliases.All.All(a => a.Scale > 0));
        }

        [TestMethod()]
        public void Copter46_ReadsWpnavSpeedInMetresPerSecond()
        {
            var get = Vehicle(("WPNAV_SPEED", 1000));
            Assert.IsTrue(ParamAliases.TryGet(get, "WP_SPD", out var v, out var name));
            Assert.AreEqual(10.0, v, 1e-9);
            Assert.AreEqual("WPNAV_SPEED", name);
            // asking by the old name gives the same answer
            Assert.IsTrue(ParamAliases.TryGet(get, "WPNAV_SPEED", out v));
            Assert.AreEqual(10.0, v, 1e-9);
        }

        [TestMethod()]
        public void Copter47_ReadsWpSpdDirectly()
        {
            var get = Vehicle(("WP_SPD", 12.5));
            Assert.IsTrue(ParamAliases.TryGet(get, "WPNAV_SPEED", out var v, out var name));
            Assert.AreEqual(12.5, v, 1e-9);
            Assert.AreEqual("WP_SPD", name);
        }

        [TestMethod()]
        public void WritesConvertBackToVehicleUnits()
        {
            Assert.AreEqual(1500.0, ParamAliases.ToVehicleUnits("WPNAV_SPEED", 15), 1e-9);
            Assert.AreEqual(15.0, ParamAliases.ToVehicleUnits("WP_SPD", 15), 1e-9);
            Assert.AreEqual(4500.0, ParamAliases.ToVehicleUnits("ANGLE_MAX", 45), 1e-9);
            Assert.AreEqual(3.0, ParamAliases.ToVehicleUnits("SYSID_THISMAV", 3), 1e-9);
            Assert.AreEqual(7.0, ParamAliases.ToVehicleUnits("NOT_A_RENAMED_PARAM", 7), 1e-9);
        }

        [TestMethod()]
        public void NamesAndResolve()
        {
            Assert.AreEqual("MAV_SYSID", ParamAliases.CurrentName("SYSID_THISMAV"));
            Assert.AreEqual("FOO", ParamAliases.CurrentName("FOO"));
            CollectionAssert.AreEqual(new[] { "RTL_ALT_M", "RTL_ALT" }, ParamAliases.Names("RTL_ALT"));

            var has = new HashSet<string> { "RTL_ALT" };
            Assert.AreEqual("RTL_ALT", ParamAliases.Resolve(has.Contains, "RTL_ALT_M"));
            Assert.IsNull(ParamAliases.Resolve(has.Contains, "WP_SPD"));
        }

        [TestMethod()]
        public void SpeedSetpoint_Copter()
        {
            var kind = ParamAliases.GetSpeedSetpoint(Vehicle(("WPNAV_SPEED", 800), ("TRIM_THROTTLE", 0)), out var v, out var name);
            Assert.AreEqual(ParamAliases.SpeedKind.MetresPerSecond, kind);
            Assert.AreEqual(8.0, v, 1e-9);
            Assert.AreEqual("WPNAV_SPEED", name);

            ParamAliases.ChangeSpeedArguments(Vehicle(("WP_SPD", 8)), 9, out var p1, out var p2, out var p3);
            Assert.AreEqual(1f, p1); // ground speed
            Assert.AreEqual(9f, p2);
            Assert.AreEqual(-1f, p3);
        }

        [TestMethod()]
        public void SpeedSetpoint_PlaneWithAirspeed()
        {
            var get = Vehicle(("AIRSPEED_CRUISE", 22), ("ARSPD_USE", 1), ("ARSPD_TYPE", 2), ("TRIM_THROTTLE", 45));
            Assert.AreEqual(ParamAliases.SpeedKind.MetresPerSecond, ParamAliases.GetSpeedSetpoint(get, out var v, out _));
            Assert.AreEqual(22.0, v, 1e-9);

            ParamAliases.ChangeSpeedArguments(get, 25, out var p1, out var p2, out var p3);
            Assert.AreEqual(0f, p1); // airspeed
            Assert.AreEqual(25f, p2);
            Assert.AreEqual(-1f, p3);
        }

        [TestMethod()]
        public void SpeedSetpoint_OldPlaneWithAirspeed()
        {
            var get = Vehicle(("TRIM_ARSPD_CM", 1800), ("ARSPD_USE", 1), ("ARSPD_ENABLE", 1), ("TRIM_THROTTLE", 45));
            Assert.AreEqual(ParamAliases.SpeedKind.MetresPerSecond, ParamAliases.GetSpeedSetpoint(get, out var v, out _));
            Assert.AreEqual(18.0, v, 1e-9);
        }

        [TestMethod()]
        public void SpeedSetpoint_PlaneWithoutAirspeedUsesThrottle()
        {
            var get = Vehicle(("AIRSPEED_CRUISE", 22), ("ARSPD_USE", 0), ("ARSPD_TYPE", 0), ("TRIM_THROTTLE", 45));
            Assert.AreEqual(ParamAliases.SpeedKind.ThrottlePercent, ParamAliases.GetSpeedSetpoint(get, out var v, out _));
            Assert.AreEqual(45.0, v, 1e-9);

            ParamAliases.ChangeSpeedArguments(get, 50, out var p1, out var p2, out var p3);
            Assert.AreEqual(0f, p1);
            Assert.AreEqual(-1f, p2); // leave airspeed alone
            Assert.AreEqual(50f, p3); // throttle %
        }

        [TestMethod()]
        public void SpeedSetpoint_Rover()
        {
            var get = Vehicle(("WP_SPEED", 2), ("CRUISE_SPEED", 2));
            Assert.AreEqual(ParamAliases.SpeedKind.MetresPerSecond, ParamAliases.GetSpeedSetpoint(get, out var v, out var name));
            Assert.AreEqual(2.0, v, 1e-9);
            Assert.AreEqual("WP_SPEED", name);
        }

        [TestMethod()]
        public void SpeedSetpoint_NothingConnected()
        {
            Assert.AreEqual(ParamAliases.SpeedKind.None, ParamAliases.GetSpeedSetpoint(Vehicle(), out _, out _));
            Assert.AreEqual(ParamAliases.SpeedKind.None, ParamAliases.GetSpeedSetpoint((Func<string, double?>)null, out _, out _));
        }

        [TestMethod()]
        public void WorksWithVehicleParamList()
        {
            var list = new MAVLink.MAVLinkParamList();
            list.Add(new MAVLink.MAVLinkParam("LOIT_SPEED", 1250, MAVLink.MAV_PARAM_TYPE.REAL32));
            Assert.IsTrue(ParamAliases.TryGet(list, "LOIT_SPEED_MS", out var v, out var name));
            Assert.AreEqual(12.5, v, 1e-4);
            Assert.AreEqual("LOIT_SPEED", name);
            Assert.IsFalse(ParamAliases.TryGet(list, "WP_SPD", out _));
        }
    }
}
