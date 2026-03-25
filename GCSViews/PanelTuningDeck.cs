using MissionPlanner.Controls;
using MissionPlanner.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ZedGraph;
using FormsLabel = System.Windows.Forms.Label;

namespace MissionPlanner.GCSViews
{
    public class PanelTuningDeck : Panel
    {
        private const string TuningSelectionSettingKey = "Tuning_Graph_Selected";
        private const int MaxSeriesCount = 20;
        private static readonly string[] DefaultSeriesKeys = { "roll", "pitch", "nav_roll", "nav_pitch" };
        private static readonly Color[] SeriesPalette =
        {
            Color.Red,
            Color.DeepSkyBlue,
            Color.LimeGreen,
            Color.Orange,
            Color.Gold,
            Color.Magenta,
            Color.MediumPurple,
            Color.Cyan,
            Color.Salmon,
            Color.Plum,
            Color.FromArgb(123, 214, 75),
            Color.FromArgb(255, 120, 48),
            Color.FromArgb(94, 234, 212),
            Color.FromArgb(167, 139, 250),
            Color.FromArgb(244, 114, 182),
            Color.FromArgb(56, 189, 248),
            Color.FromArgb(34, 197, 94),
            Color.FromArgb(250, 204, 21),
            Color.FromArgb(248, 113, 113),
            Color.FromArgb(125, 211, 252)
        };

        private readonly Font eyebrowFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private readonly Font titleFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        private readonly Font hintFont = new Font("Segoe UI", 8.25f, FontStyle.Regular);
        private readonly Font badgeFont = new Font("Segoe UI", 8.25f, FontStyle.Bold);

        private readonly Color surface = Color.FromArgb(24, 31, 44);
        private readonly Color border = Color.FromArgb(44, 54, 73);
        private readonly Color gold = Color.FromArgb(200, 168, 101);
        private readonly Color lightGray = Color.FromArgb(235, 239, 245);
        private readonly Color mutedGray = Color.FromArgb(138, 149, 168);

        private readonly List<TuningSeries> selectedSeries = new List<TuningSeries>();
        private readonly Timer redrawTimer;

        private Panel headerPanel;
        private FormsLabel lblEyebrow;
        private FormsLabel lblTitle;
        private FormsLabel lblHint;
        private FormsLabel lblStatus;
        private Panel graphBorderPanel;
        private ZedGraphControl tuningGraph;

        private bool tuningEnabled;
        private bool hostActive;
        private bool tuningSelectionRightAxis;
        private DateTime lastSampleTimeUtc = DateTime.MinValue;
        private int tickStart;
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

        public PanelTuningDeck()
        {
            BackColor = Color.FromArgb(10, 14, 20);
            Padding = new Padding(10, 0, 10, 10);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            redrawTimer = new Timer { Interval = 200 };
            redrawTimer.Tick += RedrawTimer_Tick;

            InitializeControls();
            CreateChart(tuningGraph);
            RestoreSelectedSeries();
            SetOffline(false);
            UpdateGraphTimerState();
            ApplyEmbeddedLayout();
        }

        private void InitializeControls()
        {
            graphBorderPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = border,
                Padding = new Padding(1)
            };
            Controls.Add(graphBorderPanel);

            tuningGraph = new ZedGraphControl
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(12, 18, 28),
                IsShowPointValues = false,
                IsEnableHEdit = false,
                IsEnableHPan = false,
                IsEnableHZoom = false,
                IsEnableVEdit = false,
                IsEnableVPan = false,
                IsEnableVZoom = false,
                ScrollGrace = 0D,
                ScrollMaxX = 0D,
                ScrollMaxY = 0D,
                ScrollMaxY2 = 0D,
                ScrollMinX = 0D,
                ScrollMinY = 0D,
                ScrollMinY2 = 0D
            };
            tuningGraph.DoubleClick += TuningGraph_DoubleClick;
            graphBorderPanel.Controls.Add(tuningGraph);

            headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 54,
                BackColor = surface
            };
            Controls.Add(headerPanel);

            lblEyebrow = new FormsLabel
            {
                Font = eyebrowFont,
                ForeColor = gold,
                BackColor = surface,
                Text = "TUNING",
                AutoEllipsis = true
            };
            headerPanel.Controls.Add(lblEyebrow);

            lblTitle = new FormsLabel
            {
                Font = titleFont,
                ForeColor = lightGray,
                BackColor = surface,
                Text = "Signal tuning graph",
                AutoEllipsis = true
            };
            headerPanel.Controls.Add(lblTitle);

            lblHint = new FormsLabel
            {
                Font = hintFont,
                ForeColor = mutedGray,
                BackColor = surface,
                Text = "Double-click the graph to choose live signals.",
                AutoEllipsis = true
            };
            headerPanel.Controls.Add(lblHint);

            lblStatus = new FormsLabel
            {
                Font = badgeFont,
                ForeColor = lightGray,
                BackColor = Color.FromArgb(44, 49, 59),
                Text = "OFFLINE",
                TextAlign = ContentAlignment.MiddleCenter
            };
            headerPanel.Controls.Add(lblStatus);
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

            int badgeWidth = 104;
            int badgeHeight = 24;
            int rightInset = 16;
            int topInset = 15;

            lblStatus.Bounds = new Rectangle(
                Math.Max(140, headerPanel.ClientSize.Width - badgeWidth - rightInset),
                topInset,
                badgeWidth,
                badgeHeight);

            int textWidth = Math.Max(150, lblStatus.Left - 24);
            lblEyebrow.Bounds = new Rectangle(16, 8, textWidth, 14);
            lblTitle.Bounds = new Rectangle(16, 20, textWidth, 18);
            lblHint.Bounds = new Rectangle(16, 36, textWidth, 14);
        }

        private void ApplyEmbeddedLayout()
        {
            if (headerPanel == null)
                return;

            Padding = embeddedMode ? new Padding(0) : new Padding(10, 0, 10, 10);
            headerPanel.Visible = !embeddedMode;
        }

        public void SetEnabled(bool enabled)
        {
            tuningEnabled = enabled;
            lastSampleTimeUtc = DateTime.MinValue;

            if (enabled)
                tickStart = Environment.TickCount;

            ApplyStatusState();
            UpdateGraphTimerState();
        }

        public void ActivateView()
        {
            hostActive = true;
            UpdateGraphTimerState();
        }

        public void DeactivateView()
        {
            hostActive = false;
            UpdateGraphTimerState();
        }

        public void SetOffline(bool connected)
        {
            ApplyStatusState();
        }

        public void UpdateTelemetry(CurrentState cs)
        {
            if (!tuningEnabled || cs == null || !hostActive)
                return;

            if (selectedSeries.Count == 0)
                return;

            if (lastSampleTimeUtc != DateTime.MinValue && lastSampleTimeUtc.AddMilliseconds(75) >= DateTime.UtcNow)
                return;

            double time = (Environment.TickCount - tickStart) / 1000.0;

            foreach (TuningSeries series in selectedSeries)
            {
                try
                {
                    object value = series.Property?.GetValue(cs, null);
                    if (value == null)
                        continue;

                    series.Points.Add(time, value.ConvertToDouble());
                }
                catch
                {
                }
            }

            lastSampleTimeUtc = DateTime.UtcNow;
        }

        private void ApplyStatusState()
        {
            bool connected = MainV2.comPort?.BaseStream?.IsOpen == true;

            lblStatus.Text = !tuningEnabled ? "OFF" : connected ? "LIVE" : "READY";
            lblStatus.BackColor = !tuningEnabled
                ? Color.FromArgb(44, 49, 59)
                : connected
                    ? Color.FromArgb(27, 84, 113)
                    : Color.FromArgb(78, 62, 28);

            graphBorderPanel.BackColor = !tuningEnabled
                ? border
                : connected
                    ? Color.FromArgb(54, 109, 129)
                    : Color.FromArgb(86, 72, 40);

            lblHint.Text = selectedSeries.Count == 0
                ? "Double-click the graph to choose live signals."
                : $"{selectedSeries.Count} signal{(selectedSeries.Count == 1 ? string.Empty : "s")} active | double-click to edit.";
        }

        private void UpdateGraphTimerState()
        {
            redrawTimer.Enabled = tuningEnabled && hostActive && selectedSeries.Count > 0;
        }

        private void CreateChart(ZedGraphControl graph)
        {
            GraphPane pane = graph.GraphPane;

            pane.Title.Text = "Tuning - Double click to change items";
            pane.Title.FontSpec.FontColor = lightGray;
            pane.Title.FontSpec.Size = 13;
            pane.XAxis.Title.Text = "Time (s)";
            pane.YAxis.Title.Text = "Unit";
            pane.YAxis.Title.FontSpec.Size += 2;
            pane.YAxis.Title.FontSpec.FontColor = lightGray;
            pane.YAxis.Scale.FontSpec.FontColor = lightGray;
            pane.Y2Axis.Scale.FontSpec.FontColor = lightGray;
            pane.Y2Axis.Title.FontSpec.FontColor = lightGray;
            pane.Y2Axis.Title.Text = "Aux";
            pane.Y2Axis.IsVisible = false;

            pane.XAxis.Scale.FontSpec.FontColor = mutedGray;
            pane.XAxis.Title.FontSpec.FontColor = mutedGray;
            pane.XAxis.MajorGrid.IsVisible = true;
            pane.XAxis.MajorGrid.Color = Color.FromArgb(36, 54, 78);
            pane.YAxis.MajorGrid.IsVisible = true;
            pane.YAxis.MajorGrid.Color = Color.FromArgb(28, 42, 60);
            pane.YAxis.MajorGrid.IsZeroLine = true;
            pane.YAxis.MajorTic.IsOpposite = false;
            pane.YAxis.MinorTic.IsOpposite = false;
            pane.YAxis.Scale.Align = AlignP.Inside;

            pane.XAxis.Scale.Min = 0;
            pane.XAxis.Scale.Max = 5;

            pane.Fill = new Fill(Color.FromArgb(17, 23, 35));
            pane.Chart.Fill = new Fill(Color.FromArgb(8, 12, 20));
            pane.Chart.Border.Color = Color.FromArgb(38, 49, 67);
            pane.Border.Color = Color.FromArgb(38, 49, 67);

            pane.Legend.IsVisible = true;
            pane.Legend.Position = LegendPos.TopCenter;
            pane.Legend.FontSpec.FontColor = lightGray;
            pane.Legend.Fill = new Fill(Color.FromArgb(20, 26, 38));
            pane.Legend.Border.Color = Color.FromArgb(38, 49, 67);

            graph.AxisChange();
            graph.Invalidate();
        }

        private void RestoreSelectedSeries()
        {
            CurrentState source = MainV2.comPort?.MAV?.cs ?? new CurrentState();
            string savedSelection = Settings.Instance[TuningSelectionSettingKey];

            if (!string.IsNullOrWhiteSpace(savedSelection))
            {
                string[] tokens = savedSelection.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string token in tokens)
                {
                    string propertyName = token;
                    string displayText = source.GetNameandUnit(token);

                    if (token.StartsWith("customfield", StringComparison.OrdinalIgnoreCase))
                    {
                        if (token.Length > 12)
                        {
                            propertyName = token.Substring(0, 12);
                            displayText = token.Substring(12);
                            if (!CurrentState.custom_field_names.ContainsKey(propertyName))
                                CurrentState.custom_field_names.Add(propertyName, displayText);
                        }
                        else if (CurrentState.custom_field_names.ContainsKey(propertyName))
                        {
                            displayText = CurrentState.custom_field_names[propertyName];
                        }
                    }

                    AddSeries(propertyName, displayText, false, source, false);
                }
            }
            else
            {
                foreach (string key in DefaultSeriesKeys)
                    AddSeries(key, source.GetNameandUnit(key), false, source, false);
            }

            tickStart = Environment.TickCount;
            ApplyStatusState();
            UpdateGraphTimerState();
        }

        private void TuningGraph_DoubleClick(object sender, EventArgs e)
        {
            var form = new DisplayThisForm(
                isChecked: IsSeriesChecked,
                checkChangedHandler: SelectionCheckChanged,
                mouseDownHandler: SelectionMouseDown);
            form.Show();
        }

        private bool IsSeriesChecked(string fieldName)
        {
            return selectedSeries.Exists(series => string.Equals(series.Name, fieldName, StringComparison.OrdinalIgnoreCase));
        }

        private void SelectionMouseDown(object sender, MouseEventArgs e)
        {
            tuningSelectionRightAxis = e.Button == MouseButtons.Right;
            if (tuningSelectionRightAxis && sender is CheckBox checkBox)
                checkBox.Checked = !checkBox.Checked;
        }

        private void SelectionCheckChanged(object sender, EventArgs e)
        {
            if (!(sender is CheckBox checkBox))
                return;

            ThemeManager.ApplyThemeTo(checkBox);

            CurrentState source = MainV2.comPort?.MAV?.cs ?? new CurrentState();
            if (checkBox.Checked)
            {
                bool added = AddSeries(checkBox.Name, checkBox.Text, tuningSelectionRightAxis, source, true);
                if (!added)
                    checkBox.Checked = false;
                else
                    checkBox.BackColor = Color.Green;
            }
            else
            {
                RemoveSeries(checkBox.Name);
                checkBox.BackColor = Color.Transparent;
            }

            SaveSelectedSeries();
            ApplyStatusState();
            UpdateGraphTimerState();
            tuningGraph.AxisChange();
            tuningGraph.Invalidate();
        }

        private bool AddSeries(string propertyName, string displayText, bool useRightAxis, CurrentState source, bool showLimitMessage)
        {
            if (IsSeriesChecked(propertyName))
                return true;

            if (selectedSeries.Count >= MaxSeriesCount)
            {
                if (showLimitMessage)
                    CustomMessageBox.Show("Max 20 at a time.");
                return false;
            }

            PropertyInfo property = ResolveProperty(propertyName, source);
            if (property == null)
                return false;

            var points = new RollingPointPairList(1200);
            Color color = SeriesPalette[Math.Min(selectedSeries.Count, SeriesPalette.Length - 1)];
            LineItem curve = tuningGraph.GraphPane.AddCurve(displayText, points, color, SymbolType.None);
            curve.Tag = propertyName;

            if (useRightAxis)
            {
                curve.Label.Text += " R";
                curve.IsY2Axis = true;
                curve.YAxisIndex = 0;
            }

            selectedSeries.Add(new TuningSeries
            {
                Name = propertyName,
                DisplayText = displayText,
                Property = property,
                Curve = curve,
                Points = points,
                UseRightAxis = useRightAxis
            });

            tuningGraph.GraphPane.Y2Axis.IsVisible = selectedSeries.Exists(series => series.UseRightAxis);
            return true;
        }

        private void RemoveSeries(string propertyName)
        {
            TuningSeries series = selectedSeries
                .Find(item => string.Equals(item.Name, propertyName, StringComparison.OrdinalIgnoreCase));

            if (series == null)
                return;

            tuningGraph.GraphPane.CurveList.Remove(series.Curve);
            selectedSeries.Remove(series);
            tuningGraph.GraphPane.Y2Axis.IsVisible = selectedSeries.Exists(item => item.UseRightAxis);
        }

        private static PropertyInfo ResolveProperty(string propertyName, object source)
        {
            return source?.GetType()
                .GetProperties()
                .FirstOrDefault(property => string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase));
        }

        private void SaveSelectedSeries()
        {
            try
            {
                string selected = string.Join("|",
                    selectedSeries.Select(series =>
                        series.Name.IndexOf("customfield", StringComparison.OrdinalIgnoreCase) >= 0
                            ? series.Name + series.DisplayText
                            : series.Name));

                if (!string.IsNullOrWhiteSpace(selected))
                    selected += "|";

                Settings.Instance[TuningSelectionSettingKey] = selected;
            }
            catch
            {
            }
        }

        private void RedrawTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (tuningGraph.GraphPane.CurveList.Count <= 0)
                    return;

                double time = (Environment.TickCount - tickStart) / 1000.0;
                Scale xScale = tuningGraph.GraphPane.XAxis.Scale;
                if (time > xScale.Max - xScale.MajorStep)
                {
                    xScale.Max = time + xScale.MajorStep;
                    xScale.Min = xScale.Max - 10.0;
                }

                tuningGraph.AxisChange();
                tuningGraph.Invalidate();
            }
            catch
            {
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                redrawTimer?.Stop();
                redrawTimer?.Dispose();
                eyebrowFont?.Dispose();
                titleFont?.Dispose();
                hintFont?.Dispose();
                badgeFont?.Dispose();
            }

            base.Dispose(disposing);
        }

        private sealed class TuningSeries
        {
            public string Name { get; set; }
            public string DisplayText { get; set; }
            public PropertyInfo Property { get; set; }
            public RollingPointPairList Points { get; set; }
            public LineItem Curve { get; set; }
            public bool UseRightAxis { get; set; }
        }
    }
}
