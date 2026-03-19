using System;

namespace MissionPlanner.GCSViews
{
    /// <summary>
    /// Flight command enumeration for user actions
    /// </summary>
    public enum FlightCommand
    {
        Arm,
        Disarm,
        Takeoff,
        Land,
        ReturnToLaunch,
        Loiter,
        Auto,
        SetHome
    }

    /// <summary>
    /// Tab changed event arguments
    /// </summary>
    public class TabChangedEventArgs : EventArgs
    {
        public TabId SelectedTab { get; set; }
        public TabId PreviousTab { get; set; }
    }

    /// <summary>
    /// Connection requested event arguments
    /// </summary>
    public class ConnectionRequestedEventArgs : EventArgs
    {
        public bool Connect { get; set; }
        public string Port { get; set; }
        public int BaudRate { get; set; }
    }

    /// <summary>
    /// Flight command requested event arguments
    /// </summary>
    public class FlightCommandEventArgs : EventArgs
    {
        public FlightCommand Command { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Flight mode changed event arguments
    /// </summary>
    public class FlightModeChangedEventArgs : EventArgs
    {
        public FlightMode NewMode { get; set; }
        public FlightMode PreviousMode { get; set; }
        public string ModeDescription { get; set; }
    }

    /// <summary>
    /// Telemetry update event arguments
    /// </summary>
    public class TelemetryUpdatedEventArgs : EventArgs
    {
        public TelemetryData Telemetry { get; set; }
        public DateTime UpdateTime { get; set; }
    }

    /// <summary>
    /// Connection status changed event arguments
    /// </summary>
    public class ConnectionStatusChangedEventArgs : EventArgs
    {
        public bool IsConnected { get; set; }
        public double LinkQuality { get; set; }
        public int SignalStrength { get; set; }
    }
}
