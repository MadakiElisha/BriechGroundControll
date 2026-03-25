using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;
using log4net;
using System.Reflection;
using MissionPlanner.Controls;
using MissionPlanner.Maps;
using MissionPlanner.Utilities;

namespace MissionPlanner.GCSViews
{
    /// <summary>
    /// Professional drone GCS flight data display - 100% C#
        /// Dark navy + gold professional aesthetic
        /// 3-panel layout: Telemetry (left), stacked HUD + optional auxiliary deck with mission map (center), Quick Actions (right)
    /// </summary>
    public partial class ModernFlightDataCSharp : MyUserControl, IActivate, IDeactivate
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private const int TelemetryMinWidth = 220;
        private const int TelemetryDefaultWidth = 224;
        private const int CenterContentMinWidth = 560;
        private const int HudMinWidth = 300;
        private const int MapDeckMinWidth = 520;
        private const int HudDeckMinHeight = 320;
        private const int PreviewDeckMinHeight = 160;
        private const int ActionConsoleMinWidth = 360;
        private const int ActionConsoleMaxWidth = 720;
        private const int ActionConsoleDefaultWidth = 480;
        private const double HudColumnDefaultRatio = 0.32;
        private const double HudDeckDefaultRatio = 0.60;

        // Custom controls
        private TopStatusRail statusRail;
        private SplitContainer splitMain;
        private SplitContainer splitWorkspace;
        private SplitContainer splitCenter;
        private SplitContainer splitHudStack;
        private PanelTelemetry panelTelemetry;
        private PanelHudDeck panelHudDeck;
        private PanelAuxiliaryDeck panelAuxiliaryDeck;
        private ControlArtificialHorizon hudDisplay;
        private PanelMap3DDeck panelMap3D;
        private PanelQuickActions panelActions;
        private System.Windows.Forms.Timer telemetryTimer;
        private int? preferredHudWidth;
        private int? preferredHudDeckHeight;
        private int? preferredActionWidth;
        private bool suppressSplitterPreferenceCapture = true;
        private bool pendingDeferredLayout;
        private bool deferredLayoutQueued;
        private bool timelineSeeded;
        private bool? lastTimelineConnected;
        private bool? lastTimelineArmed;
        private string lastTimelineMode = "";
        private string lastTimelineAlert = "";
        private int lastTimelineWaypoint = -1;

        // Theme colors
        private readonly Color DarkNavy = Color.FromArgb(26, 31, 46);        // #1A1F2E
        private readonly Color VeryDarkNavy = Color.FromArgb(10, 14, 20);    // #0A0E14
        private readonly Color Charcoal = Color.FromArgb(40, 45, 60);        // #282D3C
        private readonly Color LightGray = Color.FromArgb(220, 220, 220);    // #DCDCDC
        private readonly Color MediumGray = Color.FromArgb(156, 163, 175);   // #9CA3AF
        private readonly Color Gold = Color.FromArgb(200, 168, 101);         // #C8A865

        public ModernFlightDataCSharp()
        {
            InitializeComponent();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            DoubleBuffered = true;
            Resize += ModernFlightDataCSharp_Resize;
            EnsureTimelineSeeded();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            statusRail = new TopStatusRail
            {
                Dock = DockStyle.Top,
                Height = 104,
                BackColor = VeryDarkNavy
            };

            // Main container - 3-panel layout
            splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterWidth = 1,
                FixedPanel = FixedPanel.Panel1,
                BackColor = VeryDarkNavy,
                BorderStyle = BorderStyle.None
            };
            // LEFT PANEL - Telemetry
            panelTelemetry = new PanelTelemetry
            {
                Dock = DockStyle.Fill,
                BackColor = VeryDarkNavy
            };
            splitMain.Panel1.Controls.Add(panelTelemetry);

