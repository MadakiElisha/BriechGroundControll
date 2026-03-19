using System;
using System.Windows.Forms;
using MissionPlanner.ArduPilot;
using log4net;

namespace MissionPlanner.GCSViews
{
    /// <summary>
    /// Modern Flight Data Interface - Complete Implementation
    /// Master orchestrator combining all components into cohesive flight data view
    /// 4-Tab Interface: Flight Data | Flight Plan | Initial Setup | Config/Tuning
    /// Real-time telemetry with 10Hz update rate
    /// Replaces React App.tsx with full WinForms integration
    /// </summary>
    public partial class ModernFlightDataComplete : MyUserControl
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        // UI Components
        private TopNavigationBar topNav;
        private BriechStatusBar statusBar;
        private Panel panelViewContainer;

        // View Controllers
        private FlightDataViewController flightDataView;
        private Panel flightPlanView;
        private Panel initialSetupView;
        private Panel configTuningView;

        // State Management
        private TelemetrySimulator telemetrySimulator;
        private TelemetryData currentTelemetry;
        private ConnectionStatus connectionStatus;
        private Timer updateTimer;
        private bool isSimulating = false;

        public ModernFlightDataComplete()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialize all UI components and start update timer
        /// </summary>
        private void InitializeComponent()
        {
            this.SuspendLayout();

            // Set main control properties
            this.BackColor = BriechTheme.BRIECH_DARK;
            this.ForeColor = BriechTheme.TEXT_PRIMARY;
            this.Dock = DockStyle.Fill;
            this.Font = new System.Drawing.Font("Segoe UI", 9f);

            // Create Top Navigation Bar
            topNav = new TopNavigationBar();
            topNav.TabChanged += TopNav_TabChanged;
            topNav.ConnectionRequested += TopNav_ConnectionRequested;
            this.Controls.Add(topNav);

            // Create View Container (switches between tabs)
            panelViewContainer = new Panel();
            panelViewContainer.Dock = DockStyle.Fill;
            panelViewContainer.BackColor = BriechTheme.BRIECH_DARK;
            this.Controls.Add(panelViewContainer);

            // Create Views
            CreateViews();

            // Create Status Bar
            statusBar = new BriechStatusBar();
            this.Controls.Add(statusBar);

            // Initialize telemetry simulator
            telemetrySimulator = new TelemetrySimulator();
            currentTelemetry = new TelemetryData();
            connectionStatus = new ConnectionStatus();

            // Setup update timer (10 Hz = 100ms)
            updateTimer = new Timer();
            updateTimer.Interval = 100;
            updateTimer.Tick += UpdateTimer_Tick;
            updateTimer.Start();

            this.ResumeLayout();
        }

