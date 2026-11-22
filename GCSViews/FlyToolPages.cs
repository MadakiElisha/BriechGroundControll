using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using log4net;
using MissionPlanner.Controls;
using MissionPlanner.Controls.Fly;
using MissionPlanner.Utilities;
using T = MissionPlanner.Utilities.DesignTokens;

namespace MissionPlanner.GCSViews
{
    /// <summary>
    /// A Tools page drawn in the new design (Dashboard, Status, Gauges, Servo / Relay). Unlike the other tools these
    /// are not borrowed from Classic Fly: they are built here, read the same settings as their classic versions
    /// (so a pilot's dashboard values and servo names carry over) and are refreshed from the Fly screen's timer.
    /// </summary>
    [PreventTheming]
    public abstract class FlyToolPage : Panel
    {
        protected static readonly ILog log = LogManager.GetLogger(typeof(FlyToolPage));

        protected FlyToolPage()
        {
            DoubleBuffered = true;
            Padding = new Padding(T.Space2);
            BackColor = T.Current.Surface;
        }

        /// <summary>The native page for a tool key, or null when that tool is still the classic one.</summary>
        public static FlyToolPage Create(string key)
        {
            switch (key)
            {
                case "quick": return new FlyDashboardPage();
                case "status": return new FlyStatusPage();
                case "gauges": return new FlyGaugesPage();
                case "servo": return new FlyServoPage();
                default: return null;
            }
        }

        public abstract void UpdateView(CurrentState cs, bool connected);

        public virtual void ApplyTokens()
        {
            BackColor = T.Current.Surface;
            Invalidate(true);
        }

        protected static string Format(object v)
        {
            switch (v)
            {
                case null: return "--";
                case float f: return float.IsNaN(f) ? "--" : f.ToString(Math.Abs(f) >= 1000 ? "0" : "0.##", CultureInfo.InvariantCulture);
                case double d: return double.IsNaN(d) ? "--" : d.ToString(Math.Abs(d) >= 1000 ? "0" : "0.##", CultureInfo.InvariantCulture);
                case bool b: return b ? "Yes" : "No";
                case DateTime t: return t == DateTime.MinValue ? "--" : t.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                case IFormattable i: return i.ToString(null, CultureInfo.InvariantCulture);
                default: return v.ToString();
            }
        }

