using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MissionPlanner.ArduPilot;
using MissionPlanner.Controls;
using MissionPlanner.Joystick;
using MissionPlanner.Log;
using MissionPlanner.Utilities;

namespace MissionPlanner.GCSViews
{
    public class PanelQuickActions : Panel
    {
        private enum OperationsTab
        {
            Actions,
            Messages,
            PreFlight,
            Logs
        }

        private readonly Color Shell = Color.FromArgb(22, 28, 40);
        private readonly Color Surface = Color.FromArgb(18, 23, 33);
        private readonly Color SurfaceRaised = Color.FromArgb(25, 31, 44);
        private readonly Color Border = Color.FromArgb(46, 56, 74);
        private readonly Color Gold = Color.FromArgb(200, 168, 101);
        private readonly Color Info = Color.FromArgb(74, 190, 225);
        private readonly Color Success = Color.FromArgb(78, 192, 122);
        private readonly Color Warning = Color.FromArgb(228, 172, 67);
        private readonly Color Danger = Color.FromArgb(228, 84, 71);
        private readonly Color Violet = Color.FromArgb(138, 112, 218);
        private readonly Color TextPrimary = Color.FromArgb(232, 236, 243);
        private readonly Color TextMuted = Color.FromArgb(140, 151, 171);

        private readonly Font titleFont = new Font("Segoe UI", 12f, FontStyle.Bold);
        private readonly Font subtitleFont = new Font("Segoe UI", 9f, FontStyle.Regular);
        private readonly Font metaFont = new Font("Segoe UI", 8f, FontStyle.Bold);

        private Label lblTitle;
        private Label lblMode;
        private Label lblStatus;
        private Label lblAdvisory;
        private TableLayoutPanel tabBar;
        private Button btnActionsTab;
        private Button btnMessagesTab;
        private Button btnPreflightTab;
        private Button btnLogsTab;
        private Panel pageHost;
        private Panel actionsPage;
        private Panel messagesPage;
        private Panel preflightPage;
        private Panel logsPage;
        private FlightDataActions legacyActions;
        private MessagesList messagesList;
        private Button btnOpenDataflashLogs;
        private Button btnLoadTelemetryLog;
        private Button btnExportTelemetryKml;
        private Button btnPlayPauseLog;
        private TrackBar trackPlayback;
        private Label lblPlaybackPercent;
        private Label lblPlaybackState;
        private Label lblLogFile;
        private ComboBox comboPlaybackSpeed;
        private PreflightStatusCard cardLink;
        private PreflightStatusCard cardGps;
        private PreflightStatusCard cardBattery;
        private PreflightStatusCard cardPreflight;
        private PreflightStatusCard cardEkf;
        private PreflightStatusCard cardFailsafe;
        private OperationsTab activeTab;
        private CurrentState lastTelemetry;
        private bool lastConnected;
        private bool legacyActionsInitialized;
        private bool suppressLogUiEvents;
        private MethodInfo legacySetPlaybackSpeedMethod;
        private FieldInfo legacyPlaybackSpeedField;
        private readonly List<(DateTime time, string message, byte severity)> supplementalMessages =
            new List<(DateTime time, string message, byte severity)>();

        public event EventHandler ClearTrackRequested;
        public int ActivePreferredWidth => GetPreferredWidth(activeTab);

        public PanelQuickActions()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(10, 14, 20);
            Padding = new Padding(10);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            InitializeShell();
            SelectTab(OperationsTab.Messages);
            UpdateTelemetry(null, false);
        }

        public void UpdateStatus(bool armed, bool connected, string mode, string alert)
        {
            lastConnected = connected;
            UpdateHeader(armed, connected, mode, alert);
            UpdateActionButtons(lastTelemetry, connected);
        }

        public void UpdateTelemetry(CurrentState cs, bool connected)
        {
            lastTelemetry = cs;
            lastConnected = connected;
            UpdateHeader(cs?.armed ?? false, connected, cs?.mode, cs?.messageHigh);
            UpdateActionButtons(cs, connected);
            UpdatePreflight(cs, connected);
            UpdateMessages(cs);
            UpdateLogsUi();
        }

        public void PushEvent(FlightEventSeverity severity, string title, string detail)
        {
            string message = BuildSupplementalMessage(title, detail);
            if (string.IsNullOrWhiteSpace(message))
                return;

            supplementalMessages.Add((DateTime.Now, message, ConvertSeverity(severity)));
            while (supplementalMessages.Count > 120)
                supplementalMessages.RemoveAt(0);

            UpdateMessages(lastTelemetry);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            ModernUiPainter.FillRoundedRectangle(e.Graphics, Shell, bounds, 18);
            ModernUiPainter.DrawRoundedRectangle(e.Graphics, Border, 1f, bounds, 18);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                titleFont?.Dispose();
                subtitleFont?.Dispose();
                metaFont?.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeShell()
        {
            tabBar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 42,
                BackColor = Surface,
                Padding = new Padding(8, 6, 8, 6),
                ColumnCount = 4,
                RowCount = 1,
                AutoSize = false
            };
            tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            tabBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            btnActionsTab = CreateTabButton("Actions", OperationsTab.Actions);
            btnMessagesTab = CreateTabButton("Messages", OperationsTab.Messages);
            btnPreflightTab = CreateTabButton("PreFlight", OperationsTab.PreFlight);
            btnLogsTab = CreateTabButton("Logs", OperationsTab.Logs);
            tabBar.Controls.Add(btnActionsTab, 0, 0);
            tabBar.Controls.Add(btnMessagesTab, 1, 0);
            tabBar.Controls.Add(btnPreflightTab, 2, 0);
            tabBar.Controls.Add(btnLogsTab, 3, 0);

            pageHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(0, 10, 0, 0) };
            actionsPage = BuildActionsPage();
            messagesPage = BuildMessagesPage();
            preflightPage = BuildPreflightPage();
            logsPage = BuildLogsPage();
            pageHost.Controls.Add(logsPage);
            pageHost.Controls.Add(messagesPage);
            pageHost.Controls.Add(preflightPage);
            pageHost.Controls.Add(actionsPage);