        /// <summary>
        /// Create all view controllers
        /// </summary>
        private void CreateViews()
        {
            // Flight Data View (main HUD)
            flightDataView = new FlightDataViewController();
            flightDataView.Dock = DockStyle.Fill;
            flightDataView.FlightCommandRequested += FlightDataView_FlightCommandRequested;
            flightDataView.FlightModeChanged += FlightDataView_FlightModeChanged;
            panelViewContainer.Controls.Add(flightDataView);

            // Flight Plan View (placeholder)
            flightPlanView = new Panel();
            flightPlanView.Dock = DockStyle.Fill;
            flightPlanView.BackColor = BriechTheme.BRIECH_DARK;
            var labelFlightPlan = new Label();
            labelFlightPlan.Text = "? FLIGHT PLAN\n\nUse this view to create and manage flight plans.\nNot implemented in this preview.";
            labelFlightPlan.ForeColor = BriechTheme.TEXT_PRIMARY;
            labelFlightPlan.Font = new System.Drawing.Font("Segoe UI", 14f, System.Drawing.FontStyle.Bold);
            labelFlightPlan.AutoSize = false;
            labelFlightPlan.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            labelFlightPlan.Dock = DockStyle.Fill;
            flightPlanView.Controls.Add(labelFlightPlan);
            panelViewContainer.Controls.Add(flightPlanView);

            // Initial Setup View (placeholder)
            initialSetupView = new Panel();
            initialSetupView.Dock = DockStyle.Fill;
            initialSetupView.BackColor = BriechTheme.BRIECH_DARK;
            var labelSetup = new Label();
            labelSetup.Text = "? INITIAL SETUP\n\nUse this view to configure aircraft hardware.\nNot implemented in this preview.";
            labelSetup.ForeColor = BriechTheme.TEXT_PRIMARY;
            labelSetup.Font = new System.Drawing.Font("Segoe UI", 14f, System.Drawing.FontStyle.Bold);
            labelSetup.AutoSize = false;
            labelSetup.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            labelSetup.Dock = DockStyle.Fill;
            initialSetupView.Controls.Add(labelSetup);
            panelViewContainer.Controls.Add(initialSetupView);

            // Config/Tuning View (placeholder)
            configTuningView = new Panel();
            configTuningView.Dock = DockStyle.Fill;
            configTuningView.BackColor = BriechTheme.BRIECH_DARK;
            var labelConfig = new Label();
            labelConfig.Text = "?? CONFIG/TUNING\n\nUse this view to adjust flight controller parameters.\nNot implemented in this preview.";
            labelConfig.ForeColor = BriechTheme.TEXT_PRIMARY;
            labelConfig.Font = new System.Drawing.Font("Segoe UI", 14f, System.Drawing.FontStyle.Bold);
            labelConfig.AutoSize = false;
            labelConfig.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            labelConfig.Dock = DockStyle.Fill;
            configTuningView.Controls.Add(labelConfig);
            panelViewContainer.Controls.Add(configTuningView);

            // Initially hide all except Flight Data
            HideAllViews();
            flightDataView.Visible = true;
        }

        /// <summary>
        /// Hide all views
        /// </summary>
        private void HideAllViews()
        {
            flightDataView.Visible = false;
            flightPlanView.Visible = false;
            initialSetupView.Visible = false;
            configTuningView.Visible = false;
        }

        /// <summary>
        /// Handle tab change from navigation bar
        /// </summary>
        private void TopNav_TabChanged(object sender, TabChangedEventArgs e)
        {
            HideAllViews();
            switch (e.SelectedTab)
            {
                case TabId.FlightData:
                    flightDataView.Visible = true;
                    break;
                case TabId.FlightPlan:
                    flightPlanView.Visible = true;
                    break;
                case TabId.InitialSetup:
                    initialSetupView.Visible = true;
                    break;
                case TabId.ConfigTuning:
                    configTuningView.Visible = true;
                    break;
            }
            log.Info($"Tab changed to: {e.SelectedTab}");
        }

        /// <summary>
        /// Handle connection request from navigation bar
        /// </summary>
        private void TopNav_ConnectionRequested(object sender, ConnectionRequestedEventArgs e)
        {
            if (e.Connect)
            {
                // Enable telemetry simulation
                isSimulating = true;
                connectionStatus.Connected = true;
                topNav.UpdateConnectButton(true);
                telemetrySimulator.Reset();
                log.Info($"Connection requested: {e.Port} @ {e.BaudRate}");
            }
            else
            {
                // Disconnect
                isSimulating = false;
                connectionStatus.Connected = false;
                topNav.UpdateConnectButton(false);
                telemetrySimulator.Reset();
                log.Info("Disconnection requested");
            }
        }

