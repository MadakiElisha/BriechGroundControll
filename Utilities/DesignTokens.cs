using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MissionPlanner.Utilities
{
    /// <summary>
    /// Briech GCS design tokens: the one source of colour, type, spacing and sizes for the redesigned screens
    /// (design canvas board 6). Two modes: Night (default) and Sunlight (high-contrast for outdoor laptops).
    /// Every text pair meets 4.5:1 and every control outline 3:1 in both modes. State colour is never the only
    /// signal: each state also has its own shape and word.
    /// </summary>
    public static class DesignTokens
    {
        public sealed class Palette
        {
            public string Name;
            public Color AppBackground;      // bg.app
            public Color Surface;            // bg.surface (cards, header)
            public Color SurfaceRaised;      // controls on a surface
            public Color Divider;            // hairlines between areas
            public Color TextPrimary;        // text.primary
            public Color TextMuted;          // text.muted
            public Color TextSecondary;      // detail lines
            public Color BorderControl;      // border.control (button/input outline)
            public Color Brand;              // accent.brand (gold)
            public Color BrandSurface;       // selected/current fill
            public Color BrandText;          // text on BrandSurface
            public Color OnBrand;            // text on solid Brand
            public Color Ok;                 // state.ok
            public Color OkSurface;
            public Color OkBorder;
            public Color Info;               // state.info
            public Color InfoSurface;
            public Color InfoBorder;
            public Color Caution;            // state.caution
            public Color CautionSurface;
            public Color CautionBorder;
            public Color Warning;            // state.warning
            public Color WarningSurface;
            public Color WarningBorder;
            public Color WarningText;        // text on WarningSurface
            public Color Sky;                // attitude indicator
            public Color Ground;
            public Color Horizon;
        }

        public static readonly Palette Night = new Palette
        {
            Name = "Night",
            AppBackground = Hex("#0B1018"),
            Surface = Hex("#121925"),
            SurfaceRaised = Hex("#1A2230"),
            Divider = Hex("#2C3646"),
            TextPrimary = Hex("#E8ECF2"),
            TextMuted = Hex("#A9B4C6"),
            TextSecondary = Hex("#C6CEDB"),
            BorderControl = Hex("#64738F"),
            Brand = Hex("#D4B26E"),
            BrandSurface = Hex("#2A2416"),
            BrandText = Hex("#F4E3BC"),
            OnBrand = Hex("#0B1018"),
            Ok = Hex("#57D08A"),
            OkSurface = Hex("#13241B"),
            OkBorder = Hex("#2E5A40"),
            Info = Hex("#5CC2EA"),
            InfoSurface = Hex("#172433"),
            InfoBorder = Hex("#2F4A63"),
            Caution = Hex("#F0B44C"),
            CautionSurface = Hex("#2B2412"),
            CautionBorder = Hex("#6A5523"),
            Warning = Hex("#FF6B5E"),
            WarningSurface = Hex("#2A1412"),
            WarningBorder = Hex("#6B3A35"),
            WarningText = Hex("#FFE1DD"),
            Sky = Hex("#2C6FA3"),
            Ground = Hex("#6B4A2B"),
            Horizon = Hex("#F4F6FA"),
        };

        public static readonly Palette Sunlight = new Palette
        {
            Name = "Sunlight",
            AppBackground = Hex("#F4F1EA"),
            Surface = Hex("#FFFFFF"),
            SurfaceRaised = Hex("#F4F1EA"),
            Divider = Hex("#B9B3A6"),
            TextPrimary = Hex("#0E1116"),
            TextMuted = Hex("#3D4451"),
            TextSecondary = Hex("#2A303A"),
            BorderControl = Hex("#0E1116"),
            Brand = Hex("#7A5A12"),
            BrandSurface = Hex("#F4D58D"),
            BrandText = Hex("#0E1116"),
            OnBrand = Hex("#FFFFFF"),
            Ok = Hex("#15803D"),
            OkSurface = Hex("#E3F4E8"),
            OkBorder = Hex("#15803D"),
            Info = Hex("#1D4ED8"),
            InfoSurface = Hex("#E4ECFB"),
            InfoBorder = Hex("#1D4ED8"),
            Caution = Hex("#92400E"),
            CautionSurface = Hex("#FCEFD6"),
            CautionBorder = Hex("#92400E"),
            Warning = Hex("#B42318"),
            WarningSurface = Hex("#FBE4E1"),
            WarningBorder = Hex("#B42318"),
            WarningText = Hex("#5A0F09"),
            Sky = Hex("#3C82BE"),
            Ground = Hex("#7A5530"),
            Horizon = Hex("#FFFFFF"),
        };

        private static Palette current = Night;

        /// <summary>The active palette. Changing it raises <see cref="Changed"/> so screens repaint.</summary>
        public static Palette Current
        {
            get { return current; }
            set
            {
                if (value == null || value == current)
                    return;
                current = value;
                try
                {
                    Settings.Instance["design_mode"] = value.Name;
                }
                catch
                {
                }

                Changed?.Invoke(null, EventArgs.Empty);
            }
        }

        public static event EventHandler Changed;

        public static bool IsSunlight => current == Sunlight;

        public static void LoadSavedMode()
        {
            try
            {
                current = Settings.Instance["design_mode"] == Sunlight.Name ? Sunlight : Night;
            }
            catch
            {
                current = Night;
            }
        }

        public static void ToggleMode()
        {
            Current = IsSunlight ? Night : Sunlight;
        }

        // ---- type: six sizes, 12 px floor. Sizes are in points so Windows display scaling applies. ----
        private const string SansFamily = "Segoe UI";
        private const string MonoFamily = "Consolas";

        /// <summary>px to pt at 96 dpi; WinForms scales fonts with the display.</summary>
        private static float Pt(float px)
        {
            return px * 0.75f;
        }

        public static readonly Font ReadoutXL = MakeFont(MonoFamily, Pt(40), FontStyle.Bold);
        public static readonly Font Readout = MakeFont(MonoFamily, Pt(26), FontStyle.Bold);
        public static readonly Font Title = MakeFont(SansFamily, Pt(18), FontStyle.Bold);
        public static readonly Font Body = MakeFont(SansFamily, Pt(14), FontStyle.Regular);
        public static readonly Font BodyStrong = MakeFont(SansFamily, Pt(14), FontStyle.Bold);
        public static readonly Font Detail = MakeFont(SansFamily, Pt(13), FontStyle.Regular);
        public static readonly Font DetailMono = MakeFont(MonoFamily, Pt(13), FontStyle.Regular);
        public static readonly Font Label = MakeFont(SansFamily, Pt(12), FontStyle.Bold);
        public static readonly Font MonoStrong = MakeFont(MonoFamily, Pt(15), FontStyle.Bold);

        private static Font MakeFont(string family, float size, FontStyle style)
        {
            try
            {
                var f = new Font(family, size, style, GraphicsUnit.Point);
                // fall back to the platform's generic family when the face isn't installed (Linux)
                if (!string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase))
                {
                    f.Dispose();
                    var generic = family == MonoFamily ? FontFamily.GenericMonospace : FontFamily.GenericSansSerif;
                    return new Font(generic, size, style, GraphicsUnit.Point);
                }

                return f;
            }
            catch
            {
                return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
            }
        }

        // ---- spacing and targets ----
        public const int Space1 = 4;
        public const int Space2 = 8;
        public const int Space3 = 12;
        public const int Space4 = 16;
        public const int Space5 = 24;
        public const int Space6 = 32;

        public const int ButtonHeight = 44;
        public const int ModeButtonHeight = 48;
        public const int HoldButtonHeight = 64;
        public const int CardRadius = 12;
        public const int ControlRadius = 8;

        // ---- helpers ----
        public static Color Hex(string hex)
        {
            return ColorTranslator.FromHtml(hex);
        }

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            var d = Math.Max(0.1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public enum State
        {
            Neutral,
            Ok,
            Info,
            Caution,
            Warning,
        }

        public static Color StateColor(State s)
        {
            var p = current;
            switch (s)
            {
                case State.Ok: return p.Ok;
                case State.Info: return p.Info;
                case State.Caution: return p.Caution;
                case State.Warning: return p.Warning;
                default: return p.TextPrimary;
            }
        }

        public static void StateColors(State s, out Color surface, out Color border, out Color mark)
        {
            var p = current;
            switch (s)
            {
                case State.Ok:
                    surface = p.OkSurface; border = p.OkBorder; mark = p.Ok; return;
                case State.Info:
                    surface = p.InfoSurface; border = p.InfoBorder; mark = p.Info; return;
                case State.Caution:
                    surface = p.CautionSurface; border = p.CautionBorder; mark = p.Caution; return;
                case State.Warning:
                    surface = p.WarningSurface; border = p.WarningBorder; mark = p.Warning; return;
                default:
                    surface = p.SurfaceRaised; border = p.Divider; mark = p.TextMuted; return;
            }
        }
    }
}
