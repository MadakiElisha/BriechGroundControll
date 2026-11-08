using System;
using System.Collections.Generic;
using System.Linq;

namespace MissionPlanner.ArduPilot
{
    /// <summary>
    /// ArduPilot renames parameters between releases, often changing units at the same time
    /// (Plane 4.5: TRIM_ARSPD_CM cm/s -> AIRSPEED_CRUISE m/s; Copter 4.7: WPNAV_SPEED cm/s -> WP_SPD m/s, ...).
    /// The firmware converts the stored value once on upgrade, so a vehicle only ever reports one of the two names.
    /// This table lets the GCS read and write a setting by its current name on any firmware from 4.5.7 onwards,
    /// always working in the current name's units.
    ///
    /// Every row is taken from ArduPilot's own PARAMETER_CONVERSION tables (AP_Param::convert_old_parameters_scaled)
    /// or its release notes. Scale is "current value = old value * Scale".
    /// </summary>
    public static class ParamAliases
    {
        public sealed class Alias
        {
            public Alias(string current, string old, double scale, string firmware)
            {
                Current = current;
                Old = old;
                Scale = scale;
                Firmware = firmware;
            }

            /// <summary>name used by the newest firmware</summary>
            public string Current { get; }

            /// <summary>name used by older firmware</summary>
            public string Old { get; }

            /// <summary>current units = old units * Scale (0.01 for cm -> m, cdeg -> deg)</summary>
            public double Scale { get; }

            /// <summary>release that introduced the current name</summary>
            public string Firmware { get; }
        }

        private const double CentiToUnit = 0.01;

