using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MissionPlanner.ArduPilot.Tests
{
    [TestClass()]
    public class EngineStatusTests
    {
        [TestMethod()]
        public void NoDataIsUnknown()
        {
            var r = EngineStatus.Evaluate(new EngineStatus.EngineInputs(), null);
            Assert.AreEqual(EngineStatus.Severity.Unknown, r.Severity);
            Assert.IsNull(r.Rpm);
        }

        [TestMethod()]
        public void EfiWithinLimitsIsOk()
        {
            var r = EngineStatus.Evaluate(new EngineStatus.EngineInputs
            {
                EfiFresh = true, EfiHealth = 1, EfiRpm = 5200, CylinderHeadTempC = 140, ExhaustGasTempC = 600
            }, new EngineStatus.EngineLimits { ChtCautionC = 180, ChtMaxC = 220 });
            Assert.AreEqual(EngineStatus.Severity.Ok, r.Severity);
            Assert.AreEqual(5200, r.Rpm.Value, 1e-9);
            Assert.IsTrue(r.FromEfi);
        }

        [TestMethod()]
        public void ChtCautionThenLimit()
        {
            var limits = new EngineStatus.EngineLimits { ChtCautionC = 180, ChtMaxC = 220 };
            var hot = EngineStatus.Evaluate(new EngineStatus.EngineInputs
                { EfiFresh = true, EfiHealth = 1, EfiRpm = 6000, CylinderHeadTempC = 190 }, limits);
            Assert.AreEqual(EngineStatus.Severity.Caution, hot.Severity);
            Assert.AreEqual("CHT high", hot.Reason);

            var limit = EngineStatus.Evaluate(new EngineStatus.EngineInputs
                { EfiFresh = true, EfiHealth = 1, EfiRpm = 6000, CylinderHeadTempC = 225 }, limits);
            Assert.AreEqual(EngineStatus.Severity.Warning, limit.Severity);
        }

        [TestMethod()]
        public void UnhealthyEfiIsCaution()
        {
            var r = EngineStatus.Evaluate(new EngineStatus.EngineInputs { EfiFresh = true, EfiHealth = 0, EfiRpm = 3000 }, null);
            Assert.AreEqual(EngineStatus.Severity.Caution, r.Severity);
        }

        [TestMethod()]
        public void StaleEfiFallsBackToRpmSensor()
        {
            var r = EngineStatus.Evaluate(new EngineStatus.EngineInputs
                { EfiFresh = false, EfiRpm = 9999, CylinderHeadTempC = 300, SensorRpm = 4800 }, new EngineStatus.EngineLimits { ChtMaxC = 200 });
            Assert.AreEqual(4800, r.Rpm.Value, 1e-9);
            Assert.IsFalse(r.FromEfi);
            Assert.IsNull(r.ChtC, "stale EFI temperatures are not shown");
            Assert.AreEqual(EngineStatus.Severity.Ok, r.Severity);
        }

        [TestMethod()]
        public void FuelFromBatteryMonitorWithEndurance()
        {
            var r = EngineStatus.EvaluateFuel(new EngineStatus.FuelInputs
            {
                Source = EngineStatus.FuelSource.BatteryMonitor,
                BatteryRemainingPct = 50,
                TankMl = 4000,
                EfiFresh = true,
                EfiFuelFlowCm3PerMin = 40
            });
            Assert.AreEqual(50, r.RemainingPct.Value, 1e-9);
            Assert.AreEqual(2000, r.RemainingMl.Value, 1e-9);
            Assert.AreEqual(50, r.EnduranceMin.Value, 1e-9);
            Assert.AreEqual(EngineStatus.Severity.Ok, r.Severity);
        }

        [TestMethod()]
        public void FuelFromEfiConsumed()
        {
            var r = EngineStatus.EvaluateFuel(new EngineStatus.FuelInputs
            {
                Source = EngineStatus.FuelSource.EfiConsumed,
                TankMl = 3000,
                EfiFresh = true,
                EfiFuelConsumedCm3 = 2700
            });
            Assert.AreEqual(10, r.RemainingPct.Value, 1e-9);
            Assert.AreEqual(EngineStatus.Severity.Warning, r.Severity);
        }

        [TestMethod()]
        public void FuelUnknownWithoutSource()
        {
            Assert.AreEqual(EngineStatus.Severity.Unknown,
                EngineStatus.EvaluateFuel(new EngineStatus.FuelInputs { Source = EngineStatus.FuelSource.EfiConsumed }).Severity);
            Assert.AreEqual(EngineStatus.Severity.Unknown,
                EngineStatus.EvaluateFuel(new EngineStatus.FuelInputs { Source = EngineStatus.FuelSource.BatteryMonitor }).Severity);
        }
    }
}
