using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MissionPlanner.ArduPilot;
using log4net;

namespace MissionPlanner.GCSViews
{
    /// <summary>
    /// Modern Flight Data View - Professional BRIECH UAS HUD Interface
    /// 3-panel layout: Left Telemetry | Center HUD | Right Quick Actions
    /// Dark navy (#1a1f2e) with gold (#c8a865) accents
    /// </summary>
    public partial class ModernFlightData : MyUserControl
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        // Color Constants (matching design specification)
        private static readonly Color DARK_NAVY = Color.FromArgb(26, 31, 46);        // #1a1f2e
        private static readonly Color CHARCOAL = Color.FromArgb(40, 45, 60);         // Darker panels
        private static readonly Color GOLD_ACCENT = Color.FromArgb(201, 169, 97);    // #c9a961
        private static readonly Color LIGHT_GRAY = Color.FromArgb(220, 220, 220);    // Primary text
        private static readonly Color DIM_GRAY = Color.FromArgb(150, 150, 150);      // Secondary text
        private static readonly Color BORDER_GOLD = Color.FromArgb(180, 150, 80);    // Borders

        // Status Colors
        private static readonly Color STATUS_GREEN = Color.FromArgb(76, 175, 80);
        private static readonly Color STATUS_YELLOW = Color.FromArgb(255, 193, 7);
        private static readonly Color STATUS_RED = Color.FromArgb(244, 67, 54);

        // UI Components
        private Panel panelLeft;
        private Panel panelCenter;
        private Panel panelRight;
        private Panel panelTop;
        private Panel panelBottom;

        // HUD Components
        private ArtificialHorizonPanel horizonPanel;
        private CompassHeadingBar compassBar;
        private CompassRose compassRose;
        private SpeedAltitudeTapes speedAltTapes;

        // Telemetry Panels
        private List<TelemetryCard> telemetryCards = new List<TelemetryCard>();

        // Quick Action Buttons
        private List<QuickActionButton> quickActionButtons = new List<QuickActionButton>();

        // Status Bar Components
        private Label labelConnectionStatus;
        private Label labelLinkQuality;
        private Label labelPackets;
        private Label labelRSSI;

        // Update Timer
        private Timer updateTimer;
        private int updateCounter = 0;

        // Constructor
        public ModernFlightData()
        {
            InitializeComponent();
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            DoubleBuffered = true;
        }

        /// <summary>
        /// Initialize all UI components
        /// </summary>
        private void InitializeComponent()
        {
            // Main layout
            this.SuspendLayout();
            this.BackColor = DARK_NAVY;
            this.ForeColor = LIGHT_GRAY;
            this.Dock = DockStyle.Fill;
            this.Font = new Font("Segoe UI", 9f, FontStyle.Regular);

            // Create layout panels
            CreateLayoutPanels();
            CreateHUDComponents();
            CreateTelemetryCards();
            CreateQuickActionButtons();
            CreateStatusBar();

            // Setup update timer (10 Hz)
            updateTimer = new Timer();
            updateTimer.Interval = 100; // 10 Hz
            updateTimer.Tick += UpdateTimer_Tick;
            updateTimer.Start();

            this.ResumeLayout();
        }

