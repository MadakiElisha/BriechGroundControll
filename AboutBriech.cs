using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Reflection;

namespace MissionPlanner
{
    public partial class AboutBriech : Form
    {
        // ── Briech Brand Colors ──────────────────────────────────────
        private static readonly Color BriechGold = Color.FromArgb(0xC8, 0xA8, 0x65);
        private static readonly Color BriechBlack = Color.FromArgb(0x1A, 0x1A, 0x1A);
        private static readonly Color BriechDark = Color.FromArgb(0x0D, 0x0D, 0x0D);
        private static readonly Color BriechPanel = Color.FromArgb(0x22, 0x22, 0x22);
        private static readonly Color BriechBorder = Color.FromArgb(0xC8, 0xA8, 0x65);

        public AboutBriech()
        {
            InitializeComponent();
            this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.UserPaint |
                          ControlStyles.DoubleBuffer, true);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // Animate in — fade from 0 to 1
            this.Opacity = 0;
            var t = new System.Windows.Forms.Timer { Interval = 20 };
            t.Tick += (s, _) =>
            {
                this.Opacity += 0.07;
                if (this.Opacity >= 1) { this.Opacity = 1; t.Stop(); t.Dispose(); }
            };
            t.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            var rc = this.ClientRectangle;

            // ── 1. Background gradient ───────────────────────────────
            using (var bg = new LinearGradientBrush(rc, BriechDark, BriechBlack, 90f))
                g.FillRectangle(bg, rc);

            // ── 2. Gold top accent bar ───────────────────────────────
            using (var accent = new LinearGradientBrush(
                       new Rectangle(0, 0, rc.Width, 4),
                       Color.FromArgb(0xFF, BriechGold), Color.Transparent, 0f))
                g.FillRectangle(accent, 0, 0, rc.Width, 4);

            // ── 3. Logo image (Briech shield) ─────────────────────────
            DrawLogo(g, rc);

            // ── 4. "Briech" wordmark ──────────────────────────────────
            using (var titleFont = new Font("Arial Black", 26, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.White))
            {
                string title = "GROUND CONTROL";
                var sz = g.MeasureString(title, titleFont);
                g.DrawString(title, titleFont, brush,
                    (rc.Width - sz.Width) / 2f, 200);
            }

           

            // ── 5. Gold divider ───────────────────────────────────────
            DrawGoldDivider(g, 40, 265, rc.Width - 80);

            // ── 6. Info rows ──────────────────────────────────────────
            DrawInfoBlock(g, rc, 278);

            // ── 7. Bottom copyright bar ───────────────────────────────
            using (var footBrush = new SolidBrush(Color.FromArgb(40, BriechGold)))
                g.FillRectangle(footBrush, 0, rc.Height - 36, rc.Width, 36);

            DrawGoldDivider(g, 0, rc.Height - 36, rc.Width);

            using (var footFont = new Font("Arial", 8))
            using (var footText = new SolidBrush(Color.FromArgb(200, BriechGold)))
            {
                string copy = "© " + DateTime.Now.Year + "  Briech UAS  •  All Rights Reserved";
                var sz = g.MeasureString(copy, footFont);
                g.DrawString(copy, footFont, footText,
                    (rc.Width - sz.Width) / 2f,
                    rc.Height - 24);
            }

            // ── 8. Outer border ───────────────────────────────────────
            using (var border = new Pen(Color.FromArgb(90, BriechGold), 1.5f))
                g.DrawRectangle(border, 1, 1, rc.Width - 3, rc.Height - 3);
        }

        // ── Draw real Briech logo image ───────────────────────────────
        private void DrawLogo(Graphics g, Rectangle rc)
        {
            // Try loading from running directory first, then fall back to embedded resource
            Image logo = null;
            try
            {
                string[] searchPaths = new[]
                {
                    System.IO.Path.Combine(MainV2.running_directory, "logo_dark.jpg"),
                    System.IO.Path.Combine(MainV2.running_directory, "briech_logo.png"),
                    System.IO.Path.Combine(MainV2.running_directory, "Resources", "logo_dark.jpg"),
                };

                foreach (var path in searchPaths)
                {
                    if (System.IO.File.Exists(path))
                    {
                        logo = Image.FromFile(path);
                        break;
                    }
                }

                // Fall back to embedded resource if available
                if (logo == null)
                {
                    try { logo = global::MissionPlanner.Properties.Resources.logo_dark; } catch { }
                }
            }
            catch { }

            if (logo != null)
            {
                // Draw image centered, max 200px wide, maintaining aspect ratio
                int maxW = 200;
                int maxH = 160;
                float ratio = Math.Min((float)maxW / logo.Width, (float)maxH / logo.Height);
                int drawW = (int)(logo.Width * ratio);
                int drawH = (int)(logo.Height * ratio);
                int drawX = (rc.Width - drawW) / 2;
                int drawY = 20;
                g.DrawImage(logo, drawX, drawY, drawW, drawH);
                logo.Dispose();
            }
            else
            {
                // Fallback: draw the shield if no image found
                DrawShield(g, rc.Width / 2 - 60, 20, 120, 140);
            }
        }

