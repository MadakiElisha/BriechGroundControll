using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.ArduPilot;
using MissionPlanner.Comms;
using System;
using System.Diagnostics;
using System.Threading;

namespace MissionPlanner.Sitl.Tests
{
    /// <summary>
    /// End-to-end smoke test against a running ArduPlane SITL with the quadplane model.
    /// CI starts the simulator at the Briech helipad and sets BRIECH_SITL_TCP=127.0.0.1:5760; without it the
    /// test is skipped (inconclusive), so it never runs by accident on a developer machine.
    /// </summary>
    [TestClass()]
    public class QuadPlaneSitlSmokeTests
    {
        private static bool TryGetTarget(out string host, out string port)
        {
            host = port = null;
            var target = Environment.GetEnvironmentVariable("BRIECH_SITL_TCP");
            if (string.IsNullOrWhiteSpace(target) || !target.Contains(":"))
                return false;
            var i = target.LastIndexOf(':');
            host = target.Substring(0, i);
            port = target.Substring(i + 1);
            return true;
        }

        [TestMethod()]
        [TestCategory("SITL")]
        [Timeout(300000)]
        public void QuadPlaneConnectsWithEveryParameterAndIsRecognised()
        {
            if (!TryGetTarget(out var host, out var port))
                Assert.Inconclusive("Set BRIECH_SITL_TCP=host:port to run against a quadplane SITL.");

            using (var mav = new MAVLinkInterface())
            {
                mav.BaseStream = new TcpSerial { Host = host, Port = port };

                // SITL can take a while to start listening on a cold CI machine
                var sw = Stopwatch.StartNew();
                while (true)
                {
                    try
                    {
                        mav.Open(false, false, false);
                        if (mav.BaseStream.IsOpen && mav.MAV.sysid != 0)
                            break;
                    }
                    catch (Exception ex) when (sw.Elapsed < TimeSpan.FromSeconds(90))
                    {
                        Console.WriteLine("waiting for SITL: " + ex.Message);
                    }

                    if (sw.Elapsed > TimeSpan.FromSeconds(90))
                        Assert.Fail("No heartbeat from SITL at " + host + ":" + port);
                    try { mav.BaseStream.Close(); } catch { }
                    mav.BaseStream = new TcpSerial { Host = host, Port = port };
                    Thread.Sleep(3000);
                }

                Assert.AreEqual(MAVLink.MAV_AUTOPILOT.ARDUPILOTMEGA, mav.MAV.apname, "autopilot");

                // full list over the classic protocol: exercises the missing-index recovery
                var list = mav.getParamList(mav.MAV.sysid, mav.MAV.compid);
                Console.WriteLine($"params: {mav.MAV.param.Count} of {mav.MAV.param.TotalReported}");
                Assert.IsTrue(mav.MAV.param.Count > 500, "parameter count " + mav.MAV.param.Count);
                Assert.AreEqual(mav.MAV.param.TotalReported, mav.MAV.param.Count, "every parameter received");

                Assert.IsTrue(mav.MAV.param.ContainsKey("Q_ENABLE"), "Q_ENABLE present");
                Assert.AreEqual(1.0, mav.MAV.param["Q_ENABLE"].Value, 1e-6, "quadplane enabled");

                var family = FlightCommandProfile.Classify(mav.MAV.aptype, mav.MAV.param["Q_ENABLE"].Value > 0.5);
                Assert.AreEqual(FlightCommandProfile.Family.QuadPlane, family, "aptype " + mav.MAV.aptype);
                Assert.AreEqual("QLAND", FlightCommandProfile.LandMode(family));
                Assert.AreEqual(FlightCommandProfile.TakeoffMethod.GuidedNavTakeoff, FlightCommandProfile.Takeoff(family));

                // speed box resolves on this firmware (AIRSPEED_CRUISE or TRIM_THROTTLE)
                var kind = ParamAliases.GetSpeedSetpoint(mav.MAV.param, out var speed, out var name);
                Console.WriteLine($"speed setpoint {kind} {speed} from {name}");
                Assert.AreNotEqual(ParamAliases.SpeedKind.None, kind);

                // a fresh SITL sits disarmed on the helipad
                Assert.IsFalse(FlightCommandProfile.LooksAirborne(mav.MAV.cs.armed, mav.MAV.cs.landed_state, 0, 0));

                mav.BaseStream.Close();
            }
        }
    }
}
