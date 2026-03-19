using System;
using System.Drawing;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews
{
    /// <summary>
    /// Top Navigation Bar - Tab switching and connection controls
    /// Implements: TopNav.tsx functionality
    /// Layout: Logo | Tabs | Connection Controls
    /// </summary>
    public class TopNavigationBar : MyUserControl
    {
        // Events
        public event EventHandler<TabChangedEventArgs> TabChanged;
        public event EventHandler<ConnectionRequestedEventArgs> ConnectionRequested;

        // UI Components
        private Label labelLogo;
        private Button btnFlightData;
        private Button btnFlightPlan;
        private Button btnInitialSetup;
        private Button btnConfigTuning;
        private ComboBox comboPorts;
        private ComboBox comboBaudRate;
        private Button btnConnect;

        // State
        private TabId currentTab = TabId.FlightData;
        private bool isConnected = false;

        public TopNavigationBar()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialize all UI components
        /// </summary>
        private void InitializeComponent()
        {
            this.SuspendLayout();

            // Main properties
            this.Dock = DockStyle.Top;
            this.Height = 60;
            this.BackColor = BriechTheme.CHARCOAL;
            this.ForeColor = BriechTheme.TEXT_PRIMARY;
            this.Font = new Font("Segoe UI", 9f);

            // Draw border
            this.Paint += (sender, e) =>
            {
                using (var pen = new Pen(BriechTheme.BORDER_GOLD, 1))
                {
                    e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
                }
            };

            // ===== LOGO SECTION =====
            labelLogo = new Label();
            labelLogo.Text = "⬤ BRIECH UAS";
            labelLogo.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            labelLogo.ForeColor = BriechTheme.BRIECH_GOLD;
            labelLogo.Location = new Point(10, 12);
            labelLogo.Size = new Size(150, 35);
            labelLogo.AutoSize = false;
            labelLogo.TextAlign = ContentAlignment.MiddleLeft;
            this.Controls.Add(labelLogo);

            // ===== TAB BUTTONS =====
            int tabX = 170;
            int tabWidth = 110;
            int tabHeight = 35;
            int tabY = 12;

            // Flight Data Tab
            btnFlightData = CreateTabButton("FLIGHT DATA", tabX, tabY, tabWidth, tabHeight);
            btnFlightData.Click += (sender, e) => SelectTab(TabId.FlightData);
            this.Controls.Add(btnFlightData);
            tabX += tabWidth + 5;

            // Flight Plan Tab
            btnFlightPlan = CreateTabButton("FLIGHT PLAN", tabX, tabY, tabWidth, tabHeight);
            btnFlightPlan.Click += (sender, e) => SelectTab(TabId.FlightPlan);
            this.Controls.Add(btnFlightPlan);
            tabX += tabWidth + 5;

            // Initial Setup Tab
            btnInitialSetup = CreateTabButton("SETUP", tabX, tabY, tabWidth, tabHeight);
            btnInitialSetup.Click += (sender, e) => SelectTab(TabId.InitialSetup);
            this.Controls.Add(btnInitialSetup);
            tabX += tabWidth + 5;

            // Config/Tuning Tab
            btnConfigTuning = CreateTabButton("CONFIG", tabX, tabY, tabWidth, tabHeight);
            btnConfigTuning.Click += (sender, e) => SelectTab(TabId.ConfigTuning);
            this.Controls.Add(btnConfigTuning);

            // Set Flight Data as initial active
            UpdateTabButtons();

            // ===== CONNECTION CONTROLS =====
            int connX = Width - 380;

            // Port Combo
            Label labelPort = new Label();
            labelPort.Text = "Port:";
            labelPort.ForeColor = BriechTheme.TEXT_SECONDARY;
            labelPort.Location = new Point(connX, 15);
            labelPort.Size = new Size(35, 20);
            labelPort.AutoSize = false;
            labelPort.TextAlign = ContentAlignment.MiddleLeft;
            this.Controls.Add(labelPort);

            comboPorts = new ComboBox();
            comboPorts.Location = new Point(connX + 35, 12);
            comboPorts.Size = new Size(80, 20);
            comboPorts.BackColor = BriechTheme.DARK_NAVY;
            comboPorts.ForeColor = BriechTheme.TEXT_PRIMARY;
            comboPorts.DropDownStyle = ComboBoxStyle.DropDownList;
            RefreshPorts();
            this.Controls.Add(comboPorts);

            // Baud Rate Combo
            connX += 125;
            Label labelBaud = new Label();
            labelBaud.Text = "Baud:";
            labelBaud.ForeColor = BriechTheme.TEXT_SECONDARY;
            labelBaud.Location = new Point(connX, 15);
            labelBaud.Size = new Size(35, 20);
            labelBaud.AutoSize = false;
            labelBaud.TextAlign = ContentAlignment.MiddleLeft;
            this.Controls.Add(labelBaud);

            comboBaudRate = new ComboBox();
            comboBaudRate.Location = new Point(connX + 35, 12);
            comboBaudRate.Size = new Size(70, 20);
            comboBaudRate.BackColor = BriechTheme.DARK_NAVY;
            comboBaudRate.ForeColor = BriechTheme.TEXT_PRIMARY;
            comboBaudRate.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBaudRate.Items.AddRange(new object[] { "9600", "57600", "115200" });
            comboBaudRate.SelectedIndex = 2;
            this.Controls.Add(comboBaudRate);

            // Connect Button
            connX += 115;
            btnConnect = new Button();
            btnConnect.Text = "CONNECT";
            btnConnect.Location = new Point(connX, 12);
            btnConnect.Size = new Size(80, 35);
            btnConnect.BackColor = BriechTheme.STATUS_GREEN;
            btnConnect.ForeColor = BriechTheme.DARK_NAVY;
            btnConnect.FlatStyle = FlatStyle.Flat;
            btnConnect.FlatAppearance.BorderSize = 2;
            btnConnect.FlatAppearance.BorderColor = BriechTheme.BRIECH_GOLD;
            btnConnect.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            btnConnect.Click += BtnConnect_Click;
            this.Controls.Add(btnConnect);

            this.ResumeLayout();
        }

        /// <summary>
        /// Create a tab button with standard styling
        /// </summary>
        private Button CreateTabButton(string text, int x, int y, int width, int height)
        {
            var btn = new Button();
            btn.Text = text;
            btn.Location = new Point(x, y);
            btn.Size = new Size(width, height);
            btn.BackColor = BriechTheme.CHARCOAL;
            btn.ForeColor = BriechTheme.TEXT_SECONDARY;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 2;
            btn.FlatAppearance.BorderColor = Color.Transparent;
            btn.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            btn.Cursor = Cursors.Hand;
            return btn;
        }

        /// <summary>
        /// Select a tab and fire event
        /// </summary>
        private void SelectTab(TabId tab)
        {
            if (currentTab != tab)
            {
                TabId previousTab = currentTab;
                currentTab = tab;
                UpdateTabButtons();

                TabChanged?.Invoke(this, new TabChangedEventArgs
                {
                    SelectedTab = tab,
                    PreviousTab = previousTab
                });
            }
        }

        /// <summary>
        /// Update tab button appearance based on current selection
        /// </summary>
        private void UpdateTabButtons()
        {
            // Reset all
            foreach (Control ctrl in this.Controls)
            {
                if (ctrl is Button btn && (btn == btnFlightData || btn == btnFlightPlan || btn == btnInitialSetup || btn == btnConfigTuning))
                {
                    btn.BackColor = BriechTheme.CHARCOAL;
                    btn.ForeColor = BriechTheme.TEXT_SECONDARY;
                    btn.FlatAppearance.BorderColor = Color.Transparent;
                }
            }

            // Highlight active tab
            Button activeBtn = null;
            switch (currentTab)
            {
                case TabId.FlightData: activeBtn = btnFlightData; break;
                case TabId.FlightPlan: activeBtn = btnFlightPlan; break;
                case TabId.InitialSetup: activeBtn = btnInitialSetup; break;
                case TabId.ConfigTuning: activeBtn = btnConfigTuning; break;
            }

            if (activeBtn != null)
            {
                activeBtn.BackColor = BriechTheme.DARK_NAVY;
                activeBtn.ForeColor = BriechTheme.BRIECH_GOLD;
                activeBtn.FlatAppearance.BorderColor = BriechTheme.BRIECH_GOLD;
            }
        }

        /// <summary>
        /// Refresh available serial ports
        /// </summary>
        private void RefreshPorts()
        {
            comboPorts.Items.Clear();
            string[] ports = System.IO.Ports.SerialPort.GetPortNames();
            if (ports.Length > 0)
            {
                comboPorts.Items.AddRange(ports);
                comboPorts.SelectedIndex = 0;
            }
            else
            {
                comboPorts.Items.Add("No ports");
                comboPorts.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// Handle connect button click
        /// </summary>
        private void BtnConnect_Click(object sender, EventArgs e)
        {
            if (isConnected)
            {
                // Disconnect
                isConnected = false;
                btnConnect.Text = "CONNECT";
                btnConnect.BackColor = BriechTheme.STATUS_GREEN;

                ConnectionRequested?.Invoke(this, new ConnectionRequestedEventArgs
                {
                    Connect = false
                });
            }
            else
            {
                // Connect
                string port = comboPorts.SelectedItem?.ToString() ?? "COM1";
                int baud = int.TryParse(comboBaudRate.SelectedItem?.ToString(), out int b) ? b : 115200;

                isConnected = true;
                btnConnect.Text = "DISCONNECT";
                btnConnect.BackColor = BriechTheme.STATUS_RED;

                ConnectionRequested?.Invoke(this, new ConnectionRequestedEventArgs
                {
                    Connect = true,
                    Port = port,
                    BaudRate = baud
                });
            }
        }

        /// <summary>
        /// Update connect button state from external source
        /// </summary>
        public void UpdateConnectButton(bool connected)
        {
            isConnected = connected;
            if (connected)
            {
                btnConnect.Text = "DISCONNECT";
                btnConnect.BackColor = BriechTheme.STATUS_RED;
            }
            else
            {
                btnConnect.Text = "CONNECT";
                btnConnect.BackColor = BriechTheme.STATUS_GREEN;
            }
        }

        /// <summary>
        /// Get currently selected tab
        /// </summary>
        public TabId CurrentTab => currentTab;
    }
}
