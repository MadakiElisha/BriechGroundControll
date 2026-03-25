using System;
using System.Drawing;
using System.Windows.Forms;
using MissionPlanner.Utilities;

namespace MissionPlanner.GCSViews
{
    public class PanelAuxiliaryDeck : Panel
    {
        private const string ShowPreviewSettingKey = "ModernFlight.Aux.Show3D";
        private const string ShowTuningSettingKey = "ModernFlight.Aux.ShowTuning";
        private const string ActiveViewSettingKey = "ModernFlight.Aux.ActiveView";

        private readonly Font titleFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private readonly Font tabFont = new Font("Segoe UI", 8.25f, FontStyle.Bold);

        private readonly Color surface = Color.FromArgb(24, 31, 44);
        private readonly Color lightGray = Color.FromArgb(235, 239, 245);
        private readonly Color mutedGray = Color.FromArgb(138, 149, 168);
        private readonly Color gold = Color.FromArgb(200, 168, 101);
        private readonly Color activeToggle = Color.FromArgb(39, 59, 81);
        private readonly Color inactiveToggle = Color.FromArgb(26, 31, 44);
        private readonly Color activeBorder = Color.FromArgb(74, 190, 225);
        private readonly Color inactiveBorder = Color.FromArgb(46, 58, 76);

        private Panel headerPanel;
        private Label lblTitle;
        private FlowLayoutPanel toggleBar;
        private CheckBox chk3D;
        private CheckBox chkTuning;
        private Panel contentHost;
        private Label lblEmptyState;

        private readonly PanelSituationalPreviewDeck previewDeck;
        private readonly PanelTuningDeck tuningDeck;

        private AuxiliaryViewKind activeView = AuxiliaryViewKind.Preview;
        private bool hostActive;
        private bool suppressToggleEvents;

        public event EventHandler VisibilityPreferenceChanged;

        public PanelAuxiliaryDeck()
        {
            BackColor = Color.FromArgb(10, 14, 20);
            Padding = new Padding(10, 0, 10, 10);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            previewDeck = new PanelSituationalPreviewDeck
            {
                Dock = DockStyle.Fill,
                Visible = false,
                EmbeddedMode = true
            };

            tuningDeck = new PanelTuningDeck
            {
                Dock = DockStyle.Fill,
                Visible = false,
                EmbeddedMode = true
            };

            InitializeControls();
            LoadPreferences();
            ApplyPreferences(false);
        }

        public bool HasVisibleContent => true;

        private void InitializeControls()
        {
            contentHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(10, 14, 20)
            };
            Controls.Add(contentHost);

            lblEmptyState = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = mutedGray,
                BackColor = Color.FromArgb(16, 21, 31),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "Select 3D or tuning to open the auxiliary view.",
                Visible = false
            };
            contentHost.Controls.Add(lblEmptyState);
            contentHost.Controls.Add(tuningDeck);
            contentHost.Controls.Add(previewDeck);

            headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 42,
                BackColor = surface
            };
            headerPanel.Resize += (s, e) => UpdateHeaderLayout();
            Controls.Add(headerPanel);

            lblTitle = new Label
            {
                AutoEllipsis = true,
                Font = titleFont,
                ForeColor = gold,
                BackColor = surface,
                Padding = new Padding(0),
                Text = "AUXILIARY"
            };
            headerPanel.Controls.Add(lblTitle);

            toggleBar = new FlowLayoutPanel
            {
                AutoSize = false,
                Height = 24,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0),
                BackColor = surface
            };
            headerPanel.Controls.Add(toggleBar);

            chk3D = CreateToggle("3D");
            chk3D.CheckedChanged += Toggle_CheckedChanged;
            toggleBar.Controls.Add(chk3D);

            chkTuning = CreateToggle("Tuning");
            chkTuning.CheckedChanged += Toggle_CheckedChanged;
            toggleBar.Controls.Add(chkTuning);

            UpdateHeaderLayout();
        }

        private CheckBox CreateToggle(string text)
        {
            var toggle = new CheckBox
            {
                Appearance = Appearance.Button,
                AutoSize = false,
                Size = new Size(text == "Tuning" ? 72 : 54, 24),
                Text = text,
                TextAlign = ContentAlignment.MiddleCenter,
                FlatStyle = FlatStyle.Flat,
                Font = tabFont,
                ForeColor = lightGray,
                BackColor = inactiveToggle,
                UseVisualStyleBackColor = false,
                Margin = new Padding(6, 0, 0, 0)
            };

            toggle.FlatAppearance.BorderSize = 1;
            toggle.FlatAppearance.BorderColor = inactiveBorder;
            toggle.FlatAppearance.CheckedBackColor = activeToggle;
            toggle.FlatAppearance.MouseOverBackColor = Color.FromArgb(31, 39, 54);
            toggle.FlatAppearance.MouseDownBackColor = Color.FromArgb(39, 52, 71);
            return toggle;
        }

        private void Toggle_CheckedChanged(object sender, EventArgs e)
        {
            if (suppressToggleEvents)
                return;

            suppressToggleEvents = true;

            try
            {
                if (ReferenceEquals(sender, chk3D))
                {
                    if (chk3D.Checked)
                    {
                        chkTuning.Checked = false;
                        activeView = AuxiliaryViewKind.Preview;
                    }
                    else
                    {
                        chk3D.Checked = true;
                        activeView = AuxiliaryViewKind.Preview;
                    }
                }
                else if (ReferenceEquals(sender, chkTuning))
                {
                    if (chkTuning.Checked)
                    {
                        chk3D.Checked = false;
                        activeView = AuxiliaryViewKind.Tuning;
                    }
                    else
                    {
                        chkTuning.Checked = true;
                        activeView = AuxiliaryViewKind.Tuning;
                    }
                }
            }
            finally
            {
                suppressToggleEvents = false;
            }

            SavePreferences();
            ApplyPreferences(true);
        }

        private void ApplyPreferences(bool notify)
        {
            bool showPreview = activeView == AuxiliaryViewKind.Preview;
            bool showTuning = activeView == AuxiliaryViewKind.Tuning;

            previewDeck.Visible = showPreview && activeView == AuxiliaryViewKind.Preview;
            tuningDeck.Visible = showTuning && activeView == AuxiliaryViewKind.Tuning;
            lblEmptyState.Visible = false;

            tuningDeck.SetEnabled(showTuning);

            if (hostActive && showPreview && activeView == AuxiliaryViewKind.Preview)
                previewDeck.ActivateView();
            else
                previewDeck.DeactivateView();

            if (hostActive && showTuning && activeView == AuxiliaryViewKind.Tuning)
                tuningDeck.ActivateView();
            else
                tuningDeck.DeactivateView();

            StyleToggle(chk3D, showPreview);
            StyleToggle(chkTuning, showTuning);
            lblTitle.Text = showPreview ? "AUXILIARY  |  3D VIEW" : "AUXILIARY  |  TUNING";
            UpdateHeaderLayout();

            SavePreferences();

            if (notify)
                VisibilityPreferenceChanged?.Invoke(this, EventArgs.Empty);
        }

        private void StyleToggle(CheckBox toggle, bool isChecked)
        {
            if (toggle == null)
                return;

            toggle.BackColor = isChecked ? activeToggle : inactiveToggle;
            toggle.FlatAppearance.BorderColor = isChecked ? activeBorder : inactiveBorder;
            toggle.ForeColor = isChecked ? lightGray : mutedGray;
        }

        private void UpdateHeaderLayout()
        {
            if (headerPanel == null || lblTitle == null || toggleBar == null)
                return;

            const int sidePadding = 10;
            const int topPadding = 8;
            int toggleWidth = chk3D.Width + chk3D.Margin.Left + chkTuning.Width + chkTuning.Margin.Left;

            headerPanel.Height = 42;

            toggleBar.Bounds = new Rectangle(
                Math.Max(sidePadding, headerPanel.ClientSize.Width - toggleWidth - sidePadding),
                topPadding,
                toggleWidth,
                24);

            int controlsLeft = toggleBar.Left;
            int titleWidth = Math.Max(120, controlsLeft - sidePadding - 12);
            lblTitle.Bounds = new Rectangle(
                sidePadding,
                11,
                Math.Min(Math.Max(120, headerPanel.ClientSize.Width - (2 * sidePadding)), titleWidth),
                20);
        }

        private void LoadPreferences()
        {
            string active = Settings.Instance[ActiveViewSettingKey];
            bool preferPreview = Settings.Instance[ShowPreviewSettingKey] != null
                ? Settings.Instance.GetBoolean(ShowPreviewSettingKey)
                : true;
            bool preferTuning = Settings.Instance[ShowTuningSettingKey] != null
                ? Settings.Instance.GetBoolean(ShowTuningSettingKey)
                : false;

            if (string.Equals(active, AuxiliaryViewKind.Tuning.ToString(), StringComparison.OrdinalIgnoreCase))
                activeView = AuxiliaryViewKind.Tuning;
            else if (string.Equals(active, AuxiliaryViewKind.Preview.ToString(), StringComparison.OrdinalIgnoreCase))
                activeView = AuxiliaryViewKind.Preview;
            else
                activeView = preferTuning && !preferPreview ? AuxiliaryViewKind.Tuning : AuxiliaryViewKind.Preview;

            suppressToggleEvents = true;
            chk3D.Checked = activeView == AuxiliaryViewKind.Preview;
            chkTuning.Checked = activeView == AuxiliaryViewKind.Tuning;
            suppressToggleEvents = false;
        }

        private void SavePreferences()
        {
            try
            {
                Settings.Instance[ShowPreviewSettingKey] = (activeView == AuxiliaryViewKind.Preview).ToString();
                Settings.Instance[ShowTuningSettingKey] = (activeView == AuxiliaryViewKind.Tuning).ToString();
                Settings.Instance[ActiveViewSettingKey] = activeView.ToString();
            }
            catch
            {
            }
        }

        public void SetOffline(bool connected)
        {
            previewDeck.SetOffline(connected);
            tuningDeck.SetOffline(connected);
        }

        public void UpdateTelemetry(CurrentState cs)
        {
            if (activeView == AuxiliaryViewKind.Preview)
                previewDeck.UpdateTelemetry(cs);

            if (activeView == AuxiliaryViewKind.Tuning)
                tuningDeck.UpdateTelemetry(cs);
        }

        public void ActivateView()
        {
            hostActive = true;

            if (activeView == AuxiliaryViewKind.Tuning)
                tuningDeck.ActivateView();
            else
                tuningDeck.DeactivateView();

            if (activeView == AuxiliaryViewKind.Preview)
                previewDeck.ActivateView();
            else
                previewDeck.DeactivateView();
        }

        public void DeactivateView()
        {
            hostActive = false;
            tuningDeck.DeactivateView();
            previewDeck.DeactivateView();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                titleFont?.Dispose();
                tabFont?.Dispose();
            }

            base.Dispose(disposing);
        }

        private enum AuxiliaryViewKind
        {
            Preview,
            Tuning
        }
    }
}
