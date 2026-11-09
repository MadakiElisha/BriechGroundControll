using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using log4net;
using MissionPlanner.ArduPilot;
using MissionPlanner.Controls;
using MissionPlanner.Controls.Fly;
using MissionPlanner.Utilities;
using T = MissionPlanner.Utilities.DesignTokens;
using Family = MissionPlanner.ArduPilot.FlightCommandProfile.Family;

namespace MissionPlanner.GCSViews
{
    /// <summary>
    /// The redesigned Fly screen (design canvas board 1, "Fly — night", plus board 2 sunlight mode).
    /// Left: attitude display and four readouts. Centre: the live map. Right: command console with
    /// vehicle-aware flight modes (confirmed from the heartbeat), mission controls, messages and the
    /// hold-to-disarm emergency control. Top: flight status bar and alert banner. Bottom: health chips.
    /// All commands go through the shared FlightCommands helpers, so the safety rules are the same everywhere.
    /// </summary>
    [PreventTheming] // styled entirely by DesignTokens; the legacy theme would recolour its controls
    public class FlyView : MyUserControl, IActivate, IDeactivate
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(FlyView));

        public const string PreferenceKey = "fly_view_design"; // "new" (default) or "previous"

        private readonly Timer uiTimer = new Timer { Interval = 200 };

        // status bar
        private Panel statusBar;
        private Label lblVehicle, lblMode, lblLink;
        private TokenButton btnSunlight, btnPrevious;

        private AlertBanner banner;
        private string acknowledgedAlert;

        // left
        private AttitudeIndicator adi;
        private ReadoutCard cardBattery, cardHome, cardMission, cardTime;

        // centre
        private PanelMap3DDeck map;

        // right
        private TokenCard modesCard, missionCard, messagesCard;
        private TableLayoutPanel modesGrid;
        private Label modeStatus;
        private Family modesFor = (Family)(-1);
        private readonly List<TokenButton> modeButtons = new List<TokenButton>();
        private TokenButton btnPause, btnSkip, btnResume, btnSetSpeed, btnDisarm;
        private NumericUpDown speedInput;
        private Label speedUnit, speedLabel;
        private Label[] messageLines;
        private LinkLabel allMessages;

        // footer
        private FlowLayoutPanel footer;
        private HealthChip chipGps, chipEkf, chipBattery, chipFence, chipAirspeed, chipParams;
        private Label lblLogging;

        private bool commandBusy;
        private TableLayoutPanel main, consoleColumn;
        private int modeRows = 3;

        protected override void OnLayout(LayoutEventArgs e)
        {
            ApplyResponsiveLayout();
            base.OnLayout(e);
        }

        /// <summary>
        /// Fit 14-inch laptops (about 1280 x 650 usable) up to large shared displays: side columns scale with the
        /// width within limits, and on short screens the console drops the message list (still one tap away)
        /// before anything is clipped.
        /// </summary>
        private void ApplyResponsiveLayout()
        {
            if (main == null || consoleColumn == null)
                return;
            var w = Width;
            var left = Math.Max(280, Math.Min(460, (int)(w * 0.26)));
            var right = Math.Max(300, Math.Min(380, (int)(w * 0.25)));
            if (main.ColumnStyles[0].Width != left)
                main.ColumnStyles[0].Width = left;
            if (main.ColumnStyles[2].Width != right)
                main.ColumnStyles[2].Width = right;

            var h = consoleColumn.Height;
            var modeHeight = 40 + modeRows * (T.ModeButtonHeight + T.Space2) + 30;
            var missionHeight = 176f;
            var disarmHeight = 0f; // pinned outside the scrolling column
            var showMessages = h >= modeHeight + missionHeight + disarmHeight + 140;
            consoleColumn.RowStyles[0].Height = modeHeight;
            consoleColumn.RowStyles[1].Height = missionHeight;
            consoleColumn.RowStyles[2].SizeType = showMessages ? SizeType.Percent : SizeType.Absolute;
            consoleColumn.RowStyles[2].Height = showMessages ? 100f : 0f;
            messagesCard.Visible = showMessages;
            consoleColumn.RowStyles[3].Height = disarmHeight;
        }

        public FlyView()
        {
            T.LoadSavedMode();
            DoubleBuffered = true;
            Dock = DockStyle.Fill;
            BuildLayout();
            ApplyTokens();
            T.Changed += (s, e) => { if (!IsDisposed) ApplyTokens(); };
            uiTimer.Tick += (s, e) => UpdateUi();
        }

        #region layout

        private void BuildLayout()
        {
            SuspendLayout();

            // ---- status bar ----
            statusBar = new Panel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(16, 6, 12, 6) };
            var statusFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent };
            lblVehicle = new Label { AutoSize = true, Font = T.Label, Margin = new Padding(0, 13, 18, 0) };
            lblMode = new Label { AutoSize = true, Font = T.MonoStrong, Margin = new Padding(0, 9, 18, 0) };
            lblLink = new Label { AutoSize = true, Font = T.Detail, Margin = new Padding(0, 12, 18, 0) };
            statusFlow.Controls.AddRange(new Control[] { lblVehicle, lblMode, lblLink });
            var statusRight = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Color.Transparent };
            btnSunlight = new TokenButton { Width = 120, Height = 40, Text = "Sunlight", Margin = new Padding(0, 0, 8, 0) };
            btnSunlight.Click += (s, e) => T.ToggleMode();
            btnPrevious = new TokenButton { Width = 150, Height = 40, Text = "Previous layout", Style = TokenButton.Kind.Quiet, Margin = new Padding(0) };
            btnPrevious.Click += (s, e) => SwitchToPrevious();
            statusRight.Controls.Add(btnSunlight);
            statusRight.Controls.Add(btnPrevious);
            statusBar.Controls.Add(statusFlow);
            statusBar.Controls.Add(statusRight);

            banner = new AlertBanner { Dock = DockStyle.Top };
            banner.Acknowledged += (s, e) => acknowledgedAlert = currentAlertKey;
            banner.ActionClicked += async (s, e) =>
            {
                var mode = FlightCommandProfile.ReturnMode(FlightCommands.CurrentFamily());
                if (System.CustomMessageBox.Show($"Switch to {mode} now?", mode, System.CustomMessageBox.MessageBoxButtons.YesNo,
                        System.CustomMessageBox.MessageBoxIcon.Warning, mode, "Cancel") == System.CustomMessageBox.DialogResult.Yes)
                    await SendModeAsync(mode);
            };

            // ---- main grid ----
            main = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(T.Space3, T.Space3, T.Space3, 0),
                BackColor = Color.Transparent
            };
            // side columns get a share of the width within limits; the map takes the rest (see ApplyResponsiveLayout)
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340f));
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330f));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            main.Controls.Add(BuildLeftColumn(), 0, 0);

            map = new PanelMap3DDeck { Dock = DockStyle.Fill, Margin = new Padding(T.Space3, 0, T.Space3, 0), Padding = new Padding(0) };
            main.Controls.Add(map, 1, 0);

            main.Controls.Add(BuildConsole(), 2, 0);

            // ---- footer ----
            footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                Padding = new Padding(T.Space3, T.Space2 + 2, T.Space3, 0),
                WrapContents = false,
                BackColor = Color.Transparent
            };
            chipGps = new HealthChip();
            chipEkf = new HealthChip();
            chipBattery = new HealthChip();
            chipFence = new HealthChip();
            chipAirspeed = new HealthChip();
            chipParams = new HealthChip();
            lblLogging = new Label { AutoSize = true, Font = T.Detail, Margin = new Padding(T.Space2, 8, 0, 0) };
            footer.Controls.AddRange(new Control[] { chipGps, chipEkf, chipBattery, chipFence, chipAirspeed, chipParams, lblLogging });

            Controls.Add(main);
            Controls.Add(footer);
            Controls.Add(banner);
            Controls.Add(statusBar);
            ResumeLayout(true);
        }

        private Control BuildLeftColumn()
        {
            var col = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };
            col.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            col.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            col.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            col.RowStyles.Add(new RowStyle(SizeType.Absolute, 104f));
            col.RowStyles.Add(new RowStyle(SizeType.Absolute, 104f));

            adi = new AttitudeIndicator { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, T.Space3) };
            col.Controls.Add(adi, 0, 0);
            col.SetColumnSpan(adi, 2);

            cardBattery = new ReadoutCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, T.Space1, T.Space2) };
            cardHome = new ReadoutCard { Dock = DockStyle.Fill, Margin = new Padding(T.Space1, 0, 0, T.Space2) };
            cardMission = new ReadoutCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, T.Space1, 0) };
            cardTime = new ReadoutCard { Dock = DockStyle.Fill, Margin = new Padding(T.Space1, 0, 0, 0) };
            col.Controls.Add(cardBattery, 0, 1);
            col.Controls.Add(cardHome, 1, 1);
            col.Controls.Add(cardMission, 0, 2);
            col.Controls.Add(cardTime, 1, 2);
            return col;
        }

        private Control BuildConsole()
        {
            var col = consoleColumn = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };
            col.RowStyles.Add(new RowStyle(SizeType.Absolute, 252f));
            col.RowStyles.Add(new RowStyle(SizeType.Absolute, 184f));
            col.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            col.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
            col.AutoScroll = true;

            // flight modes
            modesCard = new TokenCard { Title = "Flight modes", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, T.Space3) };
            modesGrid = new TableLayoutPanel { Dock = DockStyle.Top, Height = 3 * (T.ModeButtonHeight + T.Space2), ColumnCount = 2, BackColor = Color.Transparent };
            modesGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            modesGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            modeStatus = new Label { Dock = DockStyle.Fill, Font = T.Detail, Padding = new Padding(10, 6, 6, 6), Visible = false, AutoEllipsis = true };
            modesCard.Controls.Add(modeStatus);
            modesCard.Controls.Add(modesGrid);

            // mission
            missionCard = new TokenCard { Title = "Mission", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, T.Space3) };
            var missionGrid = new TableLayoutPanel { Dock = DockStyle.Top, Height = T.ButtonHeight + T.Space2, ColumnCount = 3, BackColor = Color.Transparent };
            for (int i = 0; i < 3; i++)
                missionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            btnPause = MakeConsoleButton("Pause");
            btnSkip = MakeConsoleButton("Skip WP");
            btnResume = MakeConsoleButton("Resume…");
            btnPause.Click += async (s, e) =>
            {
                var mode = FlightCommandProfile.PauseMode(FlightCommands.CurrentFamily());
                if (mode != null)
                    await SendModeAsync(mode);
            };
            btnSkip.Click += (s, e) => SkipWaypoint();
            btnResume.Click += (s, e) =>
            {
                if (FlightCommands.ResumeMissionInteractive(FindForm()))
                    FlightPlanner.instance?.BUT_read_Click(this, null);
            };
            missionGrid.Controls.Add(btnPause, 0, 0);
            missionGrid.Controls.Add(btnSkip, 1, 0);
            missionGrid.Controls.Add(btnResume, 2, 0);

            speedLabel = new Label { Dock = DockStyle.Top, Height = 26, Font = T.Detail, Text = "Change speed", Padding = new Padding(0, 6, 0, 0) };
            var speedRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 44, ColumnCount = 3, BackColor = Color.Transparent };
            speedRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            speedRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48f));
            speedRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76f));
            speedInput = new NumericUpDown { Dock = DockStyle.Fill, Font = T.MonoStrong, DecimalPlaces = 1, Increment = 0.5m, Minimum = 0, Maximum = 120, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 4, 6, 0) };
            speedUnit = new Label { Dock = DockStyle.Fill, Font = T.Detail, Text = "m/s", TextAlign = ContentAlignment.MiddleLeft };
            btnSetSpeed = MakeConsoleButton("Set");
            btnSetSpeed.Height = 40;
            btnSetSpeed.Click += async (s, e) => await SetSpeedAsync();
            speedRow.Controls.Add(speedInput, 0, 0);
            speedRow.Controls.Add(speedUnit, 1, 0);
            speedRow.Controls.Add(btnSetSpeed, 2, 0);
            missionCard.Controls.Add(speedRow);
            missionCard.Controls.Add(speedLabel);
            missionCard.Controls.Add(missionGrid);

            // messages
            messagesCard = new TokenCard { Title = "Messages", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, T.Space3) };
            var msgList = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = Color.Transparent };
            messageLines = new Label[6];
            for (int i = 0; i < messageLines.Length; i++)
            {
                msgList.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
                messageLines[i] = new Label { Dock = DockStyle.Fill, Font = T.Detail, AutoEllipsis = true, UseMnemonic = false };
                msgList.Controls.Add(messageLines[i], 0, i);
            }

            allMessages = new LinkLabel { Text = "All messages", AutoSize = true, Font = T.Detail, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            allMessages.LinkClicked += (s, e) => ShowAllMessages();
            messagesCard.Controls.Add(msgList);
            messagesCard.Controls.Add(allMessages);
            messagesCard.Resize += (s, e) => allMessages.Location = new Point(messagesCard.Width - allMessages.Width - 14, 12);

            // emergency
            btnDisarm = new TokenButton { Dock = DockStyle.Fill, Text = "Disarm (hold) · Emergency", Style = TokenButton.Kind.Danger, Height = 52 };
            btnDisarm.Click += (s, e) => FlightCommands.ArmOrDisarmInteractive(FindForm());

            col.Controls.Add(modesCard, 0, 0);
            col.Controls.Add(missionCard, 0, 1);
            col.Controls.Add(messagesCard, 0, 2);

            // the emergency control is pinned below the console so it can never scroll or clip out of view
            btnDisarm.Dock = DockStyle.Bottom;
            btnDisarm.Height = 56;
            var host = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Color.Transparent };
            var gap = new Panel { Dock = DockStyle.Bottom, Height = T.Space2, BackColor = Color.Transparent };
            host.Controls.Add(col);
            host.Controls.Add(gap);
            host.Controls.Add(btnDisarm);
            return host;
        }

        private static TokenButton MakeConsoleButton(string text)
        {
            return new TokenButton { Text = text, Dock = DockStyle.Fill, Margin = new Padding(0, 0, T.Space2, 0), Font = T.BodyStrong };
        }

        private void ApplyTokens()
        {
            var p = T.Current;
            BackColor = p.AppBackground;
            statusBar.BackColor = p.Surface;
            lblVehicle.ForeColor = p.TextMuted;
            lblMode.ForeColor = p.TextPrimary;
            lblLink.ForeColor = p.TextSecondary;
            btnSunlight.Text = T.IsSunlight ? "Night mode" : "Sunlight";
            footer.BackColor = p.AppBackground;
            lblLogging.ForeColor = p.TextMuted;
            foreach (var l in new[] { speedLabel, speedUnit, modeStatus })
                l.ForeColor = p.TextSecondary;
            speedInput.BackColor = p.AppBackground;
            speedInput.ForeColor = p.TextPrimary;
            map?.ApplyChromePalette(p.AppBackground, p.Surface, p.BorderControl, p.TextPrimary);
            allMessages.LinkColor = p.Brand;
            allMessages.ActiveLinkColor = p.Brand;
            foreach (var m in messageLines)
                m.ForeColor = p.TextPrimary;
            Invalidate(true);
        }

        #endregion

        #region screen lifecycle

        public void Activate()
        {
            map.ActivateView();
            UpdateUi();
            uiTimer.Start();
        }

        public void Deactivate()
        {
            uiTimer.Stop();
            map.DeactivateView();
        }

        private void SwitchToPrevious()
        {
            Settings.Instance[PreferenceKey] = "previous";
            MainV2.instance?.ShowModernFlightScreen(false, true);
        }

        #endregion

        #region telemetry

        private string currentAlertKey;
        private CurrentState previewState;
        private Family previewFamily;

        /// <summary>
        /// Design preview: render the screen from a made-up telemetry snapshot instead of the link (used for the
        /// screenshots attached to PRs). Commands stay disabled because nothing is connected.
        /// </summary>
        public void ShowPreview(CurrentState sample, Family family)
        {
            previewState = sample;
            previewFamily = family;
            UpdateUi();
        }

        private void UpdateUi()
        {
            try
            {
                var port = MainV2.comPort;
                var cs = port?.MAV?.cs;
                var connected = port?.BaseStream != null && port.BaseStream.IsOpen;
                var family = FlightCommands.CurrentFamily();
                if (previewState != null)
                {
                    cs = previewState;
                    connected = true;
                    family = previewFamily;
                }

                UpdateStatusBar(cs, connected, family);
                UpdateModes(cs, connected, family);
                UpdateAttitude(cs, connected);
                UpdateReadouts(cs, connected);
                UpdateConsole(cs, connected, family);
                UpdateMessages(cs);
                UpdateFooter(cs, connected);
                UpdateAlert(cs, connected, family);

                map.UpdateTelemetry(cs);
                map.SetOffline(connected);
            }
            catch (Exception ex)
            {
                log.Debug(ex);
            }
        }

        private static string FamilyName(Family f)
        {
            switch (f)
            {
                case Family.Multirotor: return "Copter";
                case Family.Helicopter: return "Helicopter";
                case Family.FixedWing: return "Plane";
                case Family.QuadPlane: return "Quadplane";
                case Family.Rover: return "Rover";
                case Family.Boat: return "Boat";
                case Family.Sub: return "Sub";
                default: return "No vehicle";
            }
        }

        private void UpdateStatusBar(CurrentState cs, bool connected, Family family)
        {
            var p = T.Current;
            if (previewState != null)
            {
                lblVehicle.Text = (FamilyName(family) + " · SYSID 1 · PREVIEW").ToUpperInvariant();
                lblMode.Text = (cs.mode ?? "").ToUpperInvariant() + " · " + (cs.armed ? "ARMED" : "DISARMED");
                lblMode.ForeColor = cs.armed ? p.Caution : p.TextPrimary;
                lblLink.Text = "Design preview with sample values";
                return;
            }

            if (!connected || cs == null)
            {
                lblVehicle.Text = "NO VEHICLE";
                lblMode.Text = "DISCONNECTED";
                lblMode.ForeColor = p.TextMuted;
                lblLink.Text = "Connect from the toolbar above";
                return;
            }

            lblVehicle.Text = (FamilyName(family) + " · SYSID " + MainV2.comPort.MAV.sysid).ToUpperInvariant();
            lblMode.Text = (cs.mode ?? "").ToUpperInvariant() + " · " + (cs.armed ? "ARMED" : "DISARMED");
            lblMode.ForeColor = cs.armed ? p.Caution : p.TextPrimary;
            var s = MainV2.comPort.BaseStream;
            lblLink.Text = $"{s.PortName} · {(s.BaudRate > 0 ? s.BaudRate.ToString() : "")}   Link {cs.linkqualitygcs}%";
            lblLink.ForeColor = cs.linkqualitygcs < 70 ? p.Caution : p.TextSecondary;
        }

        private void UpdateAttitude(CurrentState cs, bool connected)
        {
            adi.HasData = connected && cs != null;
            if (cs != null)
            {
                adi.Roll = cs.roll;
                adi.Pitch = cs.pitch;
                adi.Heading = cs.yaw;
                adi.Airspeed = cs.airspeed;
                adi.GroundSpeed = cs.groundspeed;
                adi.Altitude = cs.alt;
                adi.ClimbRate = cs.verticalspeed;
                var speedUnitText = string.IsNullOrEmpty(CurrentState.SpeedUnit) ? "m/s" : CurrentState.SpeedUnit;
                var altUnitText = string.IsNullOrEmpty(CurrentState.AltUnit) ? "m" : CurrentState.AltUnit;
                adi.SpeedLabel = "AS " + speedUnitText;
                adi.AltLabel = "ALT " + altUnitText;
                adi.ShowAirspeed = cs.airspeed > 0.1 || FlightCommands.CurrentFamily() == Family.FixedWing ||
                                   FlightCommands.CurrentFamily() == Family.QuadPlane;
            }

            adi.UpdateValues();
        }

        private void UpdateReadouts(CurrentState cs, bool connected)
        {
            if (!connected || cs == null)
            {
                cardBattery.Set("Battery", "--", "Not connected", T.State.Neutral);
                cardHome.Set("To home", "--", "No position", T.State.Neutral);
                cardMission.Set("Mission", "--", "No mission", T.State.Neutral);
                cardTime.Set("Time in air", "--", "", T.State.Neutral);
                return;
            }

            var dist = string.IsNullOrEmpty(CurrentState.DistanceUnit) ? "m" : CurrentState.DistanceUnit;
            var batteryState = cs.battery_remaining <= 0
                ? T.State.Neutral
                : cs.battery_remaining <= 20 ? T.State.Warning : cs.battery_remaining <= 35 ? T.State.Caution : T.State.Ok;
            cardBattery.Set("Battery", cs.battery_remaining > 0 ? cs.battery_remaining + "%" : "--",
                $"{cs.battery_voltage:0.0} V · {cs.current:0.0} A", batteryState);

            var bearingHome = ((cs.AZToMAV + 180) % 360 + 360) % 360;
            var cruise = CruiseSpeed();
            var rtl = cruise > 0.5 ? " · RTL ≈ " + FormatMinutes(cs.DistToHome / CurrentState.multiplierdist / cruise) : "";
            cardHome.Set("To home", FormatDistance(cs.DistToHome, dist), $"Bearing {bearingHome:0}°{rtl}", T.State.Neutral);

            var total = previewState != null ? 14 : Math.Max(0, MainV2.comPort.MAV.wps.Count - 1);
            if (total > 0)
            {
                var eta = cs.groundspeed > 0.5 ? " · ETA " + FormatMinutes(cs.wp_dist / CurrentState.multiplierdist / (cs.groundspeed / CurrentState.multiplierspeed)) : "";
                cardMission.Set("Mission", $"WP {cs.wpno:0} / {total}", $"{FormatDistance(cs.wp_dist, dist)} to WP{eta}", T.State.Neutral);
            }
            else
            {
                cardMission.Set("Mission", "--", "No mission loaded", T.State.Neutral);
            }

            var air = TimeSpan.FromSeconds(Math.Max(0, cs.timeInAir));
            cardTime.Set("Time in air", air.TotalHours >= 1 ? air.ToString(@"h\:mm\:ss") : air.ToString(@"mm\:ss"),
                $"Wind {cs.wind_vel:0} {(string.IsNullOrEmpty(CurrentState.SpeedUnit) ? "m/s" : CurrentState.SpeedUnit)} from {cs.wind_dir:0}°",
                T.State.Neutral);
        }

        private static double CruiseSpeed()
        {
            try
            {
                var kind = ParamAliases.GetSpeedSetpoint(MainV2.comPort.MAV.param, out var v, out _);
                return kind == ParamAliases.SpeedKind.MetresPerSecond ? v : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static string FormatDistance(float value, string unit)
        {
            if (unit == "m" && value >= 1000)
                return (value / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " km";
            return value.ToString("0", CultureInfo.InvariantCulture) + " " + unit;
        }

        private static string FormatMinutes(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                return "--";
            if (seconds < 90)
                return $"{seconds:0} s";
            return $"{seconds / 60:0} min";
        }

        private bool speedEdited;

        private void UpdateConsole(CurrentState cs, bool connected, Family family)
        {
            var hasMission = connected && MainV2.comPort.MAV.wps.Count > 1;
            btnPause.Enabled = connected && FlightCommandProfile.PauseMode(family) != null && !commandBusy;
            btnSkip.Enabled = hasMission && !commandBusy;
            btnResume.Enabled = connected && !commandBusy;
            btnSetSpeed.Enabled = connected && !commandBusy;
            btnDisarm.Enabled = connected;
            btnDisarm.Text = cs != null && cs.armed ? "Disarm (hold) · Emergency" : "Arm";
            btnDisarm.Style = cs != null && cs.armed ? TokenButton.Kind.Danger : TokenButton.Kind.Normal;

            if (connected && !speedInput.Focused && !speedEdited)
            {
                var kind = ParamAliases.GetSpeedSetpoint(MainV2.comPort.MAV.param, out var v, out _);
                if (kind != ParamAliases.SpeedKind.None)
                {
                    speedUnit.Text = kind == ParamAliases.SpeedKind.ThrottlePercent ? "%" : "m/s";
                    speedLabel.Text = kind == ParamAliases.SpeedKind.ThrottlePercent ? "Change throttle" : "Change speed";
                    var d = (decimal)Math.Max(0, Math.Min((double)speedInput.Maximum, v));
                    if (speedInput.Value != d)
                        speedInput.Value = d;
                }
            }
        }

        private void UpdateMessages(CurrentState cs)
        {
            var p = T.Current;
            var items = cs?.messages?.ToList() ?? new List<(DateTime time, string message, byte severity)>();
            var recent = items.OrderByDescending(m => m.time).Take(messageLines.Length).ToList();
            for (int i = 0; i < messageLines.Length; i++)
            {
                if (i < recent.Count)
                {
                    var m = recent[i];
                    messageLines[i].Text = m.time.ToString("HH:mm:ss") + "  " + m.message;
                    messageLines[i].ForeColor = m.severity <= (byte)MAVLink.MAV_SEVERITY.ERROR ? p.Warning
                        : m.severity == (byte)MAVLink.MAV_SEVERITY.WARNING ? p.Caution : p.TextPrimary;
                }
                else
                {
                    messageLines[i].Text = "";
                }
            }

            allMessages.Text = $"All ({items.Count})";
        }

        private void UpdateFooter(CurrentState cs, bool connected)
        {
            if (!connected || cs == null)
            {
                chipGps.Set("GPS --", T.State.Neutral);
                chipEkf.Set("EKF --", T.State.Neutral);
                chipBattery.Set("Battery --", T.State.Neutral);
                chipFence.Set("Fence --", T.State.Neutral);
                chipAirspeed.Visible = false;
                chipParams.Set("Params --", T.State.Neutral);
                lblLogging.Text = "";
                return;
            }

            var fix = (int)cs.gpsstatus;
            var fixText = fix >= 6 ? "3D RTK fixed" : fix == 5 ? "3D RTK float" : fix == 4 ? "3D DGPS" : fix == 3 ? "3D" : fix == 2 ? "2D" : "No fix";
            chipGps.Set($"GPS {fixText} · {cs.satcount:0} sats", fix >= 3 ? T.State.Ok : fix == 2 ? T.State.Caution : T.State.Warning);

            chipEkf.Set(cs.ekfstatus < 0.5 ? "EKF OK" : cs.ekfstatus < 0.8 ? "EKF variance high" : "EKF bad",
                cs.ekfstatus < 0.5 ? T.State.Ok : cs.ekfstatus < 0.8 ? T.State.Caution : T.State.Warning);

            var batt = cs.battery_remaining;
            chipBattery.Set(batt <= 0 ? "Battery --" : batt <= 20 ? "Battery critical" : batt <= 35 ? "Battery low" : "Battery OK",
                batt <= 0 ? T.State.Neutral : batt <= 20 ? T.State.Warning : batt <= 35 ? T.State.Caution : T.State.Ok);

            if (cs.sensors_enabled.geofence)
                chipFence.Set(cs.sensors_health.geofence ? "Fence armed" : "Fence breached", cs.sensors_health.geofence ? T.State.Ok : T.State.Warning);
            else
                chipFence.Set("Fence off", T.State.Neutral);

            chipAirspeed.Visible = cs.sensors_present.differential_pressure;
            if (chipAirspeed.Visible)
                chipAirspeed.Set(cs.sensors_health.differential_pressure ? "Airspeed sensor OK" : "Airspeed sensor fault",
                    cs.sensors_health.differential_pressure ? T.State.Ok : T.State.Warning);

            var param = MainV2.comPort.MAV.param;
            var got = param.Count;
            var total = param.TotalReported;
            chipParams.Set(total > 0 ? $"Params {got:N0} / {total:N0}" : $"Params {got:N0}",
                total > 0 && got < total ? T.State.Caution : T.State.Neutral);

            lblLogging.Text = MainV2.comPort.logfile != null ? "Logging to tlog" : "";
        }

        private void UpdateAlert(CurrentState cs, bool connected, Family family)
        {
            if (!connected || cs == null)
            {
                banner.Visible = false;
                return;
            }

            string key = null, text = null, detail = null, action = null;
            var level = T.State.Warning;

            if (cs.armed && cs.battery_remaining > 0 && cs.battery_remaining <= 20)
            {
                key = "battery";
                text = $"Battery {cs.battery_remaining}%: return now";
                detail = $"{cs.battery_voltage:0.0} V. Home is {FormatDistance(cs.DistToHome, string.IsNullOrEmpty(CurrentState.DistanceUnit) ? "m" : CurrentState.DistanceUnit)} away.";
                action = "Send " + FlightCommandProfile.ReturnMode(family) + "…";
            }
            else if (!string.IsNullOrEmpty(cs.messageHigh) && cs.messageHighSeverity <= MAVLink.MAV_SEVERITY.ERROR)
            {
                key = "msg:" + cs.messageHigh;
                text = cs.messageHigh;
                detail = "From the autopilot";
                level = cs.messageHighSeverity <= MAVLink.MAV_SEVERITY.CRITICAL ? T.State.Warning : T.State.Caution;
            }

            currentAlertKey = key;
            if (key == null || key == acknowledgedAlert)
            {
                banner.Visible = false;
                return;
            }

            if (!banner.Visible)
                banner.ShowAlert(level, text, detail, action);
        }

        #endregion

        #region flight modes

        private void UpdateModes(CurrentState cs, bool connected, Family family)
        {
            if (family != modesFor)
                BuildModeButtons(family);

            var current = Normalize(cs?.mode);
            foreach (var b in modeButtons)
            {
                var choice = (FlightCommandProfile.ModeChoice)b.Tag;
                var isCurrent = connected && Normalize(choice.Mode) == current ||
                                connected && Normalize(ResolveModeName(choice.Mode)) == current;
                b.Style = isCurrent ? TokenButton.Kind.Current : choice.Danger ? TokenButton.Kind.Danger : TokenButton.Kind.Normal;
                b.Enabled = connected && !commandBusy;
            }

            modesCard.TitleAside = connected ? "for " + FamilyName(family) : "";
        }

        private void BuildModeButtons(Family family)
        {
            modesFor = family;
            modesGrid.SuspendLayout();
            foreach (var b in modeButtons)
                b.Dispose();
            modeButtons.Clear();
            modesGrid.Controls.Clear();
            modesGrid.RowStyles.Clear();

            var modes = FlightCommandProfile.FlyModes(family);
            var rows = (modes.Count + 1) / 2;
            modeRows = Math.Max(1, rows);
            modesGrid.RowCount = Math.Max(1, rows);
            for (int r = 0; r < rows; r++)
                modesGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, T.ModeButtonHeight + T.Space2));

            for (int i = 0; i < modes.Count; i++)
            {
                var choice = modes[i];
                var b = new TokenButton
                {
                    Text = choice.Mode.Replace('_', ' '),
                    Tag = choice,
                    Dock = DockStyle.Fill,
                    Height = T.ModeButtonHeight,
                    Margin = new Padding(0, 0, i % 2 == 0 ? T.Space1 : 0, T.Space2),
                    Style = choice.Danger ? TokenButton.Kind.Danger : TokenButton.Kind.Normal
                };
                b.Click += async (s, e) => await ModeClicked(choice);
                modeButtons.Add(b);
                modesGrid.Controls.Add(b, i % 2, i / 2);
            }

            if (modes.Count == 0)
            {
                var none = new Label { Text = "Connect a vehicle to see its flight modes.", Dock = DockStyle.Fill, Font = T.Detail, ForeColor = T.Current.TextMuted };
                modesGrid.Controls.Add(none, 0, 0);
                modesGrid.SetColumnSpan(none, 2);
            }

            modesGrid.Height = Math.Max(1, rows) * (T.ModeButtonHeight + T.Space2);
            modesGrid.ResumeLayout(true);
            ApplyResponsiveLayout();
        }

        private async System.Threading.Tasks.Task ModeClicked(FlightCommandProfile.ModeChoice choice)
        {
            if (choice.Danger)
            {
                var label = choice.Mode.Replace('_', ' ');
                if (System.CustomMessageBox.Show($"Switch to {label}? The vehicle will {(choice.Mode == "HOLD" ? "stop" : choice.Mode == "SURFACE" ? "surface" : "land")} where it is.",
                        label, System.CustomMessageBox.MessageBoxButtons.YesNo, System.CustomMessageBox.MessageBoxIcon.Warning,
                        "Cancel", label) != System.CustomMessageBox.DialogResult.No)
                    return;
            }

            await SendModeAsync(choice.Mode);
        }

        private static string Normalize(string mode)
        {
            if (string.IsNullOrEmpty(mode))
                return "";
            var chars = mode.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
            return new string(chars);
        }

        /// <summary>
        /// Map a board mode name (SMART_RTL, ALT_HOLD...) to this vehicle's spelling from its parameter metadata
        /// (Smart_RTL, SmartRTL, Depth Hold...).
        /// </summary>
        private static string ResolveModeName(string wanted)
        {
            try
            {
                var list = MissionPlanner.ArduPilot.Common.getModesList(MainV2.comPort.MAV.cs.firmware);
                var target = Normalize(wanted);
                var aliases = new Dictionary<string, string[]>
                {
                    { "althold", new[] { "depthhold" } },
                    { "poshold", new[] { "positionhold" } },
                };
                foreach (var kv in list)
                {
                    var n = Normalize(kv.Value);
                    if (n == target || aliases.TryGetValue(target, out var alt) && alt.Contains(n))
                        return kv.Value;
                }
            }
            catch
            {
            }

            return wanted;
        }

        private async System.Threading.Tasks.Task SendModeAsync(string mode)
        {
            if (commandBusy)
                return;
            commandBusy = true;
            var p = T.Current;
            var name = ResolveModeName(mode);
            modeStatus.Text = $"{mode.Replace('_', ' ')} sent · waiting for the autopilot to confirm";
            modeStatus.ForeColor = p.Info;
            modeStatus.Visible = true;
            try
            {
                var result = await FlightCommands.SetModeAndConfirmAsync(name);
                switch (result)
                {
                    case FlightCommands.ModeResult.Confirmed:
                        modeStatus.Text = $"{mode.Replace('_', ' ')} confirmed";
                        modeStatus.ForeColor = p.Ok;
                        break;
                    case FlightCommands.ModeResult.NotAvailable:
                        modeStatus.Text = $"{mode.Replace('_', ' ')} is not a mode on this vehicle. Nothing was sent.";
                        modeStatus.ForeColor = p.Caution;
                        break;
                    default:
                        modeStatus.Text = $"Not confirmed: still {MainV2.comPort.MAV.cs.mode}. See Messages.";
                        modeStatus.ForeColor = p.Warning;
                        break;
                }
            }
            catch (Exception ex)
            {
                modeStatus.Text = "Mode change failed: " + ex.Message;
                modeStatus.ForeColor = p.Warning;
            }
            finally
            {
                commandBusy = false;
                UpdateUi();
            }
        }

        #endregion

        #region mission

        private void SkipWaypoint()
        {
            var cs = MainV2.comPort.MAV.cs;
            var total = MainV2.comPort.MAV.wps.Count - 1;
            var next = (int)cs.wpno + 1;
            if (next > total)
            {
                System.CustomMessageBox.Show("Already at the last waypoint.", "Skip waypoint");
                return;
            }

            if (System.CustomMessageBox.Show($"Skip to waypoint {next} of {total}?", "Skip waypoint",
                    System.CustomMessageBox.MessageBoxButtons.YesNo, System.CustomMessageBox.MessageBoxIcon.Question,
                    "Skip", "Cancel") != System.CustomMessageBox.DialogResult.Yes)
                return;

            try
            {
                if (!MainV2.comPort.setWPCurrent(MainV2.comPort.MAV.sysid, MainV2.comPort.MAV.compid, (ushort)next))
                    System.CustomMessageBox.Show("The vehicle did not accept the new waypoint.", Strings.ERROR);
            }
            catch (Exception ex)
            {
                System.CustomMessageBox.Show("Skip waypoint failed: " + ex.Message, Strings.ERROR);
            }
        }

        private async System.Threading.Tasks.Task SetSpeedAsync()
        {
            if (commandBusy)
                return;
            commandBusy = true;
            try
            {
                ParamAliases.ChangeSpeedArguments(MainV2.comPort.MAV.param, (float)speedInput.Value, out var p1, out var p2, out var p3);
                var mav = MainV2.comPort.MAV;
                var ok = await MainV2.comPort.doCommandAsync(mav.sysid, mav.compid, MAVLink.MAV_CMD.DO_CHANGE_SPEED, p1, p2, p3, 0, 0, 0, 0);
                modeStatus.Text = ok ? $"Speed {speedInput.Value} {speedUnit.Text} accepted" : "Speed change refused by the vehicle";
                modeStatus.ForeColor = ok ? T.Current.Ok : T.Current.Warning;
                modeStatus.Visible = true;
                speedEdited = false;
            }
            catch (Exception ex)
            {
                System.CustomMessageBox.Show("Speed change failed: " + ex.Message, Strings.ERROR);
            }
            finally
            {
                commandBusy = false;
            }
        }

        private void ShowAllMessages()
        {
            var cs = MainV2.comPort.MAV.cs;
            var text = string.Join(Environment.NewLine,
                cs.messages.OrderByDescending(m => m.time).Select(m => m.time.ToString("HH:mm:ss") + "  " + m.message));
            using (var f = new Form { Text = "Messages", Size = new Size(720, 520), StartPosition = FormStartPosition.CenterParent })
            {
                var box = new TextBox
                {
                    Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                    Font = T.DetailMono, Text = text, BackColor = T.Current.Surface, ForeColor = T.Current.TextPrimary
                };
                f.Controls.Add(box);
                f.ShowDialog(FindForm());
            }
        }

        #endregion

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            speedInput.ValueChanged += (s, e) => { if (speedInput.Focused) speedEdited = true; };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                uiTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
