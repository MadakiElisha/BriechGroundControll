using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MissionPlanner.ArduPilot.Tests
{
    [TestClass()]
    public class OnboardMonitorTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 23, 12, 0, 0);

        [TestMethod()]
        public void NamesComponentsFromHeartbeat()
        {
            var m = new OnboardMonitor { SysId = 1 };
            m.IngestHeartbeat(1, 1, 1, 3, T0);       // fixed wing, ArduPilot
            m.IngestHeartbeat(1, 191, 18, 8, T0);    // onboard controller
            m.IngestHeartbeat(1, 100, 30, 8, T0);    // camera
            m.IngestHeartbeat(1, 236, 34, 8, T0);    // Remote ID
            m.IngestHeartbeat(2, 1, 1, 3, T0);       // another vehicle: ignored
            var s = m.GetSnapshot(T0.AddSeconds(1));
            CollectionAssert.AreEqual(new[] { "Autopilot", "Camera", "Companion computer", "Remote ID" },
                s.Components.Select(c => c.component.Name).ToArray());
            Assert.AreEqual((1u, (byte)191), s.Companion);
        }

        [TestMethod()]
        public void HeartbeatAgeGivesState()
        {
            var m = new OnboardMonitor();
            m.IngestHeartbeat(1, 191, 18, 8, T0);
            Assert.AreEqual(OnboardMonitor.HeartbeatState.Ok, m.GetSnapshot(T0.AddSeconds(1)).Components[0].state);
            Assert.AreEqual(OnboardMonitor.HeartbeatState.Late, m.GetSnapshot(T0.AddSeconds(3)).Components[0].state);
            Assert.AreEqual(OnboardMonitor.HeartbeatState.Lost, m.GetSnapshot(T0.AddSeconds(6)).Components[0].state);
        }

        [TestMethod()]
        public void ComputerStatusIgnoresUnusedSlots()
        {
            var m = new OnboardMonitor();
            var cpu = Enumerable.Repeat(byte.MaxValue, 8).ToArray();
            cpu[0] = 40; cpu[1] = 60; cpu[2] = 50; cpu[3] = 30;
            var temp = Enumerable.Repeat(sbyte.MaxValue, 8).ToArray();
            temp[0] = 71; temp[1] = 78;
            m.IngestComputerStatus(1, 191, 3_600_000, cpu, temp, sbyte.MaxValue, 2150, 8192,
                new uint[] { 30000, uint.MaxValue, 0, 0 }, new uint[] { 60000, uint.MaxValue, 0, 0 }, T0);

            var s = m.GetSnapshot(T0.AddSeconds(1));
            Assert.IsTrue(s.ComputerFresh);
            var c = s.Computer;
            Assert.AreEqual(4, c.CpuCores);
            Assert.AreEqual(45, c.CpuLoadPct.Value, 1e-9);
            Assert.AreEqual(60, c.CpuMaxPct.Value, 1e-9);
            Assert.AreEqual(78, c.TempCoreC);
            Assert.IsNull(c.TempBoardC);
            Assert.AreEqual(26.2, c.RamPct.Value, 0.1);
            Assert.AreEqual(50, c.StoragePct.Value, 1e-9);
            Assert.AreEqual(TimeSpan.FromHours(1), c.Uptime);
            // status alone marks it as the companion
            Assert.AreEqual("Companion computer", s.Components.Single().component.Name);
            Assert.IsFalse(m.GetSnapshot(T0.AddSeconds(10)).ComputerFresh);
        }

        [TestMethod()]
        public void NamedValuesKeepLatestPerName()
        {
            var m = new OnboardMonitor();
            m.IngestNamedValue(1, 191, "det_count", 3, true, T0);
            m.IngestNamedValue(1, 191, "det_count", 14, true, T0.AddSeconds(1));
            m.IngestNamedValue(1, 191, "infer_fps", 23.456, false, T0);
            var v = m.GetSnapshot(T0.AddSeconds(2)).Values;
            Assert.AreEqual(2, v.Count);
            Assert.AreEqual("14", OnboardMonitor.FormatValue(v.First(x => x.Name == "det_count")));
            Assert.AreEqual("23.46", OnboardMonitor.FormatValue(v.First(x => x.Name == "infer_fps")));
        }

        [TestMethod()]
        public void MessagesSkipAutopilotAndAreCapped()
        {
            var m = new OnboardMonitor();
            m.IngestText(1, 1, 6, "from the autopilot", T0);
            for (int i = 0; i < OnboardMonitor.MaxMessages + 5; i++)
                m.IngestText(1, 191, 6, "msg " + i, T0.AddSeconds(i));
            var msgs = m.GetSnapshot(T0.AddMinutes(5)).Messages;
            Assert.AreEqual(OnboardMonitor.MaxMessages, msgs.Count);
            Assert.AreEqual("msg " + (OnboardMonitor.MaxMessages + 4), msgs[0].Text, "newest first");
            Assert.IsFalse(msgs.Any(x => x.CompId == 1));
        }
    }
}