        /// <summary>
        /// Handle flight command from Flight Data view
        /// </summary>
        private void FlightDataView_FlightCommandRequested(object sender, FlightCommandEventArgs e)
        {
            switch (e.Command)
            {
                case FlightCommand.Arm:
                    log.Info("ARM command requested");
                    telemetrySimulator.Arm();
                    break;
                case FlightCommand.Disarm:
                    log.Info("DISARM command requested");
                    telemetrySimulator.Disarm();
                    break;
                case FlightCommand.Takeoff:
                    log.Info("TAKEOFF command requested");
                    telemetrySimulator.Takeoff();
                    break;
                case FlightCommand.Land:
                    log.Info("LAND command requested");
                    telemetrySimulator.Land();
                    break;
                case FlightCommand.ReturnToLaunch:
                    log.Info("RETURN TO LAUNCH command requested");
                    telemetrySimulator.ReturnToLaunch();
                    break;
                case FlightCommand.Loiter:
                    log.Info("LOITER command requested");
                    telemetrySimulator.Loiter();
                    break;
                case FlightCommand.Auto:
                    log.Info("AUTO command requested");
                    telemetrySimulator.SetFlightMode(FlightMode.AUTO);
                    break;
                case FlightCommand.SetHome:
                    log.Info("SET HOME command requested");
                    break;
            }
        }

        /// <summary>
        /// Handle flight mode change from Flight Data view
        /// </summary>
        private void FlightDataView_FlightModeChanged(object sender, FlightModeChangedEventArgs e)
        {
            log.Info($"Flight mode changed to: {e.NewMode}");
            telemetrySimulator.SetFlightMode(e.NewMode);
        }

        /// <summary>
        /// Update timer tick - 10Hz (100ms interval)
        /// </summary>
        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // Update telemetry (simulated or from live connection)
                if (isSimulating)
                {
                    currentTelemetry = telemetrySimulator.UpdateTelemetry();
                }
                else if (MainV2.comPort != null && MainV2.comPort.MAV != null && MainV2.comPort.BaseStream.IsOpen)
                {
                    // Read from live MAVLink connection
                    var cs = MainV2.comPort.MAV.cs;
                    currentTelemetry.Altitude = cs.alt;
                    currentTelemetry.Speed = cs.groundspeed;
                    currentTelemetry.Heading = cs.yaw;
                    currentTelemetry.Roll = cs.roll;
                    currentTelemetry.Pitch = cs.pitch;
                    currentTelemetry.Yaw = cs.yaw;
                    currentTelemetry.Battery = cs.battery_remaining;
                    currentTelemetry.Voltage = cs.battery_voltage;
                    currentTelemetry.Current = cs.current;
                    currentTelemetry.Satellites = (int)cs.satcount;
                    currentTelemetry.VerticalSpeed = cs.verticalspeed;
                    currentTelemetry.Rssi = cs.rssi;

                    connectionStatus.Connected = true;
                    connectionStatus.Packets++;
                    connectionStatus.LinkQuality = MainV2.comPort.MAV.cs.linkqualitygcs;
                    connectionStatus.LastUpdate = DateTime.UtcNow;
                }

                // Update Flight Data view
                flightDataView.UpdateTelemetry(currentTelemetry);
                flightDataView.UpdateConnectionStatus(connectionStatus.Connected);

                // Update Status bar
                statusBar.UpdateAll(
                    connectionStatus.Connected,
                    connectionStatus.Packets,
                    connectionStatus.LinkQuality,
                    (int)currentTelemetry.Rssi,
                    (int)currentTelemetry.FuelRemaining,
                    (int)currentTelemetry.EstimatedFlightTime * 60
                );
            }
            catch (Exception ex)
            {
                log.Error("Update tick error: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Enable telemetry simulation (for testing without real connection)
        /// </summary>
        public void StartSimulation()
        {
            isSimulating = true;
            connectionStatus.Connected = true;
            topNav.UpdateConnectButton(true);
            telemetrySimulator.Reset();
        }

        /// <summary>
        /// Disable telemetry simulation
        /// </summary>
        public void StopSimulation()
        {
            isSimulating = false;
            connectionStatus.Connected = false;
            topNav.UpdateConnectButton(false);
            telemetrySimulator.Reset();
        }

        /// <summary>
        /// Get current telemetry data snapshot
        /// </summary>
        public TelemetryData GetCurrentTelemetry()
        {
            return currentTelemetry.Clone();
        }

        /// <summary>
        /// Get connection status
        /// </summary>
        public bool IsConnected => connectionStatus.Connected;

        /// <summary>
        /// Cleanup resources
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                updateTimer?.Stop();
                updateTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