        /// <summary>
        /// Create the 3-panel layout structure
        /// </summary>
        private void CreateLayoutPanels()
        {
            // Top Navigation Bar (60px)
            panelTop = new Panel();
            panelTop.Dock = DockStyle.Top;
            panelTop.Height = 60;
            panelTop.BackColor = CHARCOAL;
            panelTop.BorderStyle = BorderStyle.FixedSingle;
            panelTop.Paint += PanelTop_Paint;
            this.Controls.Add(panelTop);

            // Bottom Status Bar (35px)
            panelBottom = new Panel();
            panelBottom.Dock = DockStyle.Bottom;
            panelBottom.Height = 35;
            panelBottom.BackColor = CHARCOAL;
            panelBottom.BorderStyle = BorderStyle.FixedSingle;
            panelBottom.Paint += PanelBottom_Paint;
            this.Controls.Add(panelBottom);

            // Left Panel (Telemetry - 320px)
            panelLeft = new Panel();
            panelLeft.Dock = DockStyle.Left;
            panelLeft.Width = 320;
            panelLeft.BackColor = DARK_NAVY;
            panelLeft.BorderStyle = BorderStyle.FixedSingle;
            panelLeft.AutoScroll = true;
            this.Controls.Add(panelLeft);

            // Right Panel (Quick Actions - 280px)
            panelRight = new Panel();
            panelRight.Dock = DockStyle.Right;
            panelRight.Width = 280;
            panelRight.BackColor = DARK_NAVY;
            panelRight.BorderStyle = BorderStyle.FixedSingle;
            panelRight.AutoScroll = true;
            this.Controls.Add(panelRight);

            // Center Panel (HUD - Flexible)
            panelCenter = new Panel();
            panelCenter.Dock = DockStyle.Fill;
            panelCenter.BackColor = DARK_NAVY;
            panelCenter.BorderStyle = BorderStyle.FixedSingle;
            panelCenter.Paint += PanelCenter_Paint;
            this.Controls.Add(panelCenter);
        }

        /// <summary>
        /// Create central HUD components
        /// </summary>
        private void CreateHUDComponents()
        {
            // Artificial Horizon (center of HUD)
            horizonPanel = new ArtificialHorizonPanel();
            horizonPanel.Location = new Point(
                (panelCenter.Width - 300) / 2,
                (panelCenter.Height - 300) / 2 + 30
            );
            horizonPanel.Size = new Size(300, 300);
            panelCenter.Controls.Add(horizonPanel);

            // Compass Heading Bar (top)
            compassBar = new CompassHeadingBar();
            compassBar.Location = new Point((panelCenter.Width - 400) / 2, 10);
            compassBar.Size = new Size(400, 30);
            panelCenter.Controls.Add(compassBar);

            // Compass Rose (bottom)
            compassRose = new CompassRose();
            compassRose.Location = new Point((panelCenter.Width - 120) / 2, panelCenter.Height - 140);
            compassRose.Size = new Size(120, 120);
            panelCenter.Controls.Add(compassRose);

            // Speed/Altitude Tapes
            speedAltTapes = new SpeedAltitudeTapes();
            speedAltTapes.Location = new Point(0, 30);
            speedAltTapes.Size = new Size(panelCenter.Width, panelCenter.Height - 100);
            panelCenter.Controls.Add(speedAltTapes);
        }

        /// <summary>
        /// Create telemetry cards for left panel
        /// </summary>
        private void CreateTelemetryCards()
        {
            telemetryCards.Clear();

            // Priority order telemetry cards
            var cardConfigs = new[]
            {
                new { Icon = "⬆", Label = "Altitude", Key = "Altitude", Unit = "m" },
                new { Icon = "→", Label = "Speed", Key = "Speed", Unit = "m/s" },
                new { Icon = "🧭", Label = "Heading", Key = "Heading", Unit = "°" },
                new { Icon = "🔋", Label = "Battery", Key = "Battery", Unit = "%" },
                new { Icon = "📡", Label = "GPS", Key = "GPS", Unit = "sats" },
                new { Icon = "📍", Label = "Distance", Key = "Distance", Unit = "m" },
                new { Icon = "⬆⬇", Label = "Vert Speed", Key = "VertSpeed", Unit = "m/s" },
            };

            int yPos = 10;
            foreach (var config in cardConfigs)
            {
                var card = new TelemetryCard
                {
                    Icon = config.Icon,
                    Label = config.Label,
                    Unit = config.Unit,
                    Location = new Point(10, yPos),
                    Size = new Size(panelLeft.Width - 20, 70),
                    BackColor = CHARCOAL,
                    ForeColor = LIGHT_GRAY
                };
                card.BorderColor = BORDER_GOLD;

                panelLeft.Controls.Add(card);
                telemetryCards.Add(card);
                yPos += 80;
            }
        }