        public static readonly IReadOnlyList<Alias> All = new[]
        {
            // ---- Copter / QuadPlane 4.7: waypoint navigation (AC_WPNav) ----
            new Alias("WP_SPD", "WPNAV_SPEED", CentiToUnit, "Copter 4.7"),
            new Alias("WP_RADIUS_M", "WPNAV_RADIUS", CentiToUnit, "Copter 4.7"),
            new Alias("WP_SPD_UP", "WPNAV_SPEED_UP", CentiToUnit, "Copter 4.7"),
            new Alias("WP_SPD_DN", "WPNAV_SPEED_DN", CentiToUnit, "Copter 4.7"),
            new Alias("WP_ACC", "WPNAV_ACCEL", CentiToUnit, "Copter 4.7"),
            new Alias("WP_ACC_Z", "WPNAV_ACCEL_Z", CentiToUnit, "Copter 4.7"),
            new Alias("WP_ACC_CNR", "WPNAV_ACCEL_C", CentiToUnit, "Copter 4.7"),
            new Alias("WP_JERK", "WPNAV_JERK", 1, "Copter 4.7"),

            // ---- Copter 4.7: loiter, circle ----
            new Alias("LOIT_SPEED_MS", "LOIT_SPEED", CentiToUnit, "Copter 4.7"),
            new Alias("LOIT_ACC_MAX_M", "LOIT_ACC_MAX", CentiToUnit, "Copter 4.7"),
            new Alias("LOIT_BRK_ACC_M", "LOIT_BRK_ACCEL", CentiToUnit, "Copter 4.7"),
            new Alias("LOIT_BRK_JRK_M", "LOIT_BRK_JERK", CentiToUnit, "Copter 4.7"),
            new Alias("CIRCLE_RADIUS_M", "CIRCLE_RADIUS", CentiToUnit, "Copter 4.7"),

            // ---- Copter 4.7: pilot, RTL, land ----
            new Alias("PILOT_SPD_UP", "PILOT_SPEED_UP", CentiToUnit, "Copter 4.7"),
            new Alias("PILOT_SPD_DN", "PILOT_SPEED_DN", CentiToUnit, "Copter 4.7"),
            new Alias("PILOT_ACC_Z", "PILOT_ACCEL_Z", CentiToUnit, "Copter 4.7"),
            new Alias("PILOT_TKO_ALT_M", "PILOT_TKOFF_ALT", CentiToUnit, "Copter 4.7"),
            new Alias("RTL_ALT_M", "RTL_ALT", CentiToUnit, "Copter 4.7"),
            new Alias("RTL_SPEED_MS", "RTL_SPEED", CentiToUnit, "Copter 4.7"),
            new Alias("RTL_ALT_FINAL_M", "RTL_ALT_FINAL", CentiToUnit, "Copter 4.7"),
            new Alias("RTL_CLIMB_MIN_M", "RTL_CLIMB_MIN", CentiToUnit, "Copter 4.7"),
            new Alias("LAND_SPD_MS", "LAND_SPEED", CentiToUnit, "Copter 4.7"),
            new Alias("LAND_SPD_HIGH_MS", "LAND_SPEED_HIGH", CentiToUnit, "Copter 4.7"),
            new Alias("LAND_ALT_LOW_M", "LAND_ALT_LOW", CentiToUnit, "Copter 4.7"),

            // ---- 4.7: attitude control (Copter ATC_*, QuadPlane Q_A_*), cdeg -> deg ----
            new Alias("ATC_ANGLE_MAX", "ANGLE_MAX", CentiToUnit, "Copter 4.7"),
            new Alias("ATC_RATE_WPY_MAX", "ATC_SLEW_YAW", CentiToUnit, "Copter 4.7"),
            new Alias("ATC_ACC_R_MAX", "ATC_ACCEL_R_MAX", CentiToUnit, "Copter 4.7"),
            new Alias("ATC_ACC_P_MAX", "ATC_ACCEL_P_MAX", CentiToUnit, "Copter 4.7"),
            new Alias("ATC_ACC_Y_MAX", "ATC_ACCEL_Y_MAX", CentiToUnit, "Copter 4.7"),
            new Alias("Q_A_ANGLE_MAX", "Q_ANGLE_MAX", CentiToUnit, "Plane 4.7"),
            new Alias("Q_A_RATE_WPY_MAX", "Q_A_SLEW_YAW", CentiToUnit, "Plane 4.7"),
            new Alias("Q_A_ACC_R_MAX", "Q_A_ACCEL_R_MAX", CentiToUnit, "Plane 4.7"),
            new Alias("Q_A_ACC_P_MAX", "Q_A_ACCEL_P_MAX", CentiToUnit, "Plane 4.7"),
            new Alias("Q_A_ACC_Y_MAX", "Q_A_ACCEL_Y_MAX", CentiToUnit, "Plane 4.7"),

            // ---- 4.7: plain renames (same units) ----
            new Alias("MAV_SYSID", "SYSID_THISMAV", 1, "4.7"),
            new Alias("MAV_GCS_SYSID", "SYSID_MYGCS", 1, "4.7"),
            new Alias("MAV_TELEM_DELAY", "TELEM_DELAY", 1, "4.7"),
            new Alias("ALT_SLOPE_MIN", "GLIDE_SLOPE_MIN", 1, "Plane 4.7"),
            new Alias("ALT_SLOPE_MAXHGT", "GLIDE_SLOPE_THR", 1, "Plane 4.7"),
            new Alias("EK3_FLOW_MAX", "EK3_MAX_FLOW", 1, "4.7"),

            // ---- Plane 4.5 (only seen on aircraft older than our 4.5.7 floor, kept for old logs/param files) ----
            new Alias("AIRSPEED_CRUISE", "TRIM_ARSPD_CM", CentiToUnit, "Plane 4.5"),
            new Alias("AIRSPEED_MIN", "ARSPD_FBW_MIN", 1, "Plane 4.5"),
            new Alias("AIRSPEED_MAX", "ARSPD_FBW_MAX", 1, "Plane 4.5"),
            new Alias("ROLL_LIMIT_DEG", "LIM_ROLL_CD", CentiToUnit, "Plane 4.5"),
            new Alias("PTCH_LIM_MAX_DEG", "LIM_PITCH_MAX", CentiToUnit, "Plane 4.5"),
            new Alias("PTCH_LIM_MIN_DEG", "LIM_PITCH_MIN", CentiToUnit, "Plane 4.5"),
            new Alias("PTCH_TRIM_DEG", "TRIM_PITCH_CD", CentiToUnit, "Plane 4.5"),
            new Alias("RTL_ALTITUDE", "ALT_HOLD_RTL", CentiToUnit, "Plane 4.5"),
            new Alias("CRUISE_ALT_FLOOR", "ALT_HOLD_FBWCM", CentiToUnit, "Plane 4.5"),
            new Alias("MIN_GROUNDSPEED", "MIN_GNDSPD_CM", CentiToUnit, "Plane 4.5"),
            new Alias("LAND_PITCH_DEG", "LAND_PITCH_CD", CentiToUnit, "Plane 4.5"),
            new Alias("Q_LAND_FINAL_SPD", "Q_LAND_SPEED", CentiToUnit, "Plane 4.5"),
            new Alias("Q_PILOT_SPD_UP", "Q_VELZ_MAX", CentiToUnit, "Plane 4.5"),
            new Alias("Q_PILOT_SPD_DN", "Q_VELZ_MAX_DN", CentiToUnit, "Plane 4.5"),
            new Alias("Q_PILOT_ACCEL_Z", "Q_ACCEL_Z", CentiToUnit, "Plane 4.5"),
            new Alias("COMPASS_DISBLMSK", "COMPASS_TYPEMASK", 1, "4.5"),
            new Alias("STAT_BOOTCNT", "SYS_NUM_RESETS", 1, "4.5"),
        };

