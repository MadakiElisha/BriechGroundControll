using System;

namespace MissionPlanner.ArduPilot
{
    /// <summary>
    /// Engine and fuel read-outs for petrol aircraft (DLE two- and four-stroke with EFI). Pure logic so the
    /// thresholds and fuel arithmetic can be unit tested; the Fly view only formats and colours the result.
    /// Units follow MAVLink EFI_STATUS: temperatures in degC, fuel flow in cm3/min, fuel consumed in cm3.
    /// </summary>
    public static class EngineStatus
    {
        public enum Severity
        {
            /// <summary>no engine data</summary>
            Unknown,
            Ok,
            Caution,
            Warning,
        }

        public sealed class EngineInputs
        {
            /// <summary>an EFI_STATUS arrived recently (a few seconds)</summary>
            public bool EfiFresh;

            /// <summary>EFI_STATUS.health (1 = healthy)</summary>
            public byte EfiHealth;

            public double EfiRpm;
            public double CylinderHeadTempC;
            public double ExhaustGasTempC;

            /// <summary>RPM sensor (RPM1_TYPE) for engines without EFI; 0 or negative when absent</summary>
            public double SensorRpm;
        }

        /// <summary>User limits; null disables that check. DLE manuals differ by model, so nothing is assumed.</summary>
        public sealed class EngineLimits
        {
            public double? ChtCautionC;
            public double? ChtMaxC;
            public double? EgtCautionC;
            public double? EgtMaxC;
            public double? RpmMax;
        }

        public sealed class EngineReadout
        {
            public double? Rpm;
            public double? ChtC;
            public double? EgtC;
            public bool FromEfi;
            public Severity Severity;
            public string Reason;
        }

        public static EngineReadout Evaluate(EngineInputs input, EngineLimits limits)
        {
            var r = new EngineReadout { Severity = Severity.Unknown };
            if (input == null)
                return r;
            limits = limits ?? new EngineLimits();

            if (input.EfiFresh)
            {
                r.FromEfi = true;
                r.Rpm = input.EfiRpm;
                if (input.CylinderHeadTempC > 0)
                    r.ChtC = input.CylinderHeadTempC;
                if (input.ExhaustGasTempC > 0)
                    r.EgtC = input.ExhaustGasTempC;
                r.Severity = Severity.Ok;

                if (input.EfiHealth != 1)
                    Raise(r, Severity.Caution, "EFI reports unhealthy");
            }
            else if (input.SensorRpm > 0)
            {
                r.Rpm = input.SensorRpm;
                r.Severity = Severity.Ok;
            }
            else
            {
                return r;
            }

            Check(r, r.ChtC, limits.ChtCautionC, limits.ChtMaxC, "CHT");
            Check(r, r.EgtC, limits.EgtCautionC, limits.EgtMaxC, "EGT");
            if (r.Rpm.HasValue && limits.RpmMax.HasValue && r.Rpm.Value > limits.RpmMax.Value)
                Raise(r, Severity.Warning, "RPM over limit");

            return r;
        }

        private static void Check(EngineReadout r, double? value, double? caution, double? max, string what)
        {
            if (!value.HasValue)
                return;
            if (max.HasValue && value.Value >= max.Value)
                Raise(r, Severity.Warning, what + " at limit");
            else if (caution.HasValue && value.Value >= caution.Value)
                Raise(r, Severity.Caution, what + " high");
        }

        private static void Raise(EngineReadout r, Severity s, string reason)
        {
            if (s > r.Severity)
            {
                r.Severity = s;
                r.Reason = reason;
            }
            else if (s == r.Severity && r.Reason == null)
            {
                r.Reason = reason;
            }
        }

        public enum FuelSource
        {
            /// <summary>a battery monitor instance set up to report fuel as remaining % (current Briech setup)</summary>
            BatteryMonitor,

            /// <summary>tank capacity minus EFI fuel consumed</summary>
            EfiConsumed,
        }

        public sealed class FuelInputs
        {
            public FuelSource Source;

            /// <summary>remaining % from the configured battery monitor instance; null when not reporting</summary>
            public int? BatteryRemainingPct;

            /// <summary>tank capacity in millilitres (= cm3); needed for EfiConsumed and for endurance</summary>
            public double? TankMl;

            public bool EfiFresh;
            public double EfiFuelConsumedCm3;
            public double EfiFuelFlowCm3PerMin;
        }

        public sealed class FuelReadout
        {
            public double? RemainingPct;
            public double? RemainingMl;

            /// <summary>minutes left at the current EFI fuel flow</summary>
            public double? EnduranceMin;

            public Severity Severity;
        }

        public static FuelReadout EvaluateFuel(FuelInputs input, double cautionPct = 30, double warningPct = 15)
        {
            var r = new FuelReadout { Severity = Severity.Unknown };
            if (input == null)
                return r;

            double? tank = input.TankMl.HasValue && input.TankMl.Value > 0 ? input.TankMl : null;

            if (input.Source == FuelSource.EfiConsumed)
            {
                if (!tank.HasValue || !input.EfiFresh)
                    return r;
                var left = Math.Max(0, tank.Value - Math.Max(0, input.EfiFuelConsumedCm3));
                r.RemainingMl = left;
                r.RemainingPct = 100.0 * left / tank.Value;
            }
            else
            {
                if (!input.BatteryRemainingPct.HasValue || input.BatteryRemainingPct.Value < 0)
                    return r;
                r.RemainingPct = Math.Min(100, input.BatteryRemainingPct.Value);
                if (tank.HasValue)
                    r.RemainingMl = tank.Value * r.RemainingPct.Value / 100.0;
            }

            if (r.RemainingMl.HasValue && input.EfiFresh && input.EfiFuelFlowCm3PerMin > 0.1)
                r.EnduranceMin = r.RemainingMl.Value / input.EfiFuelFlowCm3PerMin;

            r.Severity = r.RemainingPct <= warningPct
                ? Severity.Warning
                : r.RemainingPct <= cautionPct
                    ? Severity.Caution
                    : Severity.Ok;
            return r;
        }
    }
}
