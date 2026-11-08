using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using T = MissionPlanner.Utilities.DesignTokens;

namespace MissionPlanner.Controls.Fly
{
    /// <summary>
    /// Primary flight display from the Fly board: horizon with pitch ladder, bank arc and pointer, aircraft
    /// symbol, airspeed and altitude boxes on the sides, vertical speed and ground speed under them, heading below.
    /// Everything is drawn with the design tokens; numbers are monospaced so they don't jitter.
    /// </summary>
    public class AttitudeIndicator : TokenControl
    {
        public float Roll { get; set; }
        public float Pitch { get; set; }
        public float Heading { get; set; }
        public float Airspeed { get; set; }
        public float GroundSpeed { get; set; }
        public float Altitude { get; set; }
        public float ClimbRate { get; set; }
        public string SpeedLabel { get; set; } = "AS m/s";
        public string AltLabel { get; set; } = "ALT m";
        public bool HasData { get; set; }
        public bool ShowAirspeed { get; set; } = true;

        public AttitudeIndicator()
        {
            MinimumSize = new Size(260, 200);
        }

        public void UpdateValues()
        {
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Smooth(g);
            var p = T.Current;
            var w = Width;
            var h = Height;
            using (var bg = new SolidBrush(p.Surface))
                g.FillRectangle(bg, ClientRectangle);

            var cx = w / 2f;
            var cy = h / 2f;
            var pxPerDeg = h / 60f; // about +-30 degrees visible

            using (var clip = T.RoundedRect(new RectangleF(0.5f, 0.5f, w - 1.5f, h - 1.5f), T.CardRadius))
            {
                g.SetClip(clip);

                // horizon
                var state = g.Save();
                g.TranslateTransform(cx, cy);
                g.RotateTransform(-Roll);
                g.TranslateTransform(0, Pitch * pxPerDeg);
                var big = Math.Max(w, h) * 2;
                using (var sky = new SolidBrush(p.Sky))
                using (var ground = new SolidBrush(p.Ground))
                using (var line = new Pen(p.Horizon, 2f))
                using (var ladder = new Pen(Color.FromArgb(220, p.Horizon), 2f))
                using (var text = new SolidBrush(p.Horizon))
                {
                    g.FillRectangle(sky, -big, -big, big * 2, big);
                    g.FillRectangle(ground, -big, 0, big * 2, big);
                    g.DrawLine(line, -big, 0, big, 0);
                    for (int deg = -30; deg <= 30; deg += 5)
                    {
                        if (deg == 0)
                            continue;
                        var y = -deg * pxPerDeg;
                        var half = deg % 10 == 0 ? w * 0.08f : w * 0.04f;
                        g.DrawLine(ladder, -half, y, half, y);
                        if (deg % 10 == 0)
                            g.DrawString(Math.Abs(deg).ToString(), T.DetailMono, text, half + 4, y - 8);
                    }
                }

                g.Restore(state);

                // bank arc and pointer
                var arcR = Math.Min(w, h) * 0.38f;
                using (var arc = new Pen(Color.FromArgb(205, p.Horizon), 2f))
                {
                    g.DrawArc(arc, cx - arcR, cy - arcR, arcR * 2, arcR * 2, 210, 120);
                    foreach (var a in new[] { -60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60 })
                    {
                        var rad = (a - 90) * Math.PI / 180;
                        var len = a % 30 == 0 ? 10 : 6;
                        g.DrawLine(arc,
                            cx + (float)Math.Cos(rad) * arcR, cy + (float)Math.Sin(rad) * arcR,
                            cx + (float)Math.Cos(rad) * (arcR - len), cy + (float)Math.Sin(rad) * (arcR - len));
                    }
                }

                var pr = (-Roll - 90) * Math.PI / 180;
                var tip = new PointF(cx + (float)Math.Cos(pr) * (arcR - 2), cy + (float)Math.Sin(pr) * (arcR - 2));
                var perp = pr + Math.PI / 2;
                var baseC = new PointF(cx + (float)Math.Cos(pr) * (arcR - 14), cy + (float)Math.Sin(pr) * (arcR - 14));
                using (var gold = new SolidBrush(p.Brand))
                    g.FillPolygon(gold, new[]
                    {
                        tip,
                        new PointF(baseC.X + (float)Math.Cos(perp) * 7, baseC.Y + (float)Math.Sin(perp) * 7),
                        new PointF(baseC.X - (float)Math.Cos(perp) * 7, baseC.Y - (float)Math.Sin(perp) * 7)
                    });

                // aircraft symbol: dark outline under gold
                var wing = w * 0.095f;
                var gap = w * 0.048f;
                using (var under = new Pen(p.AppBackground, 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var over = new Pen(p.Brand, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    foreach (var pen in new[] { under, over })
                    {
                        g.DrawLine(pen, cx - gap - wing, cy, cx - gap, cy);
                        g.DrawLine(pen, cx + gap, cy, cx + gap + wing, cy);
                    }
                }

                using (var dot = new SolidBrush(p.Brand))
                    g.FillEllipse(dot, cx - 4, cy - 4, 8, 8);

                // side tapes: speed (left) and altitude (right)
                var boxW = Math.Max(64f, w * 0.17f);
                DrawSideBox(g, p, 8, cy, boxW, ShowAirspeed ? SpeedLabel : "GS m/s",
                    HasData ? (ShowAirspeed ? Airspeed : GroundSpeed).ToString("0.0") : "--",
                    HasData && ShowAirspeed ? "GS " + GroundSpeed.ToString("0.0") : "", p.TextSecondary);
                var climb = ClimbRate;
                var climbText = HasData ? (climb >= 0 ? "+" : "") + climb.ToString("0.0") + (climb > 0.2 ? " ↑" : climb < -0.2 ? " ↓" : "") : "";
                DrawSideBox(g, p, w - 8 - boxW, cy, boxW, AltLabel, HasData ? Altitude.ToString("0") : "--", climbText,
                    climb > 0.2 ? p.Ok : climb < -0.2 ? p.Caution : p.TextSecondary);

                // heading
                var hdg = HasData ? "HDG " + ((int)Math.Round((Heading % 360 + 360) % 360)).ToString("000") + "°" : "HDG ---";
                var hs = TextRenderer.MeasureText(hdg, T.MonoStrong);
                var hr = new RectangleF(cx - hs.Width / 2f - 12, h - 44, hs.Width + 24, 32);
                using (var path = T.RoundedRect(hr, 6))
                using (var bb = new SolidBrush(Color.FromArgb(205, p.AppBackground)))
                    g.FillPath(bb, path);
                TextRenderer.DrawText(g, hdg, T.MonoStrong, Rectangle.Round(hr), p.Horizon,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                g.ResetClip();
            }

            using (var border = T.RoundedRect(new RectangleF(0.5f, 0.5f, w - 1.5f, h - 1.5f), T.CardRadius))
            using (var pen = new Pen(p.Divider))
                g.DrawPath(pen, border);
        }

        private static void DrawSideBox(Graphics g, T.Palette p, float x, float cy, float w, string label, string value,
            string under, Color underColor)
        {
            var panel = new RectangleF(x, cy - 66, w, 124);
            using (var path = T.RoundedRect(panel, 6))
            using (var b = new SolidBrush(Color.FromArgb(185, p.AppBackground)))
                g.FillPath(b, path);
            TextRenderer.DrawText(g, label, T.Label, new Rectangle((int)x, (int)(cy - 62), (int)w, 18), p.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            var box = new RectangleF(x + 3, cy - 17, w - 6, 34);
            using (var path = T.RoundedRect(box, 4))
            using (var b = new SolidBrush(p.Surface))
            using (var pen = new Pen(p.Brand))
            {
                g.FillPath(b, path);
                g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, value, T.MonoStrong, Rectangle.Round(box), p.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, under, T.DetailMono, new Rectangle((int)x, (int)(cy + 26), (int)w, 22), underColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