        /// <summary>
        /// Create quick action buttons for right panel
        /// </summary>
        private void CreateQuickActionButtons()
        {
            quickActionButtons.Clear();

            var buttonConfigs = new[]
            {
                new { Label = "ARM", Color = STATUS_YELLOW },
                new { Label = "DISARM", Color = STATUS_RED },
                new { Label = "TAKEOFF", Color = STATUS_GREEN },
                new { Label = "LAND", Color = STATUS_RED },
                new { Label = "RTL", Color = STATUS_YELLOW },
                new { Label = "LOITER", Color = GOLD_ACCENT },
                new { Label = "AUTO", Color = STATUS_GREEN },
                new { Label = "SET HOME", Color = GOLD_ACCENT },
            };

            int yPos = 10;
            foreach (var config in buttonConfigs)
            {
                var btn = new QuickActionButton
                {
                    Text = config.Label,
                    Location = new Point(10, yPos),
                    Size = new Size(panelRight.Width - 20, 50),
                    BackColor = config.Color,
                    ForeColor = DARK_NAVY,
                    Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderSize = 2;
                btn.FlatAppearance.BorderColor = GOLD_ACCENT;

                panelRight.Controls.Add(btn);
                quickActionButtons.Add(btn);
                yPos += 60;
            }
        }

        /// <summary>
        /// Create status bar components
        /// </summary>
        private void CreateStatusBar()
        {
            // Connection Status
            labelConnectionStatus = new Label();
            labelConnectionStatus.Text = "● DISCONNECTED";
            labelConnectionStatus.ForeColor = STATUS_RED;
            labelConnectionStatus.Location = new Point(10, 8);
            labelConnectionStatus.Size = new Size(150, 20);
            labelConnectionStatus.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            panelBottom.Controls.Add(labelConnectionStatus);

            // Packets
            labelPackets = new Label();
            labelPackets.Text = "Packets: 0";
            labelPackets.ForeColor = DIM_GRAY;
            labelPackets.Location = new Point(160, 8);
            labelPackets.Size = new Size(100, 20);
            panelBottom.Controls.Add(labelPackets);

            // Link Quality
            labelLinkQuality = new Label();
            labelLinkQuality.Text = "Quality: 0%";
            labelLinkQuality.ForeColor = DIM_GRAY;
            labelLinkQuality.Location = new Point(260, 8);
            labelLinkQuality.Size = new Size(100, 20);
            panelBottom.Controls.Add(labelLinkQuality);

            // RSSI
            labelRSSI = new Label();
            labelRSSI.Text = "RSSI: 0dBm";
            labelRSSI.ForeColor = DIM_GRAY;
            labelRSSI.Location = new Point(360, 8);
            labelRSSI.Size = new Size(100, 20);
            panelBottom.Controls.Add(labelRSSI);

            // Copyright
            var labelCopyright = new Label();
            labelCopyright.Text = "© BRIECH UAS";
            labelCopyright.ForeColor = DIM_GRAY;
            labelCopyright.Dock = DockStyle.Right;
            labelCopyright.TextAlign = ContentAlignment.MiddleRight;
            labelCopyright.Padding = new Padding(0, 0, 10, 0);
            labelCopyright.Font = new Font("Segoe UI", 8f);
            panelBottom.Controls.Add(labelCopyright);
        }

        /// <summary>
        /// Paint handlers for panels
        /// </summary>
        private void PanelTop_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(CHARCOAL);
            // Draw "BRIECH UAS" logo and tabs
            var font = new Font("Segoe UI", 14f, FontStyle.Bold);
            e.Graphics.DrawString("BRIECH UAS", font, new SolidBrush(GOLD_ACCENT), 10, 18);
        }