        // ── Draw stylised drone shield (fallback only) ────────────────
        private void DrawShield(Graphics g, int x, int y, int w, int h)
        {
            // Shield outline
            PointF[] shield =
            {
                new PointF(x + w/2f, y + h),          // bottom tip
                new PointF(x,        y + h * 0.55f),   // bottom-left
                new PointF(x,        y + h * 0.2f),    // top-left
                new PointF(x + w/2f, y),               // top-center
                new PointF(x + w,    y + h * 0.2f),    // top-right
                new PointF(x + w,    y + h * 0.55f),   // bottom-right
            };

            using (var fill = new LinearGradientBrush(
                       new Rectangle(x, y, w, h),
                       Color.FromArgb(50, BriechGold),
                       Color.FromArgb(20, BriechGold), 90f))
                g.FillPolygon(fill, shield);

            using (var pen = new Pen(BriechGold, 2f))
                g.DrawPolygon(pen, shield);

            // Drone icon inside shield (simplified VTOL top view)
            DrawDroneIcon(g, x + w / 2, y + h / 2 - 8, (int)(w * 0.55f));
        }

        // ── Simple VTOL drone top-view icon ──────────────────────────
        private void DrawDroneIcon(Graphics g, int cx, int cy, int size)
        {
            using (var pen = new Pen(Color.White, 2f))
            using (var fill = new SolidBrush(Color.White))
            {
                int arm = size / 2;
                int motorR = size / 8;

                // 4 arms (X config)
                int d = (int)(arm * 0.7f);
                g.DrawLine(pen, cx, cy, cx - d, cy - d); // NW
                g.DrawLine(pen, cx, cy, cx + d, cy - d); // NE
                g.DrawLine(pen, cx, cy, cx - d, cy + d); // SW
                g.DrawLine(pen, cx, cy, cx + d, cy + d); // SE

                // 4 motors (circles at tips)
                g.DrawEllipse(pen, cx - d - motorR, cy - d - motorR, motorR * 2, motorR * 2);
                g.DrawEllipse(pen, cx + d - motorR, cy - d - motorR, motorR * 2, motorR * 2);
                g.DrawEllipse(pen, cx - d - motorR, cy + d - motorR, motorR * 2, motorR * 2);
                g.DrawEllipse(pen, cx + d - motorR, cy + d - motorR, motorR * 2, motorR * 2);

                // Center body
                g.FillEllipse(new SolidBrush(BriechGold), cx - 5, cy - 5, 10, 10);
            }
        }

        // ── Gold divider line ─────────────────────────────────────────
        private void DrawGoldDivider(Graphics g, int x, int y, int width)
        {
            using (var pen = new LinearGradientBrush(
                       new Point(x, y), new Point(x + width, y),
                       Color.Transparent, Color.Transparent))
            {
                // Blend: transparent → gold → transparent
                pen.LinearColors = new[] { Color.Transparent, BriechGold };
                using (var p = new Pen(pen, 1f))
                    g.DrawLine(p, x, y, x + width, y);
            }

            // Simpler solid version as fallback
            using (var solidPen = new Pen(Color.FromArgb(100, BriechGold), 1f))
                g.DrawLine(solidPen, x + 20, y, x + width - 20, y);
        }

        // ── Version / company info rows ───────────────────────────────
        private void DrawInfoBlock(Graphics g, Rectangle rc, int startY)
        {
            var rows = new (string label, string value)[]
            {
                ("Version",     GetAppVersion()),
                ("Build",       GetBuildDate()),
                ("Company",     "Briech UAS"),
                ("Website",     "www.briechuas.com"),
                ("Phone",       "+234 803 2027 605"),
                ("Support",     "support@briechuas.com"),
                ("ArduPilot",   "MAVLink 2.0 Compatible"),
                ("Platform",    System.Environment.OSVersion.ToString()),
                (".NET",        System.Environment.Version.ToString()),
            };

            using (var labelFont = new Font("Arial", 9, FontStyle.Bold))
            using (var valueFont = new Font("Arial", 9))
            using (var labelBrush = new SolidBrush(BriechGold))
            using (var valueBrush = new SolidBrush(Color.FromArgb(220, 220, 220)))
            using (var rowBrush = new SolidBrush(Color.FromArgb(15, 255, 255, 255)))
            {
                int rowH = 24;
                int col1 = 50;
                int col2 = 170;

                for (int i = 0; i < rows.Length; i++)
                {
                    int ry = startY + i * rowH;

                    // Alternating row tint
                    if (i % 2 == 0)
                        g.FillRectangle(rowBrush, col1 - 8, ry, rc.Width - col1 * 2 + 16, rowH - 2);

                    g.DrawString(rows[i].label + ":", labelFont, labelBrush, col1, ry + 4);
                    g.DrawString(rows[i].value, valueFont, valueBrush, col2, ry + 4);
                }
            }
        }

        private string GetAppVersion()
        {
            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return $"v{v.Major}.{v.Minor}.{v.Build}";
            }
            catch { return "v1.0.0"; }
        }

        private string GetBuildDate()
        {
            try
            {
                var path = Assembly.GetExecutingAssembly().Location;
                return System.IO.File.GetLastWriteTime(path).ToString("yyyy-MM-dd");
            }
            catch { return DateTime.Now.ToString("yyyy-MM-dd"); }
        }

        // ── Drag form by clicking anywhere ───────────────────────────
        private Point _dragStart;
        private bool _dragging;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _dragging = true;
            _dragStart = e.Location;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                var delta = new Point(e.X - _dragStart.X, e.Y - _dragStart.Y);
                this.Location = new Point(this.Left + delta.X, this.Top + delta.Y);
            }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }
    }
}