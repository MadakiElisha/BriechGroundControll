using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MissionPlanner.ArduPilot
{
    /// <summary>
    /// The Go / No-Go preflight board (design canvas board 5). Automatic checks from the vehicle, mission and
    /// airspace checks, and the crew's own sign-off list give one verdict: GO, HOLD (something to look at) or
    /// NO-GO (something that must be fixed). Pure logic so every rule is unit tested; the Fly screen only shows it.
    /// </summary>
    public static class PreflightChecks
    {
        public enum Status
        {
            Pass,
            Check,   // look at it; the pilot may still decide to fly
            Fail,    // must be fixed before flight
            Info     // shown for context, never blocks
        }

        public enum Verdict
        {
            Go,
            Hold,
            NoGo
        }

        public enum Section
        {
            Vehicle,
            Mission,
            Crew
        }

        public class Item
        {
            public Section Section;
            public string Text;
            public Status Status;
            /// <summary>Short value for the right-hand side ("19 m"); null shows the status word.</summary>
            public string Value;
            /// <summary>What to do about it, for Check and Fail items.</summary>
            public string Hint;
        }

        public class Limits
        {
            public double MaxHdop = 2.0;
            public int MinSats = 6;
            public double MinBatteryPct = 90;
            public double MinTerrainClearanceM = 30;
            public double MaxWindMs = 10;
            public double AirspeedAtRestMs = 3;
        }

        public class Waypoint
        {
            public ushort Command;
            public double Lat, Lng, Alt;
            /// <summary>MAV_FRAME of the item (3 = relative to home, 0 = AMSL, 10 = above terrain...).</summary>
            public byte Frame;
        }

        public class Inputs
        {
            public bool Connected;
            public bool Armed;
            public FlightCommandProfile.Family Family;
            public bool PrearmOk;
            public bool PrearmReported = true;
            public int GpsFix;
            public double Hdop;
            public int Sats;
            public double EkfVariance;     // worst normalised EKF variance (0..1+)
            public double CompassVariance;
            public int BatteryPct = -1;    // -1 unknown
            public double BatteryVoltage;
            public int ParamsReceived, ParamsTotal;
            public bool HasAirspeedSensor;
            public double Airspeed, Groundspeed;
            public double WindMs;
            public Func<string, double?> Param = n => null;
            public List<Waypoint> Mission = new List<Waypoint>();
            public double HomeAltAmsl;
            /// <summary>Terrain height AMSL at a point, or null when there is no terrain data there.</summary>
            public Func<double, double, double?> Terrain = (lat, lng) => null;
            public int RallyPoints;
            public IList<(string text, bool done)> Crew = new List<(string, bool)>();
            public Limits Limits = new Limits();
        }

        public class Result
        {
            public List<Item> Items = new List<Item>();
            public Verdict Verdict;

            public int Attention => Items.Count(i => i.Status == Status.Check || i.Status == Status.Fail);

            public string Summary
            {
                get
                {
                    switch (Verdict)
                    {
                        case Verdict.Go: return "All checks pass";
                        case Verdict.NoGo:
                            var fails = Items.Count(i => i.Status == Status.Fail);
                            return fails == 1 ? "1 item must be fixed" : $"{fails} items must be fixed";
                        default:
                            return Attention == 1 ? "1 item needs attention" : $"{Attention} items need attention";
                    }
                }
            }
        }

        // MAV_CMD values that land or take off at ground level; their altitude isn't a cruise clearance
        private static readonly HashSet<ushort> GroundCommands = new HashSet<ushort>
        {
            21,  // NAV_LAND
            22,  // NAV_TAKEOFF
            84,  // NAV_VTOL_TAKEOFF
            85,  // NAV_VTOL_LAND
            189, // DO_LAND_START
            20   // NAV_RETURN_TO_LAUNCH
        };

        // NAV commands with a position (waypoints, loiters, spline)
        private static readonly HashSet<ushort> PositionCommands = new HashSet<ushort> { 16, 17, 18, 19, 31, 82 };

        public static Result Evaluate(Inputs x)
        {
            var r = new Result();
            var L = x.Limits ?? new Limits();
            bool plane = x.Family == FlightCommandProfile.Family.FixedWing || x.Family == FlightCommandProfile.Family.QuadPlane;

            void Add(Section s, string text, Status st, string value = null, string hint = null) =>
                r.Items.Add(new Item { Section = s, Text = text, Status = st, Value = value, Hint = hint });

            if (!x.Connected)
            {
                Add(Section.Vehicle, "Vehicle connected", Status.Fail, "NO LINK", "Connect to the aircraft to run the checks.");
                AddCrew(r, x);
                r.Verdict = Verdict.NoGo;
                return r;
            }

            // ---- automatic, from the vehicle ----
            if (x.PrearmReported)
                Add(Section.Vehicle, "Pre-arm checks", x.PrearmOk ? Status.Pass : Status.Fail, null,
                    x.PrearmOk ? null : "The autopilot reports a pre-arm failure: see Messages for the reason.");

            var gpsOk = x.GpsFix >= 3 && x.Hdop > 0 && x.Hdop <= L.MaxHdop && x.Sats >= L.MinSats;
            var gpsText = $"GPS: 3D fix, HDOP ≤ {L.MaxHdop.ToString("0.0", CultureInfo.InvariantCulture)}, {L.MinSats}+ satellites";
            if (x.GpsFix < 3)
                Add(Section.Vehicle, gpsText, Status.Fail, x.GpsFix <= 1 ? "NO FIX" : "2D", "Wait for a 3D fix with a clear view of the sky.");
            else
                Add(Section.Vehicle, gpsText, gpsOk ? Status.Pass : Status.Check,
                    gpsOk ? null : $"{x.Sats} sats · {x.Hdop.ToString("0.0", CultureInfo.InvariantCulture)}",
                    gpsOk ? null : "Fix is weak: wait for more satellites or a lower HDOP.");

            var ekf = Math.Max(x.EkfVariance, x.CompassVariance);
            Add(Section.Vehicle, "EKF healthy, compasses agree",
                ekf >= 0.8 ? Status.Fail : ekf >= 0.5 ? Status.Check : Status.Pass,
                ekf >= 0.5 ? ekf.ToString("0.00", CultureInfo.InvariantCulture) : null,
                ekf >= 0.5 ? "High EKF or compass variance: move away from metal, check calibration." : null);

            if (x.BatteryPct >= 0)
                Add(Section.Vehicle, $"Battery ≥ {L.MinBatteryPct:0}% at rest",
                    x.BatteryPct < 50 ? Status.Fail : x.BatteryPct < L.MinBatteryPct ? Status.Check : Status.Pass,
                    x.BatteryPct < L.MinBatteryPct ? x.BatteryPct + "%" : null,
                    x.BatteryPct < L.MinBatteryPct ? "Fit a charged battery." : null);
            else
                Add(Section.Vehicle, "Battery level", Status.Check, x.BatteryVoltage > 0 ? x.BatteryVoltage.ToString("0.0 V", CultureInfo.InvariantCulture) : "UNKNOWN",
                    "Battery percentage isn't reported: check the voltage by hand or set the battery capacity.");

            if (x.ParamsTotal > 0 && x.ParamsReceived >= x.ParamsTotal)
                Add(Section.Vehicle, $"All {x.ParamsTotal.ToString("N0", CultureInfo.InvariantCulture)} parameters loaded", Status.Pass);
            else
                Add(Section.Vehicle, "Parameters loaded", Status.Check,
                    x.ParamsTotal > 0 ? $"{x.ParamsReceived}/{x.ParamsTotal}" : "NONE",
                    "Wait for parameters to finish loading, or refresh them.");

            if (plane && x.HasAirspeedSensor)
            {
                var atRest = x.Groundspeed < 1;
                if (atRest)
                    Add(Section.Vehicle, "Airspeed near zero at rest",
                        Math.Abs(x.Airspeed) > L.AirspeedAtRestMs ? Status.Check : Status.Pass,
                        Math.Abs(x.Airspeed) > L.AirspeedAtRestMs ? x.Airspeed.ToString("0.0 m/s", CultureInfo.InvariantCulture) : null,
                        Math.Abs(x.Airspeed) > L.AirspeedAtRestMs ? "Check the pitot cover is off and the tube is clear, then recalibrate (preflight calibration)." : null);
            }

            var missing = MissingFailsafes(x);
            Add(Section.Vehicle, "Failsafes set (RC, GCS, battery)", missing.Count == 0 ? Status.Pass : Status.Check,
                missing.Count == 0 ? null : "OFF: " + string.Join(", ", missing),
                missing.Count == 0 ? null : "Set the failsafe actions in Setup › Failsafe.");

            // ---- mission & airspace ----
            var missionCount = Math.Max(0, x.Mission.Count - 1); // item 0 is home
            Add(Section.Mission, "Mission on the vehicle", missionCount > 0 ? Status.Pass : Status.Check,
                missionCount > 0 ? $"{missionCount} items" : "NONE",
                missionCount > 0 ? null : "No mission loaded: write one from Plan, or fly manually.");

            var fenceEnable = x.Param("FENCE_ENABLE");
            if (fenceEnable.HasValue)
                Add(Section.Mission, "Geofence enabled", fenceEnable.Value >= 1 ? Status.Pass : Status.Check,
                    fenceEnable.Value >= 1 ? null : "OFF",
                    fenceEnable.Value >= 1 ? null : "Turn the fence on (FENCE_ENABLE) if the operation needs one.");

            var clearance = TerrainClearance(x);
            if (clearance.HasValue)
                Add(Section.Mission, $"Terrain clearance ≥ {L.MinTerrainClearanceM:0} m",
                    clearance.Value < 0 ? Status.Fail : clearance.Value < L.MinTerrainClearanceM ? Status.Check : Status.Pass,
                    clearance.Value.ToString("0 m", CultureInfo.InvariantCulture),
                    clearance.Value < L.MinTerrainClearanceM ? "A waypoint is close to (or below) the ground: raise it in Plan." : null);
            else if (missionCount > 0)
                Add(Section.Mission, "Terrain clearance", Status.Info, "NO DATA", "No terrain data for this area yet.");

            Add(Section.Mission, $"Wind below {L.MaxWindMs:0} m/s",
                x.WindMs > L.MaxWindMs ? Status.Check : Status.Pass,
                x.WindMs.ToString("0 m/s", CultureInfo.InvariantCulture),
                x.WindMs > L.MaxWindMs ? "Wind is above your limit." : null);

            var rallyText = x.Family == FlightCommandProfile.Family.QuadPlane ? "Rally / QRTL point set" : "Rally point set";
            Add(Section.Mission, rallyText, x.RallyPoints > 0 ? Status.Pass : Status.Info,
                x.RallyPoints > 0 ? x.RallyPoints + " set" : "HOME",
                x.RallyPoints > 0 ? null : "Without rally points the aircraft returns to home.");

            AddCrew(r, x);

            if (r.Items.Any(i => i.Status == Status.Fail))
                r.Verdict = Verdict.NoGo;
            else if (r.Items.Any(i => i.Status == Status.Check))
                r.Verdict = Verdict.Hold;
            else
                r.Verdict = Verdict.Go;
            return r;
        }

        private static void AddCrew(Result r, Inputs x)
        {
            foreach (var (text, done) in x.Crew ?? new List<(string, bool)>())
                r.Items.Add(new Item { Section = Section.Crew, Text = text, Status = done ? Status.Pass : Status.Check, Hint = done ? null : "Not ticked yet." });
        }

        /// <summary>Failsafes that are off, by name. Parameters the firmware doesn't have are skipped.</summary>
        public static List<string> MissingFailsafes(Inputs x)
        {
            var missing = new List<string>();
            bool plane = x.Family == FlightCommandProfile.Family.FixedWing || x.Family == FlightCommandProfile.Family.QuadPlane;

            double? rc = plane ? x.Param("THR_FAILSAFE") : x.Param("FS_THR_ENABLE");
            if (rc.HasValue && rc.Value == 0) missing.Add("RC");
            double? gcs = plane ? x.Param("FS_GCS_ENABL") : x.Param("FS_GCS_ENABLE");
            if (gcs.HasValue && gcs.Value == 0) missing.Add("GCS");
            double? batt = x.Param("BATT_FS_LOW_ACT");
            var battLowVolt = x.Param("BATT_LOW_VOLT");
            var battLowMah = x.Param("BATT_LOW_MAH");
            if (batt.HasValue && (batt.Value == 0 || (battLowVolt ?? 0) <= 0 && (battLowMah ?? 0) <= 0))
                missing.Add("battery");
            return missing;
        }

        /// <summary>Lowest height above terrain over the mission's cruise points, or null without terrain data.</summary>
        public static double? TerrainClearance(Inputs x)
        {
            double? worst = null;
            for (int i = 1; i < x.Mission.Count; i++)
            {
                var w = x.Mission[i];
                if (!PositionCommands.Contains(w.Command) || GroundCommands.Contains(w.Command))
                    continue;
                if (w.Lat == 0 && w.Lng == 0)
                    continue;
                var ground = x.Terrain(w.Lat, w.Lng);
                if (!ground.HasValue)
                    continue;
                double clearance;
                switch (w.Frame)
                {
                    case 10: // GLOBAL_TERRAIN_ALT
                    case 11:
                        clearance = w.Alt;
                        break;
                    case 0:  // GLOBAL (AMSL)
                    case 5:
                        clearance = w.Alt - ground.Value;
                        break;
                    default: // relative to home
                        clearance = x.HomeAltAmsl + w.Alt - ground.Value;
                        break;
                }

                if (!worst.HasValue || clearance < worst.Value)
                    worst = clearance;
            }

            return worst;
        }

        /// <summary>The crew list a new install starts with, by airframe.</summary>
        public static string[] DefaultCrewList(FlightCommandProfile.Family family)
        {
            var common = new List<string> { "Props secure, no damage", "Control surfaces move correctly" };
            if (family == FlightCommandProfile.Family.QuadPlane)
                common.Add("VTOL motor test done");
            if (family == FlightCommandProfile.Family.FixedWing || family == FlightCommandProfile.Family.QuadPlane)
                common.Add("Pitot cover removed");
            if (family == FlightCommandProfile.Family.Multirotor || family == FlightCommandProfile.Family.Helicopter)
            {
                common.Remove("Control surfaces move correctly");
                common.Add("Motor test done");
            }

            common.Add("Payload and camera secure");
            common.Add("Takeoff area clear");
            return common.ToArray();
        }

        /// <summary>A plain-text report of a check, for the Save report button.</summary>
        public static string Report(Result r, string vehicle, string operatorName, DateTime when)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Briech GCS preflight report");
            sb.AppendLine($"Vehicle: {vehicle}");
            if (!string.IsNullOrWhiteSpace(operatorName))
                sb.AppendLine($"Operator: {operatorName}");
            sb.AppendLine($"Checked: {when:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Verdict: {(r.Verdict == Verdict.Go ? "GO" : r.Verdict == Verdict.Hold ? "HOLD" : "NO-GO")} ({r.Summary})");
            foreach (var group in r.Items.GroupBy(i => i.Section))
            {
                sb.AppendLine();
                sb.AppendLine(group.Key == Section.Vehicle ? "Automatic, from the vehicle" : group.Key == Section.Mission ? "Mission and airspace" : "Crew sign-off");
                foreach (var i in group)
                {
                    var word = i.Status == Status.Pass ? "PASS" : i.Status == Status.Check ? "CHECK" : i.Status == Status.Fail ? "FAIL" : "INFO";
                    sb.AppendLine($"  [{word}] {i.Text}{(string.IsNullOrEmpty(i.Value) ? "" : " - " + i.Value)}");
                }
            }

            return sb.ToString();
        }
    }
}