        private void PanelCenter_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(DARK_NAVY);
            // Draw grid background
            DrawGridBackground(e.Graphics, panelCenter.Size);
        }

        private void PanelBottom_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(CHARCOAL);
            // Draw separator line
            using (var pen = new Pen(BORDER_GOLD, 1))
            {
                e.Graphics.DrawLine(pen, 0, 0, e.ClipRectangle.Width, 0);
            }
        }

        /// <summary>
        /// Draw background grid for HUD
        /// </summary>
        private void DrawGridBackground(Graphics g, Size size)
        {
            const int gridSize = 50;
            using (var pen = new Pen(Color.FromArgb(30, BORDER_GOLD), 1))
            {
                for (int x = 0; x < size.Width; x += gridSize)
                {
                    g.DrawLine(pen, x, 0, x, size.Height);
                }
                for (int y = 0; y < size.Height; y += gridSize)
                {
                    g.DrawLine(pen, 0, y, size.Width, y);
                }
            }
        }

        /// <summary>
        /// Update telemetry data
        /// </summary>
        private void UpdateTelemetry()
        {
            try
            {
                if (MainV2.comPort == null || MainV2.comPort.MAV == null)
                    return;

                var cs = MainV2.comPort.MAV.cs;

                // Update connection status
                if (MainV2.comPort.BaseStream.IsOpen)
                {
                    labelConnectionStatus.Text = "● CONNECTED";
                    labelConnectionStatus.ForeColor = STATUS_GREEN;
                }
                else
                {
                    labelConnectionStatus.Text = "● DISCONNECTED";
                    labelConnectionStatus.ForeColor = STATUS_RED;
                }

                // Update telemetry values
                for (int i = 0; i < telemetryCards.Count; i++)
                {
                    var card = telemetryCards[i];
                    switch (i)
                    {
                        case 0: // Altitude
                            card.Value = cs.alt.ToString("F1");
                            break;
                        case 1: // Speed
                            card.Value = cs.groundspeed.ToString("F1");
                            break;
                        case 2: // Heading
                            card.Value = cs.yaw.ToString("F0");
                            break;
                        case 3: // Battery
                            int batteryPercent = (int)cs.battery_remaining;
                            card.Value = batteryPercent.ToString();
                            card.HighlightColor = batteryPercent < 30 ? STATUS_RED : STATUS_GREEN;
                            break;
                        case 4: // GPS
                            card.Value = ((int)cs.satcount).ToString();
                            break;
                        case 5: // Distance to Home
                            card.Value = ((int)cs.wp_dist).ToString();
                            break;
                        case 6: // Vertical Speed
                            card.Value = cs.verticalspeed.ToString("F1");
                            break;
                    }
                }

                // Update link stats
                labelPackets.Text = $"Packets: 0";
                labelLinkQuality.Text = $"Quality: {MainV2.comPort.MAV.cs.linkqualitygcs}%";
                labelRSSI.Text = $"RSSI: {MainV2.comPort.MAV.cs.rssi}dBm";

                // Update HUD components
                horizonPanel?.UpdateAttitude(cs.roll, cs.pitch);
                compassBar?.UpdateHeading(cs.yaw);
                compassRose?.UpdateHeading(cs.yaw);
                speedAltTapes?.UpdateValues(cs.groundspeed, cs.alt, cs.verticalspeed);
            }
            catch (Exception ex)
            {
                log.Error("Telemetry update error: " + ex.Message);
            }
        }

        /// <summary>
        /// Timer tick for updates
        /// </summary>
        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            updateCounter++;
            if (updateCounter % 10 == 0) // Every 1000ms
            {
                UpdateTelemetry();
                Invalidate();
            }
        }

        /// <summary>
        /// Cleanup
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

    /// <summary>
    /// Artificial Horizon Panel - Shows pitch/roll attitude
    /// </summary>
    public class ArtificialHorizonPanel : Control
    {
        private float roll = 0;
        private float pitch = 0;
        private static readonly Color SKY_COLOR = Color.FromArgb(0, 100, 200);
        private static readonly Color GROUND_COLOR = Color.FromArgb(139, 69, 19);
        private static readonly Color GOLD = Color.FromArgb(201, 169, 97);

        public void UpdateAttitude(float roll, float pitch)
        {
            this.roll = roll;
            this.pitch = pitch;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawArtificialHorizon(e.Graphics);
        }

        private void DrawArtificialHorizon(Graphics g)
        {
            g.Clear(Color.FromArgb(26, 31, 46));
            int centerX = Width / 2;
            int centerY = Height / 2;

            // Draw circular background
            using (var brush = new SolidBrush(Color.Black))
            {
                g.FillEllipse(brush, 10, 10, Width - 20, Height - 20);
            }

            // Save graphics state for rotation
            var state = g.Save();

            // Transform to center and apply roll rotation
            g.TranslateTransform(centerX, centerY);
            g.RotateTransform(roll);
            g.TranslateTransform(-centerX, -centerY);

            // Draw sky (top half)
            using (var brush = new SolidBrush(SKY_COLOR))
            {
                g.FillRectangle(brush, 0, (int)(centerY - pitch * 2), Width, centerY);
            }

            // Draw ground (bottom half)
            using (var brush = new SolidBrush(GROUND_COLOR))
            {
                g.FillRectangle(brush, 0, (int)(centerY - pitch * 2) + centerY, Width, centerY);
            }

            // Draw horizon line
            using (var pen = new Pen(GOLD, 2))
            {
                g.DrawLine(pen, 0, centerY, Width, centerY);
            }

            // Restore graphics state
            g.Restore(state);

            // Draw aircraft symbol (red diamond in center)
            DrawAircraftSymbol(g, centerX, centerY);

            // Draw pitch ladder
            DrawPitchLadder(g, centerX, centerY);

            // Draw roll indicator arc
            DrawRollIndicator(g, centerX, centerY);

            // Draw circular border
            using (var pen = new Pen(GOLD, 2))
            {
                g.DrawEllipse(pen, 10, 10, Width - 20, Height - 20);
            }
        }

        private void DrawAircraftSymbol(Graphics g, int cx, int cy)
        {
            var points = new Point[]
            {
                new Point(cx, cy - 5),     // Top
                new Point(cx + 8, cy),     // Right
                new Point(cx, cy + 5),     // Bottom
                new Point(cx - 8, cy)      // Left
            };
            using (var brush = new SolidBrush(Color.Red))
            {
                g.FillPolygon(brush, points);
            }
        }

        private void DrawPitchLadder(Graphics g, int cx, int cy)
        {
            using (var pen = new Pen(GOLD, 1))
            {
                for (int i = -60; i <= 60; i += 10)
                {
                    int y = cy - (int)(i * 2);
                    if (i % 30 == 0)
                    {
                        g.DrawLine(pen, cx - 30, y, cx + 30, y);
                    }
                    else
                    {
                        g.DrawLine(pen, cx - 15, y, cx + 15, y);
                    }
                }
            }
        }

        private void DrawRollIndicator(Graphics g, int cx, int cy)
        {
            // Draw roll scale at top
            using (var pen = new Pen(GOLD, 1))
            {
                for (int angle = -60; angle <= 60; angle += 10)
                {
                    double rad = angle * Math.PI / 180;
                    int x1 = (int)(cx + 80 * Math.Sin(rad));
                    int y1 = (int)(cy - 80 * Math.Cos(rad));
                    int x2 = (int)(cx + 90 * Math.Sin(rad));
                    int y2 = (int)(cy - 90 * Math.Cos(rad));
                    g.DrawLine(pen, x1, y1, x2, y2);
                }
            }

            // Draw roll pointer
            using (var pen = new Pen(Color.Red, 3))
            {
                double rad = roll * Math.PI / 180;
                int x = (int)(cx + 95 * Math.Sin(rad));
                int y = (int)(cy - 95 * Math.Cos(rad));
                g.DrawLine(pen, cx, cy, x, y);
            }
        }
    }

    /// <summary>
    /// Compass Heading Bar
    /// </summary>
    public class CompassHeadingBar : Control
    {
        private float heading = 0;
        private static readonly Color GOLD = Color.FromArgb(201, 169, 97);

        public void UpdateHeading(float heading)
        {
            this.heading = heading;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Color.FromArgb(40, 45, 60));

            DrawCompassBar(e.Graphics);
        }

        private void DrawCompassBar(Graphics g)
        {
            int centerX = Width / 2;
            int centerY = Height / 2;
            int barWidth = 80;

            // Draw background
            using (var brush = new SolidBrush(Color.FromArgb(26, 31, 46)))
            {
                g.FillRectangle(brush, centerX - barWidth, 0, barWidth * 2, Height);
            }

            // Draw heading values
            string[] headings = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int[] angles = { 0, 45, 90, 135, 180, 225, 270, 315 };

            using (var brush = new SolidBrush(Color.FromArgb(150, 150, 150)))
            using (var font = new Font("Arial", 8))
            {
                for (int i = 0; i < headings.Length; i++)
                {
                    float offset = (angles[i] - heading + 360) % 360;
                    if (offset > 180) offset -= 360;
                    
                    int x = (int)(centerX + offset * barWidth / 60);
                    if (Math.Abs(offset) < 60)
                    {
                        g.DrawString(headings[i], font, brush, x - 8, centerY - 8);
                    }
                }
            }

            // Draw current heading indicator (center)
            using (var pen = new Pen(GOLD, 2))
            using (var brush = new SolidBrush(GOLD))
            {
                var triangle = new Point[]
                {
                    new Point(centerX - 5, Height - 8),
                    new Point(centerX + 5, Height - 8),
                    new Point(centerX, Height - 2)
                };
                g.FillPolygon(brush, triangle);
            }

            // Draw heading value
            using (var font = new Font("Arial", 12, FontStyle.Bold))
            using (var brush = new SolidBrush(GOLD))
            {
                g.DrawString(heading.ToString("F0") + "°", font, brush, centerX - 15, 2);
            }
        }
    }

    /// <summary>
    /// Compass Rose
    /// </summary>
    public class CompassRose : Control
    {
        private float heading = 0;
        private static readonly Color GOLD = Color.FromArgb(201, 169, 97);

        public void UpdateHeading(float heading)
        {
            this.heading = heading;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawCompassRose(e.Graphics);
        }

        private void DrawCompassRose(Graphics g)
        {
            int cx = Width / 2;
            int cy = Height / 2;
            int radius = Math.Min(Width, Height) / 2 - 5;

            // Draw background circle
            using (var brush = new SolidBrush(Color.FromArgb(40, 45, 60)))
            {
                g.FillEllipse(brush, cx - radius, cy - radius, radius * 2, radius * 2);
            }

            // Draw compass rose
            using (var pen = new Pen(GOLD, 1))
            {
                for (int angle = 0; angle < 360; angle += 45)
                {
                    double rad = (angle - heading) * Math.PI / 180;
                    int x1 = (int)(cx + radius * 0.8 * Math.Sin(rad));
                    int y1 = (int)(cy - radius * 0.8 * Math.Cos(rad));
                    int x2 = (int)(cx + radius * Math.Sin(rad));
                    int y2 = (int)(cy - radius * Math.Cos(rad));
                    g.DrawLine(pen, x1, y1, x2, y2);
                }
            }

            // Draw circle border
            using (var pen = new Pen(GOLD, 1))
            {
                g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
            }

            // Draw north indicator (gold needle)
            using (var pen = new Pen(GOLD, 3))
            {
                int x = (int)(cx + radius * 0.6 * Math.Sin(0));
                int y = (int)(cy - radius * 0.6 * Math.Cos(0));
                g.DrawLine(pen, cx, cy, x, y);
            }
        }
    }

    /// <summary>
    /// Speed and Altitude Tapes
    /// </summary>
    public class SpeedAltitudeTapes : Control
    {
        private float speed = 0;
        private float altitude = 0;
        private float verticalSpeed = 0;
        private static readonly Color GOLD = Color.FromArgb(201, 169, 97);

        public void UpdateValues(float speed, float altitude, float verticalSpeed)
        {
            this.speed = speed;
            this.altitude = altitude;
            this.verticalSpeed = verticalSpeed;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawTapes(e.Graphics);
        }

        private void DrawTapes(Graphics g)
        {
            int tapeWidth = 60;
            int leftX = 10;
            int rightX = Width - tapeWidth - 10;
            int tapeHeight = Height - 20;

            // Draw speed tape (left)
            DrawSpeedTape(g, leftX, 10, tapeWidth, tapeHeight);

            // Draw altitude tape (right)
            DrawAltitudeTape(g, rightX, 10, tapeWidth, tapeHeight);
        }

        private void DrawSpeedTape(Graphics g, int x, int y, int width, int height)
        {
            // Draw tape background
            using (var brush = new SolidBrush(Color.FromArgb(40, 45, 60)))
            {
                g.FillRectangle(brush, x, y, width, height);
            }

            // Draw speed scale
            int centerY = y + height / 2;
            using (var pen = new Pen(GOLD, 1))
            using (var font = new Font("Arial", 8))
            using (var brush = new SolidBrush(Color.FromArgb(150, 150, 150)))
            {
                for (int i = (int)speed - 20; i <= speed + 20; i += 5)
                {
                    int offset = (int)((i - speed) * 3);
                    int markY = centerY + offset;
                    if (markY > y && markY < y + height)
                    {
                        g.DrawLine(pen, x, markY, x + width / 2, markY);
                        if (i % 10 == 0)
                        {
                            g.DrawString(i.ToString(), font, brush, x + 5, markY - 8);
                        }
                    }
                }
            }

            // Draw speed pointer (gold triangle)
            using (var brush = new SolidBrush(GOLD))
            {
                var triangle = new Point[]
                {
                    new Point(x + width, centerY - 5),
                    new Point(x + width, centerY + 5),
                    new Point(x + width - 8, centerY)
                };
                g.FillPolygon(brush, triangle);
            }
        }

        private void DrawAltitudeTape(Graphics g, int x, int y, int width, int height)
        {
            // Draw tape background
            using (var brush = new SolidBrush(Color.FromArgb(40, 45, 60)))
            {
                g.FillRectangle(brush, x, y, width, height);
            }

            // Draw altitude scale
            int centerY = y + height / 2;
            using (var pen = new Pen(GOLD, 1))
            using (var font = new Font("Arial", 8))
            using (var brush = new SolidBrush(Color.FromArgb(150, 150, 150)))
            {
                for (int i = (int)altitude - 200; i <= altitude + 200; i += 50)
                {
                    int offset = (int)((i - altitude) * 0.15f);
                    int markY = centerY + offset;
                    if (markY > y && markY < y + height)
                    {
                        g.DrawLine(pen, x + width / 2, markY, x + width, markY);
                        if (i % 100 == 0 && i >= 0)
                        {
                            g.DrawString(i.ToString(), font, brush, x + 5, markY - 8);
                        }
                    }
                }
            }

            // Draw altitude pointer (gold triangle)
            using (var brush = new SolidBrush(GOLD))
            {
                var triangle = new Point[]
                {
                    new Point(x, centerY - 5),
                    new Point(x, centerY + 5),
                    new Point(x + 8, centerY)
                };
                g.FillPolygon(brush, triangle);
            }
        }
    }

    /// <summary>
    /// Telemetry Card Component
    /// </summary>
    public class TelemetryCard : Control
    {
        public string Icon { get; set; } = "●";
        public string Label { get; set; } = "Value";
        public string Value { get; set; } = "0";
        public string Unit { get; set; } = "unit";
        public Color BorderColor { get; set; } = Color.FromArgb(180, 150, 80);
        public Color HighlightColor { get; set; } = Color.Transparent;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // Draw border
            using (var pen = new Pen(BorderColor, 2))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }

            // Draw highlight background if set
            if (HighlightColor != Color.Transparent)
            {
                using (var brush = new SolidBrush(Color.FromArgb(80, HighlightColor)))
                {
                    e.Graphics.FillRectangle(brush, 0, 0, Width, Height);
                }
            }

            // Draw icon
            using (var font = new Font("Arial", 16))
            using (var brush = new SolidBrush(BorderColor))
            {
                e.Graphics.DrawString(Icon, font, brush, 8, 8);
            }

            // Draw label
            using (var font = new Font("Segoe UI", 9, FontStyle.Regular))
            using (var brush = new SolidBrush(Color.FromArgb(150, 150, 150)))
            {
                e.Graphics.DrawString(Label, font, brush, 35, 8);
            }

            // Draw value
            using (var font = new Font("Segoe UI", 14, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.FromArgb(220, 220, 220)))
            {
                var valueText = $"{Value} {Unit}";
                e.Graphics.DrawString(valueText, font, brush, 35, 25);
            }
        }
    }

    /// <summary>
    /// Quick Action Button
    /// </summary>
    public class QuickActionButton : Button
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);

            // Draw border
            using (var pen = new Pen(FlatAppearance.BorderColor, FlatAppearance.BorderSize))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }

            // Draw text
            var stringFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            using (var brush = new SolidBrush(ForeColor))
            {
                e.Graphics.DrawString(Text, Font, brush, ClientRectangle, stringFormat);
            }
        }
    }
}
