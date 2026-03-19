using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
    /// 3-panel layout: Telemetry (left), side-by-side HUD and 3D map deck (center), Quick Actions (right)
    /// </summary>
    public partial class ModernFlightDataCSharp : MyUserControl, IActivate, IDeactivate
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private const int TelemetryMinWidth = 220;
        private const int CenterContentMinWidth = 620;
        private const int HudMinWidth = 360;
        private const int MapDeckMinWidth = 360;

        // Custom controls
        private TopStatusRail statusRail;
        private SplitContainer splitMain;
        private SplitContainer splitCenter;
        private PanelTelemetry panelTelemetry;
        private PanelHudDeck panelHudDeck;
        private ControlArtificialHorizon hudDisplay;
        private PanelMap3DDeck panelMap3D;
        private PanelQuickActions panelActions;
        private System.Windows.Forms.Timer telemetryTimer;
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
                Height = 108,
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
                SplitterWidth = 1,
                BorderStyle = BorderStyle.None,
                BackColor = VeryDarkNavy
            };
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
            splitCenter.Panel1.Controls.Add(panelHudDeck);

            // 3D situational view (right center)
            panelMap3D = new PanelMap3DDeck
            {
                Dock = DockStyle.Fill,
                BackColor = VeryDarkNavy
            };
            splitCenter.Panel2.Controls.Add(panelMap3D);

            splitMain.Panel2.Controls.Add(splitCenter);

            // RIGHT PANEL - Quick Actions
            panelActions = new PanelQuickActions
            {
                Dock = DockStyle.Right,
                Width = 232,
                MinimumSize = new Size(210, 0),
                BackColor = VeryDarkNavy
            };
            splitMain.Panel2.Controls.Add(panelActions);

            var workspace = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = VeryDarkNavy,
                Padding = new Padding(16, 6, 16, 16)
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

            log.Info("ModernFlightDataCSharp initialized");
        }

        private void ModernFlightDataCSharp_Resize(object sender, EventArgs e)
        {
            UpdateResponsiveLayout();
        }

        private void UpdateResponsiveLayout()
        {
            if (splitMain == null || splitCenter == null || panelActions == null)
                return;

            int viewWidth = Math.Max(ClientSize.Width, 960);
            if (splitMain.Width > TelemetryMinWidth + CenterContentMinWidth + splitMain.SplitterWidth)
            {
                int desiredTelemetryWidth = Math.Max(TelemetryMinWidth, Math.Min(260, viewWidth / 6));
                int maxTelemetryWidth = splitMain.Width - CenterContentMinWidth - splitMain.SplitterWidth;
                splitMain.SplitterDistance = Math.Max(TelemetryMinWidth,
                    Math.Min(desiredTelemetryWidth, maxTelemetryWidth));
            }

            panelActions.Width = Math.Max(panelActions.MinimumSize.Width, Math.Min(238, viewWidth / 6));

            if (splitCenter.Width > HudMinWidth + MapDeckMinWidth + splitCenter.SplitterWidth)
            {
                int desiredHudWidth = (splitCenter.Width - splitCenter.SplitterWidth) / 2;
                int maxHudWidth = splitCenter.Width - MapDeckMinWidth - splitCenter.SplitterWidth;
                splitCenter.SplitterDistance = Math.Max(HudMinWidth, Math.Min(desiredHudWidth, maxHudWidth));
            }
        }

        private void TelemetryTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                bool isConnected = MainV2.comPort?.BaseStream?.IsOpen == true;
                var cs = MainV2.comPort?.MAV?.cs;

                if (cs == null)
                {
                    statusRail.SetOffline(isConnected);
                    panelHudDeck.SetOffline(isConnected);
                    panelMap3D.SetOffline(isConnected);
                    panelActions.UpdateStatus(false, isConnected, "Awaiting vehicle", "Connect to enable guided actions.");
                    TrackFlightEvents(null, isConnected);
                    return;
                }

                // Update HUD
                hudDisplay.Pitch = (float)cs.pitch;
                hudDisplay.Roll = (float)cs.roll;
                hudDisplay.Heading = (float)cs.yaw;
                hudDisplay.Mode = cs.mode;
                hudDisplay.Armed = cs.armed;
                hudDisplay.Altitude = cs.altasl;
                hudDisplay.GroundSpeed = cs.groundspeed;
                hudDisplay.VerticalSpeed = cs.verticalspeed;
                hudDisplay.BatteryRemaining = cs.battery_remaining;
                hudDisplay.Invalidate();
                panelHudDeck.UpdateFlightState(cs.mode, cs.armed, (float)cs.yaw, cs.battery_remaining);

                panelMap3D.UpdateTelemetry(cs);

                // Update telemetry
                panelTelemetry.UpdateTelemetry(cs);

                // Update status and actions
                statusRail.UpdateTelemetry(cs, isConnected);
                panelActions.UpdateStatus(cs.armed, isConnected, cs.mode, cs.messageHigh);
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
                UpdateResponsiveLayout();
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
        private readonly Font eyebrowFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private readonly Font titleFont = new Font("Segoe UI", 17f, FontStyle.Bold);
        private readonly Font cardLabelFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        private readonly Font cardValueFont = new Font("Segoe UI", 12f, FontStyle.Bold);
        private readonly Font cardDetailFont = new Font("Segoe UI", 8f, FontStyle.Regular);

        private readonly Color TitleColor = Color.FromArgb(232, 236, 242);
        private readonly Color MutedText = Color.FromArgb(140, 149, 166);
        private readonly Color CardBackground = Color.FromArgb(28, 35, 50);
        private readonly Color CardBorder = Color.FromArgb(48, 58, 78);
        private readonly StatusCard[] cards = new StatusCard[5];

        public TopStatusRail()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            DoubleBuffered = true;
            Padding = new Padding(18, 16, 18, 14);
            SetOffline(false);
        }

        public void SetOffline(bool connected)
        {
            cards[0] = new StatusCard("LINK", connected ? "DETECTED" : "OFFLINE", connected ? "Waiting for telemetry stream" : "No active vehicle connection", ModernUiPainter.GetLinkColor(connected, 0));
            cards[1] = new StatusCard("MODE", "--", "Vehicle state unavailable", Color.FromArgb(111, 120, 138));
            cards[2] = new StatusCard("BATTERY", "--", "No power data", Color.FromArgb(111, 120, 138));
            cards[3] = new StatusCard("GPS", "--", "No navigation data", Color.FromArgb(111, 120, 138));
            cards[4] = new StatusCard("ALERT", "STANDBY", "Modern flight deck ready", Color.FromArgb(200, 168, 101));
            Invalidate();
        }

        public void UpdateTelemetry(CurrentState cs, bool connected)
        {
            int linkQuality = Math.Max(0, Math.Min(100, (int)cs.linkqualitygcs));
            int battery = Math.Max(0, Math.Min(100, cs.battery_remaining));
            string mode = string.IsNullOrWhiteSpace(cs.mode) ? "UNKNOWN" : cs.mode.ToUpperInvariant();
            string alert = string.IsNullOrWhiteSpace(cs.messageHigh) ? "NOMINAL" : cs.messageHigh.ToUpperInvariant();

            cards[0] = new StatusCard("LINK", connected ? $"{linkQuality}%" : "OFFLINE",
                connected ? "Live MAVLink session" : "Vehicle disconnected",
                ModernUiPainter.GetLinkColor(connected, linkQuality));

            cards[1] = new StatusCard("MODE", mode, cs.armed ? "Armed and mission-capable" : "Safe state / preflight",
                cs.armed ? Color.FromArgb(72, 182, 132) : Color.FromArgb(200, 168, 101));

            cards[2] = new StatusCard("BATTERY", $"{battery}%", $"{cs.battery_voltage:F1} V  |  {cs.watts:F0} W",
                ModernUiPainter.GetBatteryColor(battery));

            cards[3] = new StatusCard("GPS", $"{cs.satcount:F0} SAT", $"Fix {cs.gpsstatus:F0}  |  HDOP {cs.gpshdop:0.0}",
                ModernUiPainter.GetGpsColor(cs.gpsstatus, cs.satcount));

            cards[4] = new StatusCard("ALERT", alert, $"WP {cs.wpno:F0}  |  {cs.wp_dist:F0} m to waypoint",
                string.Equals(alert, "NOMINAL", StringComparison.OrdinalIgnoreCase)
                    ? Color.FromArgb(72, 182, 132)
                    : Color.FromArgb(228, 84, 71));

            Invalidate();
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
                e.Graphics.DrawString("Live airframe, nav, power, and alert state", cardDetailFont, subtitleBrush, subtitleRect);
            }

            int gap = 12;
            int cardStartX = Padding.Left + titleWidth + 18;
            int availableWidth = Width - cardStartX - Padding.Right;
            int cardHeight = Height - Padding.Top - Padding.Bottom;
            int cardWidth = Math.Max(128, (availableWidth - gap * (cards.Length - 1)) / cards.Length);

            for (int i = 0; i < cards.Length; i++)
            {
                var cardRect = new Rectangle(cardStartX + i * (cardWidth + gap), Padding.Top, cardWidth, cardHeight);
                DrawCard(e.Graphics, cardRect, cards[i]);
            }
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
            float detailY = bounds.Bottom - 20;

            using (var labelBrush = new SolidBrush(MutedText))
            using (var valueBrush = new SolidBrush(TitleColor))
            using (var detailBrush = new SolidBrush(ModernUiPainter.WithAlpha(card.Accent, 215)))
            {
                graphics.DrawString(card.Label, cardLabelFont, labelBrush,
                    new RectangleF(contentX, labelY, contentWidth, 12), textFormat);
                graphics.DrawString(card.Value, cardValueFont, valueBrush,
                    new RectangleF(contentX, valueY, contentWidth, 18), textFormat);
                graphics.DrawString(card.Detail, cardDetailFont, detailBrush,
                    new RectangleF(contentX, detailY, contentWidth, 14), textFormat);
            }

            textFormat.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                eyebrowFont?.Dispose();
                titleFont?.Dispose();
                cardLabelFont?.Dispose();
                cardValueFont?.Dispose();
                cardDetailFont?.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Artificial Horizon Control - Shows pitch and roll attitude
    /// </summary>
    public class ControlArtificialHorizon : Control
    {
        public float Pitch { get; set; } = 0; // -90 to +90 degrees
        public float Roll { get; set; } = 0;  // -180 to +180 degrees
        public float Heading { get; set; } = 0; // 0 to 360 degrees
        public string Mode { get; set; } = "STANDBY";
        public bool Armed { get; set; }
        public float Altitude { get; set; }
        public float GroundSpeed { get; set; }
        public float VerticalSpeed { get; set; }
        public int BatteryRemaining { get; set; }

        private readonly Color SkyColor = Color.FromArgb(0, 60, 150);      // Professional sky blue
        private readonly Color GroundColor = Color.FromArgb(100, 80, 60);  // Professional ground brown
        private readonly Color HorizonLineColor = Color.FromArgb(200, 168, 101); // Gold horizon
        private readonly Color TextColor = Color.FromArgb(220, 220, 220);  // Light gray text
        private readonly Color AircraftColor = Color.Red;                   // Red aircraft symbol
        private readonly Color PanelSurface = Color.FromArgb(136, 11, 18, 30);
        private readonly Color SafeColor = Color.FromArgb(228, 84, 71);
        private readonly Color ArmedColor = Color.FromArgb(72, 182, 132);

        private Pen horizonPen;
        private Pen gridPen;
        private SolidBrush skyBrush;
        private SolidBrush groundBrush;
        private SolidBrush textBrush;
        private Font font;
        private Font metricFont;
        private Font chipFont;
        private Font detailFont;

        public ControlArtificialHorizon()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            DoubleBuffered = true;
            BackColor = Color.FromArgb(10, 14, 20);

            horizonPen = new Pen(HorizonLineColor, 2);
            gridPen = new Pen(HorizonLineColor, 1) { DashStyle = DashStyle.Dash };
            skyBrush = new SolidBrush(SkyColor);
            groundBrush = new SolidBrush(GroundColor);
            textBrush = new SolidBrush(TextColor);
            font = new Font("Segoe UI", 10, FontStyle.Bold);
            metricFont = new Font("Segoe UI", 18, FontStyle.Bold);
            chipFont = new Font("Segoe UI", 9, FontStyle.Bold);
            detailFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;

            int w = Width;
            int h = Height;
            int chipWidth = Math.Max(110, Math.Min(138, w / 5));
            int chipHeight = 32;
            int sideMetricWidth = Math.Max(112, Math.Min(134, w / 5));
            int sideMetricHeight = 72;
            int centerMetricWidth = Math.Max(156, Math.Min(182, w / 4));
            int centerMetricHeight = 54;
            int maxHudWidth = Math.Max(190, w - (sideMetricWidth * 2) - 72);
            int maxHudHeight = Math.Max(190, h - 108);
            int diameter = Math.Max(210, Math.Min(maxHudWidth, maxHudHeight));
            diameter = Math.Min(diameter, Math.Min(w - 44, h - 28));
            int hudTop = Math.Max(20, (h - diameter) / 2 - 2);
            var hudRect = new Rectangle((w - diameter) / 2, hudTop, diameter, diameter);
            int centerX = hudRect.Left + hudRect.Width / 2;
            int centerY = hudRect.Top + hudRect.Height / 2;
            int radius = hudRect.Width / 2;

            // Draw background
            e.Graphics.Clear(BackColor);

            // Create a clipping region for the circular display
            using (var clipPath = new GraphicsPath())
            {
                clipPath.AddEllipse(hudRect);
                e.Graphics.SetClip(clipPath);

                // Calculate pitch line position (1 pixel = 1 degree)
                float pitchPixels = -Pitch * 2.8f;

                // Save graphics state for rotation
                var state = e.Graphics.Save();
                e.Graphics.TranslateTransform(centerX, centerY);
                e.Graphics.RotateTransform(-Roll);

                // Draw sky
                var skyRect = new RectangleF(-radius * 2f, -radius * 2f + pitchPixels, radius * 4f, radius * 4f);
                e.Graphics.FillRectangle(skyBrush, skyRect);

                // Draw ground
                var groundRect = new RectangleF(-radius * 2f, pitchPixels, radius * 4f, radius * 4f);
                e.Graphics.FillRectangle(groundBrush, groundRect);

                // Draw horizon line
                e.Graphics.DrawLine(horizonPen, -radius * 2f, (int)pitchPixels, radius * 2f, (int)pitchPixels);

                // Draw pitch ladder
                for (int pitch = -40; pitch <= 40; pitch += 10)
                {
                    if (pitch == 0) continue;
                    float y = (pitch - Pitch) * 2.8f;
                    int lineLen = 40;
                    e.Graphics.DrawLine(gridPen, -lineLen, (int)y, lineLen, (int)y);
                    
                    // Draw pitch angle numbers
                    if (pitch % 20 == 0)
                    {
                        string label = Math.Abs(pitch).ToString();
                        var size = e.Graphics.MeasureString(label, font);
                        e.Graphics.DrawString(label, font, textBrush, 
                            new PointF(lineLen + 5, (int)y - size.Height / 2));
                        e.Graphics.DrawString(label, font, textBrush,
                            new PointF(-lineLen - size.Width - 5, (int)y - size.Height / 2));
                    }
                }

                // Restore graphics state
                e.Graphics.Restore(state);

                // Draw aircraft symbol (fixed in center)
                int symbolSize = 34;
                using (var aircraftBrush = new SolidBrush(AircraftColor))
                using (var aircraftPen = new Pen(AircraftColor, 2))
                {
                    e.Graphics.FillEllipse(aircraftBrush, centerX - 3, centerY - symbolSize / 2, 6, 10);
                    e.Graphics.DrawLine(aircraftPen, centerX - 15, centerY, centerX + 15, centerY);
                }

            }

            // Draw circular border
            using (var borderPen = new Pen(HorizonLineColor, 3))
            {
                e.Graphics.DrawEllipse(borderPen, hudRect);
            }

            int chipY = 18;
            DrawOverlayChip(e.Graphics, new Rectangle(24, chipY, chipWidth, chipHeight),
                string.IsNullOrWhiteSpace(Mode) ? "STANDBY" : Mode.ToUpperInvariant(), HorizonLineColor);

            DrawOverlayChip(e.Graphics, new Rectangle(w - chipWidth - 24, chipY, chipWidth, chipHeight),
                Armed ? "ARMED" : "SAFE", Armed ? ArmedColor : SafeColor);

            int footerY = h - sideMetricHeight - 16;
            DrawMetricPanel(e.Graphics, new Rectangle(24, footerY, sideMetricWidth, sideMetricHeight),
                "GROUND SPEED", $"{GroundSpeed:F1}", "m/s", Color.FromArgb(66, 194, 226));

            DrawMetricPanel(e.Graphics, new Rectangle(w - sideMetricWidth - 24, footerY, sideMetricWidth, sideMetricHeight),
                "ALTITUDE", $"{Altitude:F0}", "m", HorizonLineColor);

            DrawMetricPanel(e.Graphics, new Rectangle(centerX - centerMetricWidth / 2, h - centerMetricHeight - 12, centerMetricWidth, centerMetricHeight),
                "VERTICAL SPEED", $"{VerticalSpeed:+0.0;-0.0;0.0}", "m/s",
                ModernUiPainter.GetBatteryColor(BatteryRemaining), true);

            using (var infoBrush = new SolidBrush(TextColor))
            {
                e.Graphics.DrawString($"Pitch {Pitch:F1} deg", detailFont, infoBrush, new RectangleF(24, footerY - 20, 120, 16));
                e.Graphics.DrawString($"Roll {Roll:F1} deg", detailFont, infoBrush, new RectangleF(w - 144, footerY - 20, 120, 16));
                e.Graphics.DrawString($"Heading {Heading:F0} deg", detailFont, infoBrush, new RectangleF(centerX - 70, chipY + chipHeight + 10, 140, 16));
            }
        }

        private void DrawOverlayChip(Graphics graphics, Rectangle bounds, string text, Color accent)
        {
            ModernUiPainter.FillRoundedRectangle(graphics, PanelSurface, bounds, 14);
            ModernUiPainter.DrawRoundedRectangle(graphics, ModernUiPainter.WithAlpha(accent, 180), 1.2f, bounds, 14);

            using (var accentBrush = new SolidBrush(accent))
            using (var textBrushLocal = new SolidBrush(TextColor))
            {
                graphics.FillEllipse(accentBrush, bounds.X + 12, bounds.Y + 12, 10, 10);
                graphics.DrawString(text, chipFont, textBrushLocal, new RectangleF(bounds.X + 30, bounds.Y + 8, bounds.Width - 36, 18));
            }
        }

        private void DrawMetricPanel(Graphics graphics, Rectangle bounds, string label, string value, string unit, Color accent, bool compact = false)
        {
            ModernUiPainter.FillRoundedRectangle(graphics, PanelSurface, bounds, 16);
            ModernUiPainter.DrawRoundedRectangle(graphics, ModernUiPainter.WithAlpha(accent, 170), 1.1f, bounds, 16);

            using (var labelBrush = new SolidBrush(ModernUiPainter.WithAlpha(TextColor, 190)))
            using (var valueBrush = new SolidBrush(TextColor))
            using (var unitBrush = new SolidBrush(accent))
            {
                graphics.DrawString(label, detailFont, labelBrush, new RectangleF(bounds.X + 14, bounds.Y + 10, bounds.Width - 28, 14));
                graphics.DrawString(value, compact ? font : metricFont, valueBrush, new RectangleF(bounds.X + 14, bounds.Y + 24, bounds.Width - 28, compact ? 20 : 26));
                graphics.DrawString(unit, detailFont, unitBrush, new RectangleF(bounds.X + 14, bounds.Bottom - 20, bounds.Width - 28, 14));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                horizonPen?.Dispose();
                gridPen?.Dispose();
                skyBrush?.Dispose();
                groundBrush?.Dispose();
                textBrush?.Dispose();
                font?.Dispose();
                metricFont?.Dispose();
                chipFont?.Dispose();
                detailFont?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public class PanelHudDeck : Panel
    {
        private readonly Font eyebrowFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private readonly Font titleFont = new Font("Segoe UI", 12, FontStyle.Bold);
        private readonly Font subtitleFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        private readonly Font badgeFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);

        private readonly Color Surface = Color.FromArgb(24, 31, 44);
        private readonly Color Border = Color.FromArgb(44, 54, 73);
        private readonly Color Gold = Color.FromArgb(200, 168, 101);
        private readonly Color LightGray = Color.FromArgb(235, 239, 245);
        private readonly Color MutedGray = Color.FromArgb(138, 149, 168);
        private readonly Color LiveAccent = Color.FromArgb(74, 190, 225);
        private readonly Color ArmedAccent = Color.FromArgb(72, 182, 132);
        private readonly Color SafeAccent = Color.FromArgb(228, 84, 71);
        private readonly Color StandbyAccent = Color.FromArgb(110, 118, 136);

        private readonly ControlArtificialHorizon hudControl;
        private Panel headerPanel;
        private Panel hudBorderPanel;
        private Panel hudHostPanel;
        private Label lblEyebrow;
        private Label lblTitle;
        private Label lblSubtitle;
        private Label lblStatus;
        private Panel statusDot;

        public PanelHudDeck(ControlArtificialHorizon hudControl)
        {
            this.hudControl = hudControl ?? throw new ArgumentNullException(nameof(hudControl));

            BackColor = Color.FromArgb(10, 14, 20);
            Padding = new Padding(14, 12, 14, 14);
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
                Padding = new Padding(10, 8, 10, 10)
            };
            hudBorderPanel.Controls.Add(hudHostPanel);

            hudControl.Dock = DockStyle.Fill;
            hudHostPanel.Controls.Add(hudControl);

            headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 68,
                BackColor = Surface
            };
            Controls.Add(headerPanel);

            lblEyebrow = new Label
            {
                Font = eyebrowFont,
                ForeColor = Gold,
                BackColor = Surface,
                Text = "ATTITUDE DECK",
                AutoEllipsis = true
            };
            headerPanel.Controls.Add(lblEyebrow);

            lblTitle = new Label
            {
                Font = titleFont,
                ForeColor = LightGray,
                BackColor = Surface,
                Text = "HUD standing by",
                AutoEllipsis = true
            };
            headerPanel.Controls.Add(lblTitle);

            lblSubtitle = new Label
            {
                Font = subtitleFont,
                ForeColor = MutedGray,
                BackColor = Surface,
                Text = "Awaiting flight telemetry.",
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

            LayoutHeader();
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
            int badgeHeight = 28;
            int rightInset = 18;
            int topInset = 18;

            lblStatus.Bounds = new Rectangle(
                Math.Max(136, headerPanel.ClientSize.Width - badgeWidth - rightInset),
                topInset,
                badgeWidth,
                badgeHeight);

            statusDot.Bounds = new Rectangle(
                Math.Max(10, lblStatus.Left - 18),
                topInset + (badgeHeight - statusDot.Height) / 2,
                statusDot.Width,
                statusDot.Height);

            int textWidth = Math.Max(180, statusDot.Left - 28);
            lblEyebrow.Bounds = new Rectangle(18, 10, textWidth, 14);
            lblTitle.Bounds = new Rectangle(18, 24, textWidth, 20);
            lblSubtitle.Bounds = new Rectangle(18, 44, textWidth, 16);
        }

        public void SetOffline(bool connected)
        {
            ApplyDeckState(
                connected ? "SYNCING" : "OFFLINE",
                connected ? "HUD synchronizing" : "HUD standing by",
                connected
                    ? "Vehicle link detected. Waiting for live flight attitude."
                    : "Connect a vehicle to begin the live flight deck.",
                connected ? Gold : StandbyAccent,
                connected ? Color.FromArgb(78, 62, 28) : Color.FromArgb(44, 49, 59));
        }

        public void UpdateFlightState(string mode, bool armed, float heading, int batteryRemaining)
        {
            string modeLabel = string.IsNullOrWhiteSpace(mode) ? "STANDBY" : mode.ToUpperInvariant();
            Color accent = armed ? ArmedAccent : SafeAccent;

            ApplyDeckState(
                armed ? "ARMED" : "SAFE",
                armed ? "Live flight attitude deck" : "Flight attitude deck",
                $"{modeLabel}  |  Heading {heading:F0} deg  |  Battery {Math.Max(0, batteryRemaining)}%",
                accent,
                armed ? Color.FromArgb(24, 66, 52) : Color.FromArgb(74, 36, 34));
        }

        private void ApplyDeckState(string badgeText, string title, string subtitle, Color accent, Color pillBackColor)
        {
            lblStatus.Text = badgeText;
            lblTitle.Text = title;
            lblSubtitle.Text = subtitle;
            lblStatus.BackColor = pillBackColor;
            statusDot.BackColor = accent;
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
            if (disposing)
            {
                eyebrowFont?.Dispose();
                titleFont?.Dispose();
                subtitleFont?.Dispose();
                badgeFont?.Dispose();
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
        private readonly Font valueFont = new Font("Segoe UI", 15.5f, FontStyle.Bold);
        private readonly Font detailFont = new Font("Segoe UI", 8.25f, FontStyle.Regular);

        private readonly Color Surface = Color.FromArgb(27, 33, 46);
        private readonly Color Border = Color.FromArgb(46, 56, 74);
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
            ModernUiPainter.FillRoundedRectangle(e.Graphics, Surface, bounds, 18);
            ModernUiPainter.DrawRoundedRectangle(e.Graphics, Border, 1f, bounds, 18);

            var accentRect = new Rectangle(12, 12, Math.Max(34, Width / 4), 4);
            ModernUiPainter.FillRoundedRectangle(e.Graphics, AccentColor, accentRect, 2);

            using (var titleBrush = new SolidBrush(TextMuted))
            using (var valueBrush = new SolidBrush(TextPrimary))
            using (var detailBrush = new SolidBrush(ModernUiPainter.WithAlpha(AccentColor, 220)))
            using (var textFormat = new StringFormat
            {
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter
            })
            {
                float contentX = 14;
                float contentWidth = Width - 28;
                float labelY = 21;
                float valueY = 39;
                float detailY = Height - 31;

                e.Graphics.DrawString(Title, labelFont, titleBrush,
                    new RectangleF(contentX, labelY, contentWidth, 12), textFormat);
                e.Graphics.DrawString(Value, valueFont, valueBrush,
                    new RectangleF(contentX, valueY, contentWidth, 22), textFormat);
                e.Graphics.DrawString(Detail, detailFont, detailBrush,
                    new RectangleF(contentX, detailY, contentWidth, 14), textFormat);
            }

            int progressWidth = (int)((Width - 28) * Math.Max(0f, Math.Min(1f, Progress)));
            if (progressWidth > 0)
            {
                var progressTrack = new Rectangle(14, Height - 14, Width - 28, 4);
                var progressFill = new Rectangle(14, Height - 14, progressWidth, 4);
                ModernUiPainter.FillRoundedRectangle(e.Graphics, Color.FromArgb(42, 54, 76), progressTrack, 2);
                ModernUiPainter.FillRoundedRectangle(e.Graphics, AccentColor, progressFill, 2);
            }
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
        private const int MinimumVisibleMetricCount = 4;
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
        private CurrentState lastTelemetry;

        public PanelTelemetry()
        {
            BackColor = Color.FromArgb(10, 14, 20);
            Padding = new Padding(10, 12, 10, 10);
            AutoScroll = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            LoadMetricSelection();
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
                Text = "Power, nav, and airframe telemetry at a glance",
                Font = subtitleFont,
                ForeColor = MutedText,
                BackColor = Color.Transparent,
                AutoSize = false
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
            int contentWidth = Math.Max(200, ClientSize.Width - Padding.Horizontal);
            int currentTop = Padding.Top;
            int buttonWidth = Math.Min(84, Math.Max(70, contentWidth / 3));

            btnCustomize.Bounds = new Rectangle(
                Padding.Left + contentWidth - buttonWidth,
                currentTop,
                buttonWidth,
                28);

            lblHeader.Bounds = new Rectangle(Padding.Left, currentTop + 1, contentWidth - buttonWidth - 8, 24);
            currentTop += 24;

            lblSubtitle.Bounds = new Rectangle(Padding.Left, currentTop, contentWidth, 18);
            currentTop += 24;

            int gap = 10;
            int cardHeight = contentWidth < 240 ? 100 : 96;

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
                    var snapshot = BuildMetricSnapshot(selectedMetricKeys[i], cs);
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
                    foreach (var token in rawValue.ToString().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string key = token.Trim();
                        if (MetricCatalog.Any(option => option.Key == key) && !selectedMetricKeys.Contains(key))
                            selectedMetricKeys.Add(key);
                    }
                }
            }
            catch
            {
            }

            if (selectedMetricKeys.Count == 0)
                selectedMetricKeys.AddRange(DefaultMetricKeys);

            foreach (var key in DefaultMetricKeys)
            {
                if (selectedMetricKeys.Count >= MinimumVisibleMetricCount)
                    break;

                if (!selectedMetricKeys.Contains(key))
                    selectedMetricKeys.Add(key);
            }
        }

        private void SyncCardControls()
        {
            while (cards.Count < selectedMetricKeys.Count)
            {
                var card = new ModernTelemetryCard { BackColor = BackColor };
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

        private void RebuildTelemetryMenu()
        {
            if (telemetryMenu == null)
                return;

            telemetryMenu.Items.Clear();

            foreach (var metric in MetricCatalog)
            {
                bool isSelected = selectedMetricKeys.Contains(metric.Key);
                var item = new ToolStripMenuItem(metric.Label)
                {
                    Checked = isSelected,
                    CheckOnClick = false,
                    Tag = metric.Key,
                    Enabled = !isSelected || selectedMetricKeys.Count > MinimumVisibleMetricCount
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

            if (selectedMetricKeys.Contains(metricKey))
            {
                if (selectedMetricKeys.Count <= MinimumVisibleMetricCount)
                    return;

                selectedMetricKeys.Remove(metricKey);
            }
            else
            {
                selectedMetricKeys.Add(metricKey);
            }

            SaveMetricSelection();
            SyncCardControls();
            RebuildTelemetryMenu();

            if (lastTelemetry != null)
                UpdateTelemetry(lastTelemetry);
        }

        private void ResetMetricSelection()
        {
            selectedMetricKeys.Clear();
            selectedMetricKeys.AddRange(DefaultMetricKeys);

            SaveMetricSelection();
            SyncCardControls();
            RebuildTelemetryMenu();

            if (lastTelemetry != null)
                UpdateTelemetry(lastTelemetry);
        }

        private void SaveMetricSelection()
        {
            try
            {
                Settings.Instance[TelemetrySelectionSettingKey] = string.Join(",", selectedMetricKeys);
                Settings.Instance.Save();
            }
            catch
            {
            }
        }

        private static TelemetryMetricSnapshot BuildMetricSnapshot(string key, CurrentState cs)
        {
            int battery = Math.Max(0, Math.Min(100, cs.battery_remaining));

            switch (key)
            {
                case "altitude":
                    return new TelemetryMetricSnapshot(
                        "ALTITUDE",
                        $"{cs.altasl:F0} m",
                        $"Home {cs.DistToHome:F0} m",
                        Color.FromArgb(66, 194, 226),
                        Clamp01(cs.altasl / 150f));

                case "ground_speed":
                    return new TelemetryMetricSnapshot(
                        "GROUND SPEED",
                        $"{cs.groundspeed:F1} m/s",
                        $"Climb {cs.verticalspeed:+0.0;-0.0;0.0} m/s",
                        Color.FromArgb(66, 194, 226),
                        Clamp01(cs.groundspeed / 30f));

                case "battery":
                    return new TelemetryMetricSnapshot(
                        "BATTERY",
                        $"{battery}%",
                        $"{cs.battery_voltage:F1} V  |  {cs.watts:F0} W",
                        ModernUiPainter.GetBatteryColor(battery),
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
                        Color.FromArgb(230, 174, 68),
                        battery / 100f);

                case "battery_used":
                    return new TelemetryMetricSnapshot(
                        "BATTERY USED",
                        $"{cs.battery_usedmah:F0} mAh",
                        $"{cs.current:0.0} A draw  |  {cs.watts:F0} W",
                        Color.FromArgb(198, 136, 98),
                        Clamp01(1f - battery / 100f));

                case "gps":
                    return new TelemetryMetricSnapshot(
                        "GPS LOCK",
                        $"{cs.satcount:F0} sats",
                        $"Fix {cs.gpsstatus:F0}  |  HDOP {cs.gpshdop:0.0}",
                        ModernUiPainter.GetGpsColor(cs.gpsstatus, cs.satcount),
                        Clamp01(cs.satcount / 16f));

                case "navigation":
                    return new TelemetryMetricSnapshot(
                        "NAVIGATION",
                        $"WP {cs.wpno:F0}",
                        $"WP {cs.wp_dist:F0} m  |  Track {cs.distTraveled:F0} m",
                        Color.FromArgb(114, 192, 115),
                        Math.Max(0.08f, 1f - Clamp01(cs.wp_dist / 1200f)));

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
                        Color.FromArgb(200, 168, 101),
                        hasFuelTelemetry ? 0.72f : 0.08f);

                case "flight_mode":
                    return new TelemetryMetricSnapshot(
                        "FLIGHT MODE",
                        string.IsNullOrWhiteSpace(cs.mode) ? "UNKNOWN" : cs.mode.ToUpperInvariant(),
                        $"Air time {ModernUiPainter.FormatDuration(cs.timeInAir)}",
                        cs.armed ? Color.FromArgb(72, 182, 132) : Color.FromArgb(200, 168, 101),
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
    }

    public class PanelMap3DDeck : Panel
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly Font eyebrowFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private readonly Font titleFont = new Font("Segoe UI", 12, FontStyle.Bold);
        private readonly Font subtitleFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        private readonly Font badgeFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);

        private readonly Color Surface = Color.FromArgb(24, 31, 44);
        private readonly Color Border = Color.FromArgb(44, 54, 73);
        private readonly Color Gold = Color.FromArgb(200, 168, 101);
        private readonly Color LightGray = Color.FromArgb(235, 239, 245);
        private readonly Color MutedGray = Color.FromArgb(138, 149, 168);
        private readonly Color LiveAccent = Color.FromArgb(74, 190, 225);
        private readonly Color StandbyAccent = Color.FromArgb(110, 118, 136);

        private Panel headerPanel;
        private Panel mapBorderPanel;
        private Panel mapHostPanel;
        private Label lblEyebrow;
        private Label lblTitle;
        private Label lblSubtitle;
        private Label lblStatus;
        private Panel statusDot;
        private Label lblFallback;
        private Map3D mapView;
        private bool mapAvailable;
        private string initError = "";

        public PanelMap3DDeck()
        {
            BackColor = Color.FromArgb(10, 14, 20);
            Padding = new Padding(14, 12, 14, 14);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            InitializeControls();
            ApplyDeckState(false, false, "STANDBY",
                "Connect a vehicle to begin the live terrain-aware 3D view.");
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
                BackColor = Color.FromArgb(6, 10, 18)
            };
            mapBorderPanel.Controls.Add(mapHostPanel);

            headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 68,
                BackColor = Surface
            };
            Controls.Add(headerPanel);

            lblEyebrow = new Label
            {
                Font = eyebrowFont,
                ForeColor = Gold,
                BackColor = Surface,
                Text = "3D SITUATIONAL VIEW",
                AutoEllipsis = true
            };
            headerPanel.Controls.Add(lblEyebrow);

            lblTitle = new Label
            {
                Font = titleFont,
                ForeColor = LightGray,
                BackColor = Surface,
                Text = "3D map standing by",
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
                Text = "STANDBY",
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
                    Name = "modernMap3DView"
                };

                mapHostPanel.Controls.Add(mapView);
                mapAvailable = true;
            }
            catch (Exception ex)
            {
                initError = ex.Message;
                mapAvailable = false;
                log.Error("Modern flight 3D map failed to initialize", ex);

                lblFallback = new Label
                {
                    Dock = DockStyle.Fill,
                    BackColor = mapHostPanel.BackColor,
                    ForeColor = LightGray,
                    Font = subtitleFont,
                    Padding = new Padding(28),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Text = "3D map renderer unavailable.\r\nModern Flight remains usable without it."
                };
                mapHostPanel.Controls.Add(lblFallback);
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

            int badgeWidth = 114;
            int badgeHeight = 28;
            int rightInset = 18;
            int topInset = 18;

            lblStatus.Bounds = new Rectangle(
                Math.Max(140, headerPanel.ClientSize.Width - badgeWidth - rightInset),
                topInset,
                badgeWidth,
                badgeHeight);

            statusDot.Bounds = new Rectangle(
                Math.Max(10, lblStatus.Left - 18),
                topInset + (badgeHeight - statusDot.Height) / 2,
                statusDot.Width,
                statusDot.Height);

            int textWidth = Math.Max(180, statusDot.Left - 28);
            lblEyebrow.Bounds = new Rectangle(18, 10, textWidth, 14);
            lblTitle.Bounds = new Rectangle(18, 24, textWidth, 20);
            lblSubtitle.Bounds = new Rectangle(18, 44, textWidth, 16);
        }

        public void ActivateView()
        {
            if (!mapAvailable || mapView == null)
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
            if (!mapAvailable || mapView == null)
                return;

            try
            {
                mapView.Deactivate();
            }
            catch (Exception ex)
            {
                log.Debug($"Modern flight 3D deactivation error: {ex.Message}");
            }
        }

        public void SetOffline(bool connected)
        {
            ApplyDeckState(connected, false, connected ? "SYNCING" : "OFFLINE",
                connected
                    ? "Vehicle link detected. Waiting for valid telemetry before centering the 3D view."
                    : "Connect a vehicle to begin the live terrain-aware 3D view.");
        }

        public void UpdateTelemetry(CurrentState cs)
        {
            bool connected = MainV2.comPort?.BaseStream?.IsOpen == true;
            bool hasPosition = HasValidCoordinate(cs.lat, cs.lng);
            string mode = string.IsNullOrWhiteSpace(cs.mode) ? "STANDBY" : cs.mode.ToUpperInvariant();

            ApplyDeckState(connected, hasPosition,
                connected && hasPosition ? "LIVE 3D" : connected ? "SYNCING" : "OFFLINE",
                hasPosition
                    ? $"{mode}  |  {cs.lat:F5}, {cs.lng:F5}  |  Alt {cs.altasl:F0} m"
                    : connected
                        ? "Vehicle linked. Waiting for a stable position fix for the 3D scene."
                        : "Connect a vehicle to begin the live terrain-aware 3D view.");

            if (!mapAvailable || mapView == null || !connected || !hasPosition)
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
            Color accent = !mapAvailable
                ? StandbyAccent
                : hasPosition
                    ? LiveAccent
                    : connected
                        ? Gold
                        : StandbyAccent;

            lblTitle.Text = !mapAvailable
                ? "3D renderer unavailable"
                : hasPosition
                    ? "Live terrain-aware pursuit map"
                    : connected
                        ? "3D scene synchronizing"
                        : "3D map standing by";

            lblSubtitle.Text = mapAvailable
                ? subtitle
                : $"OpenGL initialization failed for the 3D renderer. {initError}";

            lblStatus.Text = mapAvailable ? badgeText : "UNAVAILABLE";
            lblStatus.BackColor = !mapAvailable
                ? Color.FromArgb(52, 58, 69)
                : hasPosition
                    ? Color.FromArgb(26, 60, 78)
                    : connected
                        ? Color.FromArgb(78, 62, 28)
                        : Color.FromArgb(44, 49, 59);

            statusDot.BackColor = accent;
            mapBorderPanel.BackColor = !mapAvailable
                ? Border
                : hasPosition
                    ? Color.FromArgb(56, 86, 106)
                    : connected
                        ? Color.FromArgb(86, 72, 40)
                        : Border;

            if (lblFallback != null)
            {
                lblFallback.Text = !mapAvailable
                    ? $"3D map renderer unavailable.\r\n{initError}"
                    : "Waiting for the 3D renderer.";
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

    public class EventTimelineControl : Control
    {
        private readonly Font labelFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        private readonly Font titleFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        private readonly Font detailFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        private readonly Color Surface = Color.FromArgb(19, 24, 34);
        private readonly Color Border = Color.FromArgb(46, 56, 74);
        private readonly Color TextPrimary = Color.FromArgb(232, 236, 243);
        private readonly Color TextMuted = Color.FromArgb(136, 148, 168);
        private readonly System.Collections.Generic.List<FlightEventItem> items =
            new System.Collections.Generic.List<FlightEventItem>();

        public EventTimelineControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            DoubleBuffered = true;
            BackColor = Surface;
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

            items.Insert(0, new FlightEventItem(severity, trimmedTitle, trimmedDetail));
            if (items.Count > 6)
                items.RemoveAt(items.Count - 1);

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

            int gap = 8;
            int eventHeight = Math.Max(52, Math.Min(64, (Height - gap * (items.Count - 1)) / Math.Max(1, items.Count)));
            int top = 0;

            for (int i = 0; i < items.Count; i++)
            {
                var bounds = new Rectangle(0, top, Width - 1, eventHeight);
                DrawEventCard(e.Graphics, bounds, items[i]);
                top += eventHeight + gap;

                if (top > Height)
                    break;
            }
        }

        private void DrawEventCard(Graphics graphics, Rectangle bounds, FlightEventItem item)
        {
            Color accent = GetAccent(item.Severity);

            ModernUiPainter.FillRoundedRectangle(graphics, Surface, bounds, 16);
            ModernUiPainter.DrawRoundedRectangle(graphics, Border, 1f, bounds, 16);
            ModernUiPainter.FillRoundedRectangle(graphics, accent, new Rectangle(bounds.X + 10, bounds.Y + 10, 4, bounds.Height - 20), 2);

            using (var labelBrush = new SolidBrush(TextMuted))
            using (var titleBrush = new SolidBrush(TextPrimary))
            using (var detailBrush = new SolidBrush(ModernUiPainter.WithAlpha(accent, 220)))
            {
                graphics.DrawString(item.Timestamp.ToString("HH:mm:ss"), labelFont, labelBrush,
                    new RectangleF(bounds.X + 22, bounds.Y + 10, bounds.Width - 30, 12));
                graphics.DrawString(item.Title, titleFont, titleBrush,
                    new RectangleF(bounds.X + 22, bounds.Y + 24, bounds.Width - 30, 16));
                graphics.DrawString(item.Detail, detailFont, detailBrush,
                    new RectangleF(bounds.X + 22, bounds.Bottom - 22, bounds.Width - 30, 14));
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
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Quick Actions Panel - Right side buttons for ARM, TAKEOFF, RTL, LAND
    /// </summary>
    public class PanelQuickActions : Panel
    {
        private Label lblTitle;
        private Label lblSubtitle;
        private Label lblHint;
        private Label lblTimeline;
        private Button btnArm;
        private Button btnTakeoff;
        private Button btnRTL;
        private Button btnLand;
        private Label lblStatus;
        private EventTimelineControl eventTimeline;

        private readonly Color DarkNavy = Color.FromArgb(26, 31, 46);
        private readonly Color Gold = Color.FromArgb(200, 168, 101);
        private readonly Color LightGray = Color.FromArgb(220, 220, 220);
        private readonly Color MutedGray = Color.FromArgb(138, 149, 168);
        private readonly Color GreenStatus = Color.FromArgb(76, 175, 80);
        private readonly Color RedStatus = Color.FromArgb(244, 67, 54);
        private readonly Color WarningStatus = Color.FromArgb(228, 172, 67);

        public PanelQuickActions()
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
                Text = "ACTION CONSOLE",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Gold,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(lblTitle);

            lblSubtitle = new Label
            {
                Text = "Guided mission controls and live flight events",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = LightGray,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(lblSubtitle);

            btnArm = CreateActionButton("ARM VEHICLE", Color.FromArgb(54, 96, 166));
            btnArm.Click += BtnArm_Click;

            btnTakeoff = CreateActionButton("TAKEOFF", Color.FromArgb(111, 90, 42));
            btnTakeoff.Click += BtnTakeoff_Click;

            btnRTL = CreateActionButton("RETURN TO LAUNCH", Color.FromArgb(68, 118, 76));
            btnRTL.Click += BtnRTL_Click;

            btnLand = CreateActionButton("LAND NOW", Color.FromArgb(122, 58, 52));
            btnLand.Click += BtnLand_Click;

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
                Text = "Connect to enable guided actions.",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = LightGray,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.TopLeft
            };
            Controls.Add(lblHint);

            lblTimeline = new Label
            {
                Text = "LIVE EVENTS",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Gold,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(lblTimeline);

            eventTimeline = new EventTimelineControl();
            Controls.Add(eventTimeline);

            UpdateLayoutMetrics();
        }

        private Button CreateActionButton(string text, Color accent)
        {
            var btn = new Button
            {
                Text = text,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(27, 35, 49),
                ForeColor = LightGray,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Tag = accent
            };

            btn.FlatAppearance.BorderColor = accent;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(38, 46, 63);
            btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(58, 67, 88);

            Controls.Add(btn);
            return btn;
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
            top += 24;

            lblStatus.Bounds = new Rectangle(left, top, contentWidth, 18);
            top += 30;

            const int buttonHeight = 48;
            const int gap = 8;

            btnArm.Bounds = new Rectangle(left, top, contentWidth, buttonHeight);
            top += buttonHeight + gap;

            btnTakeoff.Bounds = new Rectangle(left, top, contentWidth, buttonHeight);
            top += buttonHeight + gap;

            btnRTL.Bounds = new Rectangle(left, top, contentWidth, buttonHeight);
            top += buttonHeight + gap;

            btnLand.Bounds = new Rectangle(left, top, contentWidth, buttonHeight);
            top += buttonHeight + 12;

            lblHint.Bounds = new Rectangle(left, top, contentWidth, 38);
            top += 44;

            lblTimeline.Bounds = new Rectangle(left, top, contentWidth, 18);
            top += 24;

            eventTimeline.Bounds = new Rectangle(left, top, contentWidth, Math.Max(82, ClientSize.Height - top - Padding.Bottom));
        }

        public void UpdateStatus(bool armed, bool connected, string mode, string alert)
        {
            lblSubtitle.Text = string.IsNullOrWhiteSpace(mode) ? "Guided mission controls" : $"Mode: {mode.ToUpperInvariant()}";
            lblStatus.Text = $"{(armed ? "ARMED" : "SAFE")}  |  {(connected ? "LIVE LINK" : "OFFLINE")}";
            lblStatus.ForeColor = !connected ? RedStatus : armed ? GreenStatus : WarningStatus;

            btnArm.Text = armed ? "DISARM VEHICLE" : "ARM VEHICLE";
            ApplyButtonState(btnArm, armed ? Color.FromArgb(122, 58, 52) : Color.FromArgb(54, 96, 166), connected);
            ApplyButtonState(btnTakeoff, Color.FromArgb(111, 90, 42), !armed && connected);
            ApplyButtonState(btnRTL, Color.FromArgb(68, 118, 76), armed && connected);
            ApplyButtonState(btnLand, Color.FromArgb(122, 58, 52), armed && connected);

            if (!connected)
                lblHint.Text = "Connect to enable guided actions and mission commands.";
            else if (!string.IsNullOrWhiteSpace(alert))
                lblHint.Text = $"Alert: {alert}";
            else if (armed)
                lblHint.Text = "Vehicle is armed. RTL and LAND are now available.";
            else
                lblHint.Text = "Vehicle is safe. Arm or send a guided takeoff command.";

            btnArm.Enabled = connected;
            btnTakeoff.Enabled = !armed && connected;
            btnRTL.Enabled = armed && connected;
            btnLand.Enabled = armed && connected;
        }

        public void PushEvent(FlightEventSeverity severity, string title, string detail)
        {
            eventTimeline?.AddEvent(severity, title, detail);
        }

        private void ApplyButtonState(Button button, Color accent, bool enabled)
        {
            button.FlatAppearance.BorderColor = accent;
            button.BackColor = enabled ? Color.FromArgb(27, 35, 49) : Color.FromArgb(21, 25, 34);
            button.ForeColor = enabled ? LightGray : Color.FromArgb(123, 131, 145);
        }

        private void BtnArm_Click(object sender, EventArgs e)
        {
            try
            {
                if (MainV2.comPort?.BaseStream?.IsOpen == true)
                {
                    var cs = MainV2.comPort.MAV.cs;
                    var owner = FindForm();

                    if (cs.armed)
                    {
                        if (!ModernCommandDialog.ShowConfirmation(owner, "Disarm Vehicle",
                            "This will return the aircraft to a safe state and disable armed mission actions.",
                            "Disarm vehicle", Color.FromArgb(228, 84, 71)))
                            return;

                        MainV2.comPort.doARM(false, false);
                        PushEvent(FlightEventSeverity.Warning, "Disarm requested", "Vehicle disarm command transmitted.");
                    }
                    else
                    {
                        if (!ModernCommandDialog.ShowConfirmation(owner, "Arm Vehicle",
                            "This will arm the aircraft and make guided mission actions available.",
                            "Arm vehicle", Color.FromArgb(72, 182, 132)))
                            return;

                        MainV2.comPort.doARM(true, false);
                        PushEvent(FlightEventSeverity.Success, "Arm requested", "Vehicle arm command transmitted.");
                    }
                }
            }
            catch (Exception ex)
            {
                PushEvent(FlightEventSeverity.Danger, "Command failed", ex.Message);
                ModernCommandDialog.ShowNotice(FindForm(), "Command Error", ex.Message, Color.FromArgb(228, 84, 71));
            }
        }

        private void BtnTakeoff_Click(object sender, EventArgs e)
        {
            try
            {
                var owner = FindForm();
                var altitude = ModernCommandDialog.ShowTakeoffPrompt(owner, 20);

                if (altitude.HasValue)
                {
                    if (MainV2.comPort?.BaseStream?.IsOpen == true)
                    {
                        MainV2.comPort.setMode("GUIDED");
                        MainV2.comPort.doARM(true, false);
                        MainV2.comPort.doCommand(MAVLink.MAV_CMD.TAKEOFF, 0, 0, 0, 0, 0, 0, (float)altitude.Value);
                        PushEvent(FlightEventSeverity.Success, "Guided takeoff requested",
                            $"Takeoff command sent for {altitude.Value:0} m.");
                    }
                }
            }
            catch (Exception ex)
            {
                PushEvent(FlightEventSeverity.Danger, "Takeoff failed", ex.Message);
                ModernCommandDialog.ShowNotice(FindForm(), "Takeoff Error", ex.Message, Color.FromArgb(228, 84, 71));
            }
        }

        private void BtnRTL_Click(object sender, EventArgs e)
        {
            try
            {
                if (MainV2.comPort?.BaseStream?.IsOpen == true)
                {
                    if (!ModernCommandDialog.ShowConfirmation(FindForm(), "Return To Launch",
                        "The aircraft will leave its current guided task and begin the RTL sequence.",
                        "Start RTL", Color.FromArgb(68, 118, 76)))
                        return;

                    MainV2.comPort.setMode("RTL");
                    PushEvent(FlightEventSeverity.Warning, "RTL requested", "Return-to-launch command transmitted.");
                }
            }
            catch (Exception ex)
            {
                PushEvent(FlightEventSeverity.Danger, "RTL failed", ex.Message);
                ModernCommandDialog.ShowNotice(FindForm(), "RTL Error", ex.Message, Color.FromArgb(228, 84, 71));
            }
        }

        private void BtnLand_Click(object sender, EventArgs e)
        {
            try
            {
                if (MainV2.comPort?.BaseStream?.IsOpen == true)
                {
                    if (!ModernCommandDialog.ShowConfirmation(FindForm(), "Land Now",
                        "The aircraft will transition immediately into landing mode at its current position.",
                        "Start landing", Color.FromArgb(122, 58, 52)))
                        return;

                    MainV2.comPort.setMode("LAND");
                    PushEvent(FlightEventSeverity.Warning, "Landing requested", "Landing command transmitted.");
                }
            }
            catch (Exception ex)
            {
                PushEvent(FlightEventSeverity.Danger, "Landing failed", ex.Message);
                ModernCommandDialog.ShowNotice(FindForm(), "Landing Error", ex.Message, Color.FromArgb(228, 84, 71));
            }
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
