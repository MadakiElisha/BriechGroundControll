using System;
using System.Globalization;
using System.Windows.Forms;
using MissionPlanner.ArduPilot;
using MissionPlanner.Controls;

namespace MissionPlanner.Utilities
{
    /// <summary>
    /// Glue between telemetry/settings and <see cref="EngineStatus"/> for the Fly views.
    /// Limits and the fuel source are per-PC settings, edited from the status-card menu.
    /// </summary>
    public static class EngineDisplay
    {
        public const string KeyFuelSource = "engine_fuel_source";        // "battery" | "efi"
        public const string KeyFuelBattery = "engine_fuel_battery";      // battery monitor instance 1..9
        public const string KeyTankMl = "engine_tank_ml";
        public const string KeyChtCaution = "engine_cht_caution_c";
        public const string KeyChtMax = "engine_cht_max_c";
        public const string KeyEgtCaution = "engine_egt_caution_c";
        public const string KeyEgtMax = "engine_egt_max_c";
        public const string KeyRpmMax = "engine_rpm_max";

        private static readonly TimeSpan EfiStale = TimeSpan.FromSeconds(3);

        private static double? Optional(string key)
        {
            var raw = Settings.Instance[key];
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0
                ? v
                : (double?)null;
        }

        private static bool EfiFresh(CurrentState cs)
        {
            try
            {
                var msg = cs?.parent?.getPacketLast((uint)MAVLink.MAVLINK_MSG_ID.EFI_STATUS);
                return msg != null && DateTime.Now - msg.rxtime < EfiStale;
            }
            catch
            {
                return false;
            }
        }

        public static EngineStatus.EngineReadout Engine(CurrentState cs)
        {
            if (cs == null)
                return EngineStatus.Evaluate(null, null);

            var input = new EngineStatus.EngineInputs
            {
                EfiFresh = EfiFresh(cs),
                EfiHealth = cs.efi_health,
                EfiRpm = cs.efi_rpm,
                CylinderHeadTempC = cs.efi_headtemp,
                ExhaustGasTempC = cs.efi_exhasttemp,
                SensorRpm = cs.rpm1
            };
            var limits = new EngineStatus.EngineLimits
            {
                ChtCautionC = Optional(KeyChtCaution),
                ChtMaxC = Optional(KeyChtMax),
                EgtCautionC = Optional(KeyEgtCaution),
                EgtMaxC = Optional(KeyEgtMax),
                RpmMax = Optional(KeyRpmMax)
            };
            return EngineStatus.Evaluate(input, limits);
        }

        public static EngineStatus.FuelReadout Fuel(CurrentState cs)
        {
            if (cs == null)
                return EngineStatus.EvaluateFuel(null);

            var source = string.Equals(Settings.Instance[KeyFuelSource], "efi", StringComparison.OrdinalIgnoreCase)
                ? EngineStatus.FuelSource.EfiConsumed
                : EngineStatus.FuelSource.BatteryMonitor;

            var input = new EngineStatus.FuelInputs
            {
                Source = source,
                BatteryRemainingPct = BatteryRemaining(cs, Settings.Instance.GetInt32(KeyFuelBattery, 2)),
                TankMl = Optional(KeyTankMl),
                EfiFresh = EfiFresh(cs),
                EfiFuelConsumedCm3 = cs.efi_fuelconsumed,
                EfiFuelFlowCm3PerMin = cs.efi_fuelflow
            };
            return EngineStatus.EvaluateFuel(input);
        }

        private static int? BatteryRemaining(CurrentState cs, int instance)
        {
            switch (instance)
            {
                case 1: return cs.battery_remaining;
                case 2: return cs.battery_remaining2;
                case 3: return cs.battery_remaining3;
                case 4: return cs.battery_remaining4;
                case 5: return cs.battery_remaining5;
                case 6: return cs.battery_remaining6;
                case 7: return cs.battery_remaining7;
                case 8: return cs.battery_remaining8;
                case 9: return cs.battery_remaining9;
                default: return null;
            }
        }

        public static string FormatEngineDetail(EngineStatus.EngineReadout r)
        {
            if (r.Severity == EngineStatus.Severity.Unknown)
                return "No EFI or RPM sensor data";
            var parts = new System.Collections.Generic.List<string>();
            if (r.ChtC.HasValue) parts.Add($"CHT {r.ChtC:0} °C");
            if (r.EgtC.HasValue) parts.Add($"EGT {r.EgtC:0} °C");
            if (!r.FromEfi) parts.Add("RPM sensor");
            var text = string.Join("  |  ", parts);
            return r.Reason != null ? r.Reason + (text.Length > 0 ? "  |  " + text : "") : text;
        }

        public static string FormatFuelDetail(EngineStatus.FuelReadout r)
        {
            if (r.Severity == EngineStatus.Severity.Unknown)
                return "Set the fuel source in Engine & fuel settings";
            var parts = new System.Collections.Generic.List<string>();
            if (r.RemainingMl.HasValue) parts.Add($"{r.RemainingMl / 1000.0:0.00} L left");
            if (r.EnduranceMin.HasValue) parts.Add($"~{r.EnduranceMin:0} min at current flow");
            return parts.Count == 0 ? "From battery monitor" : string.Join("  |  ", parts);
        }

        /// <summary>Plain prompts for the engine/fuel settings; empty input clears a limit.</summary>
        public static void EditSettings(IWin32Window owner)
        {
            string source = Settings.Instance[KeyFuelSource] ?? "battery";
            if (InputBox.Show("Fuel source", "Fuel source: 'battery' (a battery monitor reports fuel %) or 'efi' (tank minus EFI fuel used)", ref source) != DialogResult.OK)
                return;
            source = source.Trim().ToLowerInvariant() == "efi" ? "efi" : "battery";
            Settings.Instance[KeyFuelSource] = source;

            if (source == "battery")
            {
                string inst = Settings.Instance.GetInt32(KeyFuelBattery, 2).ToString(CultureInfo.InvariantCulture);
                if (InputBox.Show("Fuel battery monitor", "Which battery monitor reports fuel (1-9)?", ref inst) != DialogResult.OK)
                    return;
                if (int.TryParse(inst, out var n) && n >= 1 && n <= 9)
                    Settings.Instance[KeyFuelBattery] = n.ToString(CultureInfo.InvariantCulture);
            }

            Prompt(KeyTankMl, "Tank capacity", "Tank capacity in millilitres (empty if unknown)");
            Prompt(KeyChtCaution, "CHT caution", "Cylinder head temperature caution, °C (empty = off)");
            Prompt(KeyChtMax, "CHT limit", "Cylinder head temperature limit, °C (empty = off)");
            Prompt(KeyEgtCaution, "EGT caution", "Exhaust gas temperature caution, °C (empty = off)");
            Prompt(KeyEgtMax, "EGT limit", "Exhaust gas temperature limit, °C (empty = off)");
            Prompt(KeyRpmMax, "RPM limit", "Maximum RPM (empty = off)");
            Settings.Instance.Save();
        }

        private static void Prompt(string key, string title, string text)
        {
            string value = Settings.Instance[key] ?? "";
            if (InputBox.Show(title, text, ref value) != DialogResult.OK)
                return;
            value = value.Trim();
            if (value.Length == 0)
            {
                Settings.Instance[key] = "";
                return;
            }

            if (double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0)
                Settings.Instance[key] = v.ToString(CultureInfo.InvariantCulture);
        }
    }
}