        /// <summary>Readable CurrentState values (numbers, flags, text), by name.</summary>
        protected static readonly PropertyInfo[] StateProperties = typeof(CurrentState).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && (p.PropertyType.IsPrimitive || p.PropertyType.IsEnum ||
                                                                           p.PropertyType == typeof(string) || p.PropertyType == typeof(DateTime) ||
                                                                           p.PropertyType == typeof(decimal)))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        protected static readonly Dictionary<string, PropertyInfo> StateByName =
            StateProperties.GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.First());

        protected static object Read(CurrentState cs, string name)
        {
            if (cs == null || name == null || !StateByName.TryGetValue(name, out var p))
                return null;
            try
            {
                return p.GetValue(cs, null);
            }
            catch
            {
                return null;
            }
        }

        protected static TextBox MakeSearchBox()
        {
            return new TextBox
            {
                Dock = DockStyle.Top,
                Font = T.Body,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = T.Current.SurfaceRaised,
                ForeColor = T.Current.TextPrimary
            };
        }

        /// <summary>Send a command off the UI thread; the callback gets true when the vehicle accepted it.</summary>
        protected async Task<bool> SendAsync(MAVLink.MAV_CMD cmd, float p1, float p2)
        {
            var port = MainV2.comPort;
            if (port?.BaseStream == null || !port.BaseStream.IsOpen)
                return false;
            try
            {
                return await Task.Run(() => port.doCommand(port.sysidcurrent, (byte)port.compidcurrent, cmd, p1, p2, 0, 0, 0, 0, 0));
            }
            catch (Exception ex)
            {
                log.Debug(ex);
                return false;
            }
        }
    }

    // =====================================================================================================
    // Dashboard
    // =====================================================================================================

    /// <summary>
    /// Pick-your-own values as readout cards. Uses the classic dashboard's saved values (dashboardItem0..29), so the
    /// same choice shows in both. Click a card to change its value; right-click to remove it.
    /// </summary>
    public class FlyDashboardPage : FlyToolPage
    {
        private class Item
        {
            public int Row, Col;
            public string Source, Label, Format = "0.00";
            public double Scale = 1, Offset;
            public string Color = "";
            public string Tail = "0|0|100"; // isGauge|gaugeMin|gaugeMax, kept as read
        }

        private static readonly string[] Defaults = { "alt", "groundspeed", "wp_dist", "yaw", "verticalspeed", "DistToMAV" };

        private readonly FlowLayoutPanel tiles;
        private readonly Label note;
        private readonly TokenButton add;
        private List<Item> items;
        private readonly List<ReadoutCard> cards = new List<ReadoutCard>();
        private readonly ToolTip tips = new ToolTip();

        public FlyDashboardPage()
        {
            AutoScroll = true;
            tiles = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Color.Transparent };
            var bar = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.Transparent };
            note = new Label { Dock = DockStyle.Fill, Font = T.Detail, TextAlign = ContentAlignment.MiddleLeft, Text = "Click a value to change it · right-click to remove · the same values show on the Classic Fly dashboard" };
            add = new TokenButton { Text = "Add a value…", Dock = DockStyle.Right, Width = 150 };
            add.Click += (s, e) => Pick(-1);
            bar.Controls.Add(note);
            bar.Controls.Add(add);
            Controls.Add(tiles);
            Controls.Add(bar);
            Resize += (s, e) => LayoutTiles();
            Load();
            ApplyTokens();
        }

        public override void ApplyTokens()
        {
            base.ApplyTokens();
            note.ForeColor = T.Current.TextMuted;
            note.BackColor = T.Current.Surface;
        }

        private void Load()
        {
            items = new List<Item>();
            for (int i = 0; i < 30; i++)
            {
                var raw = Settings.Instance["dashboardItem" + i];
                if (string.IsNullOrEmpty(raw))
                    continue;
                var parts = raw.Split('|');
                if (parts.Length < 11)
                    continue;
                try
                {
                    var it = new Item
                    {
                        Row = int.Parse(parts[0], CultureInfo.InvariantCulture),
                        Col = int.Parse(parts[1], CultureInfo.InvariantCulture),
                        Source = parts[2],
                        Label = parts[3],
                        Color = parts[4],
                        Format = string.IsNullOrEmpty(parts[5]) ? "0.00" : parts[5],
                        Scale = double.Parse(parts[6], CultureInfo.InvariantCulture),
                        Offset = double.Parse(parts[7], CultureInfo.InvariantCulture),
                        Tail = string.Join("|", parts.Skip(8))
                    };
                    if (it.Source.StartsWith("customfield:"))
                        it.Source = CurrentState.GetCustomField(it.Source.Substring(12));
                    items.Add(it);
                }
                catch (Exception ex)
                {
                    log.Debug(ex);
                }
            }

            if (items.Count == 0)
                items = Defaults.Select((d, i) => new Item { Source = d, Row = i / 3, Col = i % 3 }).ToList();
            items = items.OrderBy(i => i.Row).ThenBy(i => i.Col).ToList();
            Rebuild();
        }

        private void Save()
        {
            int cols;
            if (!int.TryParse(Settings.Instance["dashboardCols"], out cols) || cols < 1)
                cols = 5;
            for (int i = 0; i < 30; i++)
                Settings.Instance.Remove("dashboardItem" + i);
            for (int i = 0; i < items.Count && i < 30; i++)
            {
                var it = items[i];
                it.Row = i / cols;
                it.Col = i % cols;
                Settings.Instance["dashboardItem" + i] = string.Join("|", it.Row.ToString(CultureInfo.InvariantCulture),
                    it.Col.ToString(CultureInfo.InvariantCulture), it.Source, it.Label ?? "", it.Color ?? "", it.Format ?? "0.00",
                    it.Scale.ToString(CultureInfo.InvariantCulture), it.Offset.ToString(CultureInfo.InvariantCulture), it.Tail);
            }

            var rows = (items.Count + cols - 1) / cols;
            int savedRows;
            if (!int.TryParse(Settings.Instance["dashboardRows"], out savedRows) || savedRows < rows)
                Settings.Instance["dashboardRows"] = Math.Max(1, rows).ToString(CultureInfo.InvariantCulture);
            Settings.Instance["dashboardCols"] = cols.ToString(CultureInfo.InvariantCulture);
            Settings.Instance.Save();
        }

        private void Rebuild()
        {
            tiles.SuspendLayout();
            foreach (var c in cards)
                c.Dispose();
            cards.Clear();
            tiles.Controls.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                var index = i;
                var card = new ReadoutCard { Height = 104, Cursor = Cursors.Hand };
                card.Click += (s, e) => Pick(index);
                var menu = new ContextMenuStrip();
                menu.Items.Add("Change value…", null, (s, e) => Pick(index));
                menu.Items.Add("Remove", null, (s, e) =>
                {
                    items.RemoveAt(index);
                    Save();
                    Rebuild();
                });
                card.ContextMenuStrip = menu;
                tips.SetToolTip(card, "Click to change · right-click to remove");
                cards.Add(card);
                tiles.Controls.Add(card);
            }

            add.Enabled = items.Count < 30;
            LayoutTiles();
            tiles.ResumeLayout(true);
        }

        private void LayoutTiles()
        {
            var w = ClientSize.Width - Padding.Horizontal - (VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0);
            if (w <= 0)
                return;
            var perRow = Math.Max(1, Math.Min(4, w / 200));
            var tileW = (w - (perRow - 1) * T.Space2) / perRow - 1;
            for (int i = 0; i < cards.Count; i++)
            {
                cards[i].Width = tileW;
                cards[i].Margin = new Padding(0, 0, (i + 1) % perRow == 0 ? 0 : T.Space2, T.Space2);
            }
        }

        public override void UpdateView(CurrentState cs, bool connected)
        {
            for (int i = 0; i < items.Count && i < cards.Count; i++)
            {
                var it = items[i];
                var label = !string.IsNullOrEmpty(it.Label) ? it.Label : cs?.GetNameandUnit(it.Source) ?? it.Source;
                var raw = Read(cs, it.Source);
                string value;
                try
                {
                    value = !connected || raw == null ? "--"
                        : raw is string || raw is bool || raw is Enum ? Format(raw)
                        : (Convert.ToDouble(raw, CultureInfo.InvariantCulture) * it.Scale + it.Offset).ToString(it.Format, CultureInfo.InvariantCulture);
                }
                catch
                {
                    value = Format(raw);
                }

                cards[i].Set(label, value, it.Source, T.State.Neutral);
            }
        }

        private void Pick(int index)
        {
            var current = index >= 0 && index < items.Count ? items[index].Source : null;
            var chosen = FieldPicker.Show(FindForm(), current);
            if (chosen == null)
                return;
            if (index >= 0 && index < items.Count)
            {
                items[index].Source = chosen;
                items[index].Label = "";
            }
            else
            {
                items.Add(new Item { Source = chosen });
            }

            Save();
            Rebuild();
        }
    }

    /// <summary>A small searchable list of every telemetry value, for choosing what a dashboard card shows.</summary>
    internal static class FieldPicker
    {
        public static string Show(IWin32Window owner, string current)
        {
            var p = T.Current;
            var cs = MainV2.comPort?.MAV?.cs;
            var names = typeof(CurrentState).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => x.CanRead && x.GetIndexParameters().Length == 0 &&
                            (x.PropertyType == typeof(float) || x.PropertyType == typeof(double) || x.PropertyType == typeof(int) ||
                             x.PropertyType == typeof(uint) || x.PropertyType == typeof(ushort) || x.PropertyType == typeof(short) ||
                             x.PropertyType == typeof(byte) || x.PropertyType == typeof(bool)))
                .Select(x => x.Name).Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

            using (var form = new Form
            {
                Text = "Choose a value",
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(460, 560),
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                BackColor = p.Surface,
                ForeColor = p.TextPrimary,
                Padding = new Padding(T.Space3)
            })
            {
                var search = new TextBox { Dock = DockStyle.Top, Font = T.Body, BorderStyle = BorderStyle.FixedSingle, BackColor = p.SurfaceRaised, ForeColor = p.TextPrimary };
                var list = new ListBox { Dock = DockStyle.Fill, Font = T.Body, BorderStyle = BorderStyle.None, BackColor = p.Surface, ForeColor = p.TextPrimary, IntegralHeight = false };
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, FlowDirection = FlowDirection.RightToLeft, BackColor = Color.Transparent, Padding = new Padding(0, T.Space2, 0, 0) };
                var ok = new TokenButton { Text = "Use this value", Width = 150, Height = 44, Style = TokenButton.Kind.Current };
                var cancel = new TokenButton { Text = "Cancel", Width = 100, Height = 44, Margin = new Padding(0, 0, T.Space2, 0) };
                buttons.Controls.Add(ok);
                buttons.Controls.Add(cancel);
                var gap = new Panel { Dock = DockStyle.Top, Height = T.Space2, BackColor = Color.Transparent };

                Func<string, string> show = n =>
                {
                    var d = cs?.GetNameandUnit(n);
                    return string.IsNullOrEmpty(d) || d == n ? n : $"{n}  ·  {d}";
                };

                void Fill()
                {
                    var q = search.Text.Trim();
                    list.BeginUpdate();
                    list.Items.Clear();
                    foreach (var n in names)
                    {
                        var text = show(n);
                        if (q.Length == 0 || text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                            list.Items.Add(new KeyValuePair<string, string>(n, text));
                    }

                    list.DisplayMember = "Value";
                    list.EndUpdate();
                    for (int i = 0; i < list.Items.Count; i++)
                        if (((KeyValuePair<string, string>)list.Items[i]).Key == current)
                            list.SelectedIndex = i;
                    if (list.SelectedIndex < 0 && list.Items.Count > 0)
                        list.SelectedIndex = 0;
                }

                search.TextChanged += (s, e) => Fill();
                search.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Down && list.Items.Count > 0)
                    {
                        list.Focus();
                        e.Handled = true;
                    }
                };
                list.DoubleClick += (s, e) => form.DialogResult = DialogResult.OK;
                ok.Click += (s, e) => form.DialogResult = DialogResult.OK;
                cancel.Click += (s, e) => form.DialogResult = DialogResult.Cancel;
                form.CancelButton = null;
                form.KeyPreview = true;
                form.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Escape) form.DialogResult = DialogResult.Cancel;
                    if (e.KeyCode == Keys.Enter) form.DialogResult = DialogResult.OK;
                };

                form.Controls.Add(list);
                form.Controls.Add(gap);
                form.Controls.Add(search);
                form.Controls.Add(buttons);
                Fill();
                form.Shown += (s, e) => search.Focus();
                if (form.ShowDialog(owner) != DialogResult.OK || list.SelectedItem == null)
                    return null;
                return ((KeyValuePair<string, string>)list.SelectedItem).Key;
            }
        }
    }

    // =====================================================================================================
    // Status
    // =====================================================================================================

    /// <summary>Every telemetry value, live, with search. Names in muted text, values in monospace.</summary>
    public class FlyStatusPage : FlyToolPage
    {
        private readonly TextBox search;
        private readonly Label hint;
        private readonly StatusGrid grid;

        public FlyStatusPage()
        {
            search = MakeSearchBox();
            hint = new Label { Dock = DockStyle.Top, Height = 28, Font = T.Detail, TextAlign = ContentAlignment.MiddleLeft };
            grid = new StatusGrid { Dock = DockStyle.Fill };
            search.TextChanged += (s, e) => grid.Filter = search.Text.Trim();
            Controls.Add(grid);
            Controls.Add(hint);
            Controls.Add(search);
            ApplyTokens();
        }

        public override void ApplyTokens()
        {
            base.ApplyTokens();
            search.BackColor = T.Current.SurfaceRaised;
            search.ForeColor = T.Current.TextPrimary;
            hint.BackColor = T.Current.Surface;
            hint.ForeColor = T.Current.TextMuted;
        }

        public override void UpdateView(CurrentState cs, bool connected)
        {
            var rows = new List<(string, string)>(StateProperties.Length);
            foreach (var p in StateProperties)
                rows.Add((p.Name, connected ? Format(Read(cs, p.Name)) : "--"));
            grid.SetRows(rows);
            hint.Text = (string.IsNullOrEmpty(grid.Filter)
                ? $"{grid.Count} values · type to search"
                : $"{grid.Count} of {rows.Count} values match \"{grid.Filter}\"") + (connected ? "" : " · not connected");
        }

        /// <summary>Owner-drawn multi-column name / value list with its own scroll bar.</summary>
        private class StatusGrid : TokenControl
        {
            private const int RowH = 24, ColW = 300;
            private List<(string name, string value)> all = new List<(string, string)>();
            private List<(string name, string value)> shown = new List<(string, string)>();
            private readonly VScrollBar bar = new VScrollBar { Dock = DockStyle.Right, SmallChange = 1, LargeChange = 10 };
            private string filter = "";

            public StatusGrid()
            {
                Controls.Add(bar);
                bar.Scroll += (s, e) => Invalidate();
                TabStop = true;
            }

            public int Count => shown.Count;

            public string Filter
            {
                get { return filter; }
                set
                {
                    filter = value ?? "";
                    bar.Value = 0;
                    Apply();
                }
            }

            public void SetRows(List<(string, string)> rows)
            {
                all = rows;
                Apply();
            }

            private void Apply()
            {
                shown = filter.Length == 0 ? all : all.Where(r => r.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                UpdateBar();
                Invalidate();
            }

            private int Columns => Math.Max(1, (Width - bar.Width) / ColW);

            private void UpdateBar()
            {
                var lines = (shown.Count + Columns - 1) / Columns;
                var visible = Math.Max(1, Height / RowH);
                bar.Maximum = Math.Max(0, lines - 1);
                bar.LargeChange = visible;
                bar.Enabled = lines > visible;
                if (bar.Value > Math.Max(0, lines - visible))
                    bar.Value = Math.Max(0, lines - visible);
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                UpdateBar();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                var v = bar.Value - Math.Sign(e.Delta) * 3;
                bar.Value = Math.Max(0, Math.Min(Math.Max(0, bar.Maximum - bar.LargeChange + 1), v));
                Invalidate();
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                Focus();
                base.OnMouseEnter(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                var p = T.Current;
                using (var b = new SolidBrush(p.Surface))
                    g.FillRectangle(b, ClientRectangle);
                var cols = Columns;
                var colW = (Width - bar.Width) / cols;
                var visible = Height / RowH + 1;
                for (int line = 0; line < visible; line++)
                {
                    var y = line * RowH;
                    if (line % 2 == 1)
                        using (var b = new SolidBrush(p.SurfaceRaised))
                            g.FillRectangle(b, 0, y, Width - bar.Width, RowH);
                    for (int c = 0; c < cols; c++)
                    {
                        var index = (bar.Value + line) * cols + c;
                        if (index >= shown.Count)
                            break;
                        var x = c * colW;
                        var (name, value) = shown[index];
                        DrawText(g, name, T.Detail, p.TextMuted, new Rectangle(x + 8, y, colW * 55 / 100 - 8, RowH));
                        DrawText(g, value, T.DetailMono, p.TextPrimary, new Rectangle(x + colW * 55 / 100, y, colW * 45 / 100 - 8, RowH));
                    }
                }

                if (shown.Count == 0)
                    DrawText(g, "No values match.", T.Body, p.TextMuted, new Rectangle(8, 8, Width - 16, RowH));
            }
        }
    }

    // =====================================================================================================
    // Gauges
    // =====================================================================================================

    /// <summary>Four dials: airspeed, altitude, climb rate and heading, in the pilot's units.</summary>
    public class FlyGaugesPage : FlyToolPage
    {
        private readonly Dial airspeed = new Dial { Label = "Airspeed", Min = 0, BaseMax = 40, Step = 10 };
        private readonly Dial altitude = new Dial { Label = "Altitude", Min = 0, BaseMax = 150, Step = 50 };
        private readonly Dial climb = new Dial { Label = "Climb rate", Min = -10, BaseMax = 10, Step = 5, Symmetric = true };
        private readonly Dial heading = new Dial { Label = "Heading", Compass = true };
        private readonly TableLayoutPanel grid;

        public FlyGaugesPage()
        {
            grid = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            Controls.Add(grid);
            Resize += (s, e) => Arrange();
            Arrange();
        }

        private void Arrange()
        {
            var wide = ClientSize.Width >= ClientSize.Height * 2.2;
            int cols = wide ? 4 : 2, rows = wide ? 1 : 2;
            if (grid.ColumnCount == cols && grid.Controls.Count == 4)
                return;
            grid.SuspendLayout();
            grid.Controls.Clear();
            grid.ColumnStyles.Clear();
            grid.RowStyles.Clear();
            grid.ColumnCount = cols;
            grid.RowCount = rows;
            for (int c = 0; c < cols; c++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cols));
            for (int r = 0; r < rows; r++)
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));
            var dials = new Control[] { airspeed, altitude, climb, heading };
            for (int i = 0; i < 4; i++)
            {
                dials[i].Dock = DockStyle.Fill;
                dials[i].Margin = new Padding(T.Space1);
                grid.Controls.Add(dials[i], i % cols, i / cols);
            }

            grid.ResumeLayout(true);
        }

        public override void UpdateView(CurrentState cs, bool connected)
        {
            airspeed.Unit = CurrentState.SpeedUnit;
            altitude.Unit = CurrentState.AltUnit;
            climb.Unit = CurrentState.SpeedUnit == "" ? "m/s" : CurrentState.SpeedUnit;
            heading.Unit = "°";
            airspeed.SetValue(connected && cs != null ? cs.airspeed : (double?)null,
                connected && cs != null ? $"GS {cs.groundspeed:0.0}" : "");
            altitude.SetValue(connected && cs != null ? cs.alt : (double?)null,
                connected && cs != null ? $"AMSL {cs.altasl:0}" : "");
            climb.SetValue(connected && cs != null ? cs.climbrate : (double?)null, "");
            heading.SetValue(connected && cs != null ? cs.yaw : (double?)null,
                connected && cs != null ? $"course {cs.groundcourse:000}°" : "");
        }

        private class Dial : TokenControl
        {
            public string Label, Unit = "";
            public double Min, BaseMax, Step = 10;
            public bool Symmetric, Compass;
            private double? value;
            private string detail = "";

            public void SetValue(double? v, string detailText)
            {
                if (Nullable.Equals(value, v) && detail == detailText)
                    return;
                value = v;
                detail = detailText ?? "";
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                FillParentBackground(g);
                Smooth(g);
                var p = T.Current;
                using (var path = T.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 10))
                using (var fill = new SolidBrush(p.Surface))
                using (var pen = new Pen(p.Divider))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(pen, path);
                }

                DrawText(g, (Label ?? "").ToUpperInvariant() + (string.IsNullOrEmpty(Unit) ? "" : $" ({Unit})"), T.Label, p.TextMuted, new Rectangle(12, 8, Width - 24, 20));
                var size = Math.Min(Width - 24, Height - 64);
                if (size < 60)
                {
                    DrawText(g, value.HasValue ? value.Value.ToString("0.0", CultureInfo.InvariantCulture) : "--", T.Readout, p.TextPrimary, new Rectangle(12, 30, Width - 24, 40));
                    return;
                }

                var cx = Width / 2f;
                var cy = 32 + size / 2f;
                var r = size / 2f - 6;
                if (Compass)
                    PaintCompass(g, p, cx, cy, r);
                else
                    PaintArc(g, p, cx, cy, r);
                DrawText(g, detail, T.Detail, p.TextSecondary, new Rectangle(12, Height - 26, Width - 24, 20), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }

            private void PaintArc(Graphics g, DesignTokens.Palette p, float cx, float cy, float r)
            {
                const float start = 150, sweep = 240;
                var v = value ?? Min;
                var max = BaseMax;
                var min = Min;
                // the scale grows in steps rather than pinning the needle
                if (Math.Abs(v) > max * 0.95)
                    max = Math.Ceiling(Math.Abs(v) * 1.2 / Step) * Step;
                if (Symmetric)
                    min = -max;
                var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                using (var track = new Pen(p.SurfaceRaised, Math.Max(6, r / 9)))
                    g.DrawArc(track, rect, start, sweep);
                if (value.HasValue)
                {
                    var frac = (float)Math.Max(0, Math.Min(1, (v - min) / (max - min)));
                    var zeroFrac = Symmetric ? 0.5f : 0f;
                    using (var fillPen = new Pen(p.Brand, Math.Max(6, r / 9)))
                        g.DrawArc(fillPen, rect, start + sweep * Math.Min(zeroFrac, frac), sweep * Math.Abs(frac - zeroFrac));
                }

                // ticks and labels at each step
                var ticks = (int)Math.Round((max - min) / Step);
                if (ticks > 12)
                    ticks = 12;
                using (var tick = new Pen(p.BorderControl, 2))
                    for (int i = 0; i <= ticks; i++)
                    {
                        var a = (start + sweep * i / ticks) * Math.PI / 180;
                        var c = Math.Cos(a);
                        var s = Math.Sin(a);
                        g.DrawLine(tick, (float)(cx + c * (r - r / 5)), (float)(cy + s * (r - r / 5)), (float)(cx + c * (r - r / 9 - 4)), (float)(cy + s * (r - r / 9 - 4)));
                        if (r > 70 && i % Math.Max(1, ticks / 6) == 0)
                        {
                            var label = (min + (max - min) * i / ticks).ToString("0", CultureInfo.InvariantCulture);
                            var lr = r - r / 5 - 14;
                            DrawText(g, label, T.Detail, p.TextMuted, new Rectangle((int)(cx + c * lr) - 24, (int)(cy + s * lr) - 10, 48, 20),
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                        }
                    }

                var text = value.HasValue ? v.ToString(Math.Abs(v) >= 100 ? "0" : "0.0", CultureInfo.InvariantCulture) : "--";
                var font = r > 90 ? T.ReadoutXL : r > 50 ? T.Readout : T.MonoStrong;
                DrawText(g, text, font, p.TextPrimary, new Rectangle((int)(cx - r), (int)(cy - 28), (int)(r * 2), 56),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }

            private void PaintCompass(Graphics g, DesignTokens.Palette p, float cx, float cy, float r)
            {
                var hdg = value ?? 0;
                using (var ring = new Pen(p.SurfaceRaised, Math.Max(6, r / 9)))
                    g.DrawEllipse(ring, cx - r, cy - r, r * 2, r * 2);
                using (var tick = new Pen(p.BorderControl, 2))
                    for (int d = 0; d < 360; d += 30)
                    {
                        // card rotates so the heading is at the top
                        var a = (d - hdg - 90) * Math.PI / 180;
                        var c = Math.Cos(a);
                        var s = Math.Sin(a);
                        g.DrawLine(tick, (float)(cx + c * (r - r / 5)), (float)(cy + s * (r - r / 5)), (float)(cx + c * (r - r / 9 - 4)), (float)(cy + s * (r - r / 9 - 4)));
                        if (r > 50)
                        {
                            var name = d == 0 ? "N" : d == 90 ? "E" : d == 180 ? "S" : d == 270 ? "W" : r > 90 ? (d / 10).ToString(CultureInfo.InvariantCulture) : "";
                            var lr = r - r / 5 - 14;
                            DrawText(g, name, d % 90 == 0 ? T.BodyStrong : T.Detail, d == 0 ? p.Brand : p.TextMuted,
                                new Rectangle((int)(cx + c * lr) - 20, (int)(cy + s * lr) - 10, 40, 20),
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                        }
                    }

                // lubber mark at the top
                using (var b = new SolidBrush(p.Brand))
                    g.FillPolygon(b, new[] { new PointF(cx, cy - r + 2), new PointF(cx - 8, cy - r - 10), new PointF(cx + 8, cy - r - 10) });
                var text = value.HasValue ? ((int)Math.Round((hdg % 360 + 360) % 360)).ToString("000", CultureInfo.InvariantCulture) + "°" : "--";
                var font = r > 90 ? T.ReadoutXL : r > 50 ? T.Readout : T.MonoStrong;
                DrawText(g, text, font, p.TextPrimary, new Rectangle((int)(cx - r), (int)(cy - 28), (int)(r * 2), 56),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }
        }
    }

    // =====================================================================================================
    // Servo / Relay
    // =====================================================================================================

    /// <summary>
    /// Servo outputs 5-16 and relays 1-6 as rows: name, live output, Low / High / Toggle. Names, PWM values and
    /// button words are the classic page's settings (Servo{n}_desc, _low, _high, _lowdesc, _highdesc; Relay{n}_...),
    /// so they carry over. Right-click a row to rename it or change its PWM values.
    /// </summary>
    public class FlyServoPage : FlyToolPage
    {
        private class Row
        {
            public bool Relay;
            public int Number;     // servo output number, or relay instance (0-based)
            public Label Name, Output;
            public TokenButton Low, High, Toggle;
            public bool? LastHigh;
        }

        private readonly List<Row> rows = new List<Row>();
        private readonly TableLayoutPanel table;
        private readonly Label result;
        private bool busy;

        public FlyServoPage()
        {
            AutoScroll = true;
            result = new Label { Dock = DockStyle.Top, Height = 30, Font = T.Detail, TextAlign = ContentAlignment.MiddleLeft, Text = "Right-click a row to rename it or change its PWM values." };
            table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 5, BackColor = Color.Transparent };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));

            AddHeader("SERVO OUTPUTS");
            for (int n = 5; n <= 16; n++)
                AddRow(false, n);
            AddHeader("RELAYS");
            for (int n = 0; n <= 5; n++)
                AddRow(true, n);

            Controls.Add(table);
            Controls.Add(result);
            ApplyTokens();
        }

        private string Key(Row r, string suffix)
        {
            return (r.Relay ? "Relay" : "Servo") + r.Number + "_" + suffix;
        }

        private string Setting(Row r, string suffix, string fallback)
        {
            var v = Settings.Instance[Key(r, suffix)];
            return string.IsNullOrEmpty(v) ? fallback : v;
        }

        private int Pwm(Row r, bool high)
        {
            return int.TryParse(Setting(r, high ? "high" : "low", high ? "1900" : "1100"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : high ? 1900 : 1100;
        }

        private void AddHeader(string text)
        {
            var l = new Label { Text = text, Font = T.Label, Dock = DockStyle.Fill, Height = 34, TextAlign = ContentAlignment.BottomLeft, Margin = new Padding(0, 0, 0, T.Space1) };
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            table.Controls.Add(l, 0, table.RowStyles.Count - 1);
            table.SetColumnSpan(l, 5);
        }

        private void AddRow(bool relay, int number)
        {
            var row = new Row { Relay = relay, Number = number };
            row.Name = new Label { Dock = DockStyle.Fill, Font = T.Body, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, UseMnemonic = false };
            row.Output = new Label { Dock = DockStyle.Fill, Font = T.DetailMono, TextAlign = ContentAlignment.MiddleLeft };
            row.Low = new TokenButton { Dock = DockStyle.Fill, Margin = new Padding(0, 2, T.Space2, 2) };
            row.High = new TokenButton { Dock = DockStyle.Fill, Margin = new Padding(0, 2, T.Space2, 2) };
            row.Toggle = new TokenButton { Text = "Toggle", Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2) };
            row.Low.Click += async (s, e) => await Set(row, false);
            row.High.Click += async (s, e) => await Set(row, true);
            row.Toggle.Click += async (s, e) => await Set(row, !(row.LastHigh ?? false));

            var menu = new ContextMenuStrip();
            menu.Items.Add("Rename…", null, (s, e) => Edit(row, "desc", "Name for " + Default(row)));
            menu.Items.Add("Low button word…", null, (s, e) => Edit(row, "lowdesc", "Word on the Low button"));
            menu.Items.Add("High button word…", null, (s, e) => Edit(row, "highdesc", "Word on the High button"));
            if (!relay)
            {
                menu.Items.Add("Low PWM…", null, (s, e) => EditPwm(row, false));
                menu.Items.Add("High PWM…", null, (s, e) => EditPwm(row, true));
            }

            foreach (var c in new Control[] { row.Name, row.Output })
                c.ContextMenuStrip = menu;

            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            var r = table.RowStyles.Count - 1;
            table.Controls.Add(row.Name, 0, r);
            table.Controls.Add(row.Output, 1, r);
            table.Controls.Add(row.Low, 2, r);
            table.Controls.Add(row.High, 3, r);
            table.Controls.Add(row.Toggle, 4, r);
            rows.Add(row);
            Refresh(row);
        }

        private static string Default(Row r)
        {
            return r.Relay ? "Relay " + (r.Number + 1) : "Servo " + r.Number;
        }

        private void Refresh(Row r)
        {
            var name = Setting(r, "desc", "");
            r.Name.Text = string.IsNullOrEmpty(name) || name == r.Number.ToString(CultureInfo.InvariantCulture) ? Default(r) : $"{name}  ·  {Default(r)}";
            r.Low.Text = Setting(r, "lowdesc", r.Relay ? "Off" : "Low");
            r.High.Text = Setting(r, "highdesc", r.Relay ? "On" : "High");
        }

        private void Edit(Row r, string suffix, string prompt)
        {
            var v = Settings.Instance[Key(r, suffix)] ?? "";
            if (InputBox.Show(Default(r), prompt + " (empty for the default)", ref v) != DialogResult.OK)
                return;
            Settings.Instance[Key(r, suffix)] = v.Trim();
            Settings.Instance.Save();
            Refresh(r);
        }

        private void EditPwm(Row r, bool high)
        {
            var v = Pwm(r, high).ToString(CultureInfo.InvariantCulture);
            if (InputBox.Show(Default(r), (high ? "High" : "Low") + " PWM (800–2200)", ref v) != DialogResult.OK)
                return;
            if (!int.TryParse(v.Trim(), out var pwm) || pwm < 800 || pwm > 2200)
            {
                CustomMessageBox.Show("PWM must be a whole number from 800 to 2200.", Default(r));
                return;
            }

            Settings.Instance[Key(r, high ? "high" : "low")] = pwm.ToString(CultureInfo.InvariantCulture);
            Settings.Instance.Save();
        }

        private async Task Set(Row r, bool high)
        {
            if (busy)
                return;
            busy = true;
            var what = $"{Default(r)} · {(high ? r.High.Text : r.Low.Text)}";
            result.Text = what + " · sending…";
            result.ForeColor = T.Current.TextMuted;
            bool ok = r.Relay
                ? await SendAsync(MAVLink.MAV_CMD.DO_SET_RELAY, r.Number, high ? 1 : 0)
                : await SendAsync(MAVLink.MAV_CMD.DO_SET_SERVO, r.Number, Pwm(r, high));
            busy = false;
            if (IsDisposed)
                return;
            if (ok)
                r.LastHigh = high;
            result.Text = what + (ok ? " · accepted" : " · not accepted (check the output isn't assigned to a function, e.g. SERVOn_FUNCTION = 0)");
            result.ForeColor = ok ? T.Current.Ok : T.Current.Warning;
        }

        public override void ApplyTokens()
        {
            base.ApplyTokens();
            var p = T.Current;
            result.BackColor = p.Surface;
            result.ForeColor = p.TextMuted;
            foreach (Control c in table.Controls)
            {
                if (c is Label l)
                {
                    l.BackColor = p.Surface;
                    l.ForeColor = l.Font == T.Label ? p.TextMuted : p.TextPrimary;
                }
            }
        }

        public override void UpdateView(CurrentState cs, bool connected)
        {
            foreach (var r in rows)
            {
                var enabled = connected && !busy;
                r.Low.Enabled = r.High.Enabled = r.Toggle.Enabled = enabled;
                r.Low.Style = r.LastHigh == false ? TokenButton.Kind.Current : TokenButton.Kind.Normal;
                r.High.Style = r.LastHigh == true ? TokenButton.Kind.Current : TokenButton.Kind.Normal;
                if (r.Relay)
                {
                    r.Output.Text = r.LastHigh.HasValue ? (r.LastHigh.Value ? r.High.Text : r.Low.Text) : "";
                    continue;
                }

                var raw = Read(cs, "ch" + r.Number + "out");
                var pwm = raw == null ? 0 : Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                r.Output.Text = connected && pwm > 0 ? pwm.ToString("0", CultureInfo.InvariantCulture) : "--";
            }
        }
    }
}
