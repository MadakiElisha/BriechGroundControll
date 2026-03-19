using System;
using System.Collections.Generic;

namespace MissionPlanner.GCSViews
{
    /// <summary>
    /// BRIECH UAS Type Definitions
    /// Converted from TypeScript types in React application
    /// </summary>

    /// <summary>
    /// Flight modes supported by ArduCopter/ArduPlane
    /// </summary>
    public enum FlightMode
    {
        STABILIZE,
        ALT_HOLD,
        LOITER,
        AUTO,
        GUIDED,
        RTL,
        LAND,
        SPORT,
        POSHOLD
    }

    /// <summary>
    /// Tab identifiers for view navigation
    /// </summary>
    public enum TabId
    {
        FlightData,
        FlightPlan,
        InitialSetup,
        ConfigTuning
    }

    /// <summary>
    /// Complete telemetry data structure from aircraft
    /// Updated at 10Hz from MAVLink connection
    /// </summary>
    public class TelemetryData
    {
        public double Altitude { get; set; } = 0;           // meters
        public double Speed { get; set; } = 0;              // m/s
        public double Heading { get; set; } = 0;            // degrees 0-360
        public double Roll { get; set; } = 0;               // degrees -180 to 180
        public double Pitch { get; set; } = 0;              // degrees -90 to 90
        public double Yaw { get; set; } = 0;                // degrees 0-360
        public double Battery { get; set; } = 100;          // percent 0-100
        public int Gps { get; set; } = 0;                   // satellite count
        public FlightMode Mode { get; set; } = FlightMode.STABILIZE;
        public bool Armed { get; set; } = false;
        public double Distance { get; set; } = 0;           // distance to home, meters
        public double Voltage { get; set; } = 12.6;         // battery voltage
        public double Current { get; set; } = 0;            // current draw, amps
        public int Satellites { get; set; } = 0;            // GPS satellite count
        public double GroundSpeed { get; set; } = 0;        // m/s
        public double AirSpeed { get; set; } = 0;           // m/s
        public double VerticalSpeed { get; set; } = 0;      // m/s
        public double Throttle { get; set; } = 0;           // percent 0-100
        public double Rssi { get; set; } = 0;               // dBm
        public double FuelRemaining { get; set; } = 100;    // percent 0-100
        public double EstimatedFlightTime { get; set; } = 120; // minutes

        /// <summary>
        /// Create a copy of telemetry data
        /// </summary>
        public TelemetryData Clone()
        {
            return new TelemetryData
            {
                Altitude = this.Altitude,
                Speed = this.Speed,
                Heading = this.Heading,
                Roll = this.Roll,
                Pitch = this.Pitch,
                Yaw = this.Yaw,
                Battery = this.Battery,
                Gps = this.Gps,
                Mode = this.Mode,
                Armed = this.Armed,
                Distance = this.Distance,
                Voltage = this.Voltage,
                Current = this.Current,
                Satellites = this.Satellites,
                GroundSpeed = this.GroundSpeed,
                AirSpeed = this.AirSpeed,
                VerticalSpeed = this.VerticalSpeed,
                Throttle = this.Throttle,
                Rssi = this.Rssi,
                FuelRemaining = this.FuelRemaining,
                EstimatedFlightTime = this.EstimatedFlightTime
            };
        }
    }

    /// <summary>
    /// Connection status and link quality metrics
    /// </summary>
    public class ConnectionStatus
    {
        public bool Connected { get; set; } = false;
        public int Packets { get; set; } = 0;
        public double LinkQuality { get; set; } = 0;        // percent 0-100
        public DateTime? LastUpdate { get; set; } = null;
    }

    /// <summary>
    /// Navigation tab definition
    /// </summary>
    public class TabDefinition
    {
        public TabId Id { get; set; }
        public string Label { get; set; }
        public string Icon { get; set; }              // emoji or unicode

        public TabDefinition(TabId id, string label, string icon)
        {
            Id = id;
            Label = label;
            Icon = icon;
        }
    }
}
