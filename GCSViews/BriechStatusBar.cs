using System;
using System.Drawing;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews
{
    /// <summary>
    /// BRIECH Status Bar - Bottom status display showing connection, signal, and system info
    /// Implements: StatusBar.tsx functionality
    /// </summary>
    public class BriechStatusBar : Control
    {
        // UI Components
        private Label labelConnectionStatus;
        private Label labelPackets;
        private Label labelLinkQuality;
        private Label labelRSSI;
        private Label labelFuel;
        private Label labelTime;
        private Label labelCopyright;

        public BriechStatusBar()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialize all status bar components
        /// </summary>
        private void InitializeComponent()
        {
            this.SuspendLayout();

            // Main properties
            this.Dock = DockStyle.Bottom;
            this.Height = 35;
            this.BackColor = BriechTheme.CHARCOAL;
            this.ForeColor = BriechTheme.TEXT_PRIMARY;
            this.Font = new Font("Segoe UI", 9f);

            // Draw top border
            this.Paint += (sender, e) =>
            {
                using (var pen = new Pen(BriechTheme.BORDER_GOLD, 1))
                {
                    e.Graphics.DrawLine(pen, 0, 0, Width, 0);
                }
            };

            int xPos = 10;

            // ===== CONNECTION STATUS =====
            labelConnectionStatus = new Label();
            labelConnectionStatus.Text = "● DISCONNECTED";
            labelConnectionStatus.ForeColor = BriechTheme.STATUS_RED;
            labelConnectionStatus.Location = new Point(xPos, 8);
            labelConnectionStatus.Size = new Size(150, 20);
            labelConnectionStatus.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            labelConnectionStatus.AutoSize = false;
            labelConnectionStatus.TextAlign = ContentAlignment.MiddleLeft;
            this.Controls.Add(labelConnectionStatus);

            xPos += 160;

            // ===== PACKETS =====
            labelPackets = new Label();
            labelPackets.Text = "Pkts: 0";
            labelPackets.ForeColor = BriechTheme.TEXT_SECONDARY;
            labelPackets.Location = new Point(xPos, 8);
            labelPackets.Size = new Size(80, 20);
            labelPackets.AutoSize = false;
            labelPackets.TextAlign = ContentAlignment.MiddleLeft;
            this.Controls.Add(labelPackets);

            xPos += 90;

            // ===== LINK QUALITY =====
            labelLinkQuality = new Label();
            labelLinkQuality.Text = "Link: 0%";
            labelLinkQuality.ForeColor = BriechTheme.TEXT_SECONDARY;
            labelLinkQuality.Location = new Point(xPos, 8);
            labelLinkQuality.Size = new Size(100, 20);
            labelLinkQuality.AutoSize = false;
            labelLinkQuality.TextAlign = ContentAlignment.MiddleLeft;
            this.Controls.Add(labelLinkQuality);

            xPos += 110;

            // ===== RSSI =====
            labelRSSI = new Label();
            labelRSSI.Text = "RSSI: -90dBm";
            labelRSSI.ForeColor = BriechTheme.TEXT_SECONDARY;
            labelRSSI.Location = new Point(xPos, 8);
            labelRSSI.Size = new Size(120, 20);
            labelRSSI.AutoSize = false;
            labelRSSI.TextAlign = ContentAlignment.MiddleLeft;
            this.Controls.Add(labelRSSI);

            // ===== RIGHT SIDE: FUEL & TIME =====
            int rightX = Width - 280;

            // Fuel Remaining
            labelFuel = new Label();
            labelFuel.Text = "Fuel: 100%";
            labelFuel.ForeColor = BriechTheme.TEXT_SECONDARY;
            labelFuel.Location = new Point(rightX, 8);
            labelFuel.Size = new Size(100, 20);
            labelFuel.AutoSize = false;
            labelFuel.TextAlign = ContentAlignment.MiddleLeft;
            this.Controls.Add(labelFuel);

            rightX += 110;

            // Estimated Time Remaining
            labelTime = new Label();
            labelTime.Text = "ETA: --:--";
            labelTime.ForeColor = BriechTheme.TEXT_SECONDARY;
            labelTime.Location = new Point(rightX, 8);
            labelTime.Size = new Size(100, 20);
            labelTime.AutoSize = false;
            labelTime.TextAlign = ContentAlignment.MiddleLeft;
            this.Controls.Add(labelTime);

            rightX += 110;

            // ===== COPYRIGHT =====
            labelCopyright = new Label();
            labelCopyright.Text = "© BRIECH UAS";
            labelCopyright.ForeColor = BriechTheme.TEXT_SECONDARY;
            labelCopyright.Location = new Point(rightX, 8);
            labelCopyright.Size = new Size(100, 20);
            labelCopyright.AutoSize = false;
            labelCopyright.TextAlign = ContentAlignment.MiddleRight;
            labelCopyright.Font = new Font("Segoe UI", 8f);
            this.Controls.Add(labelCopyright);

            this.ResumeLayout();
        }

        /// <summary>
        /// Update all status bar values
        /// </summary>
        public void UpdateAll(bool connected, int packets, double linkQuality, int rssi, int fuelPercent, int timeRemainingSeconds)
        {
            // Update connection status
            if (connected)
            {
                labelConnectionStatus.Text = "● CONNECTED";
                labelConnectionStatus.ForeColor = BriechTheme.STATUS_GREEN;
            }
            else
            {
                labelConnectionStatus.Text = "● DISCONNECTED";
                labelConnectionStatus.ForeColor = BriechTheme.STATUS_RED;
            }

            // Update packets
            labelPackets.Text = $"Pkts: {packets}";

            // Update link quality with color coding
            labelLinkQuality.Text = $"Link: {linkQuality:F0}%";
            labelLinkQuality.ForeColor = BriechTheme.GetLinkQualityColor(linkQuality);

            // Update RSSI
            labelRSSI.Text = $"RSSI: {rssi}dBm";

            // Update fuel
            labelFuel.Text = $"Fuel: {fuelPercent}%";
            labelFuel.ForeColor = BriechTheme.GetBatteryColor(fuelPercent);

            // Update time remaining
            int minutes = timeRemainingSeconds / 60;
            int seconds = timeRemainingSeconds % 60;
            labelTime.Text = $"ETA: {minutes:D2}:{seconds:D2}";
        }

        /// <summary>
        /// Update connection status only
        /// </summary>
        public void UpdateConnectionStatus(bool connected)
        {
            if (connected)
            {
                labelConnectionStatus.Text = "● CONNECTED";
                labelConnectionStatus.ForeColor = BriechTheme.STATUS_GREEN;
            }
            else
            {
                labelConnectionStatus.Text = "● DISCONNECTED";
                labelConnectionStatus.ForeColor = BriechTheme.STATUS_RED;
            }
        }

        /// <summary>
        /// Update only link metrics
        /// </summary>
        public void UpdateLinkMetrics(int packets, double linkQuality, int rssi)
        {
            labelPackets.Text = $"Pkts: {packets}";
            labelLinkQuality.Text = $"Link: {linkQuality:F0}%";
            labelLinkQuality.ForeColor = BriechTheme.GetLinkQualityColor(linkQuality);
            labelRSSI.Text = $"RSSI: {rssi}dBm";
        }

        /// <summary>
        /// Update only fuel and time remaining
        /// </summary>
        public void UpdateFuelAndTime(int fuelPercent, int timeRemainingSeconds)
        {
            labelFuel.Text = $"Fuel: {fuelPercent}%";
            labelFuel.ForeColor = BriechTheme.GetBatteryColor(fuelPercent);

            int minutes = timeRemainingSeconds / 60;
            int seconds = timeRemainingSeconds % 60;
            labelTime.Text = $"ETA: {minutes:D2}:{seconds:D2}";
        }
    }
}