            Controls.Add(pageHost);
            Controls.Add(tabBar);
        }

        private Panel BuildActionsPage()
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            legacyActions = new FlightDataActions
            {
                Dock = DockStyle.Fill,
                BackColor = Surface
            };
            legacyActions.Initialize();
            legacyActions.SetPayloadVisible(false);
            page.Controls.Add(legacyActions);
            return page;
        }

        private Panel BuildMessagesPage()
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            var header = new Panel { Dock = DockStyle.Top, Height = 24, BackColor = Color.Transparent };
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Left,
                Width = 170,
                Text = "LIVE MESSAGES",
                Font = metaFont,
                ForeColor = Gold,
                BackColor = Color.Transparent
            });
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Right,
                Width = 72,
                Text = "SCROLL",
                TextAlign = ContentAlignment.MiddleRight,
                Font = metaFont,
                ForeColor = TextMuted,
                BackColor = Color.Transparent
            });

            messagesList = new MessagesList
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                UseWrappedRows = true,
                DisplayFontSize = 8.5f
            };
            page.Controls.Add(messagesList);
            page.Controls.Add(header);
            return page;
        }

        private Panel BuildPreflightPage()
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            var note = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Text = "Readiness, estimator, link, and safety checks.",
                Font = subtitleFont,
                ForeColor = TextMuted,
                BackColor = Color.Transparent
            };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 8, 0, 0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34f));

            cardLink = CreatePreflightCard("LINK", Info);
            cardGps = CreatePreflightCard("GPS", Gold);
            cardBattery = CreatePreflightCard("BATTERY", Danger);
            cardPreflight = CreatePreflightCard("PREFLIGHT", Violet);
            cardEkf = CreatePreflightCard("EKF", Success);
            cardFailsafe = CreatePreflightCard("FAILSAFE", Warning);

            grid.Controls.Add(cardLink, 0, 0);
            grid.Controls.Add(cardGps, 1, 0);
            grid.Controls.Add(cardBattery, 0, 1);
            grid.Controls.Add(cardPreflight, 1, 1);
            grid.Controls.Add(cardEkf, 0, 2);
            grid.Controls.Add(cardFailsafe, 1, 2);

            page.Controls.Add(grid);
            page.Controls.Add(note);
            return page;
        }

        private Panel BuildLogsPage()
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

            var note = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Text = "Telemetry playback and dataflash browsing tools.",
                Font = subtitleFont,
                ForeColor = TextMuted,
                BackColor = Color.Transparent
            };

            Panel telemetryBody;
            var telemetrySection = CreateLogsSectionPanel("TELEMETRY LOGS", "Load, scrub, play, and export telemetry logs.", out telemetryBody);
            telemetrySection.Dock = DockStyle.Fill;

            var telemetryContent = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = Color.Transparent,
                Padding = new Padding(14, 12, 14, 14)
            };
            telemetryContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            telemetryContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
            telemetryContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
            telemetryContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            telemetryContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var telemetryButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };

            btnLoadTelemetryLog = CreateLogCommandButton("Load Telemetry", 122);
            btnLoadTelemetryLog.Click += BtnLoadTelemetryLog_Click;
            telemetryButtons.Controls.Add(btnLoadTelemetryLog);

            btnPlayPauseLog = CreateLogCommandButton("Play", 84);
            btnPlayPauseLog.Click += BtnPlayPauseLog_Click;
            telemetryButtons.Controls.Add(btnPlayPauseLog);

            btnExportTelemetryKml = CreateLogCommandButton("Export KML", 108);
            btnExportTelemetryKml.Click += BtnExportTelemetryKml_Click;
            telemetryButtons.Controls.Add(btnExportTelemetryKml);

            lblLogFile = new Label
            {
                Dock = DockStyle.Fill,
                Font = subtitleFont,
                ForeColor = TextPrimary,
                BackColor = Color.Transparent,
                Text = "No telemetry log loaded.",
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 2, 0, 0)
            };

            var trackPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(0)
            };
            trackPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            trackPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64f));
            trackPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
            trackPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));

            lblPlaybackState = new Label
            {
                Dock = DockStyle.Fill,
                Font = metaFont,
                ForeColor = Gold,
                BackColor = Color.Transparent,
                Text = "STANDBY",
                TextAlign = ContentAlignment.MiddleLeft
            };
            lblPlaybackPercent = new Label
            {
                Dock = DockStyle.Fill,
                Font = metaFont,
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                Text = "--",
                TextAlign = ContentAlignment.MiddleRight
            };
            trackPlayback = new TrackBar
            {
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = 100,
                TickStyle = TickStyle.None,
                LargeChange = 5,
                SmallChange = 1,
                Margin = new Padding(0, 0, 6, 0),
                Enabled = false
            };
            trackPlayback.Scroll += TrackPlayback_Scroll;

            trackPanel.Controls.Add(lblPlaybackState, 0, 0);
            trackPanel.Controls.Add(lblPlaybackPercent, 1, 0);
            trackPanel.Controls.Add(trackPlayback, 0, 1);
            trackPanel.SetColumnSpan(trackPlayback, 2);

            var speedPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            speedPanel.Controls.Add(new Label
            {
                Width = 104,
                Height = 26,
                Text = "Playback Speed",
                Font = subtitleFont,
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            });
            comboPlaybackSpeed = new ComboBox
            {
                Width = 88,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = subtitleFont,
                BackColor = SurfaceRaised,
                ForeColor = TextPrimary
            };
            comboPlaybackSpeed.Items.AddRange(new object[] { "0.1", "0.25", "0.5", "1", "2", "4", "8" });
            comboPlaybackSpeed.SelectedIndexChanged += ComboPlaybackSpeed_SelectedIndexChanged;
            speedPanel.Controls.Add(comboPlaybackSpeed);

            telemetryContent.Controls.Add(telemetryButtons, 0, 0);
            telemetryContent.Controls.Add(lblLogFile, 0, 1);
            telemetryContent.Controls.Add(trackPanel, 0, 2);
            telemetryContent.Controls.Add(speedPanel, 0, 3);
            telemetryBody.Controls.Add(telemetryContent);

            Panel dataflashBody;
            var dataflashSection = CreateLogsSectionPanel("DATAFLASH LOGS", "Open the legacy dataflash browser and analysis tools.", out dataflashBody);
            dataflashSection.Dock = DockStyle.Top;
            dataflashSection.Height = 112;

            var dataflashContent = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(14, 12, 14, 14)
            };
            btnOpenDataflashLogs = CreateLogCommandButton("Open Browser", 118);
            btnOpenDataflashLogs.Location = new Point(0, 0);
            btnOpenDataflashLogs.Click += BtnOpenDataflashLogs_Click;
            dataflashContent.Controls.Add(btnOpenDataflashLogs);
            dataflashBody.Controls.Add(dataflashContent);

            page.Controls.Add(telemetrySection);
            page.Controls.Add(dataflashSection);
            page.Controls.Add(note);

            UpdateLogsUi();
            return page;
        }

        private Button CreateTabButton(string text, OperationsTab tab)
        {
            var button = new Button
            {
                Text = text,
                Tag = tab,
                Height = 26,
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 0, 3, 0),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = SurfaceRaised,
                ForeColor = TextMuted,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseDownBackColor = SurfaceRaised;
            button.FlatAppearance.MouseOverBackColor = SurfaceRaised;
            button.Click += (s, e) => SelectTab(tab);
            return button;
        }

        private PreflightStatusCard CreatePreflightCard(string title, Color accent)
        {
            return new PreflightStatusCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 10), Title = title, AccentColor = accent };
        }

        private Panel CreateLogsSectionPanel(string title, string detail, out Panel bodyHost)
        {
            var section = new Panel
            {
                BackColor = Surface,
                Padding = new Padding(0),
                Margin = new Padding(0, 0, 0, 10)
            };

            section.Paint += (sender, args) =>
            {
                Rectangle bounds = new Rectangle(0, 0, Math.Max(1, section.Width - 1), Math.Max(1, section.Height - 1));
                ModernUiPainter.FillRoundedRectangle(args.Graphics, Surface, bounds, 16);
                ModernUiPainter.DrawRoundedRectangle(args.Graphics, Border, 1f, bounds, 16);
                ModernUiPainter.FillRoundedRectangle(args.Graphics, Gold, new Rectangle(bounds.X + 14, bounds.Y + 12, 54, 3), 2);
            };

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 52,
                BackColor = Color.Transparent,
                Padding = new Padding(14, 10, 14, 8)
            };

            header.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = detail,
                Font = subtitleFont,
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.TopLeft
            });
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                Text = title,
                Font = metaFont,
                ForeColor = Gold,
                BackColor = Color.Transparent
            });

            bodyHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };

            section.Controls.Add(bodyHost);
            section.Controls.Add(header);

            return section;
        }

        private Button CreateLogCommandButton(string text, int width)
        {
            var button = new Button
            {
                Text = text,
                Width = width,
                Height = 28,
                Margin = new Padding(0, 0, 10, 0),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = SurfaceRaised,
                ForeColor = TextPrimary,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Gold;
            button.FlatAppearance.MouseDownBackColor = SurfaceRaised;
            button.FlatAppearance.MouseOverBackColor = SurfaceRaised;
            return button;
        }

        private void SelectTab(OperationsTab tab)
        {
            activeTab = tab;
            actionsPage.Visible = tab == OperationsTab.Actions;
            messagesPage.Visible = tab == OperationsTab.Messages;
            preflightPage.Visible = tab == OperationsTab.PreFlight;
            logsPage.Visible = tab == OperationsTab.Logs;
            ApplyTabStyle(btnActionsTab, tab == OperationsTab.Actions);
            ApplyTabStyle(btnMessagesTab, tab == OperationsTab.Messages);
            ApplyTabStyle(btnPreflightTab, tab == OperationsTab.PreFlight);
            ApplyTabStyle(btnLogsTab, tab == OperationsTab.Logs);
            UpdateLogsUi();
        }

        private void ApplyTabStyle(Button button, bool active)
        {
            button.BackColor = SurfaceRaised;
            button.ForeColor = TextPrimary;
            button.FlatAppearance.BorderColor = Gold;
            button.FlatAppearance.BorderSize = active ? 2 : 1;
        }

        private void UpdateHeader(bool armed, bool connected, string mode, string alert)
        {
            if (lblMode == null || lblStatus == null || lblAdvisory == null)
                return;

            string normalizedMode = string.IsNullOrWhiteSpace(mode) ? "UNKNOWN" : mode.ToUpperInvariant();
            lblMode.Text = $"Mode {normalizedMode}";
            lblStatus.Text = $"{(armed ? "ARMED" : "SAFE")}  |  {(connected ? "LIVE LINK" : "OFFLINE")}";
            lblStatus.ForeColor = !connected ? Danger : armed ? Success : Warning;
            lblAdvisory.Text = !connected
                ? "Awaiting vehicle link, telemetry, and mission activity."
                : !string.IsNullOrWhiteSpace(alert)
                    ? $"Active advisory: {alert.Trim()}"
                    : armed
                        ? "Command path active. Guided actions and recovery modes are available."
                        : "Vehicle is safe. Review preflight readiness before dispatch.";
        }

        private void UpdateActionButtons(CurrentState cs, bool connected)
        {
            EnsureLegacyActionsInitialized();
            RefreshLegacyActionLists(cs);
            legacyActions.Enabled = true;
        }

        private void EnsureLegacyActionsInitialized()
        {
            if (legacyActionsInitialized || legacyActions == null)
                return;

            legacyActions.CMB_action.DataSource = Enum.GetNames(typeof(FlightData.actions));
            legacyActions.BUTactiondo.Click += LegacyDoActionClick;
            legacyActions.BUT_setwp.Click += LegacySetWaypointClick;
            legacyActions.BUT_mountmode.Click += LegacySetMountClick;
            legacyActions.BUTrestartmission.Click += LegacyRestartMissionClick;
            legacyActions.BUT_resumemis.Click += LegacyResumeMissionClick;
            legacyActions.modifyandSetSpeed.Click += LegacySetSpeedClick;
            legacyActions.modifyandSetAlt.Click += LegacySetAltitudeClick;
            legacyActions.modifyandSetLoiterRad.Click += LegacySetLoiterRadiusClick;
            legacyActions.BUT_Homealt.Click += LegacyToggleHomeAltitudeClick;
            legacyActions.BUT_RAWSensor.Click += LegacyOpenRawSensorClick;
            legacyActions.BUT_joystick.Click += LegacyOpenJoystickClick;
            legacyActions.BUT_SendMSG.Click += LegacySendMessageClick;
            legacyActions.BUT_clear_track.Click += LegacyClearTrackClick;
            legacyActions.BUT_Reboot.Click += LegacyRebootClick;
            legacyActions.BUT_abortland.Click += LegacyAbortLandClick;
            legacyActions.CMB_setwp.DropDown += (s, e) => RefreshWaypointList();
            legacyActionsInitialized = true;
        }

        private void RefreshLegacyActionLists(CurrentState cs)
        {
            if (legacyActions == null)
                return;

            RefreshModeList(cs);
            RefreshWaypointList();
            RefreshMountModes();
            RefreshSetpointValues();
        }

        private void RefreshModeList(CurrentState cs)
        {
            if (legacyActions?.CMB_modes == null || cs == null)
                return;

            string currentValue = legacyActions.CMB_modes.Text;
            legacyActions.CMB_modes.DataSource = MissionPlanner.ArduPilot.Common.getModesList(cs.firmware);
            legacyActions.CMB_modes.ValueMember = "Key";
            legacyActions.CMB_modes.DisplayMember = "Value";
            legacyActions.CMB_modes.Text = string.IsNullOrWhiteSpace(currentValue) ? "Auto" : currentValue;
        }

        private void RefreshWaypointList()
        {
            if (legacyActions?.CMB_setwp == null)
                return;

            int count = MainV2.comPort?.MAV?.wps?.Count ?? 0;
            int selected = Math.Max(0, legacyActions.CMB_setwp.SelectedIndex);
            legacyActions.CMB_setwp.Items.Clear();
            for (int i = 0; i < count; i++)
                legacyActions.CMB_setwp.Items.Add(i == 0 ? "0 (Home)" : i.ToString());

            if (legacyActions.CMB_setwp.Items.Count > 0)
                legacyActions.CMB_setwp.SelectedIndex = Math.Min(selected, legacyActions.CMB_setwp.Items.Count - 1);
        }

        private void RefreshMountModes()
        {
            if (legacyActions?.CMB_mountmode == null || legacyActions.CMB_mountmode.DataSource != null)
                return;

            var options = new List<KeyValuePair<int, string>>();
            foreach (MAVLink.MAV_MOUNT_MODE mode in Enum.GetValues(typeof(MAVLink.MAV_MOUNT_MODE)))
                options.Add(new KeyValuePair<int, string>((int)mode, mode.ToString().Replace("_", " ")));

            legacyActions.CMB_mountmode.DataSource = options;
            legacyActions.CMB_mountmode.DisplayMember = "Value";
            legacyActions.CMB_mountmode.ValueMember = "Key";
        }

        private void RefreshSetpointValues()
        {
            if (legacyActions == null || MainV2.comPort?.MAV?.param == null)
                return;

            var param = MainV2.comPort.MAV.param;

            try
            {
                if (param.ContainsKey("WP_SPEED_MAX"))
                    legacyActions.modifyandSetSpeed.Value = (decimal)((float)param["WP_SPEED_MAX"] / 100.0);
                else if (param.ContainsKey("TRIM_ARSPD_CM"))
                    legacyActions.modifyandSetSpeed.Value = (decimal)((float)param["TRIM_ARSPD_CM"] / 100.0);
                else if (param.ContainsKey("TRIM_THROTTLE"))
                    legacyActions.modifyandSetSpeed.Value = (decimal)(float)param["TRIM_THROTTLE"];
            }
            catch { }

            try
            {
                if (param.ContainsKey("WP_LOITER_RAD"))
                    legacyActions.modifyandSetLoiterRad.Value = (decimal)((float)param["WP_LOITER_RAD"] * CurrentState.multiplierdist);
                else if (param.ContainsKey("LOITER_RAD"))
                    legacyActions.modifyandSetLoiterRad.Value = (decimal)((float)param["LOITER_RAD"] * CurrentState.multiplierdist);
            }
            catch { }
        }

        private int GetPreferredWidth(OperationsTab tab)
        {
            return 480;
        }

        private void UpdateMessages(CurrentState cs)
        {
            if (messagesList == null)
                return;

            var merged = new List<(DateTime time, string message, byte severity)>();
            if (cs?.messages != null && cs.messages.Count > 0)
                merged.AddRange(cs.messages);
            if (supplementalMessages.Count > 0)
                merged.AddRange(supplementalMessages);

            merged.Sort((left, right) => left.time.CompareTo(right.time));

            const int maxMessages = 1000;
            if (merged.Count > maxMessages)
                merged = merged.GetRange(merged.Count - maxMessages, maxMessages);

            messagesList.UpdateMessages(merged);
        }

        private void BtnOpenDataflashLogs_Click(object sender, EventArgs e)
        {
            var logBrowse = new LogBrowse();
            ThemeManager.ApplyThemeTo(logBrowse);
            logBrowse.Show();
        }

        private void BtnLoadTelemetryLog_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "Telemetry Logs|*.tlog;*.log;*.rlog;*.bin|All Files|*.*";
                dialog.Title = "Load Telemetry Log";

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                try
                {
                    MainV2.comPort.logplaybackfile?.Close();
                    MainV2.comPort.logplaybackfile = null;
                }
                catch
                {
                }

                try
                {
                    if (MainV2.instance?.FlightData != null)
                    {
                        MainV2.instance.FlightData.LoadLogFile(dialog.FileName);
                    }
                    else
                    {
                        MainV2.comPort.logreadmode = true;
                        MainV2.comPort.logplaybackfile = new BinaryReader(File.OpenRead(dialog.FileName));
                        MainV2.comPort.lastlogread = DateTime.MinValue;
                    }

                    PushEvent(FlightEventSeverity.Info, "Telemetry log loaded", Path.GetFileName(dialog.FileName));
                }
                catch (Exception ex)
                {
                    ModernCommandDialog.ShowNotice(this, "Load telemetry log failed", ex.Message, Danger);
                    PushEvent(FlightEventSeverity.Danger, "Log load failed", ex.Message);
                }

                UpdateLogsUi();
            }
        }

        private void BtnExportTelemetryKml_Click(object sender, EventArgs e)
        {
            var exportDialog = new MavlinkLog();
            ThemeManager.ApplyThemeTo(exportDialog);
            exportDialog.Show();
        }

        private void BtnPlayPauseLog_Click(object sender, EventArgs e)
        {
            if (MainV2.comPort?.logplaybackfile == null)
            {
                ModernCommandDialog.ShowNotice(this, "Telemetry log unavailable", "Load a telemetry log before starting playback.", Warning);
                return;
            }

            try
            {
                if (MainV2.instance?.FlightData != null)
                    MainV2.instance.FlightData.BUT_playlog_Click(this, EventArgs.Empty);
                else
                    MainV2.comPort.logreadmode = !MainV2.comPort.logreadmode;
            }
            catch (Exception ex)
            {
                ModernCommandDialog.ShowNotice(this, "Playback control failed", ex.Message, Danger);
                PushEvent(FlightEventSeverity.Danger, "Playback control failed", ex.Message);
            }

            UpdateLogsUi();
        }

        private void TrackPlayback_Scroll(object sender, EventArgs e)
        {
            if (suppressLogUiEvents || MainV2.comPort?.logplaybackfile == null)
                return;

            try
            {
                ClearTrackRequested?.Invoke(this, EventArgs.Empty);
                MainV2.comPort.lastlogread = DateTime.MinValue;
                MainV2.comPort.MAV?.cs?.ResetInternals();

                var stream = MainV2.comPort.logplaybackfile.BaseStream;
                if (stream.CanSeek)
                    stream.Position = (long)(stream.Length * (trackPlayback.Value / 100.0));
            }
            catch (Exception ex)
            {
                ModernCommandDialog.ShowNotice(this, "Playback scrub failed", ex.Message, Danger);
            }

            UpdateLogsUi();
        }

        private void ComboPlaybackSpeed_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (suppressLogUiEvents)
                return;

            if (double.TryParse(comboPlaybackSpeed.SelectedItem?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double speed))
                SetPlaybackSpeed(speed);

            UpdateLogsUi();
        }

        private void UpdateLogsUi()
        {
            if (lblLogFile == null || btnPlayPauseLog == null || trackPlayback == null || lblPlaybackPercent == null ||
                lblPlaybackState == null || comboPlaybackSpeed == null)
                return;

            suppressLogUiEvents = true;

            try
            {
                var reader = MainV2.comPort?.logplaybackfile;
                var stream = reader?.BaseStream;
                bool hasLog = stream != null;

                btnPlayPauseLog.Enabled = hasLog;
                trackPlayback.Enabled = hasLog;
                btnExportTelemetryKml.Enabled = true;

                lblPlaybackState.Text = hasLog
                    ? (MainV2.comPort.logreadmode ? "PLAYING" : "PAUSED")
                    : "STANDBY";
                lblPlaybackState.ForeColor = hasLog
                    ? (MainV2.comPort.logreadmode ? Success : Warning)
                    : TextMuted;
                btnPlayPauseLog.Text = hasLog && MainV2.comPort.logreadmode ? "Pause" : "Play";

                if (hasLog)
                {
                    lblLogFile.Text = GetLogFileName(stream);
                    int percent = stream.Length > 0
                        ? Math.Max(0, Math.Min(100, (int)Math.Round(stream.Position / (double)stream.Length * 100.0)))
                        : 0;
                    trackPlayback.Value = percent;
                    lblPlaybackPercent.Text = percent.ToString(CultureInfo.InvariantCulture) + "%";
                }
                else
                {
                    lblLogFile.Text = "No telemetry log loaded.";
                    trackPlayback.Value = 0;
                    lblPlaybackPercent.Text = "--";
                }

                string playbackSpeed = GetPlaybackSpeed().ToString("0.##", CultureInfo.InvariantCulture);
                if (comboPlaybackSpeed.Items.IndexOf(playbackSpeed) < 0)
                    comboPlaybackSpeed.Items.Add(playbackSpeed);
                comboPlaybackSpeed.SelectedItem = playbackSpeed;
            }
            finally
            {
                suppressLogUiEvents = false;
            }
        }

        private string GetLogFileName(Stream stream)
        {
            if (stream is FileStream fileStream)
                return Path.GetFileName(fileStream.Name);

            return "Telemetry log loaded";
        }

        private double GetPlaybackSpeed()
        {
            var flightData = MainV2.instance?.FlightData;
            if (flightData == null)
                return 1.0;

            if (legacyPlaybackSpeedField == null)
                legacyPlaybackSpeedField = typeof(FlightData).GetField("LogPlayBackSpeed", BindingFlags.Instance | BindingFlags.NonPublic);

            if (legacyPlaybackSpeedField?.GetValue(flightData) is double speed)
                return speed;

            return 1.0;
        }

        private void SetPlaybackSpeed(double speed)
        {
            var flightData = MainV2.instance?.FlightData;
            if (flightData == null)
                return;

            if (legacySetPlaybackSpeedMethod == null)
                legacySetPlaybackSpeedMethod = typeof(FlightData).GetMethod("SetPlaybackSpeed", BindingFlags.Instance | BindingFlags.NonPublic);

            if (legacySetPlaybackSpeedMethod != null)
            {
                legacySetPlaybackSpeedMethod.Invoke(flightData, new object[] { speed });
                return;
            }

            if (legacyPlaybackSpeedField == null)
                legacyPlaybackSpeedField = typeof(FlightData).GetField("LogPlayBackSpeed", BindingFlags.Instance | BindingFlags.NonPublic);
            legacyPlaybackSpeedField?.SetValue(flightData, speed);
        }

        private static string BuildSupplementalMessage(string title, string detail)
        {
            string trimmedTitle = (title ?? "").Trim();
            string trimmedDetail = (detail ?? "").Trim();

            if (trimmedTitle.Length == 0)
                return trimmedDetail;
            if (trimmedDetail.Length == 0)
                return trimmedTitle;

            return $"{trimmedTitle}: {trimmedDetail}";
        }

        private static byte ConvertSeverity(FlightEventSeverity severity)
        {
            switch (severity)
            {
                case FlightEventSeverity.Success:
                    return (byte)MAVLink.MAV_SEVERITY.NOTICE;
                case FlightEventSeverity.Warning:
                    return (byte)MAVLink.MAV_SEVERITY.WARNING;
                case FlightEventSeverity.Danger:
                    return (byte)MAVLink.MAV_SEVERITY.ALERT;
                default:
                    return (byte)MAVLink.MAV_SEVERITY.INFO;
            }
        }

        private void LegacyDoActionClick(object sender, EventArgs e)
        {
            if (!EnsureConnected("Action unavailable", "Connect the vehicle before dispatching mission actions."))
                return;

            FlightData.actions selectedAction = (FlightData.actions)Enum.Parse(typeof(FlightData.actions), legacyActions.CMB_action.Text);

            try
            {
                switch (selectedAction)
                {
                    case FlightData.actions.Format_SD_Card:
                        TryRunCommand(() => MainV2.comPort.doCommandInt(MainV2.comPort.MAV.sysid, MainV2.comPort.MAV.compid, MAVLink.MAV_CMD.STORAGE_FORMAT, 1, 1, 0, 0, 0, 0, 0),
                            "Storage format requested", "SD card format command sent.");
                        return;
                    case FlightData.actions.Trigger_Camera:
                        MainV2.comPort.setDigicamControl(true);
                        PushEvent(FlightEventSeverity.Success, "Camera trigger sent", "Camera trigger command dispatched.");
                        return;
                    case FlightData.actions.Scripting_cmd_stop_and_restart:
                        TryRunCommand(() => MainV2.comPort.doCommandInt(MainV2.comPort.MAV.sysid, MainV2.comPort.MAV.compid, MAVLink.MAV_CMD.SCRIPTING, (int)MAVLink.SCRIPTING_CMD.STOP_AND_RESTART, 0, 0, 0, 0, 0, 0),
                            "Script restart requested", "Scripting restart command sent.");
                        return;
                    case FlightData.actions.Scripting_cmd_stop:
                        TryRunCommand(() => MainV2.comPort.doCommandInt(MainV2.comPort.MAV.sysid, MainV2.comPort.MAV.compid, MAVLink.MAV_CMD.SCRIPTING, (int)MAVLink.SCRIPTING_CMD.STOP, 0, 0, 0, 0, 0, 0),
                            "Script stop requested", "Scripting stop command sent.");
                        return;
                    case FlightData.actions.System_Time:
                        DateTime now = DateTime.UtcNow;
                        DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                        ulong timeUnixUs = Convert.ToUInt64((now - epoch).TotalMilliseconds * 1000);
                        MainV2.comPort.sendPacket(new MAVLink.mavlink_system_time_t { time_unix_usec = timeUnixUs, time_boot_ms = 0 }, MainV2.comPort.sysidcurrent, MainV2.comPort.compidcurrent);
                        PushEvent(FlightEventSeverity.Success, "System time sent", "Vehicle clock sync packet dispatched.");
                        return;
                }

                if (!ModernCommandDialog.ShowConfirmation(this, "Dispatch Action",
                        $"Send {selectedAction.ToString().Replace('_', ' ')} to the vehicle now?", "Send", Warning))
                    return;

                if (selectedAction == FlightData.actions.Preflight_Reboot_Shutdown)
                {
                    MainV2.comPort.doReboot();
                    PushEvent(FlightEventSeverity.Warning, "Reboot requested", "Autopilot reboot command sent.");
                    return;
                }

                if (selectedAction == FlightData.actions.HighLatency_Enable)
                {
                    TryRunCommand(() => MainV2.comPort.doHighLatency(true), "High latency enabled", "High latency mode enabled.");
                    return;
                }

                if (selectedAction == FlightData.actions.HighLatency_Disable)
                {
                    TryRunCommand(() => MainV2.comPort.doHighLatency(false), "High latency disabled", "High latency mode disabled.");
                    return;
                }

                if (selectedAction == FlightData.actions.Toggle_Safety_Switch)
                {
                    byte targetSystem = (byte)MainV2.comPort.sysidcurrent;
                    if (targetSystem == 0)
                    {
                        ModernCommandDialog.ShowNotice(this, "Safety toggle unavailable", "Vehicle system id is not available yet.", Danger);
                        return;
                    }

                    uint customMode = (MainV2.comPort.MAV.cs.sensors_enabled.motor_control && MainV2.comPort.MAV.cs.sensors_enabled.seen) ? 1u : 0u;
                    MainV2.comPort.setMode(new MAVLink.mavlink_set_mode_t { custom_mode = customMode, target_system = targetSystem }, MAVLink.MAV_MODE_FLAG.SAFETY_ARMED);
                    PushEvent(FlightEventSeverity.Warning, "Safety switch toggled", "Safety mode toggle command sent.");
                    return;
                }

                if (selectedAction == FlightData.actions.Engine_Start || selectedAction == FlightData.actions.Engine_Stop)
                {
                    bool start = selectedAction == FlightData.actions.Engine_Start;
                    TryRunCommand(() => MainV2.comPort.doEngineControl((byte)MainV2.comPort.sysidcurrent, (byte)MainV2.comPort.compidcurrent, start),
                        start ? "Engine start requested" : "Engine stop requested",
                        start ? "Engine start command dispatched." : "Engine stop command dispatched.");
                    return;
                }

                int param1 = 0;
                int param2 = 0;
                int param3 = 1;

                if (selectedAction == FlightData.actions.Preflight_Calibration)
                {
                    if (MainV2.comPort.MAV.cs.firmware == Firmwares.ArduCopter2)
                        param1 = 1;
                    param3 = 1;
                }
                else if (selectedAction == FlightData.actions.Battery_Reset)
                {
                    param1 = 0xff;
                    param2 = 100;
                    param3 = 0;
                }

                MAVLink.MAV_CMD command;
                try
                {
                    command = (MAVLink.MAV_CMD)Enum.Parse(typeof(MAVLink.MAV_CMD), selectedAction.ToString().ToUpper(CultureInfo.InvariantCulture));
                }
                catch (ArgumentException)
                {
                    command = (MAVLink.MAV_CMD)Enum.Parse(typeof(MAVLink.MAV_CMD), "DO_START_" + selectedAction.ToString().ToUpper(CultureInfo.InvariantCulture));
                }

                TryRunCommand(() => MainV2.comPort.doCommand(command, param1, param2, param3, 0, 0, 0, 0),
                    $"{selectedAction.ToString().Replace('_', ' ')} sent",
                    "Mission action command dispatched.");
            }
            catch (Exception ex)
            {
                PushEvent(FlightEventSeverity.Danger, "Action failed", ex.Message);
                ModernCommandDialog.ShowNotice(this, "Action failed", ex.Message, Danger);
            }
        }

        private void LegacySetWaypointClick(object sender, EventArgs e)
        {
            if (!EnsureConnected("Set waypoint", "Connect the vehicle before changing the active mission waypoint."))
                return;

            TryRunCommand(() => MainV2.comPort.setWPCurrent(MainV2.comPort.MAV.sysid, MainV2.comPort.MAV.compid, (ushort)legacyActions.CMB_setwp.SelectedIndex),
                "Waypoint updated", $"Vehicle target waypoint set to {legacyActions.CMB_setwp.SelectedIndex}.");
        }

        private void LegacySetMountClick(object sender, EventArgs e)
        {
            if (!EnsureConnected("Set mount", "Connect the vehicle before changing mount configuration."))
                return;

            try
            {
                int selected = Convert.ToInt32(legacyActions.CMB_mountmode.SelectedValue);
                if (MainV2.comPort.MAV.param.ContainsKey("MNT_MODE"))
                    MainV2.comPort.setParam((byte)MainV2.comPort.sysidcurrent, (byte)MainV2.comPort.compidcurrent, "MNT_MODE", selected);
                else
                    MainV2.comPort.doCommand((byte)MainV2.comPort.sysidcurrent, (byte)MainV2.comPort.compidcurrent, MAVLink.MAV_CMD.DO_MOUNT_CONTROL, 0, 0, 0, 0, 0, 0, selected);

                PushEvent(FlightEventSeverity.Success, "Mount mode updated", "Payload mount mode has been updated.");
            }
            catch (Exception ex)
            {
                ModernCommandDialog.ShowNotice(this, "Set mount failed", ex.Message, Danger);
                PushEvent(FlightEventSeverity.Danger, "Mount mode failed", ex.Message);
            }
        }

        private void LegacyRestartMissionClick(object sender, EventArgs e)
        {
            if (!EnsureConnected("Restart mission", "Connect the vehicle before restarting the mission."))
                return;

            TryRunCommand(() => MainV2.comPort.setWPCurrent(MainV2.comPort.MAV.sysid, MainV2.comPort.MAV.compid, 0),
                "Mission restarted", "Mission index reset to home.");
        }

        private void LegacyResumeMissionClick(object sender, EventArgs e)
        {
            if (!EnsureConnected("Resume mission", "Connect the vehicle before resuming a mission."))
                return;

            string lastWp = Math.Max(1, MainV2.comPort.MAV.cs.lastautowp).ToString(CultureInfo.InvariantCulture);
            if (DialogResult.OK != InputBox.Show("Resume at", "Resume mission at waypoint#", ref lastWp))
                return;

            try
            {
                int timeout = 0;
                int lastWpNo = int.Parse(lastWp, CultureInfo.InvariantCulture);
                var lastWpData = MainV2.comPort.getWP((ushort)lastWpNo);
                var commands = new List<MissionPlanner.Utilities.Locationwp>();
                ushort wpCount = MainV2.comPort.getWPCount();

                for (ushort index = 0; index < wpCount; index++)
                {
                    var wpData = MainV2.comPort.getWP(index);
                    if (index < lastWpNo && index != 0)
                    {
                        if (wpData.id != (ushort)MAVLink.MAV_CMD.TAKEOFF && wpData.id < (ushort)MAVLink.MAV_CMD.LAST)
                            continue;
                        if (wpData.id > (ushort)MAVLink.MAV_CMD.DO_LAST)
                            continue;
                    }

                    commands.Add(wpData);
                }

                ushort wpNo = 0;
                MainV2.comPort.setWPTotal((ushort)commands.Count);
                foreach (var location in commands)
                {
                    var answer = MainV2.comPort.setWP(location, wpNo, (MAVLink.MAV_FRAME)location.frame);
                    if (answer != MAVLink.MAV_MISSION_RESULT.MAV_MISSION_ACCEPTED)
                    {
                        ModernCommandDialog.ShowNotice(this, "Resume mission failed", $"Upload failed for waypoint {wpNo}.", Danger);
                        return;
                    }

                    wpNo++;
                }

                MainV2.comPort.setWPACK();
                FlightPlanner.instance?.BUT_read_Click(this, EventArgs.Empty);
                MainV2.comPort.setWPCurrent(MainV2.comPort.MAV.sysid, MainV2.comPort.MAV.compid, 1);

                if (MainV2.comPort.MAV.cs.firmware == Firmwares.ArduCopter2)
                {
                    while (!string.Equals(MainV2.comPort.MAV.cs.mode, "GUIDED", StringComparison.OrdinalIgnoreCase))
                    {
                        MainV2.comPort.setMode("GUIDED");
                        Thread.Sleep(1000);
                        Application.DoEvents();
                        if (++timeout > 30)
                            throw new TimeoutException("Timed out switching to GUIDED.");
                    }

                    timeout = 0;
                    while (!MainV2.comPort.MAV.cs.armed)
                    {
                        MainV2.comPort.doARM(true);
                        Thread.Sleep(1000);
                        Application.DoEvents();
                        if (++timeout > 30)
                            throw new TimeoutException("Timed out arming vehicle.");
                    }

                    timeout = 0;
                    while (MainV2.comPort.MAV.cs.alt < (lastWpData.alt - 2))
                    {
                        if (!MainV2.comPort.doCommand((byte)MainV2.comPort.sysidcurrent, (byte)MainV2.comPort.compidcurrent, MAVLink.MAV_CMD.TAKEOFF, 0, 0, 0, 0, 0, 0, lastWpData.alt))
                            throw new InvalidOperationException("Takeoff command was not acknowledged.");

                        Thread.Sleep(1000);
                        Application.DoEvents();
                        if (++timeout > 40)
                            throw new TimeoutException("Timed out climbing to resume altitude.");
                    }
                }

                timeout = 0;
                while (!string.Equals(MainV2.comPort.MAV.cs.mode, "AUTO", StringComparison.OrdinalIgnoreCase))
                {
                    MainV2.comPort.setMode("AUTO");
                    Thread.Sleep(1000);
                    Application.DoEvents();
                    if (++timeout > 30)
                        throw new TimeoutException("Timed out switching back to AUTO.");
                }

                PushEvent(FlightEventSeverity.Success, "Mission resumed", $"Resume workflow completed from waypoint {lastWpNo}.");
            }
            catch (Exception ex)
            {
                ModernCommandDialog.ShowNotice(this, "Resume mission failed", ex.Message, Danger);
                PushEvent(FlightEventSeverity.Danger, "Resume mission failed", ex.Message);
            }
        }

        private void LegacySetSpeedClick(object sender, EventArgs e)
        {
            if (!EnsureConnected("Set speed", "Connect the vehicle before applying speed changes."))
                return;

            TryRunCommand(() => MainV2.comPort.doCommandAsync(MainV2.comPort.MAV.sysid, MainV2.comPort.MAV.compid, MAVLink.MAV_CMD.DO_CHANGE_SPEED, 0, (float)legacyActions.modifyandSetSpeed.Value, 0, 0, 0, 0, 0).GetAwaiter().GetResult(),
                "Speed updated", $"Target speed set to {legacyActions.modifyandSetSpeed.Value:0}.");
        }

        private void LegacySetAltitudeClick(object sender, EventArgs e)
        {
            if (!EnsureConnected("Set altitude", "Connect the vehicle before applying altitude changes."))
                return;

            try
            {
                int newAltitude = (int)legacyActions.modifyandSetAlt.Value;
                MainV2.comPort.setNewWPAlt(new MissionPlanner.Utilities.Locationwp { alt = newAltitude / CurrentState.multiplieralt });
                PushEvent(FlightEventSeverity.Success, "Altitude updated", $"Target altitude set to {newAltitude}.");
            }
            catch (Exception ex)
            {
                ModernCommandDialog.ShowNotice(this, "Set altitude failed", ex.Message, Danger);
                PushEvent(FlightEventSeverity.Danger, "Altitude update failed", ex.Message);
            }
        }

        private void LegacySetLoiterRadiusClick(object sender, EventArgs e)
        {
            if (!EnsureConnected("Set loiter radius", "Connect the vehicle before changing loiter radius."))
                return;

            int radius = (int)legacyActions.modifyandSetLoiterRad.Value;
            TryRunCommand(() => MainV2.comPort.setParam(new[] { "LOITER_RAD", "WP_LOITER_RAD" }, radius / CurrentState.multiplierdist),
                "Loiter radius updated", $"Loiter radius set to {radius}.");
        }

        private void LegacyToggleHomeAltitudeClick(object sender, EventArgs e)
        {
            if (MainV2.comPort?.MAV?.cs == null)
                return;

            if (MainV2.comPort.MAV.cs.altoffsethome != 0)
                MainV2.comPort.MAV.cs.altoffsethome = 0;
            else
                MainV2.comPort.MAV.cs.altoffsethome = (float)(-MainV2.comPort.MAV.cs.HomeAlt / CurrentState.multiplieralt);

            PushEvent(FlightEventSeverity.Info, "Home altitude toggled", "Home altitude offset state updated.");
        }

        private void LegacyOpenRawSensorClick(object sender, EventArgs e)
        {
            Form sensor = new RAW_Sensor();
            ThemeManager.ApplyThemeTo(sensor);
            sensor.Show();
        }

        private void LegacyOpenJoystickClick(object sender, EventArgs e)
        {
            new JoystickSetup().ShowUserControl();
        }

        private void LegacySendMessageClick(object sender, EventArgs e)
        {
            if (!EnsureConnected("Send message", "Connect the vehicle before sending a status text message."))
                return;

            string text = "";
            if (DialogResult.Cancel == InputBox.Show("Enter Message", "Enter Message to be logged", ref text))
                return;

            try
            {
                MainV2.comPort.send_text(5, text);
                PushEvent(FlightEventSeverity.Success, "Vehicle message sent", text);
            }
            catch (Exception ex)
            {
                ModernCommandDialog.ShowNotice(this, "Send message failed", ex.Message, Danger);
                PushEvent(FlightEventSeverity.Danger, "Message send failed", ex.Message);
            }
        }

        private void LegacyClearTrackClick(object sender, EventArgs e)
        {
            ClearTrackRequested?.Invoke(this, EventArgs.Empty);
            PushEvent(FlightEventSeverity.Info, "Track cleared", "Mission breadcrumb track was cleared.");
        }

        private void LegacyRebootClick(object sender, EventArgs e)
        {
            if (!EnsureConnected("Reboot autopilot", "Connect the vehicle before rebooting the autopilot."))
                return;

            if (!ModernCommandDialog.ShowConfirmation(this, "Reboot autopilot", "Reboot the connected autopilot now?", "Reboot", Danger))
                return;

            TryRunCommand(() => MainV2.comPort.doReboot(false, true), "Autopilot reboot requested", "Autopilot reboot command sent.");
        }

        private void LegacyAbortLandClick(object sender, EventArgs e)
        {
            if (!EnsureConnected("Abort landing", "Connect the vehicle before aborting a landing sequence."))
                return;

            TryRunCommand(() => MainV2.comPort.doAbortLand(), "Abort land requested", "Abort landing command sent.");
        }

        private void UpdatePreflight(CurrentState cs, bool connected)
        {
            if (cs == null)
            {
                cardLink.UpdateState("OFFLINE", connected ? "SYNC" : "OFFLINE", connected ? "Waiting for telemetry state sync." : "Vehicle stream unavailable.", Danger);
                cardGps.UpdateState("NO FIX", "PENDING", "Awaiting GPS telemetry.", Warning);
                cardBattery.UpdateState("--", "PENDING", "Awaiting battery telemetry.", Danger);
                cardPreflight.UpdateState("CHECK", "PENDING", "Pre-arm status unavailable.", Warning);
                cardEkf.UpdateState("WAIT", "PENDING", "Estimator telemetry unavailable.", Warning);
                cardFailsafe.UpdateState("CLEAR", "STANDBY", "Failsafe state unavailable.", Success);
                return;
            }

            cardLink.UpdateState($"{Math.Max(0, Math.Min(100, (int)cs.linkqualitygcs))}%", connected ? "ONLINE" : "OFFLINE",
                $"RX {cs.rxrssi}%  |  GCS link {cs.linkqualitygcs}%", connected ? Success : Danger);
            cardGps.UpdateState($"{(int)Math.Round(cs.satcount)} SAT", cs.gpsstatus >= 3 ? "3D FIX" : "LIMITED",
                $"Fix {(int)Math.Round(cs.gpsstatus)}  |  HDOP {cs.gpshdop:0.0}", cs.gpsstatus >= 3 ? Success : Warning);
            cardBattery.UpdateState($"{cs.battery_remaining:F0}%", cs.battery_remaining >= 30 ? "NOMINAL" : "WATCH",
                $"{cs.battery_voltage:0.0} V  |  {cs.watts:0} W", cs.battery_remaining >= 30 ? Success : Danger);
            cardPreflight.UpdateState(cs.prearmstatus ? "READY" : "CHECK", cs.prearmstatus ? "CLEAR" : "HOLD",
                cs.prearmstatus ? "Pre-arm checks clear." : FirstLine(cs.messageHigh, "Resolve pre-arm advisory before launch."),
                cs.prearmstatus ? Success : Warning);
            cardEkf.UpdateState(GetEkfState(cs.ekfstatus), GetEkfChip(cs.ekfstatus),
                $"Estimator index {cs.ekfstatus:0.00}", GetEkfColor(cs.ekfstatus));
            cardFailsafe.UpdateState(cs.failsafe ? "ACTIVE" : "CLEAR", cs.failsafe ? "ALERT" : "NORMAL",
                cs.failsafe ? FirstLine(cs.messageHigh, "Failsafe condition reported.") : "No active failsafe condition.",
                cs.failsafe ? Danger : Success);
        }

        private void HandleArmClick(object sender, EventArgs e)
        {
            if (!EnsureConnected("Arm / Disarm", "Connect the vehicle before sending arm or disarm commands.")) return;
            bool armTarget = !(lastTelemetry?.armed ?? false);
            if (!ModernCommandDialog.ShowConfirmation(this, armTarget ? "Arm Vehicle" : "Disarm Vehicle",
                    armTarget ? "Arm the vehicle and enable mission-critical controls?" : "Disarm the vehicle and return it to a safe state?",
                    armTarget ? "Arm" : "Disarm", armTarget ? Info : Warning)) return;
            TryRunCommand(() => MainV2.comPort.doARM(armTarget), armTarget ? "Vehicle arm command sent" : "Vehicle disarm command sent",
                armTarget ? "The flight deck requested vehicle arming." : "The flight deck requested vehicle disarm.");
        }

        private void HandleTakeoffClick(object sender, EventArgs e)
        {
            if (!EnsureConnected("Guided Takeoff", "Connect the vehicle before dispatching a guided takeoff.")) return;
            decimal? altitude = ModernCommandDialog.ShowTakeoffPrompt(this, 15);
            if (altitude == null) return;
            TryRunCommand(() =>
            {
                if (!(lastTelemetry?.armed ?? false))
                    MainV2.comPort.doARM(true);
                MainV2.comPort.setMode("GUIDED");
                return MainV2.comPort.doCommand(MAVLink.MAV_CMD.TAKEOFF, 0, 0, 0, 0, 0, 0, (float)altitude.Value);
            }, "Guided takeoff sent", $"Climb to {altitude.Value:F0} m requested in GUIDED.");
        }

        private void HandleRtlClick(object sender, EventArgs e)
        {
            if (!EnsureReadyForRecovery("Return To Launch")) return;
            if (!ModernCommandDialog.ShowConfirmation(this, "Return To Launch",
                    "Command the aircraft to recover to its configured home position now?", "Send RTL", Success)) return;
            TryRunCommand(() => { MainV2.comPort.setMode("RTL"); return true; }, "RTL requested", "Recovery to launch has been dispatched.");
        }

        private void HandleLandClick(object sender, EventArgs e)
        {
            if (!EnsureReadyForRecovery("Land Now")) return;
            if (!ModernCommandDialog.ShowConfirmation(this, "Land Now",
                    "Command the aircraft to enter LAND immediately?", "Send LAND", Danger)) return;
            TryRunCommand(() => { MainV2.comPort.setMode("LAND"); return true; }, "Landing requested", "Immediate landing command has been dispatched.");
        }

        private bool EnsureConnected(string title, string message)
        {
            if (lastConnected) return true;
            ModernCommandDialog.ShowNotice(this, title, message, Danger);
            return false;
        }

        private bool EnsureReadyForRecovery(string title)
        {
            if (!EnsureConnected(title, "Connect the vehicle before dispatching recovery modes.")) return false;
            if (lastTelemetry?.armed == true) return true;
            ModernCommandDialog.ShowNotice(this, title, "Recovery commands are available once the aircraft is armed.", Warning);
            return false;
        }

        private void TryRunCommand(Func<bool> command, string eventTitle, string eventDetail)
        {
            try
            {
                bool success = command();
                PushEvent(success ? FlightEventSeverity.Success : FlightEventSeverity.Danger, eventTitle,
                    success ? eventDetail : "The flight controller did not acknowledge the command.");
                if (!success)
                    ModernCommandDialog.ShowNotice(this, "Command not acknowledged", "The flight controller did not acknowledge the command.", Danger);
            }
            catch (Exception ex)
            {
                PushEvent(FlightEventSeverity.Danger, "Command failed", ex.Message);
                ModernCommandDialog.ShowNotice(this, "Command failed", ex.Message, Danger);
            }
        }

        private static string FirstLine(string text, string fallback)
        {
            string normalized = string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
            int index = normalized.IndexOfAny(new[] { '\r', '\n' });
            return index > 0 ? normalized.Substring(0, index) : normalized;
        }

        private static string GetEkfState(float value) => value > 0.8f ? "ALERT" : value > 0.5f ? "WATCH" : "HEALTHY";
        private static string GetEkfChip(float value) => value > 0.8f ? "RED" : value > 0.5f ? "WATCH" : "GOOD";
        private Color GetEkfColor(float value) => value > 0.8f ? Danger : value > 0.5f ? Warning : Success;
    }

    internal sealed class PreflightStatusCard : Control
    {
        private readonly Font labelFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        private readonly Font valueFont = new Font("Segoe UI", 12f, FontStyle.Bold);
        private readonly Font detailFont = new Font("Segoe UI", 8.25f, FontStyle.Regular);
        private readonly Font chipFont = new Font("Segoe UI", 7f, FontStyle.Bold);
        private readonly Color Surface = Color.FromArgb(20, 26, 37);
        private readonly Color TextPrimary = Color.FromArgb(232, 236, 243);
        private readonly Color TextMuted = Color.FromArgb(140, 151, 171);

        public string Title { get; set; } = "";
        public string ValueText { get; private set; } = "";
        public string ChipText { get; private set; } = "";
        public string DetailText { get; private set; } = "";
        public Color AccentColor { get; set; } = Color.FromArgb(74, 190, 225);
        public Color StateColor { get; private set; } = Color.FromArgb(74, 190, 225);

        public PreflightStatusCard()
        {
            DoubleBuffered = true;
            Margin = new Padding(0, 0, 10, 10);
            MinimumSize = new Size(110, 96);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
        }

        public void UpdateState(string value, string chip, string detail, Color stateColor)
        {
            ValueText = value ?? "";
            ChipText = chip ?? "";
            DetailText = detail ?? "";
            StateColor = stateColor;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            ModernUiPainter.FillRoundedRectangle(e.Graphics, Surface, bounds, 14);
            ModernUiPainter.DrawRoundedRectangle(e.Graphics, Color.FromArgb(120, AccentColor.R, AccentColor.G, AccentColor.B), 1f, bounds, 14);
            ModernUiPainter.FillRoundedRectangle(e.Graphics, AccentColor, new Rectangle(bounds.X + 12, bounds.Y + 10, 44, 3), 2);
            Rectangle chipRect = new Rectangle(bounds.Right - 70, bounds.Y + 8, 58, 18);
            ModernUiPainter.FillRoundedRectangle(e.Graphics, Color.FromArgb(34, StateColor.R, StateColor.G, StateColor.B), chipRect, 8);
            ModernUiPainter.DrawRoundedRectangle(e.Graphics, Color.FromArgb(120, StateColor.R, StateColor.G, StateColor.B), 1f, chipRect, 8);

            using (var labelBrush = new SolidBrush(TextMuted))
            using (var valueBrush = new SolidBrush(Blend(TextPrimary, AccentColor, 0.38f)))
            using (var detailBrush = new SolidBrush(TextMuted))
            using (var chipBrush = new SolidBrush(TextPrimary))
            {
                e.Graphics.DrawString(Title, labelFont, labelBrush, new RectangleF(bounds.X + 12, bounds.Y + 20, bounds.Width - 94, 14));
                e.Graphics.DrawString(ValueText, valueFont, valueBrush, new RectangleF(bounds.X + 12, bounds.Y + 40, bounds.Width - 24, 22));
                e.Graphics.DrawString(DetailText, detailFont, detailBrush, new RectangleF(bounds.X + 12, bounds.Bottom - 34, bounds.Width - 24, 24));
                e.Graphics.DrawString(ChipText, chipFont, chipBrush, new RectangleF(chipRect.X + 6, chipRect.Y + 3, chipRect.Width - 12, 12));
            }
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
    }
}