        private static readonly Dictionary<string, Alias> byCurrent =
            All.ToDictionary(a => a.Current, StringComparer.Ordinal);

        private static readonly Dictionary<string, Alias> byOld =
            All.ToDictionary(a => a.Old, StringComparer.Ordinal);

        /// <summary>Find the alias row for either name, or null.</summary>
        public static Alias Find(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (byCurrent.TryGetValue(name, out var a))
                return a;
            if (byOld.TryGetValue(name, out a))
                return a;
            return null;
        }

        /// <summary>The newest name for a parameter (returns the input if it has never been renamed).</summary>
        public static string CurrentName(string name)
        {
            return Find(name)?.Current ?? name;
        }

        /// <summary>
        /// Both names for a parameter, current first. Handy for <c>setParam(string[] names, ...)</c>
        /// only when the value has the same units under both names (Scale == 1).
        /// </summary>
        public static string[] Names(string name)
        {
            var a = Find(name);
            return a == null ? new[] { name } : new[] { a.Current, a.Old };
        }

        /// <summary>
        /// The name this vehicle actually reports for a parameter, or null if it reports neither.
        /// </summary>
        public static string Resolve(Func<string, bool> has, string name)
        {
            if (has == null || string.IsNullOrEmpty(name))
                return null;
            var a = Find(name);
            if (a == null)
                return has(name) ? name : null;
            if (has(a.Current))
                return a.Current;
            if (has(a.Old))
                return a.Old;
            return null;
        }

        /// <summary>
        /// Read a parameter in the units of its current name, whichever name the vehicle reports.
        /// </summary>
        /// <param name="get">lookup returning the raw value, or null when the vehicle has no such parameter</param>
        /// <param name="name">either name</param>
        public static bool TryGet(Func<string, double?> get, string name, out double value)
        {
            return TryGet(get, name, out value, out _);
        }