            // CENTER PANEL - HUD + 3D Display
            splitCenter = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                FixedPanel = FixedPanel.Panel1,
                BorderStyle = BorderStyle.None,
                BackColor = VeryDarkNavy
            };
            splitCenter.SplitterMoved += SplitCenter_SplitterMoved;
            hudDisplay = new ControlArtificialHorizon
            {
                Dock = DockStyle.Fill,
                BackColor = VeryDarkNavy
            };
            panelHudDeck = new PanelHudDeck(hudDisplay)
            {
                Dock = DockStyle.Fill,
                BackColor = VeryDarkNavy
            };

            panelAuxiliaryDeck = new PanelAuxiliaryDeck
            {
                Dock = DockStyle.Fill,
                BackColor = VeryDarkNavy
            };
            panelAuxiliaryDeck.VisibilityPreferenceChanged += PanelAuxiliaryDeck_VisibilityPreferenceChanged;

            splitHudStack = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 5,
                BorderStyle = BorderStyle.None,
                BackColor = VeryDarkNavy
            };
            splitHudStack.SplitterMoved += SplitHudStack_SplitterMoved;
            splitHudStack.Panel1.Controls.Add(panelHudDeck);
            splitHudStack.Panel2.Controls.Add(panelAuxiliaryDeck);
            splitCenter.Panel1.Controls.Add(splitHudStack);

            // Mission map (right center)
            panelMap3D = new PanelMap3DDeck
            {
                Dock = DockStyle.Fill,
                BackColor = VeryDarkNavy
            };
            splitCenter.Panel2.Controls.Add(panelMap3D);

            splitWorkspace = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                FixedPanel = FixedPanel.Panel2,
                BorderStyle = BorderStyle.None,
                BackColor = VeryDarkNavy
            };
            splitWorkspace.SplitterMoved += SplitWorkspace_SplitterMoved;
            splitWorkspace.Panel1.Controls.Add(splitCenter);

            // RIGHT PANEL - Quick Actions
            panelActions = new PanelQuickActions
            {
                Dock = DockStyle.Fill,
                MinimumSize = new Size(ActionConsoleMinWidth, 0),
                BackColor = VeryDarkNavy
            };
            panelActions.ClearTrackRequested += PanelActions_ClearTrackRequested;
            preferredActionWidth = panelActions.ActivePreferredWidth;
            splitWorkspace.Panel2.Controls.Add(panelActions);

            splitMain.Panel2.Controls.Add(splitWorkspace);

            var workspace = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = VeryDarkNavy,
                Padding = new Padding(12, 4, 12, 12)
            };
            workspace.Controls.Add(splitMain);

            this.Controls.Add(workspace);
            this.Controls.Add(statusRail);

            // Telemetry update timer
            telemetryTimer = new System.Windows.Forms.Timer();
            telemetryTimer.Interval = 100; // 10Hz
            telemetryTimer.Tick += TelemetryTimer_Tick;

            this.Name = "ModernFlightDataCSharp";
            this.BackColor = VeryDarkNavy;
            this.Size = new Size(1400, 800);

            this.ResumeLayout(false);
            ApplyAuxiliaryDeckVisibility();
            UpdateResponsiveLayout();

            log.Info("ModernFlightDataCSharp initialized");
        }

        private void ModernFlightDataCSharp_Resize(object sender, EventArgs e)
        {
            UpdateResponsiveLayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            TryRunDeferredLayout();
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            TryRunDeferredLayout();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            TryRunDeferredLayout();
        }

        private void ScheduleDeferredLayout(bool resetPreferences = false)
        {
            if (resetPreferences)
            {
                preferredHudWidth = null;
                preferredHudDeckHeight = null;
                preferredActionWidth = null;
            }

            pendingDeferredLayout = true;
            TryRunDeferredLayout();
        }

        private void TryRunDeferredLayout()
        {
            if (!pendingDeferredLayout || deferredLayoutQueued || IsDisposed || !IsHandleCreated || Parent == null || !Visible)
                return;

            deferredLayoutQueued = true;

            BeginInvoke((MethodInvoker)delegate
            {
                deferredLayoutQueued = false;

                if (IsDisposed || !IsHandleCreated || Parent == null || !Visible)
                    return;

                pendingDeferredLayout = false;
                UpdateResponsiveLayout();

                BeginInvoke((MethodInvoker)delegate
                {
                    if (!IsDisposed && IsHandleCreated && Parent != null && Visible)
                        UpdateResponsiveLayout();
                });
            });
        }

        private void UpdateResponsiveLayout()
        {
            if (splitMain == null || splitCenter == null || splitWorkspace == null || panelActions == null)
                return;

            suppressSplitterPreferenceCapture = true;

            try
            {
                int viewWidth = Math.Max(ClientSize.Width, 960);
                if (splitMain.Width > TelemetryMinWidth + CenterContentMinWidth + splitMain.SplitterWidth)
                {
                    int desiredTelemetryWidth = Math.Max(TelemetryMinWidth, Math.Min(TelemetryDefaultWidth, viewWidth / 6));
                    int maxTelemetryWidth = splitMain.Width - CenterContentMinWidth - splitMain.SplitterWidth;
                    splitMain.SplitterDistance = Math.Max(TelemetryMinWidth,
                        Math.Min(desiredTelemetryWidth, maxTelemetryWidth));
                }

                int workspaceAvailableWidth = Math.Max(0, splitWorkspace.Width - splitWorkspace.SplitterWidth);
                if (workspaceAvailableWidth > ActionConsoleMinWidth + CenterContentMinWidth)
                {
                    int minActionWidth = ActionConsoleMinWidth;
                    int minCenterWidth = CenterContentMinWidth;
                    int maxActionWidth = Math.Max(minActionWidth, workspaceAvailableWidth - minCenterWidth);

                    splitWorkspace.Panel1MinSize = minCenterWidth;
                    splitWorkspace.Panel2MinSize = minActionWidth;

                    int desiredActionWidth = preferredActionWidth ??
                                             Math.Max(minActionWidth, Math.Min(ActionConsoleDefaultWidth, ActionConsoleMaxWidth));
                    desiredActionWidth = Math.Max(minActionWidth, Math.Min(desiredActionWidth, maxActionWidth));
                    splitWorkspace.SplitterDistance = Math.Max(minCenterWidth,
                        Math.Min(workspaceAvailableWidth - desiredActionWidth, workspaceAvailableWidth - minActionWidth));
                }

                if (splitCenter.Width > HudMinWidth + MapDeckMinWidth + splitCenter.SplitterWidth)
                {
                    int desiredHudWidth = preferredHudWidth ?? Math.Max(HudMinWidth,
                        (int)Math.Round((splitCenter.Width - splitCenter.SplitterWidth) * HudColumnDefaultRatio));
                    int maxHudWidth = splitCenter.Width - MapDeckMinWidth - splitCenter.SplitterWidth;
                    splitCenter.SplitterDistance = Math.Max(HudMinWidth, Math.Min(desiredHudWidth, maxHudWidth));
                }

                if (splitHudStack != null)
                {
                    splitHudStack.Panel2Collapsed = false;

                    int stackAvailableHeight = Math.Max(0, splitHudStack.Height - splitHudStack.SplitterWidth);
                    if (stackAvailableHeight > HudDeckMinHeight + PreviewDeckMinHeight)
                    {
                        splitHudStack.Panel1MinSize = HudDeckMinHeight;
                        splitHudStack.Panel2MinSize = PreviewDeckMinHeight;

                        int desiredHudHeight = preferredHudDeckHeight ??
                                               Math.Max(HudDeckMinHeight,
                                                   (int)Math.Round(stackAvailableHeight * HudDeckDefaultRatio));
                        int maxHudHeight = stackAvailableHeight - PreviewDeckMinHeight;
                        splitHudStack.SplitterDistance = Math.Max(HudDeckMinHeight,
                            Math.Min(desiredHudHeight, maxHudHeight));
                    }
                }
            }
            finally
            {
                suppressSplitterPreferenceCapture = false;
            }
        }

        private void SplitCenter_SplitterMoved(object sender, SplitterEventArgs e)
        {
            if (splitCenter == null || suppressSplitterPreferenceCapture)
                return;

            preferredHudWidth = splitCenter.SplitterDistance;
        }

        private void SplitWorkspace_SplitterMoved(object sender, SplitterEventArgs e)
        {
            if (splitWorkspace == null || suppressSplitterPreferenceCapture)
                return;

            preferredActionWidth = splitWorkspace.Panel2.Width;
        }

        private void PanelActions_ClearTrackRequested(object sender, EventArgs e)
        {
            panelMap3D?.ClearTrack();

            if (MainV2.comPort?.MAV?.camerapoints != null)
                MainV2.comPort.MAV.camerapoints.Clear();
        }

        private void SplitHudStack_SplitterMoved(object sender, SplitterEventArgs e)
        {
            if (splitHudStack == null || suppressSplitterPreferenceCapture)
                return;

            preferredHudDeckHeight = splitHudStack.SplitterDistance;
        }

        private void PanelAuxiliaryDeck_VisibilityPreferenceChanged(object sender, EventArgs e)
        {
            ApplyAuxiliaryDeckVisibility();
            UpdateResponsiveLayout();
        }

        private void ApplyAuxiliaryDeckVisibility()
        {
            if (splitHudStack == null || panelAuxiliaryDeck == null)
                return;

            suppressSplitterPreferenceCapture = true;

            try
            {
                splitHudStack.Panel2Collapsed = false;
            }
            finally
            {
                suppressSplitterPreferenceCapture = false;
            }
        }

        public void ResetToDefaultLayout()
        {
            ScheduleDeferredLayout(true);
        }

        private void TelemetryTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                bool isConnected = MainV2.comPort?.BaseStream?.IsOpen == true;
                var cs = MainV2.comPort?.MAV?.cs;

                if (cs == null)
                {
                    hudDisplay.Connected = isConnected;
                    hudDisplay.Armed = false;
                    hudDisplay.Mode = "STANDBY";
                    hudDisplay.Pitch = 0;
                    hudDisplay.Roll = 0;
                    hudDisplay.Heading = 0;
                    hudDisplay.GroundCourse = 0;
                    hudDisplay.TargetHeading = 0;
                    hudDisplay.Altitude = 0;
                    hudDisplay.TargetAltitude = 0;
                    hudDisplay.GroundSpeed = 0;
                    hudDisplay.AirSpeed = 0;
                    hudDisplay.TargetSpeed = 0;
                    hudDisplay.VerticalSpeed = 0;
                    hudDisplay.AngleOfAttack = 0;
                    hudDisplay.CriticalAngleOfAttack = 0;
                    hudDisplay.DistanceToWaypoint = 0;
                    hudDisplay.WaypointNumber = 0;
                    hudDisplay.DistanceToHome = 0;
                    hudDisplay.AzToMav = 0;
                    hudDisplay.BatteryRemaining = 0;
                    hudDisplay.Invalidate();
                    statusRail.SetOffline(isConnected);
                    panelHudDeck.SetOffline(isConnected);
                    panelAuxiliaryDeck.SetOffline(isConnected);
                    panelMap3D.SetOffline(isConnected);
                    panelActions.UpdateTelemetry(null, isConnected);
                    TrackFlightEvents(null, isConnected);
                    return;
                }

                // Update HUD
                hudDisplay.Pitch = (float)cs.pitch;
                hudDisplay.Roll = (float)cs.roll;
                hudDisplay.Heading = (float)cs.yaw;
                hudDisplay.GroundCourse = cs.groundcourse;
                hudDisplay.TargetHeading = cs.nav_bearing;
                hudDisplay.Mode = cs.mode;
                hudDisplay.Armed = cs.armed;
                hudDisplay.Connected = isConnected;
                hudDisplay.Altitude = cs.altasl;
                hudDisplay.TargetAltitude = cs.targetalt;
                hudDisplay.GroundSpeed = cs.groundspeed;
                hudDisplay.AirSpeed = cs.airspeed;
                hudDisplay.TargetSpeed = cs.targetairspeed;
                hudDisplay.VerticalSpeed = cs.verticalspeed;
                hudDisplay.AngleOfAttack = cs.AOA;
                hudDisplay.CriticalAngleOfAttack = cs.crit_AOA;
                hudDisplay.DistanceToWaypoint = cs.wp_dist;
                hudDisplay.WaypointNumber = (int)Math.Round(cs.wpno);
                hudDisplay.DistanceToHome = cs.DistToHome;
                hudDisplay.AzToMav = cs.AZToMAV;
                hudDisplay.BatteryRemaining = cs.battery_remaining;
                hudDisplay.Invalidate();
                panelHudDeck.UpdateFlightState(cs.mode, cs.armed, (float)cs.yaw, cs.battery_remaining);
                panelAuxiliaryDeck.UpdateTelemetry(cs);

                panelMap3D.UpdateTelemetry(cs);

                // Update telemetry
                panelTelemetry.UpdateTelemetry(cs);

                // Update status and actions
                statusRail.UpdateTelemetry(cs, isConnected);
                panelActions.UpdateTelemetry(cs, isConnected);
                TrackFlightEvents(cs, isConnected);
            }
            catch (Exception ex)
            {
                log.Debug($"Telemetry update error: {ex.Message}");
            }
        }

        private void EnsureTimelineSeeded()
        {
            if (timelineSeeded || panelActions == null)
                return;

            panelActions.PushEvent(FlightEventSeverity.Info, "Mission deck ready",
                "Modern flight is online and waiting for live telemetry.");
            timelineSeeded = true;
        }

        private void TrackFlightEvents(CurrentState cs, bool connected)
        {
            EnsureTimelineSeeded();

            if (lastTimelineConnected == null || lastTimelineConnected.Value != connected)
            {
                panelActions.PushEvent(
                    connected ? FlightEventSeverity.Success : FlightEventSeverity.Warning,
                    connected ? "Vehicle link detected" : "Vehicle link lost",
                    connected ? "Live MAVLink telemetry is now available." : "The flight deck is waiting for the vehicle stream.");
            }

            if (cs == null)
            {
                lastTimelineConnected = connected;
                lastTimelineArmed = null;
                lastTimelineMode = "";
                lastTimelineAlert = "";
                lastTimelineWaypoint = -1;
                return;
            }

            if (lastTimelineArmed == null || lastTimelineArmed.Value != cs.armed)
            {
                panelActions.PushEvent(
                    cs.armed ? FlightEventSeverity.Success : FlightEventSeverity.Warning,
                    cs.armed ? "Vehicle armed" : "Vehicle safe",
                    cs.armed ? "Mission-critical actions are now available." : "The aircraft has returned to a safe state.");
            }

            string mode = string.IsNullOrWhiteSpace(cs.mode) ? "UNKNOWN" : cs.mode.ToUpperInvariant();
            if (!string.Equals(lastTimelineMode, mode, StringComparison.OrdinalIgnoreCase))
            {
                panelActions.PushEvent(FlightEventSeverity.Info, $"Mode changed to {mode}",
                    $"WP {cs.wpno:F0}  |  Alt {cs.altasl:F0} m  |  GS {cs.groundspeed:F1} m/s");
            }

            string alert = string.IsNullOrWhiteSpace(cs.messageHigh) ? "" : cs.messageHigh.Trim();
            if (!string.IsNullOrWhiteSpace(alert) &&
                !string.Equals(lastTimelineAlert, alert, StringComparison.OrdinalIgnoreCase))
            {
                panelActions.PushEvent(GetSeverity(cs.messageHighSeverity), "Flight alert", alert);
            }

            int waypoint = (int)Math.Round(cs.wpno);
            if (waypoint > 0 && waypoint != lastTimelineWaypoint)
            {
                panelActions.PushEvent(FlightEventSeverity.Info, $"Waypoint {waypoint} active",
                    $"{cs.wp_dist:F0} m remaining to the current target.");
            }

            lastTimelineConnected = connected;
            lastTimelineArmed = cs.armed;
            lastTimelineMode = mode;
            lastTimelineAlert = alert;
            lastTimelineWaypoint = waypoint;
        }

        private static FlightEventSeverity GetSeverity(MAVLink.MAV_SEVERITY severity)
        {
            switch (severity)
            {
                case MAVLink.MAV_SEVERITY.EMERGENCY:
                case MAVLink.MAV_SEVERITY.ALERT:
                case MAVLink.MAV_SEVERITY.CRITICAL:
                case MAVLink.MAV_SEVERITY.ERROR:
                    return FlightEventSeverity.Danger;
                case MAVLink.MAV_SEVERITY.WARNING:
                    return FlightEventSeverity.Warning;
                case MAVLink.MAV_SEVERITY.NOTICE:
                    return FlightEventSeverity.Success;
                default:
                    return FlightEventSeverity.Info;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                telemetryTimer?.Stop();
                telemetryTimer?.Dispose();
                panelAuxiliaryDeck?.DeactivateView();
                panelMap3D?.DeactivateView();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// IActivate interface implementation - called when view is shown
        /// </summary>
        public void Activate()
        {
            try
            {
                ScheduleDeferredLayout();
                panelAuxiliaryDeck?.ActivateView();
                panelMap3D?.ActivateView();

                if (telemetryTimer != null && !telemetryTimer.Enabled)
                {
                    telemetryTimer.Start();
                    log.Info("ModernFlightData: Telemetry timer started");
                }
            }
            catch (Exception ex)
            {
                log.Error("Error activating ModernFlightData: " + ex.Message);
            }
        }

        /// <summary>
        /// IDeactivate interface implementation - called when view is hidden
        /// </summary>
        public void Deactivate()
        {
            try
            {
                if (telemetryTimer != null && telemetryTimer.Enabled)
                {
                    telemetryTimer.Stop();
                    log.Info("ModernFlightData: Telemetry timer stopped");
                }

                panelAuxiliaryDeck?.DeactivateView();
                panelMap3D?.DeactivateView();
            }
            catch (Exception ex)
            {
                log.Error("Error deactivating ModernFlightData: " + ex.Message);
            }
        }
    }

    internal struct StatusCard
    {
        public string Label;
        public string Value;
        public string Detail;
        public Color Accent;

        public StatusCard(string label, string value, string detail, Color accent)
        {
            Label = label;
            Value = value;
            Detail = detail;
            Accent = accent;
        }
    }

    internal static class ModernUiPainter
    {
        public static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            int safeRadius = Math.Max(2, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
            int diameter = safeRadius * 2;

            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRoundedRectangle(Graphics graphics, Color color, Rectangle bounds, int radius)
        {
            using (var brush = new SolidBrush(color))
            using (var path = CreateRoundedPath(bounds, radius))
            {
                graphics.FillPath(brush, path);
            }
        }

        public static void DrawRoundedRectangle(Graphics graphics, Color color, float width, Rectangle bounds, int radius)
        {
            using (var pen = new Pen(color, width))
            using (var path = CreateRoundedPath(bounds, radius))
            {
                graphics.DrawPath(pen, path);
            }
        }

        public static Color WithAlpha(Color color, int alpha)
        {
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        public static Color GetBatteryColor(int remaining)
        {
            if (remaining <= 20)
                return Color.FromArgb(228, 84, 71);

            if (remaining <= 50)
                return Color.FromArgb(230, 174, 68);

            return Color.FromArgb(72, 182, 132);
        }

        public static Color GetGpsColor(float fixType, float sats)
        {
            if (fixType < 3 || sats < 6)
                return Color.FromArgb(228, 84, 71);

            if (sats < 10)
                return Color.FromArgb(230, 174, 68);

            return Color.FromArgb(72, 182, 132);
        }

        public static Color GetLinkColor(bool connected, int quality)
        {
            if (!connected)
                return Color.FromArgb(111, 120, 138);

            if (quality < 40)
                return Color.FromArgb(228, 84, 71);

            if (quality < 75)
                return Color.FromArgb(230, 174, 68);

            return Color.FromArgb(72, 182, 132);
        }

        public static string FormatDuration(double seconds)
        {
            int safeSeconds = Math.Max(0, (int)Math.Round(seconds));
            var span = TimeSpan.FromSeconds(safeSeconds);

            if (span.TotalHours >= 1)
                return $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";

            return $"{span.Minutes:00}:{span.Seconds:00}";
        }

        public static string FormatHeadingLabel(int degrees)
        {
            int normalized = ((degrees % 360) + 360) % 360;

            if (normalized == 0)
                return "N";
            if (normalized == 45)
                return "NE";
            if (normalized == 90)
                return "E";
            if (normalized == 135)
                return "SE";
            if (normalized == 180)
                return "S";
            if (normalized == 225)
                return "SW";
            if (normalized == 270)
                return "W";
            if (normalized == 315)
                return "NW";

            return normalized.ToString("000");
        }
    }

    public class TopStatusRail : Control
    {
        private const string TopStatusSelectionSettingKey = "ModernFlightTopStatusCards";
        private const string TopStatusCustomizationSettingKey = "ModernFlightTopStatusCustomizations";
        private static readonly string[] DefaultMetricKeys =
        {
            "link",
            "time_in_air",
            "distance_travelled",
            "distance_to_home",
            "alert"
        };
        private static readonly StatusMetricDefinition[] MetricCatalog =
        {
            new StatusMetricDefinition("link", "Link"),
            new StatusMetricDefinition("mode", "Mode"),
            new StatusMetricDefinition("battery", "Battery"),
            new StatusMetricDefinition("gps", "GPS"),
            new StatusMetricDefinition("alert", "Alert"),
            new StatusMetricDefinition("time_in_air", "Time in Air"),
            new StatusMetricDefinition("distance_travelled", "Distance Travelled"),
            new StatusMetricDefinition("distance_to_home", "Distance to Home")
        };

        private readonly Font eyebrowFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private readonly Font titleFont = new Font("Segoe UI", 17f, FontStyle.Bold);
        private readonly Font cardLabelFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        private readonly Font cardValueFont = new Font("Segoe UI", 10.75f, FontStyle.Bold);
        private readonly Font cardDetailFont = new Font("Segoe UI", 7.25f, FontStyle.Regular);

        private readonly Color TitleColor = Color.FromArgb(232, 236, 242);
        private readonly Color MutedText = Color.FromArgb(140, 149, 166);
        private readonly Color CardBackground = Color.FromArgb(28, 35, 50);
        private readonly Color CardBorder = Color.FromArgb(48, 58, 78);
        private readonly Color LinkAccent = Color.FromArgb(88, 193, 232);
        private readonly Color TimeInAirAccent = Color.FromArgb(224, 183, 77);
        private readonly Color DistanceTraveledAccent = Color.FromArgb(235, 142, 74);
        private readonly Color DistanceToHomeAccent = Color.FromArgb(142, 124, 232);
        private readonly Color AlertNominalAccent = Color.FromArgb(92, 202, 142);
        private readonly StatusCard[] cards = new StatusCard[5];
        private readonly Rectangle[] cardHitAreas = new Rectangle[5];
        private readonly List<string> selectedMetricKeys = new List<string>();
        private readonly List<StatusCardCustomization> customCardConfigs = new List<StatusCardCustomization>();
        private readonly ContextMenuStrip cardMenu = new ContextMenuStrip();
        private CurrentState lastTelemetry;
        private bool lastConnected;
        private int activeCardIndex = -1;

        public TopStatusRail()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            DoubleBuffered = true;
            Padding = new Padding(18, 16, 18, 14);
            Cursor = Cursors.Default;
            InitializeCardMenu();
            LoadMetricSelection();
            LoadMetricCustomizations();
            SetOffline(false);
        }

        public void SetOffline(bool connected)
        {
            lastConnected = connected;
            lastTelemetry = null;
            ApplyCards(null, connected);
        }

        public void UpdateTelemetry(CurrentState cs, bool connected)
        {
            lastTelemetry = cs;
            lastConnected = connected;
            ApplyCards(cs, connected);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);

            int titleWidth = Math.Min(250, Math.Max(190, Width / 5));
            var eyebrowRect = new Rectangle(Padding.Left, Padding.Top, titleWidth, 16);
            var titleRect = new Rectangle(Padding.Left, Padding.Top + 18, titleWidth, 34);
            var subtitleRect = new Rectangle(Padding.Left, Padding.Top + 50, titleWidth, 28);

            using (var eyebrowBrush = new SolidBrush(Color.FromArgb(200, 168, 101)))
            using (var titleBrush = new SolidBrush(TitleColor))
            using (var subtitleBrush = new SolidBrush(MutedText))
            {
                e.Graphics.DrawString("BRIECH UAS", eyebrowFont, eyebrowBrush, eyebrowRect);
                e.Graphics.DrawString("Mission Control", titleFont, titleBrush, titleRect);
                e.Graphics.DrawString("Live airframe, mission, distance, and alert state", cardDetailFont, subtitleBrush, subtitleRect);
            }

            int gap = 12;
            int cardStartX = Padding.Left + titleWidth + 18;
            int availableWidth = Width - cardStartX - Padding.Right;
            int cardHeight = Height - Padding.Top - Padding.Bottom;
            int cardWidth = Math.Max(128, (availableWidth - gap * (cards.Length - 1)) / cards.Length);

            for (int i = 0; i < cards.Length; i++)
            {
                var cardRect = new Rectangle(cardStartX + i * (cardWidth + gap), Padding.Top, cardWidth, cardHeight);
                cardHitAreas[i] = cardRect;
                DrawCard(e.Graphics, cardRect, cards[i]);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = HitTestCardIndex(e.Location) >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Cursor = Cursors.Default;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            int cardIndex = HitTestCardIndex(e.Location);
            if (cardIndex < 0)
                return;

            OpenStatusCardEditor(cardIndex);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button != MouseButtons.Right)
                return;

            int cardIndex = HitTestCardIndex(e.Location);
            if (cardIndex < 0)
                return;

            activeCardIndex = cardIndex;
            RebuildCardMenu();
            cardMenu.Show(this, e.Location);
        }

        private void DrawCard(Graphics graphics, Rectangle bounds, StatusCard card)
        {
            ModernUiPainter.FillRoundedRectangle(graphics, CardBackground, bounds, 18);
            ModernUiPainter.DrawRoundedRectangle(graphics, CardBorder, 1f, bounds, 18);

            var accentRect = new Rectangle(bounds.X + 10, bounds.Y + 10, Math.Max(28, bounds.Width / 3), 4);
            ModernUiPainter.FillRoundedRectangle(graphics, card.Accent, accentRect, 2);

            var textFormat = new StringFormat
            {
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter
            };

            float contentX = bounds.X + 14;
            float contentWidth = bounds.Width - 28;
            float labelY = bounds.Y + 20;
            float valueY = bounds.Y + 34;
            float detailY = bounds.Y + 52;
            Color valueColor = Blend(TitleColor, card.Accent, 0.82f);

            using (var labelBrush = new SolidBrush(MutedText))
            using (var valueBrush = new SolidBrush(valueColor))
            using (var detailBrush = new SolidBrush(ModernUiPainter.WithAlpha(card.Accent, 215)))
            {
                graphics.DrawString(card.Label, cardLabelFont, labelBrush,
                    new RectangleF(contentX, labelY, contentWidth, 12), textFormat);
                graphics.DrawString(card.Value, cardValueFont, valueBrush,
                    new RectangleF(contentX, valueY, contentWidth, 16), textFormat);
                graphics.DrawString(card.Detail, cardDetailFont, detailBrush,
                    new RectangleF(contentX, detailY, contentWidth, 12), textFormat);
            }

            textFormat.Dispose();
        }

        private void InitializeCardMenu()
        {
            cardMenu.ShowImageMargin = false;
            cardMenu.BackColor = Color.FromArgb(23, 29, 41);
            cardMenu.ForeColor = TitleColor;
        }

        private void RebuildCardMenu()
        {
            cardMenu.Items.Clear();

            for (int i = 0; i < MetricCatalog.Length; i++)
            {
                var metric = MetricCatalog[i];
                int existingIndex = selectedMetricKeys.IndexOf(metric.Key);
                bool isCurrent = activeCardIndex >= 0 &&
                    activeCardIndex < selectedMetricKeys.Count &&
                    selectedMetricKeys[activeCardIndex] == metric.Key;

                string label = metric.Label;
                if (existingIndex >= 0 && existingIndex != activeCardIndex)
                    label += "  (Swap)";

                var item = new ToolStripMenuItem(label)
                {
                    Checked = isCurrent,
                    CheckOnClick = false,
                    Tag = metric.Key
                };
                item.Click += CardMenuItem_Click;
                cardMenu.Items.Add(item);
            }

            cardMenu.Items.Add(new ToolStripSeparator());

            var resetItem = new ToolStripMenuItem("Reset Top Cards");
            resetItem.Click += (s, e) =>
            {
                selectedMetricKeys.Clear();
                selectedMetricKeys.AddRange(DefaultMetricKeys);
                customCardConfigs.Clear();
                SaveMetricSelection();
                SaveMetricCustomizations();
                ApplyCards(lastTelemetry, lastConnected);
            };
            cardMenu.Items.Add(resetItem);
        }

        private void CardMenuItem_Click(object sender, EventArgs e)
        {
            if (!(sender is ToolStripMenuItem menuItem) || !(menuItem.Tag is string metricKey))
                return;

            if (activeCardIndex < 0 || activeCardIndex >= selectedMetricKeys.Count)
                return;

            int existingIndex = selectedMetricKeys.IndexOf(metricKey);
            string replacedKey = selectedMetricKeys[activeCardIndex];

            if (existingIndex >= 0 && existingIndex != activeCardIndex)
                selectedMetricKeys[existingIndex] = replacedKey;

            selectedMetricKeys[activeCardIndex] = metricKey;
            EnsureCustomizationSlots();
            customCardConfigs[activeCardIndex] = null;
            SaveMetricSelection();
            SaveMetricCustomizations();
            ApplyCards(lastTelemetry, lastConnected);
        }

        private void ApplyCards(CurrentState cs, bool connected)
        {
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i] = BuildCardForIndex(i, cs, connected);
            }

            Invalidate();
        }

        private StatusCard BuildCardForIndex(int cardIndex, CurrentState cs, bool connected)
        {
            if (cardIndex < 0 || cardIndex >= cards.Length)
                return new StatusCard("STATUS", "--", "Card unavailable", CardBorder);

            EnsureCustomizationSlots();

            var customization = customCardConfigs[cardIndex];
            if (customization != null && !string.IsNullOrWhiteSpace(customization.SourceTag))
                return BuildCustomizedCard(customization, cs, connected);

            string metricKey = cardIndex < selectedMetricKeys.Count
                ? selectedMetricKeys[cardIndex]
                : DefaultMetricKeys[Math.Min(cardIndex, DefaultMetricKeys.Length - 1)];

            if (MetricCatalog.Any(metric => metric.Key == metricKey))
                return BuildCard(metricKey, cs, connected);

            return BuildCustomizedCard(new StatusCardCustomization
            {
                SourceTag = metricKey,
                Label = GetTelemetryDisplayName(ResolveTelemetrySourceKey(metricKey), cs),
                AccentColor = GetDefaultAccent(metricKey),
                NumberFormat = ResolveEditorFormat(metricKey),
                Scale = 1.0,
                Offset = 0.0,
                GaugeEnabled = false,
                GaugeMin = 0.0,
                GaugeMax = 100.0
            }, cs, connected);
        }

        private StatusCard BuildCard(string key, CurrentState cs, bool connected)
        {
            switch (key)
            {
                case "link":
                    int linkQuality = cs == null ? 0 : Math.Max(0, Math.Min(100, (int)cs.linkqualitygcs));
                    return new StatusCard(
                        "LINK",
                        connected ? $"{linkQuality}%" : "OFFLINE",
                        connected ? "Live MAVLink session" : "Vehicle disconnected",
                        connected ? ModernUiPainter.GetLinkColor(true, linkQuality) : LinkAccent);

                case "mode":
                    string mode = cs == null || string.IsNullOrWhiteSpace(cs.mode) ? "UNKNOWN" : cs.mode.ToUpperInvariant();
                    return new StatusCard(
                        "MODE",
                        mode,
                        cs != null && cs.armed ? "Armed and mission-capable" : "Safe state / preflight",
                        Color.FromArgb(102, 164, 229));

                case "battery":
                    int battery = cs == null ? 0 : Math.Max(0, Math.Min(100, cs.battery_remaining));
                    return new StatusCard(
                        "BATTERY",
                        cs == null ? "--" : $"{battery}%",
                        cs == null ? "No power data" : $"{cs.battery_voltage:F1} V  |  {cs.watts:F0} W",
                        Color.FromArgb(230, 99, 83));

                case "gps":
                    return new StatusCard(
                        "GPS",
                        cs == null ? "--" : $"{cs.satcount:F0} SAT",
                        cs == null ? "No navigation data" : $"Fix {cs.gpsstatus:F0}  |  HDOP {cs.gpshdop:0.0}",
                        Color.FromArgb(74, 201, 176));

                case "time_in_air":
                    return new StatusCard(
                        "TIME IN AIR",
                        cs == null ? "--" : ModernUiPainter.FormatDuration(cs.timeInAir),
                        connected
                            ? (cs != null && cs.armed ? "Mission timer active" : "Preflight timer ready")
                            : "Waiting for vehicle link",
                        TimeInAirAccent);

                case "distance_travelled":
                    return new StatusCard(
                        "DIST. TRAVELLED",
                        cs == null ? "--" : $"{cs.distTraveled:F0} m",
                        connected && cs != null
                            ? $"{cs.groundspeed:F1} m/s groundspeed"
                            : "Awaiting position telemetry",
                        DistanceTraveledAccent);

                case "distance_to_home":
                    return new StatusCard(
                        "DIST. TO HOME",
                        cs == null ? "--" : $"{cs.DistToHome:F0} m",
                        connected && cs != null
                            ? $"WP {cs.wpno:F0}  |  {cs.wp_dist:F0} m to waypoint"
                            : "Awaiting home reference",
                        DistanceToHomeAccent);

                case "alert":
                default:
                    string alert = cs == null || string.IsNullOrWhiteSpace(cs.messageHigh) ? "NOMINAL" : cs.messageHigh.ToUpperInvariant();
                    return new StatusCard(
                        "ALERT",
                        connected ? alert : "STANDBY",
                        connected && cs != null
                            ? $"WP {cs.wpno:F0}  |  {cs.wp_dist:F0} m to waypoint"
                            : "Modern flight deck ready",
                        string.Equals(alert, "NOMINAL", StringComparison.OrdinalIgnoreCase)
                            ? AlertNominalAccent
                            : Color.FromArgb(228, 84, 71));
            }
        }

        private StatusCard BuildCustomizedCard(StatusCardCustomization customization, CurrentState cs, bool connected)
        {
            string sourceKey = ResolveTelemetrySourceKey(customization.SourceTag);
            string label = string.IsNullOrWhiteSpace(customization.Label)
                ? GetTelemetryDisplayName(sourceKey, cs)
                : customization.Label;
            string detail = BuildStatusDetail(customization, sourceKey, cs, connected);
            Color accent = customization.AccentColor;

            if (cs == null)
                return new StatusCard(label, "--", detail, accent);

            object rawValue = typeof(CurrentState).GetProperty(sourceKey ?? string.Empty)?.GetValue(cs, null);
            return new StatusCard(label, FormatStatusValue(rawValue, customization), detail, accent);
        }

        private void LoadMetricSelection()
        {
            selectedMetricKeys.Clear();

            try
            {
                var rawValue = Settings.Instance[TopStatusSelectionSettingKey];
                if (rawValue != null)
                {
                    foreach (string token in rawValue.ToString().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string key = token.Trim();
                        if ((MetricCatalog.Any(metric => metric.Key == key) || IsSupportedTelemetryKey(key)) &&
                            !selectedMetricKeys.Contains(key))
                            selectedMetricKeys.Add(key);

                        if (selectedMetricKeys.Count == cards.Length)
                            break;
                    }
                }
            }
            catch
            {
            }

            foreach (string key in DefaultMetricKeys)
            {
                if (selectedMetricKeys.Count == cards.Length)
                    break;

                if (!selectedMetricKeys.Contains(key))
                    selectedMetricKeys.Add(key);
            }

            foreach (var metric in MetricCatalog)
            {
                if (selectedMetricKeys.Count == cards.Length)
                    break;

                if (!selectedMetricKeys.Contains(metric.Key))
                    selectedMetricKeys.Add(metric.Key);
            }
        }

        private void SaveMetricSelection()
        {
            try
            {
                EnsureCustomizationSlots();
                Settings.Instance[TopStatusSelectionSettingKey] = string.Join(",", selectedMetricKeys);
                Settings.Instance.Save();
            }
            catch
            {
            }
        }

        private void LoadMetricCustomizations()
        {
            customCardConfigs.Clear();

            try
            {
                var rawValue = Settings.Instance[TopStatusCustomizationSettingKey];
                if (rawValue != null)
                {
                    foreach (string token in rawValue.ToString().Split(new[] { "||" }, StringSplitOptions.None))
                    {
                        customCardConfigs.Add(StatusCardCustomization.TryDeserialize(token));
                        if (customCardConfigs.Count == cards.Length)
                            break;
                    }
                }
            }
            catch
            {
            }

            EnsureCustomizationSlots();
        }

        private void SaveMetricCustomizations()
        {
            try
            {
                EnsureCustomizationSlots();
                Settings.Instance[TopStatusCustomizationSettingKey] = string.Join("||", customCardConfigs.Select(StatusCardCustomization.Serialize));
                Settings.Instance.Save();
            }
            catch
            {
            }
        }

        private void EnsureCustomizationSlots()
        {
            while (customCardConfigs.Count < cards.Length)
                customCardConfigs.Add(null);
        }

        private void OpenStatusCardEditor(int cardIndex)
        {
            if (cardIndex < 0 || cardIndex >= cards.Length)
                return;

            using (var proxy = CreateStatusQuickViewProxy(cardIndex))
            using (var editor = new QuickViewOptions(proxy))
            {
                editor.StartPosition = FormStartPosition.CenterParent;
                ThemeManager.ApplyThemeTo(editor);
                editor.ShowDialog(FindForm());
                ApplyStatusQuickViewProxy(cardIndex, proxy);
            }
        }

        private QuickView CreateStatusQuickViewProxy(int cardIndex)
        {
            EnsureCustomizationSlots();

            var proxy = new QuickView
            {
                Tag = ResolveEditorSourceTag(cardIndex),
                desc = ResolveEditorLabel(cardIndex),
                numberColor = ResolveEditorColor(cardIndex),
                numberColorBackup = ResolveEditorColor(cardIndex),
                numberformat = ResolveEditorFormat(cardIndex),
                scale = ResolveEditorScale(cardIndex),
                offset = ResolveEditorOffset(cardIndex),
                isGauge = ResolveEditorGaugeEnabled(cardIndex),
                gaugeMin = ResolveEditorGaugeMin(cardIndex),
                gaugeMax = ResolveEditorGaugeMax(cardIndex)
            };

            return proxy;
        }

        private void ApplyStatusQuickViewProxy(int cardIndex, QuickView proxy)
        {
            string sourceTag = proxy.Tag as string;
            if (string.IsNullOrWhiteSpace(sourceTag))
                return;

            EnsureCustomizationSlots();

            selectedMetricKeys[cardIndex] = sourceTag;
            customCardConfigs[cardIndex] = new StatusCardCustomization
            {
                SourceTag = sourceTag,
                Label = proxy.desc,
                AccentColor = proxy.numberColorBackup,
                NumberFormat = string.IsNullOrWhiteSpace(proxy.numberformat) ? "0.00" : proxy.numberformat,
                Scale = proxy.scale,
                Offset = proxy.offset,
                GaugeEnabled = proxy.isGauge,
                GaugeMin = proxy.gaugeMin,
                GaugeMax = proxy.gaugeMax
            };

            SaveMetricSelection();
            SaveMetricCustomizations();
            ApplyCards(lastTelemetry, lastConnected);
        }

        private string ResolveEditorSourceTag(int cardIndex)
        {
            EnsureCustomizationSlots();

            var customization = customCardConfigs[cardIndex];
            if (customization != null && !string.IsNullOrWhiteSpace(customization.SourceTag))
                return customization.SourceTag;

            string key = selectedMetricKeys[cardIndex];
            switch (key)
            {
                case "link":
                    return "linkqualitygcs";
                case "mode":
                    return "armed";
                case "battery":
                    return "battery_remaining";
                case "gps":
                    return "satcount";
                case "alert":
                    return "failsafe";
                case "time_in_air":
                    return "timeInAir";
                case "distance_travelled":
                    return "distTraveled";
                case "distance_to_home":
                    return "DistToHome";
                default:
                    return key;
            }
        }

        private string ResolveEditorLabel(int cardIndex)
        {
            EnsureCustomizationSlots();

            var customization = customCardConfigs[cardIndex];
            if (customization != null && !string.IsNullOrWhiteSpace(customization.Label))
                return customization.Label;

            return cardIndex >= 0 && cardIndex < cards.Length && !string.IsNullOrWhiteSpace(cards[cardIndex].Label)
                ? cards[cardIndex].Label
                : GetTelemetryDisplayName(ResolveTelemetrySourceKey(ResolveEditorSourceTag(cardIndex)), MainV2.comPort?.MAV?.cs);
        }

        private Color ResolveEditorColor(int cardIndex)
        {
            EnsureCustomizationSlots();

            var customization = customCardConfigs[cardIndex];
            if (customization != null)
                return customization.AccentColor;

            return cardIndex >= 0 && cardIndex < cards.Length
                ? cards[cardIndex].Accent
                : GetDefaultAccent(selectedMetricKeys[cardIndex]);
        }

        private string ResolveEditorFormat(int cardIndex)
        {
            EnsureCustomizationSlots();

            var customization = customCardConfigs[cardIndex];
            if (customization != null && !string.IsNullOrWhiteSpace(customization.NumberFormat))
                return customization.NumberFormat;

            return ResolveEditorFormat(ResolveEditorSourceTag(cardIndex));
        }

        private static string ResolveEditorFormat(string sourceTag)
        {
            switch (ResolveTelemetrySourceKey(sourceTag))
            {
                case "linkqualitygcs":
                case "battery_remaining":
                case "satcount":
                case "distTraveled":
                case "DistToHome":
                    return "0";
                case "timeInAir":
                    return "mm\\:ss";
                default:
                    return "0.00";
            }
        }

        private double ResolveEditorScale(int cardIndex)
        {
            EnsureCustomizationSlots();
            return customCardConfigs[cardIndex]?.Scale ?? 1.0;
        }

        private double ResolveEditorOffset(int cardIndex)
        {
            EnsureCustomizationSlots();
            return customCardConfigs[cardIndex]?.Offset ?? 0.0;
        }

        private bool ResolveEditorGaugeEnabled(int cardIndex)
        {
            EnsureCustomizationSlots();
            return customCardConfigs[cardIndex]?.GaugeEnabled ?? false;
        }

        private double ResolveEditorGaugeMin(int cardIndex)
        {
            EnsureCustomizationSlots();
            return customCardConfigs[cardIndex]?.GaugeMin ?? 0.0;
        }

        private double ResolveEditorGaugeMax(int cardIndex)
        {
            EnsureCustomizationSlots();
            return customCardConfigs[cardIndex]?.GaugeMax ?? 100.0;
        }

        private int HitTestCardIndex(Point location)
        {
            for (int i = 0; i < cardHitAreas.Length; i++)
            {
                if (cardHitAreas[i].Contains(location))
                    return i;
            }

            return -1;
        }

        private static Color Blend(Color baseColor, Color accentColor, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));

            return Color.FromArgb(
                255,
                (int)(baseColor.R + ((accentColor.R - baseColor.R) * amount)),
                (int)(baseColor.G + ((accentColor.G - baseColor.G) * amount)),
                (int)(baseColor.B + ((accentColor.B - baseColor.B) * amount)));
        }

        private static string ResolveTelemetrySourceKey(string sourceTag)
        {
            if (string.IsNullOrWhiteSpace(sourceTag))
                return "battery_remaining";

            if (sourceTag.StartsWith("customfield:", StringComparison.OrdinalIgnoreCase))
                return CurrentState.GetCustomField(sourceTag.Substring("customfield:".Length));

            return sourceTag;
        }

        private static bool IsSupportedTelemetryKey(string key)
        {
            string sourceKey = ResolveTelemetrySourceKey(key);
            var property = typeof(CurrentState).GetProperty(sourceKey ?? string.Empty);
            if (property == null)
                return false;

            Type propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (propertyType == typeof(bool))
                return true;

            return propertyType == typeof(byte) ||
                   propertyType == typeof(sbyte) ||
                   propertyType == typeof(short) ||
                   propertyType == typeof(ushort) ||
                   propertyType == typeof(int) ||
                   propertyType == typeof(uint) ||
                   propertyType == typeof(long) ||
                   propertyType == typeof(ulong) ||
                   propertyType == typeof(float) ||
                   propertyType == typeof(double) ||
                   propertyType == typeof(decimal);
        }

        private static string GetTelemetryDisplayName(string sourceKey, CurrentState cs)
        {
            if (cs == null || string.IsNullOrWhiteSpace(sourceKey))
                return "Telemetry";

            return cs.GetNameandUnit(sourceKey);
        }

        private static string BuildStatusDetail(StatusCardCustomization customization, string sourceKey, CurrentState cs, bool connected)
        {
            if (!connected)
                return "Awaiting vehicle telemetry";

            string defaultLabel = GetTelemetryDisplayName(sourceKey, cs);
            if (customization.GaugeEnabled)
                return $"Range {customization.GaugeMin:0.##} to {customization.GaugeMax:0.##}";

            if (string.IsNullOrWhiteSpace(customization.Label) ||
                customization.Label.Equals(defaultLabel, StringComparison.OrdinalIgnoreCase))
            {
                return defaultLabel;
            }

            return defaultLabel;
        }

        private static string FormatStatusValue(object rawValue, StatusCardCustomization customization)
        {
            if (rawValue == null)
                return "--";

            if (rawValue is bool booleanValue)
                return booleanValue ? "TRUE" : "FALSE";

            if (!TryConvertToDouble(rawValue, out double numericValue))
                return Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? "--";

            double adjustedValue = (numericValue * customization.Scale) + customization.Offset;
            string format = string.IsNullOrWhiteSpace(customization.NumberFormat) ? "0.00" : customization.NumberFormat;

            try
            {
                if (format.Contains(":"))
                    return TimeSpan.FromSeconds(adjustedValue).ToString(format);

                return adjustedValue.ToString(format, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return adjustedValue.ToString("0.00", CultureInfo.InvariantCulture);
            }
        }

        private static bool TryConvertToDouble(object rawValue, out double value)
        {
            if (rawValue == null)
            {
                value = 0;
                return false;
            }

            try
            {
                value = Convert.ToDouble(rawValue, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                value = 0;
                return false;
            }
        }

        private Color GetDefaultAccent(string key)
        {
            switch (key)
            {
                case "link":
                case "linkqualitygcs":
                    return LinkAccent;
                case "mode":
                    return Color.FromArgb(102, 164, 229);
                case "battery":
                case "battery_remaining":
                    return Color.FromArgb(230, 99, 83);
                case "gps":
                case "satcount":
                    return Color.FromArgb(74, 201, 176);
                case "time_in_air":
                case "timeInAir":
                    return TimeInAirAccent;
                case "distance_travelled":
                case "distTraveled":
                    return DistanceTraveledAccent;
                case "distance_to_home":
                case "DistToHome":
                    return DistanceToHomeAccent;
                case "alert":
                case "failsafe":
                    return AlertNominalAccent;
                default:
                    return Color.FromArgb(111, 120, 138);
            }
        }

        private sealed class StatusMetricDefinition
        {
            public StatusMetricDefinition(string key, string label)
            {
                Key = key;
                Label = label;
            }

            public string Key { get; }
            public string Label { get; }
        }

        private sealed class StatusCardCustomization
        {
            public string SourceTag { get; set; }
            public string Label { get; set; }
            public Color AccentColor { get; set; } = Color.FromArgb(111, 120, 138);
            public string NumberFormat { get; set; } = "0.00";
            public double Scale { get; set; } = 1.0;
            public double Offset { get; set; }
            public bool GaugeEnabled { get; set; }
            public double GaugeMin { get; set; }
            public double GaugeMax { get; set; } = 100.0;

            public static string Serialize(StatusCardCustomization customization)
            {
                if (customization == null || string.IsNullOrWhiteSpace(customization.SourceTag))
                    return string.Empty;

                return string.Join(";",
                    EncodePart(customization.SourceTag),
                    EncodePart(customization.Label),
                    customization.AccentColor.ToArgb().ToString(CultureInfo.InvariantCulture),
                    EncodePart(customization.NumberFormat),
                    customization.Scale.ToString("R", CultureInfo.InvariantCulture),
                    customization.Offset.ToString("R", CultureInfo.InvariantCulture),
                    customization.GaugeEnabled ? "1" : "0",
                    customization.GaugeMin.ToString("R", CultureInfo.InvariantCulture),
                    customization.GaugeMax.ToString("R", CultureInfo.InvariantCulture));
            }

            public static StatusCardCustomization TryDeserialize(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return null;

                try
                {
                    string[] parts = raw.Split(';');
                    if (parts.Length < 9)
                        return null;

                    return new StatusCardCustomization
                    {
                        SourceTag = DecodePart(parts[0]),
                        Label = DecodePart(parts[1]),
                        AccentColor = Color.FromArgb(int.Parse(parts[2], CultureInfo.InvariantCulture)),
                        NumberFormat = DecodePart(parts[3]),
                        Scale = double.Parse(parts[4], CultureInfo.InvariantCulture),
                        Offset = double.Parse(parts[5], CultureInfo.InvariantCulture),
                        GaugeEnabled = parts[6] == "1",
                        GaugeMin = double.Parse(parts[7], CultureInfo.InvariantCulture),
                        GaugeMax = double.Parse(parts[8], CultureInfo.InvariantCulture)
                    };
                }
                catch
                {
                    return null;
                }
            }

            private static string EncodePart(string value)
            {
                return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty));
            }

            private static string DecodePart(string value)
            {
                return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cardMenu?.Dispose();
                eyebrowFont?.Dispose();
                titleFont?.Dispose();
                cardLabelFont?.Dispose();
                cardValueFont?.Dispose();
                cardDetailFont?.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    // ControlArtificialHorizon moved to its own file for maintainability.

    public class PanelHudDeck : Panel
    {
        private readonly Color Surface = Color.FromArgb(24, 31, 44);
        private readonly Color Border = Color.FromArgb(44, 54, 73);
        private readonly Color Gold = Color.FromArgb(200, 168, 101);
        private readonly Color ArmedAccent = Color.FromArgb(72, 182, 132);
        private readonly Color SafeAccent = Color.FromArgb(228, 84, 71);
        private readonly Color StandbyAccent = Color.FromArgb(110, 118, 136);

        private readonly ControlArtificialHorizon hudControl;
        private Panel hudBorderPanel;
        private Panel hudHostPanel;

        public PanelHudDeck(ControlArtificialHorizon hudControl)
        {
            this.hudControl = hudControl ?? throw new ArgumentNullException(nameof(hudControl));

            BackColor = Color.FromArgb(10, 14, 20);
            Padding = new Padding(6);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            InitializeControls();
            SetOffline(false);
        }

        private void InitializeControls()
        {
            hudBorderPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Border,
                Padding = new Padding(1)
            };
            Controls.Add(hudBorderPanel);

            hudHostPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(6, 10, 18),
                Padding = new Padding(1)
            };
            hudBorderPanel.Controls.Add(hudHostPanel);

            hudControl.Dock = DockStyle.Fill;
            hudHostPanel.Controls.Add(hudControl);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
        }

        public void SetOffline(bool connected)
        {
            ApplyDeckState(connected ? Gold : StandbyAccent);
        }

        public void UpdateFlightState(string mode, bool armed, float heading, int batteryRemaining)
        {
            ApplyDeckState(armed ? ArmedAccent : SafeAccent);
        }

        private void ApplyDeckState(Color accent)
        {
            hudBorderPanel.BackColor = accent == ArmedAccent
                ? Color.FromArgb(44, 79, 68)
                : accent == SafeAccent
                    ? Color.FromArgb(88, 48, 52)
                    : accent == Gold
                        ? Color.FromArgb(86, 72, 40)
                        : Border;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
    }

    public class PanelSituationalPreviewDeck : Panel
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly Font eyebrowFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private readonly Font titleFont = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        private readonly Font subtitleFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        private readonly Font badgeFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private readonly Font stateFont = new Font("Segoe UI", 9.25f, FontStyle.Regular);

        private readonly Color Surface = Color.FromArgb(24, 31, 44);
        private readonly Color Border = Color.FromArgb(44, 54, 73);
        private readonly Color Gold = Color.FromArgb(200, 168, 101);
        private readonly Color LightGray = Color.FromArgb(235, 239, 245);
        private readonly Color MutedGray = Color.FromArgb(138, 149, 168);
        private readonly Color LiveAccent = Color.FromArgb(74, 190, 225);
        private readonly Color StandbyAccent = Color.FromArgb(110, 118, 136);
        private readonly Color PreviewSurface = Color.FromArgb(18, 22, 30);
        private readonly Color PreviewSyncSurface = Color.FromArgb(25, 29, 42);

        private Panel headerPanel;
        private Panel previewBorderPanel;
        private Panel previewHostPanel;
        private Label lblEyebrow;
        private Label lblTitle;
        private Label lblSubtitle;
        private Label lblStatus;
        private Panel statusDot;
        private Label lblState;
        private Map3D mapView;
        private bool previewAvailable;
        private string previewInitError = "";
        private bool embeddedMode;

        public bool EmbeddedMode
        {
            get => embeddedMode;
            set
            {
                if (embeddedMode == value)
                    return;

                embeddedMode = value;
                ApplyEmbeddedLayout();
            }
        }

        public PanelSituationalPreviewDeck()
        {
            BackColor = Color.FromArgb(10, 14, 20);
            Padding = new Padding(10, 0, 10, 10);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            InitializeControls();
            SetOffline(false);
            ApplyEmbeddedLayout();
        }

        private void InitializeControls()
        {
            previewBorderPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Border,
                Padding = new Padding(1)
            };
            Controls.Add(previewBorderPanel);

            previewHostPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = PreviewSurface
            };
            previewBorderPanel.Controls.Add(previewHostPanel);

            lblState = new Label
            {
                Dock = DockStyle.Fill,
                Font = stateFont,
                ForeColor = LightGray,
                BackColor = PreviewSyncSurface,
                Padding = new Padding(18),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "Connect vehicle\r\nfor 3D situational view"
            };
            previewHostPanel.Controls.Add(lblState);

            headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 56,
                BackColor = Surface
            };
            Controls.Add(headerPanel);

            lblEyebrow = new Label
            {
                Font = eyebrowFont,
                ForeColor = Gold,
                BackColor = Surface,
                Text = "3D SITUATIONAL",
                AutoEllipsis = true
            };
            headerPanel.Controls.Add(lblEyebrow);

            lblTitle = new Label
            {
                Font = titleFont,
                ForeColor = LightGray,
                BackColor = Surface,
                Text = "3D view standing by",
                AutoEllipsis = true
            };
            headerPanel.Controls.Add(lblTitle);

            lblSubtitle = new Label
            {
                Font = subtitleFont,
                ForeColor = MutedGray,
                BackColor = Surface,
                Text = "Live terrain-relative situational view.",
                AutoEllipsis = true
            };
            headerPanel.Controls.Add(lblSubtitle);

            statusDot = new Panel
            {
                BackColor = StandbyAccent,
                Size = new Size(8, 8)
            };
            headerPanel.Controls.Add(statusDot);

            lblStatus = new Label
            {
                Font = badgeFont,
                ForeColor = LightGray,
                BackColor = Color.FromArgb(44, 49, 59),
                Text = "OFFLINE",
                TextAlign = ContentAlignment.MiddleCenter
            };
            headerPanel.Controls.Add(lblStatus);

            TryInitializeMapView();
            LayoutHeader();
        }

        private void TryInitializeMapView()
        {
            try
            {
                mapView = new Map3D
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.Black,
                    Name = "modernHudSituationalMap3D",
                    Visible = false
                };

                previewHostPanel.Controls.Add(mapView);
                mapView.SendToBack();
                previewAvailable = true;
            }
            catch (Exception ex)
            {
                previewInitError = ex.Message;
                previewAvailable = false;
                log.Error("Modern flight left-stack 3D map failed to initialize", ex);
            }
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            LayoutHeader();
        }

        private void LayoutHeader()
        {
            if (headerPanel == null)
                return;

            int badgeWidth = 112;
            int badgeHeight = 26;
            int topInset = 18;
            int rightInset = 18;

            lblStatus.Bounds = new Rectangle(
                Math.Max(130, headerPanel.ClientSize.Width - badgeWidth - rightInset),
                topInset,
                badgeWidth,
                badgeHeight);

            statusDot.Bounds = new Rectangle(
                Math.Max(10, lblStatus.Left - 18),
                topInset + (badgeHeight - statusDot.Height) / 2,
                statusDot.Width,
                statusDot.Height);

            int textWidth = Math.Max(160, statusDot.Left - 28);
            lblEyebrow.Bounds = new Rectangle(18, 10, textWidth, 14);
            lblTitle.Bounds = new Rectangle(18, 23, textWidth, 18);
            lblSubtitle.Bounds = new Rectangle(18, 40, textWidth, 15);
        }

        private void ApplyEmbeddedLayout()
        {
            if (headerPanel == null)
                return;

            Padding = embeddedMode ? new Padding(0) : new Padding(10, 0, 10, 10);
            headerPanel.Visible = !embeddedMode;
        }

        public void ActivateView()
        {
            if (!previewAvailable || mapView == null)
                return;

            try
            {
                mapView.Activate();
            }
            catch (Exception ex)
            {
                log.Debug($"Modern flight stacked 3D activation error: {ex.Message}");
            }
        }

        public void DeactivateView()
        {
            try
            {
                mapView?.Deactivate();
            }
            catch (Exception ex)
            {
                log.Debug($"Modern flight stacked 3D deactivation error: {ex.Message}");
            }
        }

        public void SetOffline(bool connected)
        {
            ApplyDeckState(
                connected ? "SYNCING" : "OFFLINE",
                connected ? "3D view syncing" : "3D view standing by",
                connected
                    ? "Vehicle link detected. Waiting for live position and attitude."
                    : "Connect a vehicle to begin the live 3D situational view.",
                connected ? Gold : StandbyAccent,
                connected ? Color.FromArgb(78, 62, 28) : Color.FromArgb(44, 49, 59),
                connected ? PreviewSyncSurface : PreviewSurface,
                connected
                    ? "Waiting for valid position\r\nand attitude telemetry"
                    : "Connect vehicle\r\nfor 3D situational view",
                false);
        }

        public void UpdateTelemetry(CurrentState cs)
        {
            bool connected = MainV2.comPort?.BaseStream?.IsOpen == true;
            bool hasPosition = HasValidCoordinate(cs.lat, cs.lng);
            string mode = string.IsNullOrWhiteSpace(cs.mode) ? "STANDBY" : cs.mode.ToUpperInvariant();
            bool livePreview = previewAvailable && connected && hasPosition && mapView != null;

            ApplyDeckState(
                livePreview ? "LIVE 3D" : connected ? "SYNCING" : "OFFLINE",
                livePreview ? "3D situational - live" : connected ? "3D view syncing" : "3D view standing by",
                livePreview
                    ? $"{mode}  |  Alt {cs.altasl:F0} m  |  GS {cs.groundspeed:F1} m/s"
                    : connected
                        ? "Vehicle linked. Waiting for valid position and attitude telemetry."
                        : "Connect a vehicle to begin the live 3D situational view.",
                livePreview ? LiveAccent : connected ? Gold : StandbyAccent,
                livePreview ? Color.FromArgb(26, 60, 78) : connected ? Color.FromArgb(78, 62, 28) : Color.FromArgb(44, 49, 59),
                livePreview ? Color.Black : connected ? PreviewSyncSurface : PreviewSurface,
                !previewAvailable
                    ? (string.IsNullOrWhiteSpace(previewInitError)
                        ? "3D view unavailable."
                        : $"3D view unavailable.\r\n{previewInitError}")
                    : !connected
                        ? "Connect vehicle\r\nfor 3D situational view"
                        : !hasPosition
                            ? "Waiting for valid position\r\nand attitude telemetry"
                            : $"{mode}\r\nAlt {cs.altasl:F0} m  |  GS {cs.groundspeed:F1} m/s",
                livePreview);

            if (!livePreview)
                return;

            try
            {
                mapView.rpy = new MissionPlanner.Utilities.Vector3(cs.roll, cs.pitch, cs.yaw);

                double terrainAlt = srtm.getAltitude(cs.lat, cs.lng).alt;
                double relativeAlt = CurrentState.multiplieralt == 0
                    ? cs.alt
                    : cs.alt / CurrentState.multiplieralt;

                mapView.LocationCenter = new PointLatLngAlt(cs.lat, cs.lng, terrainAlt + relativeAlt, "here");
                mapView.Velocity = new MissionPlanner.Utilities.Vector3(cs.vx, cs.vy, cs.vz);

                var waypoints = MainV2.comPort?.MAV?.wps?.Values;
                mapView.WPs = waypoints != null
                    ? waypoints.Select(item => (Locationwp)item).ToList()
                    : new List<Locationwp>();
            }
            catch (Exception ex)
            {
                log.Debug($"Modern flight stacked 3D update error: {ex.Message}");
            }
        }

        private void ApplyDeckState(string badgeText, string title, string subtitle, Color accent,
            Color pillBackColor, Color previewBackColor, string stateMessage, bool livePreview)
        {
            lblStatus.Text = !previewAvailable ? "3D OFF" : badgeText;
            lblTitle.Text = title;
            lblSubtitle.Text = subtitle;
            lblStatus.BackColor = !previewAvailable ? Color.FromArgb(64, 59, 86) : pillBackColor;
            statusDot.BackColor = !previewAvailable ? StandbyAccent : accent;

            previewBorderPanel.BackColor = !previewAvailable
                ? Color.FromArgb(94, 88, 118)
                : livePreview
                    ? Color.FromArgb(56, 107, 130)
                    : accent == Gold
                        ? Color.FromArgb(86, 72, 40)
                        : Border;

            previewHostPanel.BackColor = previewBackColor;
            lblState.BackColor = previewBackColor;
            lblState.Text = stateMessage;
            lblState.Visible = !livePreview;

            if (mapView != null)
            {
                mapView.BackColor = Color.Black;
                mapView.Visible = livePreview;
            }
        }

        private static bool HasValidCoordinate(double latitude, double longitude)
        {
            return Math.Abs(latitude) > 0.00001 || Math.Abs(longitude) > 0.00001;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DeactivateView();
                eyebrowFont?.Dispose();
                titleFont?.Dispose();
                subtitleFont?.Dispose();
                badgeFont?.Dispose();
                stateFont?.Dispose();
            }

            base.Dispose(disposing);
        }

    }

    /// <summary>
    /// Telemetry Cards Panel - Left side showing altitude, speed, battery, etc.
    /// </summary>
    public class ModernTelemetryCard : Control
    {
        private readonly Font labelFont = new Font("Segoe UI", 8.25f, FontStyle.Bold);
        private readonly Font valueFont = new Font("Segoe UI", 16f, FontStyle.Bold);
        private readonly Font detailFont = new Font("Segoe UI", 8f, FontStyle.Regular);

        private readonly Color Surface = Color.FromArgb(27, 33, 46);
        private readonly Color TextPrimary = Color.FromArgb(235, 239, 245);
        private readonly Color TextMuted = Color.FromArgb(138, 149, 168);

        public string Title { get; set; } = "";
        public string Value { get; set; } = "--";
        public string Detail { get; set; } = "";
        public Color AccentColor { get; set; } = Color.FromArgb(200, 168, 101);
        public float Progress { get; set; }

        public ModernTelemetryCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            DoubleBuffered = true;
            Margin = new Padding(0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Color readingColor = Blend(TextPrimary, AccentColor, 0.88f);
            Color borderColor = ModernUiPainter.WithAlpha(readingColor, 165);
            ModernUiPainter.FillRoundedRectangle(e.Graphics, Surface, bounds, 18);
            ModernUiPainter.DrawRoundedRectangle(e.Graphics, borderColor, 1f, bounds, 18);

            var accentRect = new Rectangle(16, 12, Math.Max(40, Width / 4), 4);
            ModernUiPainter.FillRoundedRectangle(e.Graphics, AccentColor, accentRect, 2);

            using (var titleBrush = new SolidBrush(TextMuted))
            using (var valueBrush = new SolidBrush(readingColor))
            using (var detailBrush = new SolidBrush(ModernUiPainter.WithAlpha(AccentColor, 220)))
            using (var singleLineFormat = new StringFormat
            {
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter
            })
            using (var detailFormat = new StringFormat
            {
                FormatFlags = StringFormatFlags.LineLimit,
                Trimming = StringTrimming.EllipsisWord
            })
            {
                float contentX = 16;
                float contentWidth = Width - 32;
                float labelY = accentRect.Bottom + 9;
                float valueY = labelY + 17;
                float progressTop = Height - 15;
                float detailY = valueY + 28;
                float detailHeight = Math.Max(16, progressTop - detailY - 8);

                e.Graphics.DrawString(Title, labelFont, titleBrush,
                    new RectangleF(contentX, labelY, contentWidth, 13), singleLineFormat);
                e.Graphics.DrawString(Value, valueFont, valueBrush,
                    new RectangleF(contentX, valueY, contentWidth, 24), singleLineFormat);
                e.Graphics.DrawString(Detail, detailFont, detailBrush,
                    new RectangleF(contentX, detailY, contentWidth, detailHeight), detailFormat);
            }

            int progressWidth = (int)((Width - 32) * Math.Max(0f, Math.Min(1f, Progress)));
            if (progressWidth > 0)
            {
                var progressTrack = new Rectangle(16, Height - 15, Width - 32, 4);
                var progressFill = new Rectangle(16, Height - 15, progressWidth, 4);
                ModernUiPainter.FillRoundedRectangle(e.Graphics, Color.FromArgb(42, 54, 76), progressTrack, 2);
                ModernUiPainter.FillRoundedRectangle(e.Graphics, AccentColor, progressFill, 2);
            }
        }

        private static Color Blend(Color baseColor, Color accentColor, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));

            return Color.FromArgb(
                255,
                (int)(baseColor.R + ((accentColor.R - baseColor.R) * amount)),
                (int)(baseColor.G + ((accentColor.G - baseColor.G) * amount)),
                (int)(baseColor.B + ((accentColor.B - baseColor.B) * amount)));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                labelFont?.Dispose();
                valueFont?.Dispose();
                detailFont?.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    public class PanelTelemetry : Panel
    {
        private const string TelemetrySelectionSettingKey = "ModernFlightTelemetryCards";
        private const string TelemetryCustomizationSettingKey = "ModernFlightTelemetryCardCustomizations";
        private static readonly string[] DefaultMetricKeys =
        {
            "altitude",
            "ground_speed",
            "battery",
            "endurance",
            "gps",
            "navigation"
        };
        private static readonly TelemetryMetricDefinition[] MetricCatalog =
        {
            new TelemetryMetricDefinition("altitude", "Altitude"),
            new TelemetryMetricDefinition("ground_speed", "Ground Speed"),
            new TelemetryMetricDefinition("battery", "Battery"),
            new TelemetryMetricDefinition("endurance", "Est. Remaining"),
            new TelemetryMetricDefinition("battery_used", "Battery Used"),
            new TelemetryMetricDefinition("gps", "GPS Lock"),
            new TelemetryMetricDefinition("navigation", "Navigation"),
            new TelemetryMetricDefinition("time_in_air", "Time in Air"),
            new TelemetryMetricDefinition("distance_travelled", "Distance Travelled"),
            new TelemetryMetricDefinition("distance_to_home", "Distance to Home"),
            new TelemetryMetricDefinition("fuel_system", "Fuel System"),
            new TelemetryMetricDefinition("flight_mode", "Flight Mode")
        };

        private readonly Font headerFont = new Font("Segoe UI", 15, FontStyle.Bold);
        private readonly Font subtitleFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        private readonly Font buttonFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private readonly Color HeaderText = Color.FromArgb(234, 238, 244);
        private readonly Color MutedText = Color.FromArgb(138, 149, 168);
        private readonly Color ButtonSurface = Color.FromArgb(27, 33, 46);
        private readonly Color ButtonBorder = Color.FromArgb(56, 68, 90);
        private readonly Color ButtonText = Color.FromArgb(226, 231, 239);

        private Label lblHeader;
        private Label lblSubtitle;
        private Button btnCustomize;
        private ContextMenuStrip telemetryMenu;
        private readonly List<ModernTelemetryCard> cards = new List<ModernTelemetryCard>();
        private readonly List<string> selectedMetricKeys = new List<string>();
        private readonly List<TelemetryCardCustomization> customMetricConfigs = new List<TelemetryCardCustomization>();
        private CurrentState lastTelemetry;
        private int activeTelemetryCardIndex = -1;

        public PanelTelemetry()
        {
            BackColor = Color.FromArgb(10, 14, 20);
            Padding = new Padding(10, 12, 10, 10);
            AutoScroll = false;
            AutoScrollMinSize = Size.Empty;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            LoadMetricSelection();
            LoadMetricCustomizations();
            InitializeControls();
        }

        private void InitializeControls()
        {
            lblHeader = new Label
            {
                Text = "Mission Vitals",
                Font = headerFont,
                ForeColor = HeaderText,
                BackColor = Color.Transparent,
                AutoSize = false
            };
            Controls.Add(lblHeader);

            lblSubtitle = new Label
            {
                Text = "Double-click any card to edit it",
                Font = subtitleFont,
                ForeColor = MutedText,
                BackColor = Color.Transparent,
                AutoSize = false,
                AutoEllipsis = true
            };
            Controls.Add(lblSubtitle);

            btnCustomize = new Button
            {
                Text = "EDIT",
                Font = buttonFont,
                FlatStyle = FlatStyle.Flat,
                ForeColor = ButtonText,
                BackColor = ButtonSurface,
                TabStop = false,
                Cursor = Cursors.Hand
            };
            btnCustomize.FlatAppearance.BorderColor = ButtonBorder;
            btnCustomize.FlatAppearance.MouseDownBackColor = Color.FromArgb(39, 48, 66);
            btnCustomize.FlatAppearance.MouseOverBackColor = Color.FromArgb(34, 42, 58);
            btnCustomize.Click += (s, e) => ShowTelemetryMenu();
            btnCustomize.Visible = false;
            btnCustomize.Enabled = false;
            Controls.Add(btnCustomize);

            telemetryMenu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                BackColor = Color.FromArgb(23, 29, 41),
                ForeColor = HeaderText
            };

            SyncCardControls();
            RebuildTelemetryMenu();

            UpdateLayoutMetrics();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            UpdateLayoutMetrics();
        }

        private void UpdateLayoutMetrics()
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return;

            int gap = 8;
            int contentWidth = Math.Max(176, ClientSize.Width - Padding.Horizontal);
            int currentTop = Padding.Top;
            int titleHeight = 28;
            int subtitleHeight = 18;

            lblHeader.Bounds = new Rectangle(Padding.Left, currentTop, contentWidth, titleHeight);
            currentTop += titleHeight - 2;

            lblSubtitle.Bounds = new Rectangle(Padding.Left, currentTop, contentWidth, subtitleHeight);
            currentTop += subtitleHeight + 10;

            int availableHeight = Math.Max(0, ClientSize.Height - currentTop - Padding.Bottom);
            int cardCount = Math.Max(1, cards.Count);
            int totalGapHeight = Math.Max(0, cardCount - 1) * gap;
            int cardHeight = (availableHeight - totalGapHeight) / cardCount;
            cardHeight = Math.Max(86, Math.Min(contentWidth < 240 ? 110 : 104, cardHeight));

            for (int i = 0; i < cards.Count; i++)
            {
                cards[i].Bounds = new Rectangle(Padding.Left, currentTop, contentWidth, cardHeight);
                currentTop += cardHeight + gap;
            }
        }

        public void UpdateTelemetry(CurrentState cs)
        {
            try
            {
                lastTelemetry = cs;

                SyncCardControls();

                for (int i = 0; i < cards.Count; i++)
                {
                    var snapshot = BuildSnapshotForCard(i, cs);
                    cards[i].Title = snapshot.Title;
                    cards[i].Value = snapshot.Value;
                    cards[i].Detail = snapshot.Detail;
                    cards[i].AccentColor = snapshot.AccentColor;
                    cards[i].Progress = snapshot.Progress;
                    cards[i].Invalidate();
                }
            }
            catch
            {
            }
        }

        private void LoadMetricSelection()
        {
            selectedMetricKeys.Clear();

            try
            {
                var rawValue = Settings.Instance[TelemetrySelectionSettingKey];
                if (rawValue != null)
                {
                    foreach (string token in rawValue.ToString().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string key = token.Trim();
                        if (IsSupportedTelemetryKey(key) && !selectedMetricKeys.Contains(key))
                            selectedMetricKeys.Add(key);

                        if (selectedMetricKeys.Count == DefaultMetricKeys.Length)
                            break;
                    }
                }
            }
            catch
            {
            }

            foreach (string key in DefaultMetricKeys)
            {
                if (selectedMetricKeys.Count == DefaultMetricKeys.Length)
                    break;

                if (!selectedMetricKeys.Contains(key))
                    selectedMetricKeys.Add(key);
            }

            foreach (var metric in MetricCatalog)
            {
                if (selectedMetricKeys.Count == DefaultMetricKeys.Length)
                    break;

                if (!selectedMetricKeys.Contains(metric.Key))
                    selectedMetricKeys.Add(metric.Key);
            }
        }

        private static bool IsSupportedTelemetryKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            if (MetricCatalog.Any(metric => metric.Key == key))
                return true;

            if (key.StartsWith("customfield:", StringComparison.OrdinalIgnoreCase))
                return true;

            return typeof(CurrentState).GetProperty(key) != null;
        }

        private void LoadMetricCustomizations()
        {
            customMetricConfigs.Clear();

            try
            {
                var rawValue = Settings.Instance[TelemetryCustomizationSettingKey];
                if (rawValue != null)
                {
                    string[] entries = rawValue.ToString().Split(new[] { "||" }, StringSplitOptions.None);
                    foreach (string entry in entries)
                    {
                        customMetricConfigs.Add(TelemetryCardCustomization.TryDeserialize(entry));
                    }
                }
            }
            catch
            {
            }

            EnsureCustomizationSlots();
        }

        private void SaveMetricCustomizations()
        {
            try
            {
                EnsureCustomizationSlots();
                Settings.Instance[TelemetryCustomizationSettingKey] = string.Join("||",
                    customMetricConfigs.Select(TelemetryCardCustomization.Serialize));
                Settings.Instance.Save();
            }
            catch
            {
            }
        }

        private void EnsureCustomizationSlots()
        {
            while (customMetricConfigs.Count < selectedMetricKeys.Count)
                customMetricConfigs.Add(null);

            while (customMetricConfigs.Count > selectedMetricKeys.Count)
                customMetricConfigs.RemoveAt(customMetricConfigs.Count - 1);
        }

        private void SyncCardControls()
        {
            EnsureCustomizationSlots();

            while (cards.Count < selectedMetricKeys.Count)
            {
                var card = new ModernTelemetryCard { BackColor = BackColor, Cursor = Cursors.Hand };
                card.MouseDoubleClick += TelemetryCard_MouseDoubleClick;
                cards.Add(card);
                Controls.Add(card);
            }

            while (cards.Count > selectedMetricKeys.Count)
            {
                var card = cards[cards.Count - 1];
                Controls.Remove(card);
                cards.RemoveAt(cards.Count - 1);
                card.Dispose();
            }

            btnCustomize?.BringToFront();
            lblHeader?.BringToFront();
            lblSubtitle?.BringToFront();
            UpdateLayoutMetrics();
        }

        private void ShowTelemetryMenu()
        {
            telemetryMenu?.Show(btnCustomize, new Point(0, btnCustomize.Height + 4));
        }

        private void ShowTelemetryMenu(int cardIndex, Control source, Point location)
        {
            activeTelemetryCardIndex = cardIndex;
            RebuildTelemetryMenu();
            telemetryMenu?.Show(source, location);
        }

        private TelemetryMetricSnapshot BuildSnapshotForCard(int cardIndex, CurrentState cs)
        {
            if (cardIndex < 0 || cardIndex >= selectedMetricKeys.Count)
                return new TelemetryMetricSnapshot("TELEMETRY", "--", "Metric unavailable", GetMetricAccent(""), 0f);

            EnsureCustomizationSlots();

            var customization = cardIndex < customMetricConfigs.Count ? customMetricConfigs[cardIndex] : null;
            if (customization != null && !string.IsNullOrWhiteSpace(customization.SourceTag))
                return BuildCustomizedMetricSnapshot(customization, cs);

            string metricKey = selectedMetricKeys[cardIndex];
            if (MetricCatalog.Any(metric => metric.Key == metricKey))
                return BuildMetricSnapshot(metricKey, cs);

            return BuildCustomizedMetricSnapshot(new TelemetryCardCustomization
            {
                SourceTag = metricKey,
                Label = GetTelemetryDisplayName(ResolveTelemetrySourceKey(metricKey), cs),
                AccentColor = GetMetricAccent(metricKey),
                NumberFormat = ResolveEditorFormat(cardIndex),
                Scale = 1.0,
                Offset = 0.0,
                GaugeEnabled = false,
                GaugeMin = 0.0,
                GaugeMax = 100.0
            }, cs);
        }

        private TelemetryMetricSnapshot BuildCustomizedMetricSnapshot(TelemetryCardCustomization customization, CurrentState cs)
        {
            string sourceKey = ResolveTelemetrySourceKey(customization.SourceTag);
            string title = string.IsNullOrWhiteSpace(customization.Label)
                ? GetTelemetryDisplayName(sourceKey, cs)
                : customization.Label;
            string detail = BuildTelemetryDetail(customization, sourceKey, cs);
            Color accent = customization.AccentColor;

            if (cs == null)
                return new TelemetryMetricSnapshot(title.ToUpperInvariant(), "--", detail, accent, 0f);

            object rawValue = typeof(CurrentState).GetProperty(sourceKey ?? string.Empty)?.GetValue(cs, null);
            string value = FormatTelemetryValue(rawValue, customization);
            float progress = ResolveTelemetryProgress(rawValue, customization);

            return new TelemetryMetricSnapshot(title.ToUpperInvariant(), value, detail, accent, progress);
        }

        private static string ResolveTelemetrySourceKey(string sourceTag)
        {
            if (string.IsNullOrWhiteSpace(sourceTag))
                return "battery_voltage";

            if (sourceTag.StartsWith("customfield:", StringComparison.OrdinalIgnoreCase))
                return CurrentState.GetCustomField(sourceTag.Substring("customfield:".Length));

            return sourceTag;
        }

        private static string GetTelemetryDisplayName(string sourceKey, CurrentState cs)
        {
            if (cs == null || string.IsNullOrWhiteSpace(sourceKey))
                return "Telemetry";

            return cs.GetNameandUnit(sourceKey);
        }

        private static string BuildTelemetryDetail(TelemetryCardCustomization customization, string sourceKey, CurrentState cs)
        {
            string defaultLabel = GetTelemetryDisplayName(sourceKey, cs);
            if (customization.GaugeEnabled)
                return $"Range {customization.GaugeMin:0.##} to {customization.GaugeMax:0.##}";

            if (string.IsNullOrWhiteSpace(customization.Label) ||
                customization.Label.Equals(defaultLabel, StringComparison.OrdinalIgnoreCase))
            {
                return sourceKey?.Replace("_", " ") ?? "live telemetry";
            }

            return defaultLabel;
        }

        private static string FormatTelemetryValue(object rawValue, TelemetryCardCustomization customization)
        {
            if (rawValue == null)
                return "--";

            if (rawValue is bool booleanValue)
                return booleanValue ? "TRUE" : "FALSE";

            if (!TryConvertToDouble(rawValue, out double numericValue))
                return Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? "--";

            double adjustedValue = (numericValue * customization.Scale) + customization.Offset;
            string format = string.IsNullOrWhiteSpace(customization.NumberFormat) ? "0.00" : customization.NumberFormat;

            try
            {
                if (format.Contains(":"))
                    return TimeSpan.FromSeconds(adjustedValue).ToString(format);

                return adjustedValue.ToString(format, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return adjustedValue.ToString("0.00", CultureInfo.InvariantCulture);
            }
        }

        private static float ResolveTelemetryProgress(object rawValue, TelemetryCardCustomization customization)
        {
            if (rawValue is bool booleanValue)
                return booleanValue ? 1f : 0.08f;

            if (!TryConvertToDouble(rawValue, out double numericValue))
                return 0.08f;

            double adjustedValue = (numericValue * customization.Scale) + customization.Offset;

            if (customization.GaugeEnabled && customization.GaugeMax > customization.GaugeMin)
                return Clamp01((adjustedValue - customization.GaugeMin) / (customization.GaugeMax - customization.GaugeMin));

            return 0.08f;
        }

        private static bool TryConvertToDouble(object rawValue, out double value)
        {
            if (rawValue == null)
            {
                value = 0;
                return false;
            }

            try
            {
                value = Convert.ToDouble(rawValue, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                value = 0;
                return false;
            }
        }

        private void RebuildTelemetryMenu()
        {
            if (telemetryMenu == null)
                return;

            telemetryMenu.Items.Clear();

            foreach (var metric in MetricCatalog)
            {
                int existingIndex = selectedMetricKeys.IndexOf(metric.Key);
                bool isCurrent = activeTelemetryCardIndex >= 0 &&
                    activeTelemetryCardIndex < selectedMetricKeys.Count &&
                    selectedMetricKeys[activeTelemetryCardIndex] == metric.Key;

                string label = metric.Label;
                if (existingIndex >= 0 && existingIndex != activeTelemetryCardIndex)
                    label += "  (Swap)";

                var item = new ToolStripMenuItem(label)
                {
                    Checked = isCurrent,
                    CheckOnClick = false,
                    Tag = metric.Key
                };
                item.Click += TelemetryMetricMenuItem_Click;
                telemetryMenu.Items.Add(item);
            }

            telemetryMenu.Items.Add(new ToolStripSeparator());

            var resetItem = new ToolStripMenuItem("Reset to Default");
            resetItem.Click += (s, e) => ResetMetricSelection();
            telemetryMenu.Items.Add(resetItem);
        }

        private void TelemetryMetricMenuItem_Click(object sender, EventArgs e)
        {
            if (!(sender is ToolStripMenuItem menuItem) || !(menuItem.Tag is string metricKey))
                return;

            if (activeTelemetryCardIndex < 0 || activeTelemetryCardIndex >= selectedMetricKeys.Count)
                return;

            int existingIndex = selectedMetricKeys.IndexOf(metricKey);
            string replacedKey = selectedMetricKeys[activeTelemetryCardIndex];

            if (existingIndex >= 0 && existingIndex != activeTelemetryCardIndex)
                selectedMetricKeys[existingIndex] = replacedKey;

            selectedMetricKeys[activeTelemetryCardIndex] = metricKey;
            EnsureCustomizationSlots();
            customMetricConfigs[activeTelemetryCardIndex] = null;

            SaveMetricSelection();
            SaveMetricCustomizations();
            SyncCardControls();
            RebuildTelemetryMenu();

            if (lastTelemetry != null)
                UpdateTelemetry(lastTelemetry);
        }

        private void ResetMetricSelection()
        {
            selectedMetricKeys.Clear();
            selectedMetricKeys.AddRange(DefaultMetricKeys);
            customMetricConfigs.Clear();

            SaveMetricSelection();
            SaveMetricCustomizations();
            SyncCardControls();
            RebuildTelemetryMenu();

            if (lastTelemetry != null)
                UpdateTelemetry(lastTelemetry);
        }

        private void SaveMetricSelection()
        {
            try
            {
                EnsureCustomizationSlots();
                Settings.Instance[TelemetrySelectionSettingKey] = string.Join(",", selectedMetricKeys);
                Settings.Instance.Save();
            }
            catch
            {
            }
        }

        private static Color GetMetricAccent(string key)
        {
            switch (key)
            {
                case "altitude":
                    return Color.FromArgb(66, 194, 226);
                case "ground_speed":
                    return Color.FromArgb(93, 141, 255);
                case "battery":
                    return Color.FromArgb(236, 96, 86);
                case "endurance":
                    return Color.FromArgb(230, 174, 68);
                case "battery_used":
                    return Color.FromArgb(198, 136, 98);
                case "gps":
                    return Color.FromArgb(82, 201, 150);
                case "navigation":
                    return Color.FromArgb(152, 214, 96);
                case "time_in_air":
                    return Color.FromArgb(110, 129, 234);
                case "distance_travelled":
                    return Color.FromArgb(230, 139, 74);
                case "distance_to_home":
                    return Color.FromArgb(162, 110, 232);
                case "fuel_system":
                    return Color.FromArgb(202, 150, 88);
                case "flight_mode":
                    return Color.FromArgb(214, 118, 184);
                default:
                    return Color.FromArgb(111, 120, 138);
            }
        }

        private static TelemetryMetricSnapshot BuildMetricSnapshot(string key, CurrentState cs)
        {
            int battery = Math.Max(0, Math.Min(100, cs.battery_remaining));
            Color accent = GetMetricAccent(key);

            switch (key)
            {
                case "altitude":
                    return new TelemetryMetricSnapshot(
                        "ALTITUDE",
                        $"{cs.altasl:F0} m",
                        $"Home {cs.DistToHome:F0} m",
                        accent,
                        Clamp01(cs.altasl / 150f));

                case "ground_speed":
                    return new TelemetryMetricSnapshot(
                        "GROUND SPEED",
                        $"{cs.groundspeed:F1} m/s",
                        $"Climb {cs.verticalspeed:+0.0;-0.0;0.0} m/s",
                        accent,
                        Clamp01(cs.groundspeed / 30f));

                case "battery":
                    return new TelemetryMetricSnapshot(
                        "BATTERY",
                        $"{battery}%",
                        $"{cs.battery_voltage:F1} V  |  {cs.watts:F0} W",
                        accent,
                        battery / 100f);

                case "endurance":
                    double remainingSeconds = ResolveRemainingSeconds(cs);
                    string remainingValue = remainingSeconds > 0
                        ? ModernUiPainter.FormatDuration(remainingSeconds)
                        : "--";
                    string remainingDetail = cs.battery_remainmin > 0.1
                        ? $"Used {cs.battery_usedmah:F0} mAh"
                        : remainingSeconds > 0
                            ? "Trend estimate from discharge"
                            : "Awaiting battery time estimate";
                    return new TelemetryMetricSnapshot(
                        "EST. REMAINING",
                        remainingValue,
                        remainingDetail,
                        accent,
                        battery / 100f);

                case "battery_used":
                    return new TelemetryMetricSnapshot(
                        "BATTERY USED",
                        $"{cs.battery_usedmah:F0} mAh",
                        $"{cs.current:0.0} A draw  |  {cs.watts:F0} W",
                        accent,
                        Clamp01(1f - battery / 100f));

                case "gps":
                    return new TelemetryMetricSnapshot(
                        "GPS LOCK",
                        $"{cs.satcount:F0} sats",
                        $"Fix {cs.gpsstatus:F0}  |  HDOP {cs.gpshdop:0.0}",
                        accent,
                        Clamp01(cs.satcount / 16f));

                case "navigation":
                    return new TelemetryMetricSnapshot(
                        "NAVIGATION",
                        $"WP {cs.wpno:F0}",
                        $"WP {cs.wp_dist:F0} m  |  Track {cs.distTraveled:F0} m",
                        accent,
                        Math.Max(0.08f, 1f - Clamp01(cs.wp_dist / 1200f)));

                case "time_in_air":
                    return new TelemetryMetricSnapshot(
                        "TIME IN AIR",
                        ModernUiPainter.FormatDuration(cs.timeInAir),
                        cs.armed ? "Mission timer active" : "Preflight timer ready",
                        accent,
                        Clamp01(cs.timeInAir / 1800f));

                case "distance_travelled":
                    return new TelemetryMetricSnapshot(
                        "DIST. TRAVELLED",
                        $"{cs.distTraveled:F0} m",
                        $"{cs.groundspeed:F1} m/s groundspeed",
                        accent,
                        Clamp01(cs.distTraveled / 5000f));

                case "distance_to_home":
                    return new TelemetryMetricSnapshot(
                        "DIST. TO HOME",
                        $"{cs.DistToHome:F0} m",
                        $"{cs.nav_bearing:F0} deg bearing",
                        accent,
                        Math.Max(0.08f, 1f - Clamp01(cs.DistToHome / 3000f)));

                case "fuel_system":
                    bool hasFuelTelemetry = cs.efi_fuelflow > 0 || cs.efi_fuelconsumed > 0 || cs.efi_fuelpressure > 0;
                    return new TelemetryMetricSnapshot(
                        "FUEL SYSTEM",
                        hasFuelTelemetry
                            ? cs.efi_fuelflow > 0 ? $"{cs.efi_fuelflow:F0} cc/min" : $"{cs.efi_fuelconsumed:F0} cc"
                        : "--",
                        hasFuelTelemetry
                            ? cs.efi_fuelflow > 0 ? $"Used {cs.efi_fuelconsumed:F0} cc" : $"Pressure {cs.efi_fuelpressure:F0} kPa"
                            : "No EFI fuel telemetry",
                        accent,
                        hasFuelTelemetry ? 0.72f : 0.08f);

                case "flight_mode":
                    return new TelemetryMetricSnapshot(
                        "FLIGHT MODE",
                        string.IsNullOrWhiteSpace(cs.mode) ? "UNKNOWN" : cs.mode.ToUpperInvariant(),
                        $"Air time {ModernUiPainter.FormatDuration(cs.timeInAir)}",
                        accent,
                        cs.armed ? 1f : 0.25f);

                default:
                    return new TelemetryMetricSnapshot(
                        "TELEMETRY",
                        "--",
                        "Metric unavailable",
                        Color.FromArgb(111, 120, 138),
                        0f);
            }
        }

        private static float Clamp01(double value)
        {
            return (float)Math.Max(0d, Math.Min(1d, value));
        }

        private static double ResolveRemainingSeconds(CurrentState cs)
        {
            if (cs.battery_remainmin > 0.1)
                return cs.battery_remainmin * 60.0;

            double consumedPercent = 100 - Math.Max(0, Math.Min(100, cs.battery_remaining));
            if (consumedPercent < 5 || cs.timeInAir < 120)
                return 0;

            double secondsPerPercent = cs.timeInAir / consumedPercent;
            return Math.Max(0, secondsPerPercent * cs.battery_remaining);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                headerFont?.Dispose();
                subtitleFont?.Dispose();
                buttonFont?.Dispose();
                telemetryMenu?.Dispose();
            }

            base.Dispose(disposing);
        }

        private sealed class TelemetryMetricDefinition
        {
            public TelemetryMetricDefinition(string key, string label)
            {
                Key = key;
                Label = label;
            }

            public string Key { get; }
            public string Label { get; }
        }

        private sealed class TelemetryMetricSnapshot
        {
            public TelemetryMetricSnapshot(string title, string value, string detail, Color accentColor, float progress)
            {
                Title = title;
                Value = value;
                Detail = detail;
                AccentColor = accentColor;
                Progress = progress;
            }

            public string Title { get; }
            public string Value { get; }
            public string Detail { get; }
            public Color AccentColor { get; }
            public float Progress { get; }
        }

        private sealed class TelemetryCardCustomization
        {
            public string SourceTag { get; set; }
            public string Label { get; set; }
            public Color AccentColor { get; set; } = Color.FromArgb(111, 120, 138);
            public string NumberFormat { get; set; } = "0.00";
            public double Scale { get; set; } = 1.0;
            public double Offset { get; set; }
            public bool GaugeEnabled { get; set; }
            public double GaugeMin { get; set; }
            public double GaugeMax { get; set; } = 100.0;

            public static string Serialize(TelemetryCardCustomization customization)
            {
                if (customization == null || string.IsNullOrWhiteSpace(customization.SourceTag))
                    return string.Empty;

                return string.Join(";",
                    EncodePart(customization.SourceTag),
                    EncodePart(customization.Label),
                    customization.AccentColor.ToArgb().ToString(CultureInfo.InvariantCulture),
                    EncodePart(customization.NumberFormat),
                    customization.Scale.ToString("R", CultureInfo.InvariantCulture),
                    customization.Offset.ToString("R", CultureInfo.InvariantCulture),
                    customization.GaugeEnabled ? "1" : "0",
                    customization.GaugeMin.ToString("R", CultureInfo.InvariantCulture),
                    customization.GaugeMax.ToString("R", CultureInfo.InvariantCulture));
            }

            public static TelemetryCardCustomization TryDeserialize(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return null;

                try
                {
                    string[] parts = raw.Split(';');
                    if (parts.Length < 9)
                        return null;

                    return new TelemetryCardCustomization
                    {
                        SourceTag = DecodePart(parts[0]),
                        Label = DecodePart(parts[1]),
                        AccentColor = Color.FromArgb(int.Parse(parts[2], CultureInfo.InvariantCulture)),
                        NumberFormat = DecodePart(parts[3]),
                        Scale = double.Parse(parts[4], CultureInfo.InvariantCulture),
                        Offset = double.Parse(parts[5], CultureInfo.InvariantCulture),
                        GaugeEnabled = parts[6] == "1",
                        GaugeMin = double.Parse(parts[7], CultureInfo.InvariantCulture),
                        GaugeMax = double.Parse(parts[8], CultureInfo.InvariantCulture)
                    };
                }
                catch
                {
                    return null;
                }
            }

            private static string EncodePart(string value)
            {
                return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty));
            }

            private static string DecodePart(string value)
            {
                return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty));
            }
        }

        private void OpenTelemetryCardEditor(int cardIndex)
        {
            if (cardIndex < 0 || cardIndex >= selectedMetricKeys.Count)
                return;

            using (var proxy = CreateTelemetryQuickViewProxy(cardIndex))
            using (var editor = new QuickViewOptions(proxy))
            {
                editor.StartPosition = FormStartPosition.CenterParent;
                ThemeManager.ApplyThemeTo(editor);
                editor.ShowDialog(FindForm());
                ApplyTelemetryQuickViewProxy(cardIndex, proxy);
            }
        }

        private QuickView CreateTelemetryQuickViewProxy(int cardIndex)
        {
            EnsureCustomizationSlots();

            var proxy = new QuickView
            {
                Tag = ResolveEditorSourceTag(cardIndex),
                desc = ResolveEditorLabel(cardIndex),
                numberColor = ResolveEditorColor(cardIndex),
                numberColorBackup = ResolveEditorColor(cardIndex),
                numberformat = ResolveEditorFormat(cardIndex),
                scale = ResolveEditorScale(cardIndex),
                offset = ResolveEditorOffset(cardIndex),
                isGauge = ResolveEditorGaugeEnabled(cardIndex),
                gaugeMin = ResolveEditorGaugeMin(cardIndex),
                gaugeMax = ResolveEditorGaugeMax(cardIndex)
            };

            return proxy;
        }

        private void ApplyTelemetryQuickViewProxy(int cardIndex, QuickView proxy)
        {
            string sourceTag = proxy.Tag as string;
            if (string.IsNullOrWhiteSpace(sourceTag))
                return;

            EnsureCustomizationSlots();

            selectedMetricKeys[cardIndex] = sourceTag;
            customMetricConfigs[cardIndex] = new TelemetryCardCustomization
            {
                SourceTag = sourceTag,
                Label = proxy.desc,
                AccentColor = proxy.numberColorBackup,
                NumberFormat = string.IsNullOrWhiteSpace(proxy.numberformat) ? "0.00" : proxy.numberformat,
                Scale = proxy.scale,
                Offset = proxy.offset,
                GaugeEnabled = proxy.isGauge,
                GaugeMin = proxy.gaugeMin,
                GaugeMax = proxy.gaugeMax
            };

            SaveMetricSelection();
            SaveMetricCustomizations();
            SyncCardControls();

            if (lastTelemetry != null)
                UpdateTelemetry(lastTelemetry);
        }

        private string ResolveEditorSourceTag(int cardIndex)
        {
            EnsureCustomizationSlots();

            var customization = customMetricConfigs[cardIndex];
            if (customization != null && !string.IsNullOrWhiteSpace(customization.SourceTag))
                return customization.SourceTag;

            string key = selectedMetricKeys[cardIndex];
            switch (key)
            {
                case "altitude":
                    return "altasl";
                case "ground_speed":
                    return "groundspeed";
                case "battery":
                    return "battery_voltage";
                case "endurance":
                    return "battery_remainmin";
                case "gps":
                    return "satcount";
                case "navigation":
                    return "wp_dist";
                case "time_in_air":
                    return "timeInAir";
                case "distance_travelled":
                    return "distTraveled";
                case "distance_to_home":
                    return "DistToHome";
                case "battery_used":
                    return "battery_usedmah";
                case "fuel_system":
                    return "efi_fuelflow";
                case "flight_mode":
                    return "armed";
                default:
                    return key;
            }
        }

        private string ResolveEditorLabel(int cardIndex)
        {
            EnsureCustomizationSlots();

            var customization = customMetricConfigs[cardIndex];
            if (customization != null && !string.IsNullOrWhiteSpace(customization.Label))
                return customization.Label;

            if (cardIndex >= 0 && cardIndex < cards.Count && !string.IsNullOrWhiteSpace(cards[cardIndex].Title))
                return cards[cardIndex].Title;

            string sourceKey = ResolveTelemetrySourceKey(ResolveEditorSourceTag(cardIndex));
            return GetTelemetryDisplayName(sourceKey, MainV2.comPort?.MAV?.cs);
        }

        private Color ResolveEditorColor(int cardIndex)
        {
            EnsureCustomizationSlots();

            var customization = customMetricConfigs[cardIndex];
            if (customization != null)
                return customization.AccentColor;

            if (cardIndex >= 0 && cardIndex < cards.Count)
                return cards[cardIndex].AccentColor;

            return GetMetricAccent(selectedMetricKeys[cardIndex]);
        }

        private string ResolveEditorFormat(int cardIndex)
        {
            EnsureCustomizationSlots();

            var customization = customMetricConfigs[cardIndex];
            if (customization != null && !string.IsNullOrWhiteSpace(customization.NumberFormat))
                return customization.NumberFormat;

            string sourceTag = ResolveEditorSourceTag(cardIndex);
            switch (ResolveTelemetrySourceKey(sourceTag))
            {
                case "altasl":
                case "satcount":
                case "wp_dist":
                case "distTraveled":
                case "DistToHome":
                case "battery_remaining":
                case "battery_usedmah":
                case "efi_fuelflow":
                    return "0";
                case "timeInAir":
                    return "mm\\:ss";
                default:
                    return "0.00";
            }
        }

        private double ResolveEditorScale(int cardIndex)
        {
            EnsureCustomizationSlots();
            return customMetricConfigs[cardIndex]?.Scale ?? 1.0;
        }

        private double ResolveEditorOffset(int cardIndex)
        {
            EnsureCustomizationSlots();
            return customMetricConfigs[cardIndex]?.Offset ?? 0.0;
        }

        private bool ResolveEditorGaugeEnabled(int cardIndex)
        {
            EnsureCustomizationSlots();
            return customMetricConfigs[cardIndex]?.GaugeEnabled ?? false;
        }

        private double ResolveEditorGaugeMin(int cardIndex)
        {
            EnsureCustomizationSlots();
            return customMetricConfigs[cardIndex]?.GaugeMin ?? 0.0;
        }

        private double ResolveEditorGaugeMax(int cardIndex)
        {
            EnsureCustomizationSlots();
            return customMetricConfigs[cardIndex]?.GaugeMax ?? 100.0;
        }

        private void TelemetryCard_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (!(sender is ModernTelemetryCard card))
                return;

            int cardIndex = cards.IndexOf(card);
            if (cardIndex < 0)
                return;

            OpenTelemetryCardEditor(cardIndex);
        }
    }

    public class PanelMap3DDeck : Panel
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly Font eyebrowFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private readonly Font titleFont = new Font("Segoe UI", 12, FontStyle.Bold);
        private readonly Font subtitleFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        private readonly Font badgeFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private readonly Font overlayTitleFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private readonly Font overlayDetailFont = new Font("Segoe UI", 8f, FontStyle.Regular);
        private readonly Font mapButtonFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        private readonly Font previewTitleFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        private readonly Font previewStateFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);

        private readonly Color Surface = Color.FromArgb(24, 31, 44);
        private readonly Color Border = Color.FromArgb(44, 54, 73);
        private readonly Color Gold = Color.FromArgb(200, 168, 101);
        private readonly Color LightGray = Color.FromArgb(235, 239, 245);
        private readonly Color MutedGray = Color.FromArgb(138, 149, 168);
        private readonly Color LiveAccent = Color.FromArgb(74, 190, 225);
        private readonly Color StandbyAccent = Color.FromArgb(110, 118, 136);
        private readonly Color MapSurface = Color.FromArgb(10, 18, 17);
        private readonly Color MapOverlaySurface = Color.FromArgb(24, 31, 44);
        private readonly Color PreviewSurface = Color.FromArgb(101, 87, 156);
        private readonly Color PreviewBorder = Color.FromArgb(141, 126, 198);
        private readonly bool showEmbeddedPreview;

        private Panel headerPanel;
        private Panel mapBorderPanel;
        private Panel mapHostPanel;
        private Panel mapChromePanel;
        private FlowLayoutPanel mapButtonBar;
        private Label lblEyebrow;
        private Label lblTitle;
        private Label lblSubtitle;
        private Label lblStatus;
        private Panel statusDot;
        private Label lblMapOverlayTitle;
        private Label lblMapOverlaySubtitle;
        private Button btnSatellite;
        private Button btnTerrain;
        private Button btnZoomIn;
        private Button btnZoomOut;
        private Panel previewBorderPanel;
        private Panel previewPanel;
        private Panel previewHeaderPanel;
        private Panel previewContentPanel;
        private Panel previewResizeHandle;
        private Button btnPreviewMaximize;
        private Label lblPreviewTitle;
        private Label lblPreviewBadge;
        private Label lblPreviewState;
        private myGMAP tacticalMap;
        private TacticalMapGlassOverlay tacticalGlassOverlay;
        private GMapOverlay mapOverlay;
        private GMapOverlay missionOverlay;
        private GMapRoute breadcrumbRoute;
        private GMapRoute homeRoute;
        private GMarkerGoogle homeMarker;
        private GMapMarkerPlane aircraftMarker;
        private readonly List<PointLatLng> breadcrumbPoints = new List<PointLatLng>();
        private int lastMissionWaypointCount = -1;
        private DateTime lastMissionOverlayRefresh = DateTime.MinValue;
        private Map3D mapView;
        private bool previewAvailable;
        private string previewInitError = "";
        private GMapProvider selectedMapProvider;
        private Size preferredPreviewSize = new Size(280, 180);
        private Size appliedPreviewSize = new Size(280, 180);
        private Size previewRestoreSize = new Size(280, 180);
        private bool previewExpanded;
        private bool updatingMapPosition;
        private bool updatingMapZoom;
        private DateTime manualNavigationUntilUtc = DateTime.MinValue;
        private bool resizingPreview;
        private Point previewResizeStartCursor;
        private Size previewResizeStartSize;

        public PanelMap3DDeck(bool showEmbeddedPreview = false)
        {
            this.showEmbeddedPreview = showEmbeddedPreview;
            BackColor = Color.FromArgb(10, 14, 20);
            Padding = new Padding(10, 8, 10, 10);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            InitializeControls();
            ApplyDeckState(false, false, "OFFLINE",
                "Connect a vehicle to begin the live mission map.");
            ApplyPreviewState(false, false, "STANDBY", null);
        }

        private void InitializeControls()
        {
            mapBorderPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Border,
                Padding = new Padding(1)
            };
            Controls.Add(mapBorderPanel);

            mapHostPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = MapSurface
            };
            mapBorderPanel.Controls.Add(mapHostPanel);

            headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 0,
                BackColor = Surface,
                Visible = false
            };
            Controls.Add(headerPanel);

            lblEyebrow = new Label
            {
                Font = eyebrowFont,
                ForeColor = Gold,
                BackColor = Surface,
                Text = "2D MAP VIEW",
                AutoEllipsis = true
            };
            headerPanel.Controls.Add(lblEyebrow);

            lblTitle = new Label
            {
                Font = titleFont,
                ForeColor = LightGray,
                BackColor = Surface,
                Text = "Mission map standing by",
                AutoEllipsis = true
            };
            headerPanel.Controls.Add(lblTitle);

            lblSubtitle = new Label
            {
                Font = subtitleFont,
                ForeColor = MutedGray,
                BackColor = Surface,
                Text = "Waiting for live telemetry.",
                AutoEllipsis = true
            };
            headerPanel.Controls.Add(lblSubtitle);

            statusDot = new Panel
            {
                BackColor = StandbyAccent,
                Size = new Size(8, 8)
            };
            headerPanel.Controls.Add(statusDot);

            lblStatus = new Label
            {
                Font = badgeFont,
                ForeColor = LightGray,
                BackColor = Color.FromArgb(44, 49, 59),
                Text = "OFFLINE",
                TextAlign = ContentAlignment.MiddleCenter
            };
            headerPanel.Controls.Add(lblStatus);

            InitializeTacticalMap();
            InitializeMapChrome();

            if (showEmbeddedPreview)
            {
                InitializePreviewPanel();
                TryInitializeMapView();
            }

            LayoutHeader();
            LayoutMapChrome();
        }

        private void InitializeTacticalMap()
        {
            tacticalMap = new myGMAP
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(8, 12, 20),
                EmptyTileColor = Color.FromArgb(20, 24, 33),
                CanDragMap = true,
                DragButton = MouseButtons.Left,
                DisableFocusOnMouseEnter = true,
                GrayScaleMode = false,
                HelperLineOption = HelperLineOptions.DontShow,
                HoldInvalidation = false,
                LevelsKeepInMemmory = 5,
                MarkersEnabled = true,
                MaxZoom = 22,
                MinZoom = 2,
                MouseWheelZoomType = GMap.NET.MouseWheelZoomType.MousePositionWithoutCenter,
                NegativeMode = false,
                PolygonsEnabled = false,
                RetryLoadTile = 0,
                RoutesEnabled = true,
                ScaleMode = ScaleModes.Fractional,
                SelectedAreaFillColor = Color.FromArgb(33, 65, 105, 225),
                ShowTileGridLines = false,
                Zoom = 16
            };

            tacticalMap.CacheLocation = Settings.GetDataDirectory() + "gmapcache" + System.IO.Path.DirectorySeparatorChar;
            tacticalMap.OnPositionChanged += TacticalMap_OnPositionChanged;
            tacticalMap.OnMapZoomChanged += TacticalMap_OnMapZoomChanged;

            mapOverlay = new GMapOverlay("modern-flight-map");

            breadcrumbRoute = new GMapRoute("breadcrumb")
            {
                Stroke = new Pen(Color.FromArgb(235, 90, 210, 204), 2.8f)
                {
                    LineJoin = LineJoin.Round,
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    DashPattern = new[] { 6f, 4f }
                },
                IsHitTestVisible = false,
                ArrowMode = GMapRoute.ArrowDrawMode.SinglePerRoute
            };

            homeRoute = new GMapRoute("home-vector")
            {
                Stroke = new Pen(Color.FromArgb(200, 214, 180, 101), 1.8f)
                {
                    DashStyle = DashStyle.Dash,
                    DashPattern = new[] { 4f, 4f }
                },
                IsHitTestVisible = false
            };

            homeMarker = new GMarkerGoogle(PointLatLng.Empty, GMarkerGoogleType.blue_dot)
            {
                IsVisible = false,
                ToolTipText = "Home"
            };

            aircraftMarker = new GMapMarkerPlane(0, PointLatLng.Empty, 0, 0, 0, 0, 0)
            {
                IsVisible = false
            };

            mapOverlay.Routes.Add(homeRoute);
            mapOverlay.Routes.Add(breadcrumbRoute);
            mapOverlay.Markers.Add(homeMarker);
            mapOverlay.Markers.Add(aircraftMarker);
            tacticalMap.Overlays.Add(mapOverlay);

            selectedMapProvider = GMapProviders.GoogleSatelliteMap;

            tacticalMap.MapProvider = selectedMapProvider;
            LoadSavedMapView();

            mapHostPanel.Controls.Add(tacticalMap);
            tacticalMap.SendToBack();
        }

        private void InitializeMapChrome()
        {
            mapChromePanel = new Panel
            {
                BackColor = Color.FromArgb(18, 24, 33),
                Padding = new Padding(6, 4, 6, 4)
            };
            mapHostPanel.Controls.Add(mapChromePanel);

            mapButtonBar = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.FromArgb(18, 24, 33)
            };
            mapChromePanel.Controls.Add(mapButtonBar);

            btnSatellite = CreateMapButton("Satellite");
            btnSatellite.Click += (s, e) => SetMapProvider(GMapProviders.GoogleSatelliteMap);
            mapButtonBar.Controls.Add(btnSatellite);

            btnTerrain = CreateMapButton("Terrain");
            btnTerrain.Click += (s, e) => SetMapProvider(GMapProviders.GoogleTerrainMap);
            mapButtonBar.Controls.Add(btnTerrain);

            btnZoomIn = CreateMapButton("+");
            btnZoomIn.Click += (s, e) =>
            {
                if (tacticalMap != null && tacticalMap.Zoom < tacticalMap.MaxZoom)
                    tacticalMap.Zoom += 1;
            };
            mapButtonBar.Controls.Add(btnZoomIn);

            btnZoomOut = CreateMapButton("-");
            btnZoomOut.Click += (s, e) =>
            {
                if (tacticalMap != null && tacticalMap.Zoom > tacticalMap.MinZoom)
                    tacticalMap.Zoom -= 1;
            };
            mapButtonBar.Controls.Add(btnZoomOut);

            ApplyMapProviderButtonState();
        }

        private void InitializePreviewPanel()
        {
            previewBorderPanel = new Panel
            {
                BackColor = PreviewBorder,
                Padding = new Padding(1)
            };
            mapHostPanel.Controls.Add(previewBorderPanel);

            previewPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = PreviewSurface
            };
            previewBorderPanel.Controls.Add(previewPanel);

            previewHeaderPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = Color.FromArgb(90, 76, 140)
            };
            previewPanel.Controls.Add(previewHeaderPanel);

            lblPreviewTitle = new Label
            {
                Font = previewTitleFont,
                ForeColor = LightGray,
                BackColor = previewHeaderPanel.BackColor,
                Text = "3D Situational",
                AutoEllipsis = true
            };
            previewHeaderPanel.Controls.Add(lblPreviewTitle);

            lblPreviewBadge = new Label
            {
                Font = badgeFont,
                ForeColor = LightGray,
                BackColor = Color.FromArgb(73, 65, 110),
                Text = "OFFLINE",
                TextAlign = ContentAlignment.MiddleCenter
            };
            previewHeaderPanel.Controls.Add(lblPreviewBadge);

            btnPreviewMaximize = CreateMapButton("MAX");
            btnPreviewMaximize.BackColor = Color.FromArgb(73, 65, 110);
            btnPreviewMaximize.Click += (s, e) => TogglePreviewExpanded();
            previewHeaderPanel.Controls.Add(btnPreviewMaximize);

            previewContentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = PreviewSurface
            };
            previewPanel.Controls.Add(previewContentPanel);

            lblPreviewState = new Label
            {
                Dock = DockStyle.Fill,
                Font = previewStateFont,
                ForeColor = Color.FromArgb(232, 235, 245),
                BackColor = PreviewSurface,
                Padding = new Padding(16),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "Connect vehicle for 3D view"
            };
            previewContentPanel.Controls.Add(lblPreviewState);

            previewResizeHandle = new Panel
            {
                Size = new Size(18, 18),
                BackColor = Color.FromArgb(126, 112, 182),
                Cursor = Cursors.SizeNWSE,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            previewResizeHandle.Paint += PreviewResizeHandle_Paint;
            previewResizeHandle.MouseDown += PreviewResizeHandle_MouseDown;
            previewResizeHandle.MouseMove += PreviewResizeHandle_MouseMove;
            previewResizeHandle.MouseUp += PreviewResizeHandle_MouseUp;
            previewPanel.Controls.Add(previewResizeHandle);
            previewResizeHandle.BringToFront();
        }

        private Button CreateMapButton(string text)
        {
            var button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Font = mapButtonFont,
                BackColor = Color.FromArgb(18, 22, 30),
                ForeColor = LightGray,
                TabStop = false,
                UseVisualStyleBackColor = false,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter
            };

            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(46, 58, 76);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(27, 36, 48);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(34, 44, 58);
            return button;
        }

        private int GetMapButtonWidth(Button button, int minimumWidth, int horizontalPadding = 18)
        {
            if (button == null)
                return minimumWidth;

            Size preferredSize = button.GetPreferredSize(new Size(int.MaxValue, 22));
            Size measured = TextRenderer.MeasureText(
                button.Text ?? string.Empty,
                button.Font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            return Math.Max(minimumWidth, Math.Max(preferredSize.Width, measured.Width + horizontalPadding));
        }

        private void TryInitializeMapView()
        {
            try
            {
                mapView = new Map3D
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.Black,
                    Name = "modernMap3DPreview",
                    Visible = false
                };

                previewContentPanel.Controls.Add(mapView);
                mapView.SendToBack();
                previewAvailable = true;
            }
            catch (Exception ex)
            {
                previewInitError = ex.Message;
                previewAvailable = false;
                log.Error("Modern flight 3D map failed to initialize", ex);
            }
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            LayoutHeader();
        }

        private void LayoutHeader()
        {
            if (mapHostPanel == null || mapChromePanel == null || mapButtonBar == null)
                return;

            int buttonBarWidth = mapButtonBar?.GetPreferredSize(Size.Empty).Width ?? 0;
            int toolbarHeight = 30;
            int toolbarWidth = buttonBarWidth + mapChromePanel.Padding.Horizontal;

            mapChromePanel.Bounds = new Rectangle(
                14,
                14,
                Math.Max(90, toolbarWidth),
                toolbarHeight);

            mapButtonBar.Bounds = new Rectangle(
                mapChromePanel.Padding.Left,
                mapChromePanel.Padding.Top,
                buttonBarWidth,
                22);

            mapChromePanel.BringToFront();
            mapButtonBar.BringToFront();
        }

        private void LayoutMapChrome()
        {
            if (mapHostPanel == null)
                return;
            int zoomButtonWidth = 28;
            int terrainButtonWidth = GetMapButtonWidth(btnTerrain, 84, 24);
            int satelliteButtonWidth = GetMapButtonWidth(btnSatellite, 96, 24);

            btnSatellite.Margin = new Padding(0, 0, 6, 0);
            btnTerrain.Margin = new Padding(0, 0, 6, 0);
            btnZoomIn.Margin = new Padding(0, 0, 4, 0);
            btnZoomOut.Margin = Padding.Empty;

            btnSatellite.Size = new Size(satelliteButtonWidth, 22);
            btnTerrain.Size = new Size(terrainButtonWidth, 22);
            btnZoomIn.Size = new Size(zoomButtonWidth, 22);
            btnZoomOut.Size = new Size(zoomButtonWidth, 22);

            mapButtonBar?.PerformLayout();
            LayoutHeader();

            if (!showEmbeddedPreview || previewBorderPanel == null)
                return;

            int previewWidth = Math.Max(220, Math.Min(preferredPreviewSize.Width, Math.Max(260, mapHostPanel.ClientSize.Width / 2)));
            int previewHeight = Math.Max(130, Math.Min(preferredPreviewSize.Height, Math.Max(150, mapHostPanel.ClientSize.Height / 2)));

            if (previewExpanded)
            {
                int expandedLeft = 18;
                int expandedTop = 18;
                int expandedWidth = Math.Max(340, mapHostPanel.ClientSize.Width - 36);
                int expandedHeight = Math.Max(220, mapHostPanel.ClientSize.Height - expandedTop - 18);
                appliedPreviewSize = new Size(expandedWidth, expandedHeight);
                previewBorderPanel.Bounds = new Rectangle(expandedLeft, expandedTop, expandedWidth, expandedHeight);
            }
            else
            {
                appliedPreviewSize = new Size(previewWidth, previewHeight);
                previewBorderPanel.Bounds = new Rectangle(
                    Math.Max(18, mapHostPanel.ClientSize.Width - previewWidth - 18),
                    Math.Max(18, mapHostPanel.ClientSize.Height - previewHeight - 18),
                    previewWidth,
                    previewHeight);
            }

            btnPreviewMaximize.Text = previewExpanded ? "RESTORE" : "MAX";
            btnPreviewMaximize.Bounds = new Rectangle(
                Math.Max(102, previewHeaderPanel.ClientSize.Width - 160),
                4,
                68,
                20);

            lblPreviewBadge.Bounds = new Rectangle(
                Math.Max(92, btnPreviewMaximize.Left - 82),
                4,
                76,
                20);

            lblPreviewTitle.Bounds = new Rectangle(10, 7, Math.Max(80, lblPreviewBadge.Left - 18), 14);

            previewResizeHandle.Location = new Point(
                Math.Max(0, previewPanel.ClientSize.Width - previewResizeHandle.Width - 4),
                Math.Max(previewHeaderPanel.Bottom + 4, previewPanel.ClientSize.Height - previewResizeHandle.Height - 4));
            previewResizeHandle.Visible = !previewExpanded;
            previewBorderPanel.BringToFront();
        }

        private void PreviewResizeHandle_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (var pen = new Pen(Color.FromArgb(210, 228, 233, 245), 1.4f))
            {
                e.Graphics.DrawLine(pen, 5, 13, 13, 5);
                e.Graphics.DrawLine(pen, 8, 13, 13, 8);
                e.Graphics.DrawLine(pen, 11, 13, 13, 11);
            }
        }

        private void PreviewResizeHandle_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || previewExpanded)
                return;

            resizingPreview = true;
            previewResizeStartCursor = System.Windows.Forms.Cursor.Position;
            previewResizeStartSize = appliedPreviewSize;
            previewResizeHandle.Capture = true;
        }

        private void PreviewResizeHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (!resizingPreview || mapHostPanel == null || previewExpanded)
                return;

            Point currentCursor = System.Windows.Forms.Cursor.Position;
            int deltaX = currentCursor.X - previewResizeStartCursor.X;
            int deltaY = currentCursor.Y - previewResizeStartCursor.Y;

            preferredPreviewSize = new Size(
                previewResizeStartSize.Width + deltaX,
                previewResizeStartSize.Height + deltaY);

            LayoutMapChrome();
        }

        private void PreviewResizeHandle_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            resizingPreview = false;
            previewResizeHandle.Capture = false;
        }

        private void TogglePreviewExpanded()
        {
            if (!showEmbeddedPreview)
                return;

            if (!previewExpanded)
            {
                previewRestoreSize = appliedPreviewSize;
                previewExpanded = true;
            }
            else
            {
                previewExpanded = false;
                preferredPreviewSize = previewRestoreSize;
            }

            LayoutMapChrome();
        }

        public void ActivateView()
        {
            if (!showEmbeddedPreview)
                return;

            if (!previewAvailable || mapView == null)
                return;

            try
            {
                mapView.Activate();
            }
            catch (Exception ex)
            {
                log.Debug($"Modern flight 3D activation error: {ex.Message}");
            }
        }

        public void DeactivateView()
        {
            try
            {
                if (showEmbeddedPreview)
                    mapView?.Deactivate();
            }
            catch (Exception ex)
            {
                log.Debug($"Modern flight 3D deactivation error: {ex.Message}");
            }

            SaveMapView();
        }

        public void SetOffline(bool connected)
        {
            ApplyDeckState(connected, false, connected ? "SYNCING" : "OFFLINE",
                connected
                    ? "Vehicle link detected. Waiting for a valid position fix."
                    : "Connect a vehicle to begin the live mission map.");
            ApplyPreviewState(connected, false, "STANDBY", null);
            UpdateTacticalOverlayState(connected, false, false, "STANDBY", 0, 0, 0, 0, 0);
        }

        public void UpdateTelemetry(CurrentState cs)
        {
            bool connected = MainV2.comPort?.BaseStream?.IsOpen == true;
            bool hasPosition = HasValidCoordinate(cs.lat, cs.lng);
            string mode = string.IsNullOrWhiteSpace(cs.mode) ? "STANDBY" : cs.mode.ToUpperInvariant();

            ApplyDeckState(connected, hasPosition,
                connected && hasPosition ? "LIVE MAP" : connected ? "SYNCING" : "OFFLINE",
                hasPosition
                    ? $"{mode}  |  {cs.lat:F5}, {cs.lng:F5}  |  Alt {cs.altasl:F0} m"
                    : connected
                        ? "Vehicle linked. Waiting for a stable position fix for the mission map."
                        : "Connect a vehicle to begin the live mission map.");

            UpdateTacticalMap(cs);
            ApplyPreviewState(connected, hasPosition, mode, cs);

            if (!previewAvailable || mapView == null || !connected || !hasPosition)
                return;

            try
            {
                mapView.rpy = new MissionPlanner.Utilities.Vector3(cs.roll, cs.pitch, cs.yaw);

                double terrainAlt = srtm.getAltitude(cs.lat, cs.lng).alt;
                double relativeAlt = CurrentState.multiplieralt == 0
                    ? cs.alt
                    : cs.alt / CurrentState.multiplieralt;

                mapView.LocationCenter = new PointLatLngAlt(cs.lat, cs.lng, terrainAlt + relativeAlt, "here");
                mapView.Velocity = new MissionPlanner.Utilities.Vector3(cs.vx, cs.vy, cs.vz);

                var waypoints = MainV2.comPort?.MAV?.wps?.Values;
                mapView.WPs = waypoints != null
                    ? waypoints.Select(item => (Locationwp)item).ToList()
                    : new System.Collections.Generic.List<Locationwp>();
            }
            catch (Exception ex)
            {
                log.Debug($"Modern flight 3D update error: {ex.Message}");
            }
        }

        private void ApplyDeckState(bool connected, bool hasPosition, string badgeText, string subtitle)
        {
            mapBorderPanel.BackColor = hasPosition
                ? Color.FromArgb(55, 102, 114)
                : connected
                    ? Color.FromArgb(92, 78, 46)
                    : Border;

            if (mapChromePanel != null)
            {
                mapChromePanel.BackColor = Color.FromArgb(18, 24, 33);
                if (mapButtonBar != null)
                    mapButtonBar.BackColor = mapChromePanel.BackColor;
            }
        }

        private void ApplyPreviewState(bool connected, bool hasPosition, string mode, CurrentState cs)
        {
            if (!showEmbeddedPreview || previewBorderPanel == null || lblPreviewBadge == null ||
                previewPanel == null || previewContentPanel == null || lblPreviewState == null)
            {
                return;
            }

            bool livePreview = previewAvailable && connected && hasPosition && mapView != null;

            lblPreviewBadge.Text = !previewAvailable
                ? "3D OFF"
                : livePreview
                    ? "LIVE 3D"
                    : connected
                        ? "SYNCING"
                        : "OFFLINE";

            lblPreviewBadge.BackColor = !previewAvailable
                ? Color.FromArgb(83, 74, 120)
                : livePreview
                    ? Color.FromArgb(33, 93, 126)
                    : connected
                        ? Color.FromArgb(111, 90, 48)
                        : Color.FromArgb(73, 65, 110);

            if (btnPreviewMaximize != null)
            {
                btnPreviewMaximize.BackColor = livePreview
                    ? Color.FromArgb(29, 83, 112)
                    : connected
                        ? Color.FromArgb(95, 81, 144)
                        : Color.FromArgb(73, 65, 110);
                btnPreviewMaximize.ForeColor = LightGray;
            }

            previewBorderPanel.BackColor = !previewAvailable
                ? PreviewBorder
                : livePreview
                    ? Color.FromArgb(79, 129, 168)
                    : PreviewBorder;

            previewPanel.BackColor = livePreview ? Color.FromArgb(18, 22, 30) : PreviewSurface;
            previewContentPanel.BackColor = livePreview ? Color.Black : PreviewSurface;
            lblPreviewState.BackColor = previewContentPanel.BackColor;

            if (!previewAvailable)
            {
                lblPreviewState.Text = string.IsNullOrWhiteSpace(previewInitError)
                    ? "3D preview unavailable."
                    : $"3D preview unavailable.\r\n{previewInitError}";
                lblPreviewState.Visible = true;
                if (mapView != null)
                    mapView.Visible = false;
                return;
            }

            if (!connected)
            {
                lblPreviewState.Text = "Connect vehicle\r\nfor 3D view";
                lblPreviewState.Visible = true;
                mapView.Visible = false;
                return;
            }

            if (!hasPosition)
            {
                lblPreviewState.Text = "Waiting for valid position\r\nand attitude telemetry";
                lblPreviewState.Visible = true;
                mapView.Visible = false;
                return;
            }

            lblPreviewState.Text = $"{mode}\r\nAlt {cs.altasl:F0} m  |  GS {cs.groundspeed:F1} m/s";
            lblPreviewState.Visible = false;
            mapView.Visible = true;
        }

        private void UpdateTacticalMap(CurrentState cs)
        {
            if (tacticalMap == null)
                return;

            bool hasAircraft = HasValidCoordinate(cs.lat, cs.lng);
            bool hasHome = HasValidCoordinate(cs.HomeLocation.Lat, cs.HomeLocation.Lng);

            if (!hasHome && HasValidCoordinate(cs.PlannedHomeLocation.Lat, cs.PlannedHomeLocation.Lng))
            {
                hasHome = true;
            }

            aircraftMarker.IsVisible = hasAircraft;
            homeMarker.IsVisible = hasHome;

            if (hasAircraft)
            {
                var currentPoint = new PointLatLng(cs.lat, cs.lng);
                aircraftMarker.Position = currentPoint;
                aircraftMarker.Heading = cs.yaw;
                aircraftMarker.Cog = cs.groundcourse > 0 ? cs.groundcourse : cs.yaw;
                aircraftMarker.Nav_bearing = cs.nav_bearing;
                aircraftMarker.Target = cs.target_bearing;

                if (!tacticalMap.IsDragging && !IsManualNavigationActive())
                {
                    if (tacticalMap.Position.IsEmpty ||
                        GMapProviders.EmptyProvider.Projection.GetDistance(tacticalMap.Position, currentPoint) > 0.05)
                    {
                        SetMapPositionInternal(currentPoint);
                    }
                }

                if (breadcrumbPoints.Count == 0 ||
                    GMapProviders.EmptyProvider.Projection.GetDistance(
                        breadcrumbPoints[breadcrumbPoints.Count - 1], currentPoint) > 0.008)
                {
                    breadcrumbPoints.Add(currentPoint);
                    if (breadcrumbPoints.Count > 120)
                        breadcrumbPoints.RemoveAt(0);

                    breadcrumbRoute.Points.Clear();
                    breadcrumbRoute.Points.AddRange(breadcrumbPoints);
                    tacticalMap.UpdateRouteLocalPosition(breadcrumbRoute);
                }
            }

            if (hasHome)
            {
                double homeLat = HasValidCoordinate(cs.HomeLocation.Lat, cs.HomeLocation.Lng)
                    ? cs.HomeLocation.Lat
                    : cs.PlannedHomeLocation.Lat;
                double homeLng = HasValidCoordinate(cs.HomeLocation.Lat, cs.HomeLocation.Lng)
                    ? cs.HomeLocation.Lng
                    : cs.PlannedHomeLocation.Lng;

                var homePoint = new PointLatLng(homeLat, homeLng);
                homeMarker.Position = homePoint;

                homeRoute.Points.Clear();
                if (hasAircraft)
                {
                    homeRoute.Points.Add(new PointLatLng(cs.lat, cs.lng));
                    homeRoute.Points.Add(homePoint);
                }

                tacticalMap.UpdateRouteLocalPosition(homeRoute);

                if (!hasAircraft && tacticalMap.Position.IsEmpty)
                {
                    SetMapPositionInternal(homePoint);
                }
            }
            else
            {
                homeRoute.Points.Clear();
                tacticalMap.UpdateRouteLocalPosition(homeRoute);
            }

            UpdateMissionOverlay(cs);

            UpdateTacticalOverlayState(
                MainV2.comPort?.BaseStream?.IsOpen == true,
                hasAircraft,
                hasHome,
                cs.mode,
                (float)cs.yaw,
                cs.DistToHome,
                cs.linkqualitygcs,
                cs.lat,
                cs.lng);

            tacticalMap.Invalidate();
            tacticalGlassOverlay?.Invalidate();
        }

        private void UpdateMissionOverlay(CurrentState cs)
        {
            if (tacticalMap == null)
                return;

            var waypointValues = MainV2.comPort?.MAV?.wps?.Values;
            if (waypointValues == null)
            {
                ClearMissionOverlay();
                return;
            }

            var missionItems = waypointValues.Select(item => (Locationwp)item).ToList();
            if (missionItems.Count == 0)
            {
                ClearMissionOverlay();
                return;
            }

            var now = DateTime.UtcNow;
            if (missionOverlay != null &&
                missionItems.Count == lastMissionWaypointCount &&
                lastMissionOverlayRefresh.AddSeconds(2) > now)
            {
                return;
            }

            var homeplla = new PointLatLngAlt(
                cs.HomeLocation.Lat,
                cs.HomeLocation.Lng,
                cs.HomeLocation.Alt / CurrentState.multiplieralt,
                "H");

            if (!HasValidCoordinate(homeplla.Lat, homeplla.Lng))
            {
                homeplla = new PointLatLngAlt(
                    cs.PlannedHomeLocation.Lat,
                    cs.PlannedHomeLocation.Lng,
                    cs.PlannedHomeLocation.Alt / CurrentState.multiplieralt,
                    "H");
            }

            if (!HasValidCoordinate(homeplla.Lat, homeplla.Lng) && missionItems.Count > 0)
            {
                homeplla = new PointLatLngAlt(
                    missionItems[0].lat,
                    missionItems[0].lng,
                    missionItems[0].alt / CurrentState.multiplieralt,
                    "H");
            }

            List<Locationwp> routeMissionItems = new List<Locationwp>(missionItems);
            if (routeMissionItems.Count > 0)
                routeMissionItems.RemoveAt(0);

            GMapOverlay activeOverlay;
            if (Settings.Instance.GetBoolean("UseWPOverlay2", true))
            {
                var wpOverlay2 = new WPOverlay2
                {
                    VehicleClass = MainV2.comPort.MAV.cs.vehicleClass,
                    ShowPlusMarkers = false
                };

                wpOverlay2.CreateOverlay(
                    homeplla,
                    routeMissionItems,
                    0 / CurrentState.multiplieralt,
                    0 / CurrentState.multiplieralt,
                    CurrentState.multiplieralt);

                activeOverlay = wpOverlay2.overlay;
            }
            else
            {
                var wpOverlay = new MissionPlanner.ArduPilot.WPOverlay();
                wpOverlay.CreateOverlay(
                    homeplla,
                    routeMissionItems,
                    0 / CurrentState.multiplieralt,
                    0 / CurrentState.multiplieralt,
                    CurrentState.multiplieralt);

                activeOverlay = wpOverlay.overlay;
            }

            ClearMissionOverlay();

            missionOverlay = activeOverlay;

            int insertIndex = mapOverlay != null
                ? tacticalMap.Overlays.IndexOf(mapOverlay)
                : tacticalMap.Overlays.Count;
            if (insertIndex < 0)
                insertIndex = tacticalMap.Overlays.Count;

            tacticalMap.Overlays.Insert(insertIndex, missionOverlay);
            missionOverlay.ForceUpdate();

            lastMissionWaypointCount = missionItems.Count;
            lastMissionOverlayRefresh = now;
        }

        private void ClearMissionOverlay()
        {
            if (tacticalMap != null)
            {
                if (missionOverlay != null)
                {
                    tacticalMap.Overlays.Remove(missionOverlay);
                }

                var staleMissionOverlays = tacticalMap.Overlays
                    .Where(overlay => overlay != mapOverlay &&
                                      (overlay.Id == "WPOverlay2" || overlay.Id == "WPOverlay"))
                    .ToList();

                foreach (var overlay in staleMissionOverlays)
                {
                    tacticalMap.Overlays.Remove(overlay);
                    DisposeMissionOverlayRoutes(overlay);
                }
            }

            DisposeMissionOverlayRoutes(missionOverlay);
            missionOverlay = null;
            lastMissionWaypointCount = -1;
        }

        private static void DisposeMissionOverlayRoutes(GMapOverlay overlay)
        {
            if (overlay == null)
                return;

            foreach (var route in overlay.Routes)
            {
                route?.Stroke?.Dispose();
            }
        }

        private void UpdateTacticalOverlayState(bool connected, bool hasPosition, bool hasHome, string mode,
            float heading, float distanceToHome, float linkQuality, double latitude, double longitude)
        {
            if (tacticalGlassOverlay == null)
                return;

            tacticalGlassOverlay.IsConnected = connected;
            tacticalGlassOverlay.HasPosition = hasPosition;
            tacticalGlassOverlay.HasHome = hasHome;
            tacticalGlassOverlay.Mode = string.IsNullOrWhiteSpace(mode) ? "STANDBY" : mode.ToUpperInvariant();
            tacticalGlassOverlay.Heading = heading;
            tacticalGlassOverlay.DistanceToHome = distanceToHome;
            tacticalGlassOverlay.LinkQuality = linkQuality;
            tacticalGlassOverlay.PositionLabel = hasPosition
                ? $"{latitude:F4}, {longitude:F4}"
                : "Awaiting position";
        }

        private void LoadSavedMapView()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(Settings.Instance["maplast_lat"]))
                {
                    SetMapPositionInternal(new PointLatLng(
                        Settings.Instance.GetDouble("maplast_lat"),
                        Settings.Instance.GetDouble("maplast_lng")));

                    if (Math.Round(Settings.Instance.GetDouble("maplast_lat"), 1) == 0)
                    {
                        SetMapZoomInternal(3);
                    }
                    else
                    {
                        SetMapZoomInternal(Math.Max(tacticalMap.MinZoom,
                            Math.Min(tacticalMap.MaxZoom, Settings.Instance.GetFloat("maplast_zoom"))));
                    }

                    return;
                }
            }
            catch (Exception ex)
            {
                log.Debug($"Modern flight map restore error: {ex.Message}");
            }

            SetMapPositionInternal(new PointLatLng(0, 0));
            SetMapZoomInternal(3);
        }

        private void SaveMapView()
        {
            if (tacticalMap == null || tacticalMap.Position.IsEmpty)
                return;

            try
            {
                Settings.Instance["maplast_lat"] = tacticalMap.Position.Lat.ToString();
                Settings.Instance["maplast_lng"] = tacticalMap.Position.Lng.ToString();
                Settings.Instance["maplast_zoom"] = tacticalMap.Zoom.ToString();
                Settings.Instance.Save();
            }
            catch (Exception ex)
            {
                log.Debug($"Modern flight map save error: {ex.Message}");
            }
        }

        private void SetMapProvider(GMapProvider provider)
        {
            if (tacticalMap == null || provider == null)
                return;

            selectedMapProvider = provider;
            tacticalMap.MapProvider = provider;
            ApplyMapProviderButtonState();
        }

        private void ApplyMapProviderButtonState()
        {
            if (btnSatellite == null || btnTerrain == null)
                return;

            ApplyMapButtonStyle(btnSatellite, selectedMapProvider == GMapProviders.GoogleSatelliteMap);
            ApplyMapButtonStyle(btnTerrain, selectedMapProvider == GMapProviders.GoogleTerrainMap);
        }

        private void TacticalMap_OnPositionChanged(PointLatLng point)
        {
            if (updatingMapPosition)
                return;

            MarkManualNavigation();
        }

        private void TacticalMap_OnMapZoomChanged()
        {
            if (updatingMapZoom)
                return;

            MarkManualNavigation();
        }

        private void MarkManualNavigation()
        {
            manualNavigationUntilUtc = DateTime.UtcNow.AddSeconds(12);
        }

        private bool IsManualNavigationActive()
        {
            return manualNavigationUntilUtc > DateTime.UtcNow;
        }

        private void SetMapPositionInternal(PointLatLng position)
        {
            if (tacticalMap == null)
                return;

            updatingMapPosition = true;
            try
            {
                tacticalMap.Position = position;
            }
            finally
            {
                updatingMapPosition = false;
            }
        }

        private void SetMapZoomInternal(double zoom)
        {
            if (tacticalMap == null)
                return;

            updatingMapZoom = true;
            try
            {
                tacticalMap.Zoom = zoom;
            }
            finally
            {
                updatingMapZoom = false;
            }
        }

        private void ApplyMapButtonStyle(Button button, bool selected)
        {
            button.BackColor = selected ? Color.FromArgb(34, 88, 108) : Color.FromArgb(18, 24, 33);
            button.ForeColor = selected ? Color.FromArgb(234, 242, 247) : LightGray;
            button.FlatAppearance.BorderColor = selected
                ? Color.FromArgb(118, 208, 199)
                : Color.FromArgb(46, 58, 76);
        }

        public void ClearTrack()
        {
            breadcrumbPoints.Clear();
            breadcrumbRoute?.Points.Clear();
            tacticalMap?.UpdateRouteLocalPosition(breadcrumbRoute);
            tacticalMap?.Invalidate();
        }

        private static bool HasValidCoordinate(double latitude, double longitude)
        {
            return Math.Abs(latitude) > 0.00001 || Math.Abs(longitude) > 0.00001;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DeactivateView();
                ClearMissionOverlay();
                breadcrumbRoute?.Stroke?.Dispose();
                homeRoute?.Stroke?.Dispose();
                eyebrowFont?.Dispose();
                titleFont?.Dispose();
                subtitleFont?.Dispose();
                badgeFont?.Dispose();
                overlayTitleFont?.Dispose();
                overlayDetailFont?.Dispose();
                mapButtonFont?.Dispose();
                previewTitleFont?.Dispose();
                previewStateFont?.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    internal sealed class TacticalMapGlassOverlay : Control
    {
        private readonly Font chipFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private readonly Font detailFont = new Font("Segoe UI", 7.8f, FontStyle.Regular);
        private readonly Color LiveAccent = Color.FromArgb(90, 210, 204);
        private readonly Color GoldAccent = Color.FromArgb(214, 188, 128);
        private readonly Color StandbyAccent = Color.FromArgb(116, 126, 145);
        private readonly Color Surface = Color.FromArgb(132, 13, 19, 28);

        public bool IsConnected { get; set; }
        public bool HasPosition { get; set; }
        public bool HasHome { get; set; }
        public string Mode { get; set; } = "STANDBY";
        public float Heading { get; set; }
        public float DistanceToHome { get; set; }
        public float LinkQuality { get; set; }
        public string PositionLabel { get; set; } = "Awaiting position";

        public TacticalMapGlassOverlay()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            DoubleBuffered = true;
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (ClientSize.Width < 80 || ClientSize.Height < 80)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color accent = HasPosition
                ? LiveAccent
                : IsConnected
                    ? GoldAccent
                    : StandbyAccent;

            DrawVignette(e.Graphics);
            DrawGrid(e.Graphics, accent);
            DrawCornerFrame(e.Graphics, accent);
            DrawCenterReticle(e.Graphics, accent);

            DrawChip(e.Graphics,
                new Rectangle(18, Height - 46, 178, 28),
                IsConnected ? Mode : "MAP OFFLINE",
                HasPosition ? $"HDG {Heading:000}°" : "Awaiting telemetry",
                accent);

            DrawChip(e.Graphics,
                new Rectangle(Math.Max(18, Width - 206), Height - 46, 188, 28),
                HasHome ? $"HOME {DistanceToHome:0} m" : "HOME --",
                PositionLabel,
                GoldAccent);
        }

        private void DrawVignette(Graphics graphics)
        {
            using (var topBrush = new LinearGradientBrush(
                       new Rectangle(0, 0, Width, 96),
                       Color.FromArgb(168, 9, 14, 21),
                       Color.FromArgb(0, 9, 14, 21),
                       LinearGradientMode.Vertical))
            using (var bottomBrush = new LinearGradientBrush(
                       new Rectangle(0, Height - 110, Width, 110),
                       Color.FromArgb(0, 9, 14, 21),
                       Color.FromArgb(176, 9, 14, 21),
                       LinearGradientMode.Vertical))
            using (var leftBrush = new LinearGradientBrush(
                       new Rectangle(0, 0, 96, Height),
                       Color.FromArgb(116, 9, 14, 21),
                       Color.FromArgb(0, 9, 14, 21),
                       LinearGradientMode.Horizontal))
            using (var rightBrush = new LinearGradientBrush(
                       new Rectangle(Width - 96, 0, 96, Height),
                       Color.FromArgb(0, 9, 14, 21),
                       Color.FromArgb(116, 9, 14, 21),
                       LinearGradientMode.Horizontal))
            {
                graphics.FillRectangle(topBrush, 0, 0, Width, 96);
                graphics.FillRectangle(bottomBrush, 0, Height - 110, Width, 110);
                graphics.FillRectangle(leftBrush, 0, 0, 96, Height);
                graphics.FillRectangle(rightBrush, Width - 96, 0, 96, Height);
            }
        }

        private void DrawGrid(Graphics graphics, Color accent)
        {
            using (var gridPen = new Pen(Color.FromArgb(38, accent.R, accent.G, accent.B), 1f))
            using (var majorPen = new Pen(Color.FromArgb(58, accent.R, accent.G, accent.B), 1.1f))
            {
                int[] verticals = { Width / 4, Width / 2, Width * 3 / 4 };
                int[] horizontals = { Height / 4, Height / 2, Height * 3 / 4 };

                foreach (int x in verticals)
                {
                    graphics.DrawLine(x == Width / 2 ? majorPen : gridPen, x, 0, x, Height);
                }

                foreach (int y in horizontals)
                {
                    graphics.DrawLine(y == Height / 2 ? majorPen : gridPen, 0, y, Width, y);
                }
            }
        }

        private void DrawCornerFrame(Graphics graphics, Color accent)
        {
            using (var pen = new Pen(Color.FromArgb(168, accent.R, accent.G, accent.B), 1.6f))
            {
                int m = 14;
                int len = 18;

                graphics.DrawLine(pen, m, m, m + len, m);
                graphics.DrawLine(pen, m, m, m, m + len);

                graphics.DrawLine(pen, Width - m - len, m, Width - m, m);
                graphics.DrawLine(pen, Width - m, m, Width - m, m + len);

                graphics.DrawLine(pen, m, Height - m, m + len, Height - m);
                graphics.DrawLine(pen, m, Height - m - len, m, Height - m);

                graphics.DrawLine(pen, Width - m - len, Height - m, Width - m, Height - m);
                graphics.DrawLine(pen, Width - m, Height - m - len, Width - m, Height - m);
            }
        }

        private void DrawCenterReticle(Graphics graphics, Color accent)
        {
            int cx = Width / 2;
            int cy = Height / 2;

            using (var pen = new Pen(Color.FromArgb(148, accent.R, accent.G, accent.B), 1.4f))
            using (var ringPen = new Pen(Color.FromArgb(92, accent.R, accent.G, accent.B), 1f))
            {
                graphics.DrawEllipse(ringPen, cx - 14, cy - 14, 28, 28);
                graphics.DrawLine(pen, cx - 20, cy, cx - 6, cy);
                graphics.DrawLine(pen, cx + 6, cy, cx + 20, cy);
                graphics.DrawLine(pen, cx, cy - 20, cx, cy - 6);
                graphics.DrawLine(pen, cx, cy + 6, cx, cy + 20);
            }
        }

        private void DrawChip(Graphics graphics, Rectangle bounds, string title, string detail, Color accent)
        {
            ModernUiPainter.FillRoundedRectangle(graphics, Surface, bounds, 14);
            ModernUiPainter.DrawRoundedRectangle(graphics, Color.FromArgb(132, accent.R, accent.G, accent.B), 1f, bounds, 14);

            using (var titleBrush = new SolidBrush(Color.FromArgb(236, 240, 245)))
            using (var detailBrush = new SolidBrush(Color.FromArgb(198, accent.R, accent.G, accent.B)))
            {
                graphics.DrawString(title, chipFont, titleBrush,
                    new RectangleF(bounds.X + 12, bounds.Y + 5, bounds.Width - 24, 12));
                graphics.DrawString(detail, detailFont, detailBrush,
                    new RectangleF(bounds.X + 12, bounds.Y + 15, bounds.Width - 24, 10));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                chipFont?.Dispose();
                detailFont?.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Compass and Map Panel - Bottom center showing heading
    /// </summary>
    public class PanelCompass : Panel
    {
        private readonly Font headingFont = new Font("Segoe UI", 16, FontStyle.Bold);
        private readonly Font cardTitleFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private readonly Font cardValueFont = new Font("Segoe UI", 13, FontStyle.Bold);
        private readonly Font cardDetailFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);

        private readonly Color Gold = Color.FromArgb(200, 168, 101);
        private readonly Color LightGray = Color.FromArgb(220, 220, 220);
        private readonly Color MutedGray = Color.FromArgb(138, 149, 168);
        private readonly Color Surface = Color.FromArgb(24, 31, 44);
        private readonly Color Border = Color.FromArgb(44, 54, 73);
        private readonly Color PanelSurface = Color.FromArgb(18, 23, 34);
        private readonly Color TrackColor = Color.FromArgb(74, 190, 225);
        private readonly Color HomeColor = Color.FromArgb(200, 168, 101);

        private readonly myGMAP miniMap;
        private readonly GMapOverlay mapOverlay;
        private readonly GMapRoute breadcrumbRoute;
        private readonly GMapRoute homeRoute;
        private readonly GMarkerGoogle homeMarker;
        private readonly GMapMarkerPlane aircraftMarker;
        private readonly System.Collections.Generic.List<PointLatLng> breadcrumbPoints =
            new System.Collections.Generic.List<PointLatLng>();

        private Rectangle ribbonRect;
        private Rectangle mapFrameRect;
        private Rectangle sidePanelRect;
        private Rectangle progressRect;
        private Rectangle guidanceRect;
        private Rectangle positionRect;

        public float Heading { get; set; }
        public float WaypointNumber { get; set; }
        public float WaypointDistance { get; set; }
        public float DistanceToHome { get; set; }
        public float TimeInAir { get; set; }
        public float DistanceTraveled { get; set; }
        public float GroundCourse { get; set; }
        public float NavBearing { get; set; }
        public float TargetBearing { get; set; }
        public float LinkQuality { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double HomeLatitude { get; set; }
        public double HomeLongitude { get; set; }
        public bool IsConnected { get; set; }
        public string Mode { get; set; } = "STANDBY";

        public PanelCompass()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            DoubleBuffered = true;
            BackColor = Color.FromArgb(10, 14, 20);

            miniMap = new myGMAP
            {
                BackColor = Color.FromArgb(8, 12, 20),
                EmptyTileColor = Color.FromArgb(20, 24, 33),
                CanDragMap = true,
                DisableFocusOnMouseEnter = true,
                GrayScaleMode = false,
                HelperLineOption = HelperLineOptions.DontShow,
                HoldInvalidation = false,
                LevelsKeepInMemmory = 5,
                MarkersEnabled = true,
                MaxZoom = 22,
                MinZoom = 2,
                MouseWheelZoomType = GMap.NET.MouseWheelZoomType.MousePositionWithoutCenter,
                NegativeMode = false,
                PolygonsEnabled = false,
                RetryLoadTile = 0,
                RoutesEnabled = true,
                ScaleMode = ScaleModes.Fractional,
                SelectedAreaFillColor = Color.FromArgb(33, 65, 105, 225),
                ShowTileGridLines = false,
                Zoom = 16
            };

            miniMap.MapProvider = FlightData.mymap?.MapProvider ?? GMapProviders.OpenStreetMap;
            miniMap.Position = PointLatLng.Empty;

            mapOverlay = new GMapOverlay("modern-flight-map");

            breadcrumbRoute = new GMapRoute("breadcrumb")
            {
                Stroke = new Pen(Color.FromArgb(220, TrackColor), 2.4f)
                {
                    LineJoin = LineJoin.Round,
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                },
                IsHitTestVisible = false,
                ArrowMode = GMapRoute.ArrowDrawMode.SinglePerRoute
            };

            homeRoute = new GMapRoute("home-vector")
            {
                Stroke = new Pen(Color.FromArgb(185, HomeColor), 1.4f)
                {
                    DashStyle = DashStyle.Dash
                },
                IsHitTestVisible = false
            };

            homeMarker = new GMarkerGoogle(PointLatLng.Empty, GMarkerGoogleType.blue_dot)
            {
                IsVisible = false,
                ToolTipText = "Home"
            };

            aircraftMarker = new GMapMarkerPlane(0, PointLatLng.Empty, 0, 0, 0, 0, 0)
            {
                IsVisible = false
            };

            mapOverlay.Routes.Add(homeRoute);
            mapOverlay.Routes.Add(breadcrumbRoute);
            mapOverlay.Markers.Add(homeMarker);
            mapOverlay.Markers.Add(aircraftMarker);
            miniMap.Overlays.Add(mapOverlay);
            Controls.Add(miniMap);

            UpdateLayoutMetrics();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            UpdateLayoutMetrics();
            Invalidate();
        }

        private void UpdateLayoutMetrics()
        {
            int outerPadding = 14;
            int gap = 8;

            ribbonRect = new Rectangle(outerPadding, 14, Math.Max(200, Width - outerPadding * 2), 58);

            int bodyTop = ribbonRect.Bottom + 10;
            int bodyHeight = Math.Max(100, Height - bodyTop - 14);
            int sideWidth = Math.Max(184, Math.Min(216, Width / 5));
            int mapWidth = Width - outerPadding * 2 - sideWidth - gap;

            if (mapWidth < 320)
            {
                sideWidth = Math.Max(168, sideWidth - (320 - mapWidth));
                mapWidth = Width - outerPadding * 2 - sideWidth - gap;
            }

            mapFrameRect = new Rectangle(outerPadding, bodyTop, Math.Max(280, mapWidth), bodyHeight);
            sidePanelRect = new Rectangle(mapFrameRect.Right + gap, bodyTop,
                Math.Max(180, Width - mapFrameRect.Right - gap - outerPadding), bodyHeight);

            int inset = 3;
            miniMap.Bounds = new Rectangle(
                mapFrameRect.X + inset,
                mapFrameRect.Y + inset,
                Math.Max(80, mapFrameRect.Width - inset * 2),
                Math.Max(80, mapFrameRect.Height - inset * 2));

            int cardGap = 8;
            int cardHeight = Math.Max(58, (sidePanelRect.Height - cardGap * 2) / 3);
            progressRect = new Rectangle(sidePanelRect.X, sidePanelRect.Y, sidePanelRect.Width, cardHeight);
            guidanceRect = new Rectangle(sidePanelRect.X, progressRect.Bottom + cardGap, sidePanelRect.Width, cardHeight);
            positionRect = new Rectangle(sidePanelRect.X, guidanceRect.Bottom + cardGap, sidePanelRect.Width,
                Math.Max(58, sidePanelRect.Bottom - guidanceRect.Bottom - cardGap));
        }

        public void SetOffline(bool connected)
        {
            IsConnected = connected;
            Invalidate();
        }

        public void UpdateTelemetry(CurrentState cs)
        {
            Heading = (float)cs.yaw;
            WaypointNumber = cs.wpno;
            WaypointDistance = cs.wp_dist;
            DistanceToHome = cs.DistToHome;
            TimeInAir = cs.timeInAir;
            DistanceTraveled = cs.distTraveled;
            GroundCourse = cs.groundcourse;
            NavBearing = cs.nav_bearing;
            TargetBearing = cs.target_bearing;
            LinkQuality = cs.linkqualitygcs;
            Latitude = cs.lat;
            Longitude = cs.lng;
            Mode = cs.mode;
            IsConnected = MainV2.comPort?.BaseStream?.IsOpen == true;

            if (HasValidCoordinate(cs.HomeLocation.Lat, cs.HomeLocation.Lng))
            {
                HomeLatitude = cs.HomeLocation.Lat;
                HomeLongitude = cs.HomeLocation.Lng;
            }
            else if (HasValidCoordinate(cs.PlannedHomeLocation.Lat, cs.PlannedHomeLocation.Lng))
            {
                HomeLatitude = cs.PlannedHomeLocation.Lat;
                HomeLongitude = cs.PlannedHomeLocation.Lng;
            }
            else
            {
                HomeLatitude = 0;
                HomeLongitude = 0;
            }

            UpdateMapTelemetry();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var panelBounds = new Rectangle(0, 0, Width - 1, Height - 1);
            ModernUiPainter.FillRoundedRectangle(e.Graphics, Surface, panelBounds, 20);
            ModernUiPainter.DrawRoundedRectangle(e.Graphics, Border, 1f, panelBounds, 20);

            ModernUiPainter.FillRoundedRectangle(e.Graphics, Color.FromArgb(16, 20, 30), ribbonRect, 16);
            ModernUiPainter.DrawRoundedRectangle(e.Graphics, Color.FromArgb(53, 64, 86), 1f, ribbonRect, 16);

            DrawHeadingRibbon(e.Graphics, ribbonRect);

            ModernUiPainter.FillRoundedRectangle(e.Graphics, PanelSurface, mapFrameRect, 18);
            ModernUiPainter.DrawRoundedRectangle(e.Graphics, ModernUiPainter.WithAlpha(TrackColor, 140), 1f, mapFrameRect, 18);

            using (var titleBrush = new SolidBrush(LightGray))
            using (var subtitleBrush = new SolidBrush(MutedGray))
            using (var accentBrush = new SolidBrush(IsConnected ? TrackColor : MutedGray))
            {
                e.Graphics.DrawString("TACTICAL MAP", cardTitleFont, titleBrush,
                    new RectangleF(mapFrameRect.X + 14, mapFrameRect.Y + 10, mapFrameRect.Width - 28, 14));
                e.Graphics.DrawString(IsConnected ? "Live vehicle track and home vector" : "Waiting for live telemetry",
                    cardDetailFont, subtitleBrush, new RectangleF(mapFrameRect.X + 14, mapFrameRect.Bottom - 22, mapFrameRect.Width - 90, 16));
                e.Graphics.FillEllipse(accentBrush, mapFrameRect.Right - 24, mapFrameRect.Y + 12, 8, 8);
            }

            DrawMissionProgressCard(e.Graphics, progressRect);
            DrawGuidanceCard(e.Graphics, guidanceRect);
            DrawPositionCard(e.Graphics, positionRect);
        }

        private void DrawHeadingRibbon(Graphics graphics, Rectangle bounds)
        {
            int centerX = bounds.Left + bounds.Width / 2;
            float pixelsPerDegree = Math.Max(2.6f, bounds.Width / 120f);
            int visibleDegrees = (int)Math.Ceiling(bounds.Width / pixelsPerDegree / 2f) + 10;

            using (var tickPen = new Pen(ModernUiPainter.WithAlpha(LightGray, 120), 1f))
            using (var majorPen = new Pen(ModernUiPainter.WithAlpha(Gold, 220), 1.6f))
            using (var labelBrush = new SolidBrush(LightGray))
            using (var mutedBrush = new SolidBrush(MutedGray))
            {
                for (int delta = -visibleDegrees; delta <= visibleDegrees; delta += 5)
                {
                    int degree = (int)Math.Round(Heading) + delta;
                    int normalized = ((degree % 360) + 360) % 360;
                    float x = centerX + delta * pixelsPerDegree;

                    if (x < bounds.Left + 12 || x > bounds.Right - 12)
                        continue;

                    bool isMajor = normalized % 15 == 0;
                    int tickHeight = isMajor ? 18 : 10;
                    int y1 = bounds.Top + 18;
                    int y2 = y1 + tickHeight;

                    graphics.DrawLine(isMajor ? majorPen : tickPen, x, y1, x, y2);

                    if (normalized % 30 == 0)
                    {
                        string label = ModernUiPainter.FormatHeadingLabel(normalized);
                        var labelSize = graphics.MeasureString(label, cardTitleFont);
                        graphics.DrawString(label, cardTitleFont, normalized % 90 == 0 ? labelBrush : mutedBrush,
                            x - labelSize.Width / 2, y2 + 4);
                    }
                }
            }

            using (var markerBrush = new SolidBrush(Gold))
            using (var headingBrush = new SolidBrush(LightGray))
            {
                Point[] marker =
                {
                    new Point(centerX, bounds.Top + 10),
                    new Point(centerX - 8, bounds.Top + 22),
                    new Point(centerX + 8, bounds.Top + 22)
                };
                graphics.FillPolygon(markerBrush, marker);

                string headingText = $"{Heading:F0} deg";
                var headingSize = graphics.MeasureString(headingText, headingFont);
                graphics.DrawString(headingText, headingFont, headingBrush,
                    centerX - headingSize.Width / 2, bounds.Bottom - headingSize.Height - 8);
            }
        }

        private void DrawInfoCard(Graphics graphics, Rectangle bounds, string title, string value, string detail, Color accent)
        {
            ModernUiPainter.FillRoundedRectangle(graphics, Color.FromArgb(18, 23, 34), bounds, 16);
            ModernUiPainter.DrawRoundedRectangle(graphics, ModernUiPainter.WithAlpha(accent, 150), 1f, bounds, 16);

            using (var titleBrush = new SolidBrush(MutedGray))
            using (var valueBrush = new SolidBrush(LightGray))
            using (var detailBrush = new SolidBrush(ModernUiPainter.WithAlpha(accent, 220)))
            {
                graphics.DrawString(title, cardTitleFont, titleBrush, new RectangleF(bounds.X + 14, bounds.Y + 10, bounds.Width - 28, 14));
                graphics.DrawString(value, cardValueFont, valueBrush, new RectangleF(bounds.X + 14, bounds.Y + 26, bounds.Width - 28, 20));
                graphics.DrawString(detail, cardDetailFont, detailBrush, new RectangleF(bounds.X + 14, bounds.Bottom - 22, bounds.Width - 28, 16));
            }
        }

        private void DrawMissionProgressCard(Graphics graphics, Rectangle bounds)
        {
            DrawInfoCard(graphics, bounds, "MISSION PROGRESS", $"WP {WaypointNumber:F0}",
                $"{WaypointDistance:F0} m to target", Gold);

            float progress = WaypointDistance <= 0
                ? 1f
                : Math.Max(0.06f, 1f - Math.Min(WaypointDistance, 1200f) / 1200f);

            var trackRect = new Rectangle(bounds.X + 14, bounds.Bottom - 14, bounds.Width - 28, 5);
            var fillRect = new Rectangle(trackRect.X, trackRect.Y, (int)(trackRect.Width * progress), trackRect.Height);
            ModernUiPainter.FillRoundedRectangle(graphics, Color.FromArgb(39, 47, 65), trackRect, 2);
            ModernUiPainter.FillRoundedRectangle(graphics, Gold, fillRect, 2);
        }

        private void DrawGuidanceCard(Graphics graphics, Rectangle bounds)
        {
            DrawInfoCard(graphics, bounds, "GUIDANCE", $"{DistanceToHome:F0} m home",
                $"Track {GroundCourse:F0} deg  |  Nav {NavBearing:F0} deg", TrackColor);

            using (var labelBrush = new SolidBrush(MutedGray))
            using (var valueBrush = new SolidBrush(LightGray))
            {
                graphics.DrawString($"Target {TargetBearing:F0} deg", cardDetailFont, labelBrush,
                    new RectangleF(bounds.X + 14, bounds.Bottom - 22, bounds.Width - 28, 14));
                graphics.DrawString($"{DistanceTraveled:F0} m traveled", cardDetailFont, valueBrush,
                    new RectangleF(bounds.X + 14, bounds.Bottom - 38, bounds.Width - 28, 14));
            }
        }

        private void DrawPositionCard(Graphics graphics, Rectangle bounds)
        {
            ModernUiPainter.FillRoundedRectangle(graphics, PanelSurface, bounds, 16);
            ModernUiPainter.DrawRoundedRectangle(graphics, Color.FromArgb(76, 89, 110), 1f, bounds, 16);

            using (var titleBrush = new SolidBrush(MutedGray))
            using (var valueBrush = new SolidBrush(LightGray))
            using (var detailBrush = new SolidBrush(ModernUiPainter.WithAlpha(TrackColor, 220)))
            {
                graphics.DrawString("POSITION", cardTitleFont, titleBrush,
                    new RectangleF(bounds.X + 14, bounds.Y + 12, bounds.Width - 28, 14));
                graphics.DrawString($"{Latitude:F5}, {Longitude:F5}", cardValueFont, valueBrush,
                    new RectangleF(bounds.X + 14, bounds.Y + 30, bounds.Width - 28, 22));
                graphics.DrawString($"HOME {HomeLatitude:F5}, {HomeLongitude:F5}", cardDetailFont, detailBrush,
                    new RectangleF(bounds.X + 14, bounds.Y + 56, bounds.Width - 28, 16));
                graphics.DrawString($"{ModernUiPainter.FormatDuration(TimeInAir)} airborne  |  Link {LinkQuality:F0}%",
                    cardDetailFont, valueBrush, new RectangleF(bounds.X + 14, bounds.Bottom - 22, bounds.Width - 28, 16));
            }
        }

        private void UpdateMapTelemetry()
        {
            bool hasAircraft = HasValidCoordinate(Latitude, Longitude);
            bool hasHome = HasValidCoordinate(HomeLatitude, HomeLongitude);

            aircraftMarker.IsVisible = hasAircraft;
            homeMarker.IsVisible = hasHome;

            if (hasAircraft)
            {
                var currentPoint = new PointLatLng(Latitude, Longitude);
                aircraftMarker.Position = currentPoint;
                aircraftMarker.Heading = Heading;
                aircraftMarker.Cog = GroundCourse > 0 ? GroundCourse : Heading;
                aircraftMarker.Nav_bearing = NavBearing;
                aircraftMarker.Target = TargetBearing;

                if (!miniMap.IsDragging)
                {
                    if (miniMap.Position.IsEmpty ||
                        GMapProviders.EmptyProvider.Projection.GetDistance(miniMap.Position, currentPoint) > 0.05)
                    {
                        miniMap.Position = currentPoint;
                    }
                }

                if (breadcrumbPoints.Count == 0 ||
                    GMapProviders.EmptyProvider.Projection.GetDistance(breadcrumbPoints[breadcrumbPoints.Count - 1], currentPoint) > 0.008)
                {
                    breadcrumbPoints.Add(currentPoint);
                    if (breadcrumbPoints.Count > 80)
                        breadcrumbPoints.RemoveAt(0);

                    breadcrumbRoute.Points.Clear();
                    breadcrumbRoute.Points.AddRange(breadcrumbPoints);
                    miniMap.UpdateRouteLocalPosition(breadcrumbRoute);
                }
            }

            if (hasHome)
            {
                var homePoint = new PointLatLng(HomeLatitude, HomeLongitude);
                homeMarker.Position = homePoint;

                homeRoute.Points.Clear();
                if (hasAircraft)
                {
                    homeRoute.Points.Add(new PointLatLng(Latitude, Longitude));
                    homeRoute.Points.Add(homePoint);
                }

                miniMap.UpdateRouteLocalPosition(homeRoute);
            }
            else
            {
                homeRoute.Points.Clear();
                miniMap.UpdateRouteLocalPosition(homeRoute);
            }

            if (!hasAircraft && hasHome && miniMap.Position.IsEmpty)
            {
                miniMap.Position = new PointLatLng(HomeLatitude, HomeLongitude);
            }

            miniMap.Invalidate();
        }

        private static bool HasValidCoordinate(double latitude, double longitude)
        {
            return Math.Abs(latitude) > 0.00001 || Math.Abs(longitude) > 0.00001;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                breadcrumbRoute?.Stroke?.Dispose();
                homeRoute?.Stroke?.Dispose();
                headingFont?.Dispose();
                cardTitleFont?.Dispose();
                cardValueFont?.Dispose();
                cardDetailFont?.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    public enum FlightEventSeverity
    {
        Info,
        Success,
        Warning,
        Danger
    }

    public class FlightEventItem
    {
        public FlightEventItem(FlightEventSeverity severity, string title, string detail)
        {
            Severity = severity;
            Title = title ?? "";
            Detail = detail ?? "";
            Timestamp = DateTime.Now;
        }

        public FlightEventSeverity Severity { get; }
        public string Title { get; }
        public string Detail { get; }
        public DateTime Timestamp { get; }
    }

    public class ActionCommandButton : Control
    {
        private readonly Font titleFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        private readonly Font detailFont = new Font("Segoe UI", 8.25f, FontStyle.Regular);
        private readonly Font chipFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        private readonly Color TextPrimary = Color.FromArgb(232, 236, 243);
        private readonly Color TextMuted = Color.FromArgb(150, 160, 177);
        private readonly Color InactiveText = Color.FromArgb(176, 184, 198);
        private readonly Color InactiveDetail = Color.FromArgb(118, 128, 144);

        private string commandTitle = "";
        private string commandDetail = "";
        private string stateText = "";
        private Color accentColor = Color.FromArgb(74, 190, 225);
        private bool commandAvailable = true;
        private bool hovered;
        private bool pressed;

        public ActionCommandButton()
        {
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            BackColor = Color.FromArgb(22, 28, 40);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(180, 62);
        }

        public string CommandTitle
        {
            get => commandTitle;
            set
            {
                commandTitle = value ?? "";
                Invalidate();
            }
        }

        public string CommandDetail
        {
            get => commandDetail;
            set
            {
                commandDetail = value ?? "";
                Invalidate();
            }
        }

        public string StateText
        {
            get => stateText;
            set
            {
                stateText = value ?? "";
                Invalidate();
            }
        }

        public Color AccentColor
        {
            get => accentColor;
            set
            {
                accentColor = value;
                Invalidate();
            }
        }

        public bool CommandAvailable
        {
            get => commandAvailable;
            set
            {
                commandAvailable = value;
                Cursor = commandAvailable ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = false;
            pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            pressed = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            Color baseSurface = CommandAvailable ? Color.FromArgb(24, 30, 42) : Color.FromArgb(18, 22, 31);
            Color filledSurface = hovered && CommandAvailable
                ? Blend(baseSurface, AccentColor, pressed ? 0.25f : 0.14f)
                : baseSurface;
            Color border = CommandAvailable
                ? ModernUiPainter.WithAlpha(AccentColor, hovered ? 230 : 182)
                : Color.FromArgb(74, 86, 104);
            Color stripColor = CommandAvailable ? AccentColor : Color.FromArgb(82, 92, 108);
            Color titleColor = CommandAvailable ? TextPrimary : InactiveText;
            Color detailColor = CommandAvailable ? TextMuted : InactiveDetail;

            ModernUiPainter.FillRoundedRectangle(e.Graphics, filledSurface, bounds, 14);
            ModernUiPainter.DrawRoundedRectangle(e.Graphics, border, 1.15f, bounds, 14);
            ModernUiPainter.FillRoundedRectangle(e.Graphics,
                Color.FromArgb(CommandAvailable ? 225 : 140, stripColor.R, stripColor.G, stripColor.B),
                new Rectangle(bounds.X + 14, bounds.Y + 10, Math.Min(68, Math.Max(38, bounds.Width / 3)), 4), 2);

            Rectangle chipRect = BuildChipRectangle(e.Graphics, bounds);
            if (!string.IsNullOrWhiteSpace(StateText))
            {
                Color chipFill = Color.FromArgb(CommandAvailable ? 42 : 28, stripColor.R, stripColor.G, stripColor.B);
                Color chipBorder = Color.FromArgb(CommandAvailable ? 128 : 86, stripColor.R, stripColor.G, stripColor.B);
                ModernUiPainter.FillRoundedRectangle(e.Graphics, chipFill, chipRect, 8);
                ModernUiPainter.DrawRoundedRectangle(e.Graphics, chipBorder, 1f, chipRect, 8);

                using (var chipBrush = new SolidBrush(titleColor))
                using (var chipFormat = new StringFormat())
                {
                    chipFormat.Alignment = StringAlignment.Center;
                    chipFormat.LineAlignment = StringAlignment.Center;
                    e.Graphics.DrawString(StateText, chipFont, chipBrush, chipRect, chipFormat);
                }
            }

            RectangleF titleBounds = new RectangleF(bounds.X + 14, bounds.Y + 20, bounds.Width - 28, 18);
            RectangleF detailBounds = new RectangleF(bounds.X + 14, bounds.Y + 40, bounds.Width - 28, Math.Max(18, bounds.Height - 64));

            using (var titleBrush = new SolidBrush(titleColor))
            using (var detailBrush = new SolidBrush(detailColor))
            using (var titleFormat = new StringFormat())
            using (var detailFormat = new StringFormat())
            {
                titleFormat.Trimming = StringTrimming.EllipsisCharacter;
                detailFormat.Trimming = StringTrimming.EllipsisWord;
                detailFormat.LineAlignment = StringAlignment.Near;

                e.Graphics.DrawString(CommandTitle, titleFont, titleBrush, titleBounds, titleFormat);
                e.Graphics.DrawString(CommandDetail, detailFont, detailBrush, detailBounds, detailFormat);
            }
        }

        private Rectangle BuildChipRectangle(Graphics graphics, Rectangle bounds)
        {
            if (string.IsNullOrWhiteSpace(StateText))
                return Rectangle.Empty;

            int chipWidth = (int)Math.Ceiling(graphics.MeasureString(StateText, chipFont).Width) + 18;
            chipWidth = Math.Max(52, Math.Min(bounds.Width - 28, chipWidth));
            return new Rectangle(bounds.Right - chipWidth - 12, bounds.Bottom - 26, chipWidth, 18);
        }

        private static Color Blend(Color baseColor, Color accentColor, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                baseColor.A,
                (int)(baseColor.R + ((accentColor.R - baseColor.R) * amount)),
                (int)(baseColor.G + ((accentColor.G - baseColor.G) * amount)),
                (int)(baseColor.B + ((accentColor.B - baseColor.B) * amount)));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                titleFont?.Dispose();
                detailFont?.Dispose();
                chipFont?.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    public class EventTimelineControl : ScrollableControl
    {
        private const int MaxEvents = 30;
        private const int EventGap = 8;
        private const int EventCardHeight = 96;
        private const int ScrollStep = 28;
        private readonly Font labelFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        private readonly Font titleFont = new Font("Segoe UI", 9.25f, FontStyle.Bold);
        private readonly Font detailFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        private readonly Font chipFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        private readonly Color Surface = Color.FromArgb(19, 24, 34);
        private readonly Color Border = Color.FromArgb(46, 56, 74);
        private readonly Color TextPrimary = Color.FromArgb(232, 236, 243);
        private readonly Color TextMuted = Color.FromArgb(136, 148, 168);
        private readonly Color TextSecondary = Color.FromArgb(194, 202, 215);
        private readonly System.Collections.Generic.List<FlightEventItem> items =
            new System.Collections.Generic.List<FlightEventItem>();

        public EventTimelineControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            DoubleBuffered = true;
            TabStop = true;
            BackColor = Surface;
            AutoScroll = true;
            AutoScrollMinSize = Size.Empty;
            MouseEnter += (s, e) => Focus();
        }

        public void AddEvent(FlightEventSeverity severity, string title, string detail)
        {
            string trimmedTitle = (title ?? "").Trim();
            string trimmedDetail = (detail ?? "").Trim();

            if (trimmedTitle.Length == 0 && trimmedDetail.Length == 0)
                return;

            if (items.Count > 0 &&
                items[0].Severity == severity &&
                string.Equals(items[0].Title, trimmedTitle, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(items[0].Detail, trimmedDetail, StringComparison.OrdinalIgnoreCase))
                return;

            bool shouldPinToTop = VerticalScroll.Value <= 12;
            items.Insert(0, new FlightEventItem(severity, trimmedTitle, trimmedDetail));
            if (items.Count > MaxEvents)
                items.RemoveAt(items.Count - 1);

            UpdateScrollMetrics();
            if (shouldPinToTop)
                AutoScrollPosition = new Point(0, 0);

            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScrollMetrics();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            if (!VerticalScroll.Visible)
                return;

            int maxValue = Math.Max(VerticalScroll.Minimum, VerticalScroll.Maximum - VerticalScroll.LargeChange + 1);
            int delta = e.Delta > 0 ? -ScrollStep : ScrollStep;
            int newValue = Math.Max(VerticalScroll.Minimum, Math.Min(maxValue, VerticalScroll.Value + delta));
            AutoScrollPosition = new Point(0, newValue);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);

            if (items.Count == 0)
            {
                using (var mutedBrush = new SolidBrush(TextMuted))
                {
                    e.Graphics.DrawString("No recent mission events.", detailFont, mutedBrush,
                        new RectangleF(2, 6, Width - 4, 18));
                }
                return;
            }

            int eventWidth = Math.Max(92, ClientSize.Width - (VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth + 3 : 1));
            int top = 0;
            e.Graphics.TranslateTransform(0, AutoScrollPosition.Y);

            for (int i = 0; i < items.Count; i++)
            {
                var bounds = new Rectangle(0, top, eventWidth, EventCardHeight);
                DrawEventCard(e.Graphics, bounds, items[i]);
                top += EventCardHeight + EventGap;
            }
        }

        private void UpdateScrollMetrics()
        {
            int contentHeight = items.Count == 0 ? 0 : items.Count * EventCardHeight + Math.Max(0, items.Count - 1) * EventGap;
            AutoScrollMinSize = new Size(0, contentHeight);
        }

        private void DrawEventCard(Graphics graphics, Rectangle bounds, FlightEventItem item)
        {
            Color accent = GetAccent(item.Severity);

            ModernUiPainter.FillRoundedRectangle(graphics, Surface, bounds, 16);
            ModernUiPainter.DrawRoundedRectangle(graphics, Color.FromArgb(74, accent.R, accent.G, accent.B), 1f, bounds, 16);
            ModernUiPainter.FillRoundedRectangle(graphics, accent, new Rectangle(bounds.X + 10, bounds.Y + 10, 4, bounds.Height - 20), 2);

            using (var timeBrush = new SolidBrush(ModernUiPainter.WithAlpha(accent, 235)))
            using (var titleBrush = new SolidBrush(TextPrimary))
            using (var detailBrush = new SolidBrush(TextSecondary))
            using (var chipTextBrush = new SolidBrush(TextPrimary))
            using (var dividerPen = new Pen(Color.FromArgb(48, accent.R, accent.G, accent.B), 1f))
            using (var titleFormat = new StringFormat())
            using (var detailFormat = new StringFormat())
            {
                titleFormat.Trimming = StringTrimming.EllipsisCharacter;
                detailFormat.Trimming = StringTrimming.EllipsisWord;
                detailFormat.Alignment = StringAlignment.Near;
                detailFormat.LineAlignment = StringAlignment.Near;

                graphics.DrawString(item.Timestamp.ToString("HH:mm:ss"), labelFont, timeBrush,
                    new RectangleF(bounds.X + 22, bounds.Y + 10, 68, 12));

                var chipRect = new Rectangle(bounds.Right - 84, bounds.Y + 8, 72, 18);
                ModernUiPainter.FillRoundedRectangle(graphics, Color.FromArgb(32, accent.R, accent.G, accent.B), chipRect, 8);
                ModernUiPainter.DrawRoundedRectangle(graphics, Color.FromArgb(118, accent.R, accent.G, accent.B), 1f, chipRect, 8);
                graphics.DrawString(GetSeverityLabel(item.Severity), chipFont, chipTextBrush,
                    new RectangleF(chipRect.X + 8, chipRect.Y + 3, chipRect.Width - 16, 12));

                graphics.DrawString(item.Title, titleFont, titleBrush,
                    new RectangleF(bounds.X + 22, bounds.Y + 30, bounds.Width - 34, 16), titleFormat);
                graphics.DrawLine(dividerPen, bounds.X + 22, bounds.Y + 50, bounds.Right - 16, bounds.Y + 50);
                graphics.DrawString(item.Detail, detailFont, detailBrush,
                    new RectangleF(bounds.X + 22, bounds.Y + 56, bounds.Width - 34, bounds.Height - 64), detailFormat);
            }
        }

        private static string GetSeverityLabel(FlightEventSeverity severity)
        {
            switch (severity)
            {
                case FlightEventSeverity.Success:
                    return "SUCCESS";
                case FlightEventSeverity.Warning:
                    return "WARNING";
                case FlightEventSeverity.Danger:
                    return "ALERT";
                default:
                    return "INFO";
            }
        }

        private static Color GetAccent(FlightEventSeverity severity)
        {
            switch (severity)
            {
                case FlightEventSeverity.Success:
                    return Color.FromArgb(72, 182, 132);
                case FlightEventSeverity.Warning:
                    return Color.FromArgb(228, 172, 67);
                case FlightEventSeverity.Danger:
                    return Color.FromArgb(228, 84, 71);
                default:
                    return Color.FromArgb(74, 190, 225);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                labelFont?.Dispose();
                titleFont?.Dispose();
                detailFont?.Dispose();
                chipFont?.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Message Center Panel - Right side live vehicle, warning, and mission events
    /// </summary>
    public class LegacyMessageCenterPanel : Panel
    {
        private Label lblTitle;
        private Label lblSubtitle;
        private Label lblHint;
        private Label lblTimeline;
        private Label lblTimelineMeta;
        private Label lblStatus;
        private EventTimelineControl eventTimeline;

        private readonly Color DarkNavy = Color.FromArgb(26, 31, 46);
        private readonly Color Gold = Color.FromArgb(200, 168, 101);
        private readonly Color LightGray = Color.FromArgb(220, 220, 220);
        private readonly Color MutedGray = Color.FromArgb(138, 149, 168);
        private readonly Color GreenStatus = Color.FromArgb(76, 175, 80);
        private readonly Color RedStatus = Color.FromArgb(244, 67, 54);
        private readonly Color WarningStatus = Color.FromArgb(228, 172, 67);

        public LegacyMessageCenterPanel()
        {
            BackColor = Color.FromArgb(10, 14, 20);
            Padding = new Padding(12);
            BorderStyle = BorderStyle.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            InitializeButtons();
        }

        private void InitializeButtons()
        {
            lblTitle = new Label
            {
                Text = "MESSAGE CENTER",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Gold,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(lblTitle);

            lblSubtitle = new Label
            {
                Text = "Live vehicle, warning, and mission events",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = LightGray,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(lblSubtitle);

            lblStatus = new Label
            {
                Text = "SAFE  |  OFFLINE",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = RedStatus,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(lblStatus);

            lblHint = new Label
            {
                Text = "Awaiting telemetry, pilot advisories, and mission events.",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = LightGray,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.TopLeft
            };
            Controls.Add(lblHint);

            lblTimeline = new Label
            {
                Text = "LIVE MESSAGES",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Gold,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(lblTimeline);

            lblTimelineMeta = new Label
            {
                Text = "SCROLL",
                Font = new Font("Segoe UI", 7.25f, FontStyle.Bold),
                ForeColor = MutedGray,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight
            };
            Controls.Add(lblTimelineMeta);

            eventTimeline = new EventTimelineControl();
            Controls.Add(eventTimeline);

            UpdateLayoutMetrics();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            UpdateLayoutMetrics();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            ModernUiPainter.FillRoundedRectangle(e.Graphics, Color.FromArgb(22, 28, 40), bounds, 18);
            ModernUiPainter.DrawRoundedRectangle(e.Graphics, Color.FromArgb(45, 55, 74), 1f, bounds, 18);
        }

        private void UpdateLayoutMetrics()
        {
            int contentWidth = Math.Max(140, ClientSize.Width - Padding.Horizontal - 2);
            int left = Padding.Left;
            int top = Padding.Top;

            lblTitle.Bounds = new Rectangle(left, top, contentWidth, 24);
            top += 24;

            lblSubtitle.Bounds = new Rectangle(left, top, contentWidth, 18);
            top += 22;

            lblStatus.Bounds = new Rectangle(left, top, contentWidth, 18);
            top += 24;

            lblHint.Bounds = new Rectangle(left, top, contentWidth, 36);
            top += 42;

            int headerWidth = Math.Max(0, contentWidth - 74);
            lblTimeline.Bounds = new Rectangle(left, top, headerWidth, 18);
            lblTimelineMeta.Bounds = new Rectangle(left + headerWidth, top, contentWidth - headerWidth, 18);
            top += 24;

            eventTimeline.Bounds = new Rectangle(left, top, contentWidth,
                Math.Max(140, ClientSize.Height - top - Padding.Bottom));
        }

        public void UpdateStatus(bool armed, bool connected, string mode, string alert)
        {
            string normalizedMode = string.IsNullOrWhiteSpace(mode) ? "UNKNOWN" : mode.ToUpperInvariant();
            lblSubtitle.Text = $"Mode {normalizedMode}";
            lblStatus.Text = $"{(armed ? "ARMED" : "SAFE")}  |  {(connected ? "LIVE LINK" : "OFFLINE")}";
            lblStatus.ForeColor = !connected ? RedStatus : armed ? GreenStatus : WarningStatus;

            if (!connected)
                lblHint.Text = "Awaiting vehicle link, telemetry, and mission activity.";
            else if (!string.IsNullOrWhiteSpace(alert))
                lblHint.Text = $"Active advisory: {alert}";
            else if (armed)
                lblHint.Text = "Aircraft is armed. Monitoring live flight events and safety messages.";
            else
                lblHint.Text = "Aircraft is safe. Monitoring preflight, navigation, and system messages.";
        }

        public void PushEvent(FlightEventSeverity severity, string title, string detail)
        {
            eventTimeline?.AddEvent(severity, title, detail);
        }
    }

    public static class ModernCommandDialog
    {
        private static readonly Color Surface = Color.FromArgb(24, 31, 44);
        private static readonly Color Shell = Color.FromArgb(10, 14, 20);
        private static readonly Color TextPrimary = Color.FromArgb(232, 236, 243);
        private static readonly Color TextMuted = Color.FromArgb(138, 149, 168);

        public static bool ShowConfirmation(IWin32Window owner, string title, string message, string confirmText, Color accent)
        {
            using (var dialog = CreateBaseDialog(title, message, accent, 430, 240))
            {
                var cancelButton = CreateButton("Cancel", Color.FromArgb(58, 67, 88), DialogResult.Cancel);
                var confirmButton = CreateButton(confirmText, accent, DialogResult.OK);

                cancelButton.Bounds = new Rectangle(dialog.ClientSize.Width - 222, dialog.ClientSize.Height - 58, 96, 34);
                confirmButton.Bounds = new Rectangle(dialog.ClientSize.Width - 116, dialog.ClientSize.Height - 58, 96, 34);

                dialog.Controls.Add(cancelButton);
                dialog.Controls.Add(confirmButton);
                dialog.AcceptButton = confirmButton;
                dialog.CancelButton = cancelButton;

                return dialog.ShowDialog(owner) == DialogResult.OK;
            }
        }

        public static decimal? ShowTakeoffPrompt(IWin32Window owner, decimal defaultAltitude)
        {
            Color accent = Color.FromArgb(200, 168, 101);

            using (var dialog = CreateBaseDialog("Guided Takeoff",
                "Choose the target altitude for a guided takeoff. The vehicle will switch to GUIDED and arm if needed.",
                accent, 460, 296))
            {
                var fieldLabel = new Label
                {
                    Text = "Target altitude",
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    ForeColor = TextPrimary,
                    BackColor = Color.Transparent,
                    Bounds = new Rectangle(24, 118, 180, 18)
                };

                var unitLabel = new Label
                {
                    Text = "meters AGL",
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                    ForeColor = TextMuted,
                    BackColor = Color.Transparent,
                    Bounds = new Rectangle(24, 194, 180, 16)
                };

                var altitudeInput = new NumericUpDown
                {
                    Minimum = 3,
                    Maximum = 300,
                    DecimalPlaces = 0,
                    Value = Math.Max(3, Math.Min(300, defaultAltitude)),
                    Font = new Font("Segoe UI", 20f, FontStyle.Bold),
                    ForeColor = TextPrimary,
                    BackColor = Surface,
                    BorderStyle = BorderStyle.FixedSingle,
                    TextAlign = HorizontalAlignment.Center,
                    Bounds = new Rectangle(24, 142, dialog.ClientSize.Width - 48, 44)
                };

                var cancelButton = CreateButton("Cancel", Color.FromArgb(58, 67, 88), DialogResult.Cancel);
                var confirmButton = CreateButton("Launch", accent, DialogResult.OK);
                cancelButton.Bounds = new Rectangle(dialog.ClientSize.Width - 222, dialog.ClientSize.Height - 58, 96, 34);
                confirmButton.Bounds = new Rectangle(dialog.ClientSize.Width - 116, dialog.ClientSize.Height - 58, 96, 34);

                dialog.Controls.Add(fieldLabel);
                dialog.Controls.Add(altitudeInput);
                dialog.Controls.Add(unitLabel);
                dialog.Controls.Add(cancelButton);
                dialog.Controls.Add(confirmButton);
                dialog.AcceptButton = confirmButton;
                dialog.CancelButton = cancelButton;

                return dialog.ShowDialog(owner) == DialogResult.OK ? altitudeInput.Value : (decimal?)null;
            }
        }

        public static void ShowNotice(IWin32Window owner, string title, string message, Color accent)
        {
            using (var dialog = CreateBaseDialog(title, message, accent, 420, 220))
            {
                var okButton = CreateButton("OK", accent, DialogResult.OK);
                okButton.Bounds = new Rectangle(dialog.ClientSize.Width - 116, dialog.ClientSize.Height - 58, 96, 34);
                dialog.Controls.Add(okButton);
                dialog.AcceptButton = okButton;
                dialog.ShowDialog(owner);
            }
        }

        private static Form CreateBaseDialog(string title, string message, Color accent, int width, int height)
        {
            var dialog = new Form
            {
                Width = width,
                Height = height,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                BackColor = Shell,
                ForeColor = TextPrimary,
                Padding = new Padding(0),
                Text = title
            };

            var accentBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 4,
                BackColor = accent
            };

            var titleLabel = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = TextPrimary,
                BackColor = Color.Transparent,
                Bounds = new Rectangle(24, 22, width - 48, 26)
            };

            var messageLabel = new Label
            {
                Text = message,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                Bounds = new Rectangle(24, 58, width - 48, 52)
            };

            var shellPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Shell
            };

            shellPanel.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var bounds = new Rectangle(12, 12, shellPanel.ClientSize.Width - 24, shellPanel.ClientSize.Height - 24);
                ModernUiPainter.FillRoundedRectangle(e.Graphics, Surface, bounds, 20);
                ModernUiPainter.DrawRoundedRectangle(e.Graphics, ModernUiPainter.WithAlpha(accent, 170), 1f, bounds, 20);
            };

            shellPanel.Controls.Add(titleLabel);
            shellPanel.Controls.Add(messageLabel);
            dialog.Controls.Add(shellPanel);
            dialog.Controls.Add(accentBar);

            return dialog;
        }

        private static Button CreateButton(string text, Color accent, DialogResult dialogResult)
        {
            var button = new Button
            {
                Text = text,
                DialogResult = dialogResult,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = TextPrimary,
                BackColor = Color.FromArgb(27, 35, 49),
                FlatStyle = FlatStyle.Flat
            };

            button.FlatAppearance.BorderColor = accent;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(58, 67, 88);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(38, 46, 63);
            return button;
        }
    }
}
