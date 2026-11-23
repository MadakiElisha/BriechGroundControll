using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using log4net;
using MissionPlanner.ArduPilot;
using MissionPlanner.Controls;
using MissionPlanner.Controls.Fly;
using MissionPlanner.Utilities;
using T = MissionPlanner.Utilities.DesignTokens;

namespace MissionPlanner.GCSViews
{
    /// <summary>
    /// The Go / No-Go preflight board on the new Fly screen (design canvas board 5): automatic checks from the
    /// vehicle, mission and airspace checks, and the crew's sign-off list, with one verdict. Arming from the Fly
    /// screen asks for an explicit override unless the verdict is GO. The rules live in
    /// <see cref="PreflightChecks"/>; this view gathers the inputs and shows the result.
    /// </summary>
    [PreventTheming]
    public class FlyPreflightPanel : Panel
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(FlyPreflightPanel));

        public const string KeyCrewList = "preflight_crew_list";     // one item per line; empty = airframe default
        public const string KeyOperator = "preflight_operator";
        public const string KeyLimits = "preflight_limits";          // hdop;sats;battery%;terrain m;wind m/s

        /// <summary>Ticks survive leaving and re-opening the board, but not a new vehicle or a restart.</summary>
        private static readonly HashSet<string> ticked = new HashSet<string>();
        private static string tickedFor;

        /// <summary>The latest verdict, for the Fly header and the Arm button.</summary>
        public static PreflightChecks.Result Latest { get; private set; }

        public event EventHandler BackRequested;

        private readonly Label title, subtitle;
        private readonly VerdictBox verdict;
        private readonly TokenCard vehicleCard, missionCard, crewCard;
        private readonly FlowLayoutPanel vehicleRows, missionRows, crewRows;
        private readonly TokenButton save, editList, resetTicks;
        private readonly Label armNote;
        private readonly TableLayoutPanel columns;
        private readonly ToolTip tips = new ToolTip();
        private string crewSignature;

        public FlyPreflightPanel()
        {
            DoubleBuffered = true;
            Padding = new Padding(T.Space4, T.Space3, T.Space4, T.Space3);

            // the header's Preflight button turns into "Back to Fly" while this board is open
            var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 96, ColumnCount = 2, BackColor = Color.Transparent };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            var titles = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            title = new Label { Dock = DockStyle.Top, Height = 40, Font = T.Title, TextAlign = ContentAlignment.BottomLeft, AutoEllipsis = true, UseMnemonic = false };
            subtitle = new Label { Dock = DockStyle.Top, Height = 26, Font = T.Detail, TextAlign = ContentAlignment.TopLeft, AutoEllipsis = true, UseMnemonic = false };
            titles.Controls.Add(subtitle);
            titles.Controls.Add(title);
            verdict = new VerdictBox { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
            header.Controls.Add(titles, 0, 0);
            header.Controls.Add(verdict, 1, 0);

            columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, BackColor = Color.Transparent, Padding = new Padding(0, T.Space3, 0, 0) };
            for (int i = 0; i < 3; i++)
                columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));

            vehicleCard = MakeColumn("Automatic: from the vehicle", out vehicleRows);
            missionCard = MakeColumn("Mission and airspace", out missionRows);
            crewCard = MakeColumn("Crew sign-off", out crewRows);

            var crewButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44 + T.Space2 + 36, BackColor = Color.Transparent, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            editList = new TokenButton { Text = "Edit list…", Width = 104, Height = 44, Margin = new Padding(0, 0, T.Space2, T.Space2) };
            resetTicks = new TokenButton { Text = "Untick all", Width = 104, Height = 44, Margin = new Padding(0, 0, T.Space2, T.Space2) };
            save = new TokenButton { Text = "Save report", Width = 124, Height = 44, Margin = new Padding(0, 0, 0, T.Space2) };
            editList.Click += (s, e) => EditList();
            resetTicks.Click += (s, e) =>
            {
                ticked.Clear();
                crewSignature = null;
                UpdateView();
            };
            save.Click += (s, e) => SaveReport();
            armNote = new Label { Width = 400, Height = 30, Font = T.Detail, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Text = "Arming asks you to confirm an override unless the verdict is GO." };
            crewButtons.Controls.Add(editList);
            crewButtons.Controls.Add(resetTicks);
            crewButtons.Controls.Add(save);
            crewButtons.Controls.Add(armNote);
            crewButtons.Resize += (s, e) => armNote.Width = Math.Max(100, crewButtons.ClientSize.Width - 4);
            crewCard.Controls.Add(crewButtons);
            crewRows.BringToFront();

            columns.Controls.Add(vehicleCard, 0, 0);
            columns.Controls.Add(missionCard, 1, 0);
            columns.Controls.Add(crewCard, 2, 0);

            Controls.Add(columns);
            Controls.Add(header);
            ApplyTokens();
        }

        private TokenCard MakeColumn(string heading, out FlowLayoutPanel rows)
        {
            var card = new TokenCard { Title = heading, Dock = DockStyle.Fill, Margin = new Padding(0, 0, T.Space3, 0) };
            rows = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.Transparent
            };
            var r = rows;
            rows.Resize += (s, e) => FitRows(r);
            card.Controls.Add(rows);
            return card;
        }

        /// <summary>Rows fill the column's width, leaving room for the scroll bar so there's never a sideways one.</summary>
        private static void FitRows(FlowLayoutPanel rows)
        {
            var w = rows.ClientSize.Width - 2;
            var needsBar = rows.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical) > rows.ClientSize.Height;
            if (needsBar && !rows.VerticalScroll.Visible)
                w -= SystemInformation.VerticalScrollBarWidth;
            w = Math.Max(120, w);
            foreach (Control c in rows.Controls)
                if (c.Width != w)
                    c.Width = w;
        }

        public void ApplyTokens()
        {
            var p = T.Current;
            BackColor = p.AppBackground;
            foreach (var l in new[] { title, subtitle, armNote })
                l.BackColor = l == armNote ? p.Surface : p.AppBackground;
            title.ForeColor = p.TextPrimary;
            subtitle.ForeColor = p.TextMuted;
            armNote.ForeColor = p.TextMuted;
            Invalidate(true);
        }

        // ---------------- inputs ----------------

        private static string[] CrewList(FlightCommandProfile.Family family)
        {
            var saved = Settings.Instance[KeyCrewList];
            if (!string.IsNullOrWhiteSpace(saved))
                return saved.Split(new[] { '\n', '|' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            return PreflightChecks.DefaultCrewList(family);
        }

        private static PreflightChecks.Limits LoadLimits()
        {
            var l = new PreflightChecks.Limits();
            var parts = (Settings.Instance[KeyLimits] ?? "").Split(';');
            double v;
            if (parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0) l.MaxHdop = v;
            if (parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0) l.MinSats = (int)v;
            if (parts.Length > 2 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0) l.MinBatteryPct = v;
            if (parts.Length > 3 && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 0) l.MinTerrainClearanceM = v;
            if (parts.Length > 4 && double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0) l.MaxWindMs = v;
            return l;
        }

        /// <summary>Gather the board's inputs from the live link (or a preview sample).</summary>
        public static PreflightChecks.Inputs BuildInputs(CurrentState previewState, FlightCommandProfile.Family previewFamily)
        {
            var port = MainV2.comPort;
            var mav = port?.MAV;
            var preview = previewState != null;
            var cs = preview ? previewState : mav?.cs;
            var family = preview ? previewFamily : FlightCommands.CurrentFamily();
            var x = new PreflightChecks.Inputs
            {
                Connected = preview || (port?.BaseStream != null && port.BaseStream.IsOpen),
                Family = family,
                Limits = LoadLimits()
            };

            var vehicleKey = preview ? "preview" : $"{mav?.sysid}:{mav?.aptype}";
            if (tickedFor != vehicleKey)
            {
                ticked.Clear();
                tickedFor = vehicleKey;
            }

            var crew = CrewList(family);
            x.Crew = crew.Select(c => (c, ticked.Contains(c))).ToList();
            if (cs == null || !x.Connected)
                return x;

            x.Armed = cs.armed;
            x.PrearmOk = preview || cs.prearmstatus;
            x.PrearmReported = preview || cs.sensors_present.prearm || cs.sensors_enabled.prearm;
            x.GpsFix = (int)cs.gpsstatus;
            x.Hdop = cs.gpshdop;
            x.Sats = (int)cs.satcount;
            x.EkfVariance = cs.ekfstatus;
            x.CompassVariance = cs.ekfcompv;
            x.BatteryPct = cs.battery_remaining > 0 || cs.battery_voltage > 0 ? cs.battery_remaining : -1;
            x.BatteryVoltage = cs.battery_voltage;
            x.Airspeed = cs.airspeed / Math.Max(1e-6, CurrentState.multiplierspeed == 0 ? 1 : CurrentState.multiplierspeed);
            x.Groundspeed = cs.groundspeed / Math.Max(1e-6, CurrentState.multiplierspeed == 0 ? 1 : CurrentState.multiplierspeed);
            x.WindMs = cs.wind_vel / Math.Max(1e-6, CurrentState.multiplierspeed == 0 ? 1 : CurrentState.multiplierspeed);
            x.HomeAltAmsl = cs.HomeAlt;

            if (preview)
            {
                // a believable board for screenshots: one terrain item and one crew item need attention
                var sample = new Dictionary<string, double> { { "THR_FAILSAFE", 1 }, { "FS_GCS_ENABL", 1 }, { "BATT_FS_LOW_ACT", 2 }, { "BATT_LOW_VOLT", 21 }, { "FENCE_ENABLE", 1 }, { "ARSPD_TYPE", 1 } };
                x.Param = n => sample.TryGetValue(n, out var v) ? v : (double?)null;
                x.ParamsReceived = x.ParamsTotal = 1412;
                x.HasAirspeedSensor = true;
                x.Airspeed = 0.4;
                x.Groundspeed = 0;
                x.GpsFix = 6;
                x.Hdop = 0.7;
                x.BatteryPct = 97;
                x.HomeAltAmsl = 300;
                x.Mission = new List<PreflightChecks.Waypoint>
                {
                    new PreflightChecks.Waypoint { Command = 16 },
                    new PreflightChecks.Waypoint { Command = 84, Lat = 8.893, Lng = 7.264, Alt = 40, Frame = 3 },
                    new PreflightChecks.Waypoint { Command = 16, Lat = 8.896, Lng = 7.270, Alt = 120, Frame = 3 },
                    new PreflightChecks.Waypoint { Command = 16, Lat = 8.899, Lng = 7.275, Alt = 80, Frame = 3 },
                    new PreflightChecks.Waypoint { Command = 85, Lat = 8.893, Lng = 7.264, Alt = 0, Frame = 3 }
                };
                x.Terrain = (lat, lng) => lat > 8.898 ? 361 : 305;
                x.RallyPoints = 1;
                if (ticked.Count == 0 && crew.Length > 1)
                    foreach (var c in crew.Where(c => !c.StartsWith("Pitot")))
                        ticked.Add(c);
                x.Crew = crew.Select(c => (c, ticked.Contains(c))).ToList();
                return x;
            }

            try
            {
                var plist = mav.param;
                x.Param = n => plist.ContainsKey(n) ? (double?)plist[n].Value : null;
                x.ParamsReceived = plist.TotalReceived;
                x.ParamsTotal = plist.TotalReported;
                x.HasAirspeedSensor = (x.Param("ARSPD_TYPE") ?? 0) > 0 && (x.Param("ARSPD_USE") ?? 1) > 0;
                x.Mission = mav.wps.OrderBy(k => k.Key).Select(k => new PreflightChecks.Waypoint
                {
                    Command = k.Value.command,
                    Lat = k.Value.x / 1e7,
                    Lng = k.Value.y / 1e7,
                    Alt = k.Value.z,
                    Frame = k.Value.frame
                }).ToList();
                x.RallyPoints = mav.rallypoints.Count;
                x.Terrain = (lat, lng) =>
                {
                    var t = srtm.getAltitude(lat, lng);
                    return t.currenttype == srtm.tiletype.valid ? t.alt : (double?)null;
                };
            }
            catch (Exception ex)
            {
                log.Debug(ex);
            }

            return x;
        }

        // ---------------- view ----------------

        private CurrentState previewState;
        private FlightCommandProfile.Family previewFamily;

        public void SetPreview(CurrentState sample, FlightCommandProfile.Family family)
        {
            previewState = sample;
            previewFamily = family;
        }

        /// <summary>Re-run the checks; cheap enough for the Fly screen's timer (terrain lookups are cached).</summary>
        public static PreflightChecks.Result Evaluate(CurrentState previewState, FlightCommandProfile.Family previewFamily)
        {
            try
            {
                Latest = PreflightChecks.Evaluate(BuildInputs(previewState, previewFamily));
            }
            catch (Exception ex)
            {
                log.Debug(ex);
            }

            return Latest;
        }

        public void UpdateView()
        {
            var r = Evaluate(previewState, previewFamily);
            if (r == null)
                return;
            var mav = MainV2.comPort?.MAV;
            var family = previewState != null ? previewFamily : FlightCommands.CurrentFamily();
            var familyName = family == FlightCommandProfile.Family.QuadPlane ? "Quadplane"
                : family == FlightCommandProfile.Family.FixedWing ? "Plane"
                : family == FlightCommandProfile.Family.Multirotor ? "Multirotor"
                : family == FlightCommandProfile.Family.Helicopter ? "Helicopter"
                : family == FlightCommandProfile.Family.Rover ? "Rover" : "Vehicle";
            title.Text = $"Preflight: {familyName}" + (mav != null && previewState == null && mav.sysid > 0 ? $" · system {mav.sysid}" : "");
            var op = Settings.Instance[KeyOperator];
            subtitle.Text = (string.IsNullOrWhiteSpace(op) ? "" : "Operator " + op + " · ") + $"checked {DateTime.Now:HH:mm:ss} · re-checks every second";
            verdict.Set(r.Verdict, r.Summary);

            Fill(vehicleRows, r.Items.Where(i => i.Section == PreflightChecks.Section.Vehicle).ToList());
            Fill(missionRows, r.Items.Where(i => i.Section == PreflightChecks.Section.Mission).ToList());

            var crew = r.Items.Where(i => i.Section == PreflightChecks.Section.Crew).ToList();
            var signature = string.Join("\n", crew.Select(c => c.Text));
            if (signature != crewSignature)
            {
                crewSignature = signature;
                crewRows.SuspendLayout();
                foreach (Control c in crewRows.Controls.Cast<Control>().ToList())
                    c.Dispose();
                crewRows.Controls.Clear();
                foreach (var item in crew)
                {
                    var row = new CrewRow { Text = item.Text, Width = Math.Max(120, crewRows.ClientSize.Width - 2), Margin = new Padding(0, 0, 0, T.Space2) };
                    row.Toggled += (s, e) =>
                    {
                        var cr = (CrewRow)s;
                        if (cr.Checked) ticked.Add(cr.Text);
                        else ticked.Remove(cr.Text);
                        UpdateView();
                    };
                    crewRows.Controls.Add(row);
                }

                crewRows.ResumeLayout(true);
            }

            for (int i = 0; i < crew.Count && i < crewRows.Controls.Count; i++)
                ((CrewRow)crewRows.Controls[i]).Checked = crew[i].Status == PreflightChecks.Status.Pass;
            FitRows(vehicleRows);
            FitRows(missionRows);
            FitRows(crewRows);
        }

        private void Fill(FlowLayoutPanel rows, List<PreflightChecks.Item> items)
        {
            while (rows.Controls.Count < items.Count)
                rows.Controls.Add(new CheckRow { Width = Math.Max(120, rows.ClientSize.Width - 2), Margin = new Padding(0, 0, 0, T.Space2) });
            while (rows.Controls.Count > items.Count)
            {
                var last = rows.Controls[rows.Controls.Count - 1];
                rows.Controls.Remove(last);
                last.Dispose();
            }

            for (int i = 0; i < items.Count; i++)
            {
                var row = (CheckRow)rows.Controls[i];
                row.Set(items[i]);
                tips.SetToolTip(row, items[i].Hint ?? "");
            }
        }

        private void EditList()
        {
            var family = previewState != null ? previewFamily : FlightCommands.CurrentFamily();
            string text = string.Join("\r\n", CrewList(family));
            if (InputBox.Show("Crew sign-off list", "One item per line. Leave empty to go back to the default list for this airframe.", ref text, false, true) != DialogResult.OK)
                return;
            var lines = (text ?? "").Replace("\r", "").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            Settings.Instance[KeyCrewList] = string.Join("\n", lines);
            Settings.Instance.Save();
            crewSignature = null;
            UpdateView();
        }

        private void SaveReport()
        {
            var r = Latest;
            if (r == null)
                return;
            string op = Settings.Instance[KeyOperator] ?? "";
            if (InputBox.Show("Save report", "Operator name for the report", ref op) != DialogResult.OK)
                return;
            Settings.Instance[KeyOperator] = op.Trim();
            Settings.Instance.Save();
            try
            {
                var dir = Path.Combine(Settings.Instance.LogDir, "preflight");
                Directory.CreateDirectory(dir);
                var mav = MainV2.comPort?.MAV;
                var vehicle = title.Text.Replace("Preflight: ", "");
                var file = Path.Combine(dir, $"preflight-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                File.WriteAllText(file, PreflightChecks.Report(r, vehicle, op.Trim(), DateTime.Now));
                if (CustomMessageBox.Show("Report saved:\n" + file + "\n\nOpen the folder?", "Save report",
                        CustomMessageBox.MessageBoxButtons.YesNo) == CustomMessageBox.DialogResult.Yes)
                    Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("Couldn't save the report: " + ex.Message, "Save report");
            }
        }

        /// <summary>
        /// Called by the Fly screen's Arm button. True to go ahead: either the verdict is GO, or the pilot chose to
        /// override after seeing what is outstanding.
        /// </summary>
        public static bool ConfirmArmOverride(IWin32Window owner, CurrentState previewState, FlightCommandProfile.Family previewFamily)
        {
            var r = Evaluate(previewState, previewFamily);
            if (r == null || r.Verdict == PreflightChecks.Verdict.Go)
                return true;
            var open = r.Items.Where(i => i.Status == PreflightChecks.Status.Fail || i.Status == PreflightChecks.Status.Check)
                .Take(6).Select(i => (i.Status == PreflightChecks.Status.Fail ? "■ " : "▲ ") + i.Text + (string.IsNullOrEmpty(i.Value) ? "" : " (" + i.Value + ")"));
            var word = r.Verdict == PreflightChecks.Verdict.NoGo ? "NO-GO" : "HOLD";
            return CustomMessageBox.Show($"Preflight is {word}: {r.Summary}.\n\n{string.Join("\n", open)}\n\nArm anyway?",
                       "Preflight " + word, CustomMessageBox.MessageBoxButtons.YesNo, CustomMessageBox.MessageBoxIcon.Warning,
                       "Cancel", "Override and continue") == CustomMessageBox.DialogResult.No;
        }

        // ---------------- pieces ----------------

        /// <summary>The big verdict: word, colour and shape (circle GO, triangle HOLD, square NO-GO).</summary>
        private class VerdictBox : TokenControl
        {
            private PreflightChecks.Verdict verdict = PreflightChecks.Verdict.Hold;
            private string summary = "";

            public void Set(PreflightChecks.Verdict v, string s)
            {
                if (verdict == v && summary == s)
                    return;
                verdict = v;
                summary = s;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                FillParentBackground(g);
                Smooth(g);
                var state = verdict == PreflightChecks.Verdict.Go ? T.State.Ok : verdict == PreflightChecks.Verdict.Hold ? T.State.Caution : T.State.Warning;
                T.StateColors(state, out var surface, out var border, out var mark);
                using (var path = T.RoundedRect(new RectangleF(1, 1, Width - 2.5f, Height - 2.5f), T.CardRadius))
                using (var b = new SolidBrush(surface))
                using (var pen = new Pen(mark, 2))
                {
                    g.FillPath(b, path);
                    g.DrawPath(pen, path);
                }

                var word = verdict == PreflightChecks.Verdict.Go ? "GO" : verdict == PreflightChecks.Verdict.Hold ? "HOLD" : "NO-GO";
                var cx = 30f;
                var cy = Height / 2f - 8;
                using (var b = new SolidBrush(mark))
                {
                    if (state == T.State.Ok) g.FillEllipse(b, cx - 9, cy - 9, 18, 18);
                    else if (state == T.State.Caution) g.FillPolygon(b, new[] { new PointF(cx, cy - 10), new PointF(cx + 10, cy + 8), new PointF(cx - 10, cy + 8) });
                    else g.FillRectangle(b, cx - 8, cy - 8, 16, 16);
                }

                DrawText(g, word, T.Readout, mark, new Rectangle(50, 6, Width - 60, Height / 2 + 2));
                DrawText(g, summary, T.Detail, T.Current.TextPrimary, new Rectangle(18, Height / 2 + 8, Width - 30, Height / 2 - 14));
            }
        }

        /// <summary>One automatic check: text on the left, PASS / CHECK / FAIL or a value on the right.</summary>
        private class CheckRow : TokenControl
        {
            private PreflightChecks.Item item;

            public CheckRow()
            {
                Height = 44;
            }

            public void Set(PreflightChecks.Item i)
            {
                if (item != null && item.Text == i.Text && item.Status == i.Status && item.Value == i.Value)
                    return;
                item = i;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                FillParentBackground(g);
                Smooth(g);
                if (item == null)
                    return;
                var state = item.Status == PreflightChecks.Status.Pass ? T.State.Ok
                    : item.Status == PreflightChecks.Status.Check ? T.State.Caution
                    : item.Status == PreflightChecks.Status.Fail ? T.State.Warning : T.State.Neutral;
                T.StateColors(state, out var surface, out var border, out var mark);
                using (var path = T.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), T.ControlRadius))
                using (var b = new SolidBrush(surface))
                using (var pen = new Pen(border))
                {
                    g.FillPath(b, path);
                    g.DrawPath(pen, path);
                }

                var word = item.Value ?? (item.Status == PreflightChecks.Status.Pass ? "PASS" : item.Status == PreflightChecks.Status.Check ? "CHECK" : item.Status == PreflightChecks.Status.Fail ? "FAIL" : "INFO");
                var wordW = Math.Min(Width / 2, TextRenderer.MeasureText(word, T.BodyStrong).Width + 8);
                DrawText(g, item.Text, T.Body, T.Current.TextPrimary, new Rectangle(12, 0, Width - wordW - 28, Height));
                DrawText(g, word, T.BodyStrong, state == T.State.Neutral ? T.Current.TextMuted : mark, new Rectangle(Width - wordW - 12, 0, wordW, Height),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }

        /// <summary>A crew checklist row: a large tick box and the item, the whole row is the target.</summary>
        private class CrewRow : TokenControl
        {
            private bool isChecked;
            private bool hover;
            public event EventHandler Toggled;

            public CrewRow()
            {
                Height = 48;
                Cursor = Cursors.Hand;
                TabStop = true;
            }

            public bool Checked
            {
                get { return isChecked; }
                set
                {
                    if (isChecked == value)
                        return;
                    isChecked = value;
                    AccessibleName = Text + (value ? ", ticked" : ", not ticked");
                    Invalidate();
                }
            }

            protected override void OnClick(EventArgs e)
            {
                isChecked = !isChecked;
                Invalidate();
                Toggled?.Invoke(this, EventArgs.Empty);
                base.OnClick(e);
            }

            protected override void OnKeyUp(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Space)
                    OnClick(EventArgs.Empty);
                base.OnKeyUp(e);
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                FillParentBackground(g);
                Smooth(g);
                var p = T.Current;
                using (var path = T.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), T.ControlRadius))
                using (var b = new SolidBrush(p.SurfaceRaised))
                using (var pen = new Pen(hover || Focused ? p.Brand : p.Divider, hover || Focused ? 2 : 1))
                {
                    g.FillPath(b, path);
                    g.DrawPath(pen, path);
                }

                var box = new RectangleF(12, Height / 2f - 11, 22, 22);
                using (var path = T.RoundedRect(box, 5))
                {
                    if (isChecked)
                        using (var b = new SolidBrush(p.Ok))
                            g.FillPath(b, path);
                    using (var pen = new Pen(isChecked ? p.Ok : p.BorderControl, 2))
                        g.DrawPath(pen, path);
                }

                if (isChecked)
                    using (var pen = new Pen(p.OnBrand, 3)) // no line caps: not in the Android/Mac drawing shim
                        g.DrawLines(pen, new[] { new PointF(box.X + 5, box.Y + 11), new PointF(box.X + 9.5f, box.Y + 16), new PointF(box.X + 17, box.Y + 6) });

                DrawText(g, Text, T.Body, p.TextPrimary, new Rectangle(46, 0, Width - 56, Height));
            }
        }
    }
}
