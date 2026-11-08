using System.Collections.Generic;

namespace MissionPlanner.ArduPilot
{
    /// <summary>
    /// Which flight commands make sense for which ArduPilot vehicle. Pure logic so it can be unit tested;
    /// the UI wrappers live in MissionPlanner.Utilities.FlightCommands.
    /// </summary>
    public static class FlightCommandProfile
    {
        public enum Family
        {
            Unknown,
            Multirotor,
            Helicopter,
            FixedWing,
            /// <summary>ArduPlane with Q_ENABLE set, or a heartbeat that reports a VTOL type</summary>
            QuadPlane,
            Rover,
            Boat,
            Sub,
        }

        public enum TakeoffMethod
        {
            /// <summary>no takeoff for this vehicle (rover, boat, sub)</summary>
            None,

            /// <summary>GUIDED, arm, MAV_CMD_NAV_TAKEOFF (Copter, Heli, QuadPlane VTOL climb)</summary>
            GuidedNavTakeoff,

            /// <summary>Fixed-wing TAKEOFF mode climbing to TKOFF_ALT</summary>
            PlaneTakeoffMode,
        }

        public static Family Classify(MAVLink.MAV_TYPE type, bool quadPlaneEnabled)
        {
            switch (type)
            {
                case MAVLink.MAV_TYPE.QUADROTOR:
                case MAVLink.MAV_TYPE.HEXAROTOR:
                case MAVLink.MAV_TYPE.OCTOROTOR:
                case MAVLink.MAV_TYPE.TRICOPTER:
                case MAVLink.MAV_TYPE.COAXIAL:
                case MAVLink.MAV_TYPE.DODECAROTOR:
                case MAVLink.MAV_TYPE.DECAROTOR:
                case MAVLink.MAV_TYPE.GENERIC_MULTIROTOR:
                    return Family.Multirotor;
                case MAVLink.MAV_TYPE.HELICOPTER:
                    return Family.Helicopter;
                case MAVLink.MAV_TYPE.FIXED_WING:
                    return quadPlaneEnabled ? Family.QuadPlane : Family.FixedWing;
                case MAVLink.MAV_TYPE.VTOL_DUOROTOR:
                case MAVLink.MAV_TYPE.VTOL_QUADROTOR:
                case MAVLink.MAV_TYPE.VTOL_TILTROTOR:
                case MAVLink.MAV_TYPE.VTOL_RESERVED2:
                case MAVLink.MAV_TYPE.VTOL_RESERVED3:
                case MAVLink.MAV_TYPE.VTOL_RESERVED4:
                case MAVLink.MAV_TYPE.VTOL_RESERVED5:
                    return Family.QuadPlane;
                case MAVLink.MAV_TYPE.GROUND_ROVER:
                    return Family.Rover;
                case MAVLink.MAV_TYPE.SURFACE_BOAT:
                    return Family.Boat;
                case MAVLink.MAV_TYPE.SUBMARINE:
                    return Family.Sub;
                default:
                    return Family.Unknown;
            }
        }

        public static bool IsAircraft(Family f)
        {
            return f == Family.Multirotor || f == Family.Helicopter || f == Family.FixedWing || f == Family.QuadPlane;
        }

        /// <summary>
        /// The "stop here" mode for the Land button, or null when the vehicle has none.
        /// ArduPlane has no LAND mode: a quadplane uses QLAND, a conventional plane can only RTL or fly a landing mission.
        /// </summary>
        public static string LandMode(Family f)
        {
            switch (f)
            {
                case Family.Multirotor:
                case Family.Helicopter:
                    return "LAND";
                case Family.QuadPlane:
                    return "QLAND";
                case Family.Rover:
                case Family.Boat:
                    return "HOLD";
                case Family.Sub:
                    return "SURFACE";
                default:
                    return null;
            }
        }

        /// <summary>Button text for <see cref="LandMode"/>.</summary>
        public static string LandLabel(Family f)
        {
            switch (f)
            {
                case Family.QuadPlane:
                    return "VTOL land (QLAND)";
                case Family.Rover:
                case Family.Boat:
                    return "Hold";
                case Family.Sub:
                    return "Surface";
                default:
                    return "Land";
            }
        }

        /// <summary>
        /// Return-home mode. Every ArduPilot vehicle except Sub has RTL; on a quadplane RTL flies home on the wing
        /// and Q_RTL_MODE decides how it lands, which is what we want from a distance (QRTL is VTOL-only).
        /// </summary>
        public static string ReturnMode(Family f)
        {
            return f == Family.Sub ? "SURFACE" : "RTL";
        }

        public static TakeoffMethod Takeoff(Family f)
        {
            switch (f)
            {
                case Family.Multirotor:
                case Family.Helicopter:
                case Family.QuadPlane:
                    return TakeoffMethod.GuidedNavTakeoff;
                case Family.FixedWing:
                    return TakeoffMethod.PlaneTakeoffMode;
                default:
                    return TakeoffMethod.None;
            }
        }

        /// <summary>
        /// Best guess at whether an armed vehicle is flying. Prefers EXTENDED_SYS_STATE.landed_state
        /// (1 on ground, 2 in air, 3 taking off, 4 landing); otherwise height above home or ground speed.
        /// A false "airborne" only makes the GCS more careful, so the thresholds are low.
        /// </summary>
        public static bool LooksAirborne(bool armed, byte landedState, double heightAboveHomeM, double groundSpeedMs)
        {
            if (!armed)
                return false;
            switch (landedState)
            {
                case 1:
                    return false;
                case 2:
                case 3:
                case 4:
                    return true;
            }

            return heightAboveHomeM > 3 || groundSpeedMs > 3;
        }

        /// <summary>
        /// Resume-mission filter. Given the vehicle's mission (index 0 = home) and the item to resume at,
        /// returns the indexes to re-upload: home, any takeoff before the resume point (so a vehicle on the ground
        /// can still climb out), DO_ commands before it (camera, servo, speed changes still apply), then everything
        /// from the resume point on. Navigation, conditions, DO_JUMP and DO_LAND_START before the resume point are
        /// dropped: their sequence numbers would be wrong after renumbering.
        /// </summary>
        public static List<int> ResumeMissionItems(IList<ushort> commandIds, int resumeAt)
        {
            var keep = new List<int>();
            if (commandIds == null)
                return keep;

            for (int i = 0; i < commandIds.Count; i++)
            {
                if (i == 0 || i >= resumeAt)
                {
                    keep.Add(i);
                    continue;
                }

                var id = commandIds[i];
                if (id == (ushort)MAVLink.MAV_CMD.TAKEOFF || id == (ushort)MAVLink.MAV_CMD.VTOL_TAKEOFF)
                {
                    keep.Add(i);
                    continue;
                }

                if (id == (ushort)MAVLink.MAV_CMD.DO_JUMP || id == (ushort)MAVLink.MAV_CMD.DO_LAND_START)
                    continue;

                // MAV_CMD_DO_* lives between CONDITION_LAST (159) and DO_LAST (240)
                if (id > (ushort)MAVLink.MAV_CMD.CONDITION_LAST && id <= (ushort)MAVLink.MAV_CMD.DO_LAST)
                    keep.Add(i);
            }

            return keep;
        }
    }
}
