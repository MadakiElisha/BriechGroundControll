using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews
{
    /// <summary>
    /// Flight Data View Controller - Main HUD interface
    /// Implements: FlightDataView.tsx functionality
    /// 3-Panel layout: Left Telemetry | Center HUD | Right Quick Actions
    /// Uses HUD components from ModernFlightData.cs
    /// </summary>
    public class FlightDataViewController : MyUserControl
    {
        // Events
        public event EventHandler<FlightCommandEventArgs> FlightCommandRequested;
        public event EventHandler<FlightModeChangedEventArgs> FlightModeChanged;

        // Layout Panels
        private Panel panelLeft;
        private Panel panelCenter;
        private Panel panelRight;

        // HUD Components (Center) - using existing classes from ModernFlightData.cs
        private ArtificialHorizonPanel horizonPanel;
        private CompassHeadingBar compassBar;
        private SpeedAltitudeTapes speedAltTapes;
        private Label labelFlightMode;

        // Telemetry Cards (Left)
        private List<TelemetryCard> telemetryCards = new List<TelemetryCard>();

        // Quick Actions (Right)
        private List<Button> quickActionButtons = new List<Button>();
        private ComboBox comboFlightModes;

        // Current telemetry
        private TelemetryData currentTelemetry;
        private bool isConnected = false;

        public FlightDataViewController()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialize all components
        /// </summary>
        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.BackColor = BriechTheme.BRIECH_DARK;
            this.ForeColor = BriechTheme.TEXT_PRIMARY;
            this.Dock = DockStyle.Fill;
            this.Font = new Font("Segoe UI", 9f);

            // Create layout
            CreateLayoutPanels();
            CreateHUDComponents();
            CreateTelemetryCards();
            CreateQuickActionButtons();

            currentTelemetry = new TelemetryData();

            this.ResumeLayout();
        }

        /// <summary>
        /// Create the 3-panel layout
        /// </summary>
        private void CreateLayoutPanels()
        {
            // Left Panel (Telemetry - 320px)
            panelLeft = new Panel();
            panelLeft.Dock = DockStyle.Left;
            panelLeft.Width = 320;
            panelLeft.BackColor = BriechTheme.BRIECH_DARK;
            panelLeft.BorderStyle = BorderStyle.FixedSingle;
            panelLeft.AutoScroll = true;
            this.Controls.Add(panelLeft);

            // Right Panel (Quick Actions - 280px)
            panelRight = new Panel();
            panelRight.Dock = DockStyle.Right;
            panelRight.Width = 280;
            panelRight.BackColor = BriechTheme.BRIECH_DARK;
            panelRight.BorderStyle = BorderStyle.FixedSingle;
            panelRight.AutoScroll = true;
            this.Controls.Add(panelRight);

            // Center Panel (HUD - Flexible)
            panelCenter = new Panel();
            panelCenter.Dock = DockStyle.Fill;
            panelCenter.BackColor = BriechTheme.BRIECH_DARK;
            panelCenter.BorderStyle = BorderStyle.FixedSingle;
            panelCenter.Paint += PanelCenter_Paint;
            this.Controls.Add(panelCenter);
        }

        /// <summary>
        /// Create central HUD components
        /// </summary>
        private void CreateHUDComponents()
        {
            // Artificial Horizon (center)
            horizonPanel = new ArtificialHorizonPanel();
            horizonPanel.Size = new Size(300, 300);
            horizonPanel.Location = new Point(
                (panelCenter.Width - 300) / 2,
                (panelCenter.Height - 300) / 2 + 20
            );
            panelCenter.Controls.Add(horizonPanel);

            // Compass Heading Bar (top center)
            compassBar = new CompassHeadingBar();
            compassBar.Size = new Size(400, 30);
            compassBar.Location = new Point((panelCenter.Width - 400) / 2, 10);
            panelCenter.Controls.Add(compassBar);

            // Flight Mode Display (top right of center)
            labelFlightMode = new Label();
            labelFlightMode.Text = "MODE: STABILIZE";
            labelFlightMode.ForeColor = BriechTheme.BRIECH_GOLD;
            labelFlightMode.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            labelFlightMode.Location = new Point(panelCenter.Width - 250, 10);
            labelFlightMode.Size = new Size(240, 25);
            labelFlightMode.AutoSize = false;
            labelFlightMode.TextAlign = ContentAlignment.MiddleRight;
            panelCenter.Controls.Add(labelFlightMode);

            // Speed/Altitude Tapes (sides)
            speedAltTapes = new SpeedAltitudeTapes();
            speedAltTapes.Size = new Size(panelCenter.Width, panelCenter.Height);
            speedAltTapes.Location = new Point(0, 0);
            panelCenter.Controls.Add(speedAltTapes);
        }

        /// <summary>
        /// Create telemetry cards for left panel
        /// </summary>
        private void CreateTelemetryCards()
        {
            telemetryCards.Clear();

            var cardConfigs = new[]
            {
                new { Icon = "⬆", Label = "Altitude", Key = "Altitude", Unit = "m" },
                new { Icon = "→", Label = "Speed", Key = "Speed", Unit = "m/s" },
                new { Icon = "🧭", Label = "Heading", Key = "Heading", Unit = "°" },
                new { Icon = "🔋", Label = "Battery", Key = "Battery", Unit = "%" },
                new { Icon = "📡", Label = "GPS", Key = "GPS", Unit = "sats" },
                new { Icon = "📍", Label = "Distance", Key = "Distance", Unit = "m" },
                new { Icon = "↕", Label = "V-Speed", Key = "VertSpeed", Unit = "m/s" },
            };

            int yPos = 10;
            foreach (var config in cardConfigs)
            {
                var card = new TelemetryCard
                {
                    Icon = config.Icon,
                    Label = config.Label,
                    Unit = config.Unit,
                    Value = "0",
                    Location = new Point(10, yPos),
                    Size = new Size(panelLeft.Width - 20, 70),
                    BackColor = BriechTheme.CHARCOAL,
                    ForeColor = BriechTheme.TEXT_PRIMARY,
                    BorderColor = BriechTheme.BORDER_GOLD
                };

                panelLeft.Controls.Add(card);
                telemetryCards.Add(card);
                yPos += 80;
            }
        }

        /// <summary>
        /// Create quick action buttons and flight mode selector
        /// </summary>
        private void CreateQuickActionButtons()
        {
            quickActionButtons.Clear();
            int yPos = 10;

            // Flight Mode Selector
            Label labelMode = new Label();
            labelMode.Text = "Flight Mode";
            labelMode.ForeColor = BriechTheme.TEXT_SECONDARY;
            labelMode.Location = new Point(10, yPos);
            labelMode.Size = new Size(panelRight.Width - 20, 20);
            labelMode.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            panelRight.Controls.Add(labelMode);

            yPos += 25;

            comboFlightModes = new ComboBox();
            comboFlightModes.Location = new Point(10, yPos);
            comboFlightModes.Size = new Size(panelRight.Width - 20, 25);
            comboFlightModes.BackColor = BriechTheme.DARK_NAVY;
            comboFlightModes.ForeColor = BriechTheme.TEXT_PRIMARY;
            comboFlightModes.DropDownStyle = ComboBoxStyle.DropDownList;
            comboFlightModes.Items.AddRange(new object[]
            {
                FlightMode.STABILIZE,
                FlightMode.ALT_HOLD,
                FlightMode.LOITER,
                FlightMode.AUTO,
                FlightMode.GUIDED,
                FlightMode.RTL,
                FlightMode.LAND,
                FlightMode.SPORT,
                FlightMode.POSHOLD
            });
            comboFlightModes.SelectedIndex = 0;
            comboFlightModes.SelectedIndexChanged += (sender, e) =>
            {
                if (comboFlightModes.SelectedItem is FlightMode mode)
                {
                    FlightModeChanged?.Invoke(this, new FlightModeChangedEventArgs
                    {
                        NewMode = mode,
                        PreviousMode = currentTelemetry.Mode
                    });
                }
            };
            panelRight.Controls.Add(comboFlightModes);

            yPos += 35;

            // Quick Action Buttons
            var buttonConfigs = new[]
            {
                new { Label = "ARM", Color = BriechTheme.BTN_ARM, Command = FlightCommand.Arm },
                new { Label = "DISARM", Color = BriechTheme.BTN_DISARM, Command = FlightCommand.Disarm },
                new { Label = "TAKEOFF", Color = BriechTheme.BTN_TAKEOFF, Command = FlightCommand.Takeoff },
                new { Label = "LAND", Color = BriechTheme.BTN_LAND, Command = FlightCommand.Land },
                new { Label = "RTL", Color = BriechTheme.BTN_RTL, Command = FlightCommand.ReturnToLaunch },
                new { Label = "LOITER", Color = BriechTheme.BTN_LOITER, Command = FlightCommand.Loiter },
                new { Label = "AUTO", Color = BriechTheme.BTN_AUTO, Command = FlightCommand.Auto },
                new { Label = "SET HOME", Color = BriechTheme.BTN_SET_HOME, Command = FlightCommand.SetHome },
            };

            foreach (var config in buttonConfigs)
            {
                var btn = new Button();
                btn.Text = config.Label;
                btn.Location = new Point(10, yPos);
                btn.Size = new Size(panelRight.Width - 20, 50);
                btn.BackColor = config.Color;
                btn.ForeColor = BriechTheme.DARK_NAVY;
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderSize = 2;
                btn.FlatAppearance.BorderColor = BriechTheme.BRIECH_GOLD;
                btn.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
                btn.Cursor = Cursors.Hand;

                var command = config.Command;
                btn.Click += (sender, e) =>
                {
                    FlightCommandRequested?.Invoke(this, new FlightCommandEventArgs
                    {
                        Command = command,
                        Description = config.Label
                    });
                };

                panelRight.Controls.Add(btn);
                quickActionButtons.Add(btn);
                yPos += 55;
            }
        }

        /// <summary>
        /// Paint center panel with grid background
        /// </summary>
        private void PanelCenter_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(BriechTheme.BRIECH_DARK);
            DrawGridBackground(e.Graphics, panelCenter.Size);
        }

        /// <summary>
        /// Draw background grid
        /// </summary>
        private void DrawGridBackground(Graphics g, Size size)
        {
            const int gridSize = 50;
            using (var pen = new Pen(BriechTheme.GRID_COLOR, 1))
            {
                for (int x = 0; x < size.Width; x += gridSize)
                    g.DrawLine(pen, x, 0, x, size.Height);
                for (int y = 0; y < size.Height; y += gridSize)
                    g.DrawLine(pen, 0, y, size.Width, y);
            }
        }

        /// <summary>
        /// Update telemetry data and refresh display
        /// </summary>
        public void UpdateTelemetry(TelemetryData telemetry)
        {
            currentTelemetry = telemetry.Clone();

            // Update HUD components
            horizonPanel?.UpdateAttitude((float)telemetry.Roll, (float)telemetry.Pitch);
            compassBar?.UpdateHeading((float)telemetry.Heading);
            speedAltTapes?.UpdateValues((float)telemetry.Speed, (float)telemetry.Altitude, (float)telemetry.VerticalSpeed);

            // Update flight mode display
            labelFlightMode.Text = $"MODE: {telemetry.Mode}";
            if (!comboFlightModes.Items.Contains(telemetry.Mode))
            {
                comboFlightModes.SelectedIndex = 0;
            }

            // Update telemetry cards
            if (telemetryCards.Count >= 7)
            {
                telemetryCards[0].Value = telemetry.Altitude.ToString("F1");
                telemetryCards[1].Value = telemetry.Speed.ToString("F1");
                telemetryCards[2].Value = telemetry.Heading.ToString("F0");
                telemetryCards[3].Value = telemetry.Battery.ToString("F0");
                telemetryCards[3].HighlightColor = BriechTheme.GetBatteryColor(telemetry.Battery);
                telemetryCards[4].Value = telemetry.Satellites.ToString();
                telemetryCards[5].Value = telemetry.Distance.ToString("F0");
                telemetryCards[6].Value = telemetry.VerticalSpeed.ToString("F2");
            }

            // Invalidate for redraw
            this.Invalidate();
        }

        /// <summary>
        /// Update connection status
        /// </summary>
        public void UpdateConnectionStatus(bool connected)
        {
            isConnected = connected;
        }

        /// <summary>
        /// Get current telemetry snapshot
        /// </summary>
        public TelemetryData GetCurrentTelemetry()
        {
            return currentTelemetry.Clone();
        }

        /// <summary>
        /// Cleanup
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                panelLeft?.Dispose();
                panelCenter?.Dispose();
                panelRight?.Dispose();
                horizonPanel?.Dispose();
                compassBar?.Dispose();
                speedAltTapes?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
