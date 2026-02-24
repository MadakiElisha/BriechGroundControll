namespace MissionPlanner
{
    partial class AboutBriech
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.btnClose = new System.Windows.Forms.Button();
            this.SuspendLayout();

            // ── Close Button ──────────────────────────────────────────
            this.btnClose.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnClose.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(0xC8, 0xA8, 0x65);
            this.btnClose.FlatAppearance.BorderSize = 1;
            this.btnClose.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(0xC8, 0xA8, 0x65);
            this.btnClose.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(0xA0, 0x88, 0x45);
            this.btnClose.BackColor = System.Drawing.Color.FromArgb(0x22, 0x22, 0x22);
            this.btnClose.ForeColor = System.Drawing.Color.FromArgb(0xC8, 0xA8, 0x65);
            this.btnClose.Font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold);
            this.btnClose.Text = "CLOSE";
            this.btnClose.Size = new System.Drawing.Size(120, 34);
            // Close button: sits between info rows and copyright bar
            this.btnClose.Location = new System.Drawing.Point(140, 530);
            this.btnClose.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnClose.Click += new System.EventHandler(this.btnClose_Click);
            this.btnClose.MouseEnter += (s, e) => {
                this.btnClose.ForeColor = System.Drawing.Color.Black;
            };
            this.btnClose.MouseLeave += (s, e) => {
                this.btnClose.ForeColor = System.Drawing.Color.FromArgb(0xC8, 0xA8, 0x65);
            };

            // ── Form ──────────────────────────────────────────────────
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(0x1A, 0x1A, 0x1A);
            // Height = rows end 494 + gap 36 + button 34 + gap 16 + copyright 36 = 616
            this.ClientSize = new System.Drawing.Size(400, 616);
            this.Controls.Add(this.btnClose);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "AboutBriech";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "About Briech UAS";
            this.ResumeLayout(false);
        }

        private void btnClose_Click(object sender, System.EventArgs e)
        {
            this.Close();
        }

        private System.Windows.Forms.Button btnClose;
    }
}