using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MissionPlanner.Utilities;
using T = MissionPlanner.Utilities.DesignTokens;

namespace MissionPlanner.Controls.Fly
{
    /// <summary>Base for token-styled controls: double-buffered, repaints when the design mode changes.</summary>
    public abstract class TokenControl : Control
    {
        protected TokenControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor,
                true);
            T.Changed += OnTokensChanged;
        }

        private void OnTokensChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
                return;
            if (InvokeRequired)
                BeginInvoke((Action)Invalidate);
            else
                Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            T.Changed -= OnTokensChanged;
            base.Dispose(disposing);
        }

        protected static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        }

        protected void FillParentBackground(Graphics g)
        {
            using (var b = new SolidBrush(Parent?.BackColor ?? T.Current.AppBackground))
                g.FillRectangle(b, ClientRectangle);
        }

        protected static void DrawText(Graphics g, string text, Font font, Color color, Rectangle r,
            TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                                    TextFormatFlags.NoPadding)
        {
            TextRenderer.DrawText(g, text ?? "", font, r, color, flags);
        }
    }

    /// <summary>Rounded surface panel with an optional caps title (a card on the boards).</summary>
    public class TokenCard : Panel
    {
        private string title;

        public TokenCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            T.Changed += (s, e) => { if (!IsDisposed) { ApplyTokens(); Invalidate(true); } };
            ApplyTokens();
        }

        public string Title
        {
            get { return title; }
            set
            {
                title = value;
                Padding = new Padding(14, string.IsNullOrEmpty(title) ? 14 : 40, 14, 14);
                Invalidate();
            }
        }

        public string TitleAside { get; set; }

        private void ApplyTokens()
        {
            BackColor = T.Current.Surface;
            ForeColor = T.Current.TextPrimary;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var b = new SolidBrush(Parent?.BackColor ?? T.Current.AppBackground))
                e.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = T.RoundedRect(r, T.CardRadius))
            using (var fill = new SolidBrush(T.Current.Surface))
            using (var pen = new Pen(T.Current.Divider))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            if (!string.IsNullOrEmpty(title))
            {
                TextRenderer.DrawText(g, title.ToUpperInvariant(), T.Label, new Rectangle(14, 12, Width - 28, 20),
                    T.Current.TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                if (!string.IsNullOrEmpty(TitleAside))
                    TextRenderer.DrawText(g, TitleAside, T.Detail, new Rectangle(14, 12, Width - 28, 20),
                        T.Current.TextMuted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    /// <summary>A readout card: caps label, big monospaced value, one detail line. Value colour carries state.</summary>
    public class ReadoutCard : TokenControl
    {
        private string label = "", value = "--", detail = "";
        private T.State state = T.State.Neutral;

        public ReadoutCard()
        {
            Height = 96;
        }

        public void Set(string label, string value, string detail, T.State state)
        {
            if (this.label == label && this.value == value && this.detail == detail && this.state == state)
                return;
            this.label = label;
            this.value = value;
            this.detail = detail;
            this.state = state;
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
            using (var pen = new Pen(state == T.State.Warning ? p.WarningBorder : p.Divider))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            int pad = 12;
            DrawText(g, label.ToUpperInvariant(), T.Label, p.TextMuted, new Rectangle(pad, pad - 2, Width - 2 * pad, 18));
            var valueColor = state == T.State.Neutral || state == T.State.Ok ? p.TextPrimary : T.StateColor(state);
            DrawText(g, value, T.Readout, valueColor, new Rectangle(pad, 26, Width - 2 * pad, 40));
            DrawText(g, detail, T.Detail, p.TextSecondary, new Rectangle(pad, Height - 28, Width - 2 * pad, 20));
        }
    }

    /// <summary>
    /// Health chip: shape + word + colour (circle = ok, triangle = caution, square = warning, ring = info/neutral).
    /// </summary>
    public class HealthChip : TokenControl
    {
        private string text = "";
        private T.State state = T.State.Neutral;

        public HealthChip()
        {
            Height = 32;
            Margin = new Padding(0, 0, T.Space2, T.Space2);
        }

        public void Set(string text, T.State state)
        {
            if (this.text == text && this.state == state)
                return;
            this.text = text;
            this.state = state;
            Width = TextRenderer.MeasureText(text, T.Detail).Width + 40;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            FillParentBackground(g);
            Smooth(g);
            T.StateColors(state, out var surface, out var border, out var mark);
            using (var path = T.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height / 2f))
            using (var fill = new SolidBrush(surface))
            using (var pen = new Pen(border))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            float cx = 16, cy = Height / 2f;
            using (var b = new SolidBrush(mark))
            using (var pen = new Pen(mark, 2f))
            {
                switch (state)
                {
                    case T.State.Ok:
                        g.FillEllipse(b, cx - 4, cy - 4, 8, 8);
                        break;
                    case T.State.Caution:
                        g.FillPolygon(b, new[] { new PointF(cx, cy - 5), new PointF(cx + 5, cy + 4), new PointF(cx - 5, cy + 4) });
                        break;
                    case T.State.Warning:
                        g.FillRectangle(b, cx - 4, cy - 4, 8, 8);
                        break;
                    default:
                        g.DrawEllipse(pen, cx - 4, cy - 4, 8, 8);
                        break;
                }
            }

            DrawText(g, text, T.Detail, T.Current.TextPrimary, new Rectangle(28, 0, Width - 34, Height));
        }
    }

    /// <summary>Command/mode button following the tokens: normal, current (gold), danger (red), busy.</summary>
    public class TokenButton : TokenControl
    {
        public enum Kind
        {
            Normal,
            Current,
            Danger,
            Quiet,
        }

        private Kind kind = Kind.Normal;
        private bool hover, pressed;

        public TokenButton()
        {
            Height = T.ButtonHeight;
            Cursor = Cursors.Hand;
            TabStop = true;
            Font = T.BodyStrong;
        }

        public Kind Style
        {
            get { return kind; }
            set
            {
                if (kind == value)
                    return;
                kind = value;
                Invalidate();
            }
        }

        public bool Busy { get; set; }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            // Space activates a focused button like a standard one; Enter deliberately does not
            if (e.KeyCode == Keys.Space && Enabled)
                OnClick(EventArgs.Empty);
            base.OnKeyUp(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            FillParentBackground(g);
            Smooth(g);
            var p = T.Current;
            Color fill, border, text;
            float borderWidth = 1f;
            switch (kind)
            {
                case Kind.Current:
                    fill = p.BrandSurface; border = p.Brand; text = p.BrandText; borderWidth = 2f; break;
                case Kind.Danger:
                    fill = p.WarningSurface; border = p.Warning; text = p.WarningText; break;
                case Kind.Quiet:
                    fill = p.Surface; border = p.Divider; text = p.TextMuted; break;
                default:
                    fill = p.SurfaceRaised; border = p.BorderControl; text = p.TextPrimary; break;
            }

            if (!Enabled)
            {
                text = p.TextMuted;
                border = p.Divider;
            }
            else if (pressed)
            {
                fill = ControlPaint.Dark(fill, 0.05f);
            }
            else if (hover)
            {
                fill = ControlPaint.Light(fill, 0.08f);
            }

            var r = new RectangleF(borderWidth / 2 + 0.5f, borderWidth / 2 + 0.5f, Width - borderWidth - 1.5f, Height - borderWidth - 1.5f);
            using (var path = T.RoundedRect(r, T.ControlRadius))
            using (var b = new SolidBrush(fill))
            using (var pen = new Pen(border, borderWidth))
            {
                g.FillPath(b, path);
                g.DrawPath(pen, path);
            }

            if (Focused && ShowFocusCues)
            {
                using (var path = T.RoundedRect(new RectangleF(3, 3, Width - 7, Height - 7), T.ControlRadius - 2))
                using (var pen = new Pen(p.Info, 2f) { DashStyle = DashStyle.Dot })
                    g.DrawPath(pen, path);
            }

            DrawText(g, Busy ? Text + " …" : Text, Font, text, ClientRectangle,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Full-width alert banner: level tag + message + detail + optional action + Acknowledge.</summary>
    public class AlertBanner : Panel
    {
        private readonly Label tag, message, detail;
        private readonly TokenButton action, acknowledge;
        private T.State level = T.State.Warning;

        public event EventHandler ActionClicked;
        public event EventHandler Acknowledged;

        public AlertBanner()
        {
            Height = 56;
            Padding = new Padding(16, 8, 16, 8);
            Visible = false;
            tag = new Label { AutoSize = true, Font = T.Label, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 8, 14, 0) };
            message = new Label { AutoSize = true, Font = T.BodyStrong, Margin = new Padding(0, 10, 14, 0) };
            detail = new Label { AutoSize = true, Font = T.Detail, Margin = new Padding(0, 12, 14, 0) };
            action = new TokenButton { Width = 150, Height = 40, Style = TokenButton.Kind.Danger, Visible = false };
            acknowledge = new TokenButton { Width = 130, Height = 40, Text = "Acknowledge", Style = TokenButton.Kind.Quiet };
            action.Click += (s, e) => ActionClicked?.Invoke(this, EventArgs.Empty);
            acknowledge.Click += (s, e) => { Visible = false; Acknowledged?.Invoke(this, EventArgs.Empty); };

            var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Color.Transparent };
            right.Controls.Add(action);
            right.Controls.Add(acknowledge);
            var left = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent };
            left.Controls.Add(tag);
            left.Controls.Add(message);
            left.Controls.Add(detail);
            Controls.Add(left);
            Controls.Add(right);
            T.Changed += (s, e) => { if (!IsDisposed) ApplyTokens(); };
            ApplyTokens();
        }

        public void ShowAlert(T.State level, string text, string detailText, string actionText = null)
        {
            this.level = level;
            tag.Text = level == T.State.Warning ? "WARNING" : level == T.State.Caution ? "CAUTION" : "ADVISORY";
            message.Text = text;
            detail.Text = detailText ?? "";
            action.Text = actionText ?? "";
            action.Visible = !string.IsNullOrEmpty(actionText);
            ApplyTokens();
            Visible = true;
        }

        private void ApplyTokens()
        {
            var p = T.Current;
            T.StateColors(level, out var surface, out var border, out var mark);
            BackColor = surface;
            tag.BackColor = mark;
            tag.ForeColor = level == T.State.Warning || level == T.State.Caution ? p.OnBrand : p.Surface;
            message.ForeColor = p.TextPrimary;
            detail.ForeColor = p.TextSecondary;
            Invalidate(true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            T.StateColors(level, out _, out _, out var mark);
            using (var pen = new Pen(mark, 2f))
                e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }
}