        public static bool TryGet(Func<string, double?> get, string name, out double value, out string reportedName)
        {
            value = 0;
            reportedName = null;
            if (get == null || string.IsNullOrEmpty(name))
                return false;

            var a = Find(name);
            if (a == null)
            {
                var v = get(name);
                if (!v.HasValue)
                    return false;
                value = v.Value;
                reportedName = name;
                return true;
            }

            var cur = get(a.Current);
            if (cur.HasValue)
            {
                value = cur.Value;
                reportedName = a.Current;
                return true;
            }

            var old = get(a.Old);
            if (old.HasValue)
            {
                value = old.Value * a.Scale;
                reportedName = a.Old;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Convert a value given in the current name's units into the units of <paramref name="reportedName"/>,
        /// ready to send to the vehicle with setParam(reportedName, ...).
        /// </summary>
        public static double ToVehicleUnits(string reportedName, double valueInCurrentUnits)
        {
            var a = Find(reportedName);
            if (a == null || a.Current == reportedName || a.Scale == 0)
                return valueInCurrentUnits;
            return valueInCurrentUnits / a.Scale;
        }

        /// <summary>Convenience overload for a vehicle's parameter list.</summary>
        public static bool TryGet(MAVLink.MAVLinkParamList param, string name, out double value)
        {
            return TryGet(Lookup(param), name, out value, out _);
        }

        public static bool TryGet(MAVLink.MAVLinkParamList param, string name, out double value, out string reportedName)
        {
            return TryGet(Lookup(param), name, out value, out reportedName);
        }

        public static string Resolve(MAVLink.MAVLinkParamList param, string name)
        {
            if (param == null)
                return null;
            return Resolve(param.ContainsKey, name);
        }

        public static Func<string, double?> Lookup(MAVLink.MAVLinkParamList param)
        {
            if (param == null)
                return _ => null;
            return n =>
            {
                if (!param.ContainsKey(n))
                    return null;
                var p = param[n];
                return p == null ? (double?)null : p.Value;
            };
        }

        /// <summary>What the Fly view's speed box should show for this vehicle.</summary>
        public enum SpeedKind
        {
            None,
            /// <summary>metres per second (waypoint ground speed or cruise airspeed)</summary>
            MetresPerSecond,
            /// <summary>Plane without an airspeed sensor: cruise throttle percent</summary>
            ThrottlePercent,
        }

        /// <summary>
        /// The cruise / waypoint speed setpoint a pilot expects to adjust from the Fly view, on any ArduPilot vehicle
        /// and firmware from 4.5.7 to current. Copter: WP_SPD (WPNAV_SPEED before 4.7). Plane with airspeed:
        /// AIRSPEED_CRUISE (TRIM_ARSPD_CM before 4.5). Plane without airspeed: TRIM_THROTTLE %. Rover/boat: WP_SPEED.
        /// </summary>
        public static SpeedKind GetSpeedSetpoint(Func<string, double?> get, out double value, out string reportedName)
        {
            value = 0;
            reportedName = null;
            if (get == null)
                return SpeedKind.None;

            // Copter / heli / multirotor (QuadPlane uses Q_WP_*, handled by the plane branch below)
            if (TryGet(get, "WP_SPD", out value, out reportedName))
                return SpeedKind.MetresPerSecond;

            // Rover / boat
            if (!get("AIRSPEED_CRUISE").HasValue && !get("TRIM_ARSPD_CM").HasValue)
            {
                var rover = get("WP_SPEED");
                if (rover.HasValue)
                {
                    value = rover.Value;
                    reportedName = "WP_SPEED";
                    return SpeedKind.MetresPerSecond;
                }

                // very old copter firmware
                var legacy = get("WP_SPEED_MAX");
                if (legacy.HasValue)
                {
                    value = legacy.Value / 100.0;
                    reportedName = "WP_SPEED_MAX";
                    return SpeedKind.MetresPerSecond;
                }
            }

            // Plane
            if (PlaneUsesAirspeed(get) && TryGet(get, "AIRSPEED_CRUISE", out value, out reportedName))
                return SpeedKind.MetresPerSecond;

            var trim = get("TRIM_THROTTLE");
            if (trim.HasValue)
            {
                value = trim.Value;
                reportedName = "TRIM_THROTTLE";
                return SpeedKind.ThrottlePercent;
            }

            value = 0;
            reportedName = null;
            return SpeedKind.None;
        }

        public static SpeedKind GetSpeedSetpoint(MAVLink.MAVLinkParamList param, out double value, out string reportedName)
        {
            return GetSpeedSetpoint(Lookup(param), out value, out reportedName);
        }

        /// <summary>
        /// MAV_CMD_DO_CHANGE_SPEED arguments for the Fly view's speed box.
        /// p1 speed type (0 airspeed for plane, 1 ground speed otherwise), p2 speed m/s (-1 = no change),
        /// p3 throttle % (-1 = no change). A plane without an airspeed sensor is adjusted by throttle.
        /// </summary>
        public static void ChangeSpeedArguments(Func<string, double?> get, float boxValue,
            out float speedType, out float speed, out float throttle)
        {
            var kind = GetSpeedSetpoint(get, out _, out var name);
            var isPlane = name == "AIRSPEED_CRUISE" || name == "TRIM_ARSPD_CM" || name == "TRIM_THROTTLE";
            speedType = isPlane ? 0 : 1;
            if (kind == SpeedKind.ThrottlePercent)
            {
                speed = -1;
                throttle = boxValue;
            }
            else
            {
                speed = boxValue;
                throttle = -1;
            }
        }

        public static void ChangeSpeedArguments(MAVLink.MAVLinkParamList param, float boxValue,
            out float speedType, out float speed, out float throttle)
        {
            ChangeSpeedArguments(Lookup(param), boxValue, out speedType, out speed, out throttle);
        }

        /// <summary>
        /// Plane flies to an airspeed target when ARSPD_USE is 1 and a sensor is configured
        /// (ARSPD_TYPE &gt; 0, or ARSPD_ENABLE = 1 on firmware before 3.8).
        /// </summary>
        public static bool PlaneUsesAirspeed(Func<string, double?> get)
        {
            var use = get("ARSPD_USE");
            if (!use.HasValue || use.Value < 0.5)
                return false;
            var type = get("ARSPD_TYPE");
            if (type.HasValue)
                return type.Value > 0;
            var enable = get("ARSPD_ENABLE");
            return enable.HasValue && enable.Value > 0.5;
        }
    }
}
