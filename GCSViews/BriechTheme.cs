using System.Drawing;

namespace MissionPlanner.GCSViews
{
    /// <summary>
    /// BRIECH UAS Professional Theme - Centralized Color Constants
    /// Extracted from Tailwind CSS configuration and design specification
    /// Dark navy (#1a1f2e) with gold (#c8a865) accents
    /// </summary>
    public static class BriechTheme
    {
        // ============================================
        // PRIMARY BRIECH COLORS (from Tailwind config)
        // ============================================
        
        /// <summary>Gold accent color - Primary UI accent and highlights</summary>
        public static readonly Color BRIECH_GOLD = Color.FromArgb(200, 168, 101);      // #c8a865

        /// <summary>Lighter gold for secondary highlights and hover states</summary>
        public static readonly Color BRIECH_GOLD_LIGHT = Color.FromArgb(212, 182, 117); // #d4b675

        /// <summary>Primary dark background - Main panel/container color</summary>
        public static readonly Color BRIECH_DARK = Color.FromArgb(10, 14, 20);         // #0a0e14

        /// <summary>Even darker background - Deep panel/modal color</summary>
        public static readonly Color BRIECH_DARKER = Color.FromArgb(6, 10, 15);        // #060a0f

        /// <summary>Panel background - Used for card/section backgrounds</summary>
        public static readonly Color BRIECH_PANEL = Color.FromArgb(26, 31, 46);        // #1a1f2e

        // ============================================
        // NEUTRAL COLORS (Text, Borders, Backgrounds)
        // ============================================

        /// <summary>Light gray - Primary text color on dark backgrounds</summary>
        public static readonly Color TEXT_PRIMARY = Color.FromArgb(220, 220, 220);     // #dcdcdc

        /// <summary>Dim gray - Secondary text, disabled states</summary>
        public static readonly Color TEXT_SECONDARY = Color.FromArgb(150, 150, 150);   // #969696

        /// <summary>Dark charcoal - Panel dividers, secondary backgrounds</summary>
        public static readonly Color CHARCOAL = Color.FromArgb(40, 45, 60);           // #282d3c

        /// <summary>Border accent - Gold-tinted borders</summary>
        public static readonly Color BORDER_GOLD = Color.FromArgb(180, 150, 80);      // #b49650

        /// <summary>Very dark background - Component borders</summary>
        public static readonly Color DARK_NAVY = Color.FromArgb(26, 31, 46);          // #1a1f2e

        // ============================================
        // STATUS COLORS (Conditional States)
        // ============================================

        /// <summary>Success/Connected status - Green indicator</summary>
        public static readonly Color STATUS_GREEN = Color.FromArgb(76, 175, 80);      // #4caf50

        /// <summary>Warning/Caution status - Yellow/Amber indicator</summary>
        public static readonly Color STATUS_YELLOW = Color.FromArgb(255, 193, 7);     // #ffc107

        /// <summary>Critical/Error/Disconnected status - Red indicator</summary>
        public static readonly Color STATUS_RED = Color.FromArgb(244, 67, 54);        // #f44336

        // ============================================
        // EXTENDED STATUS COLORS (Severity Variants)
        // ============================================

        /// <summary>Dark red for error backgrounds (low opacity)</summary>
        public static readonly Color STATUS_RED_DARK = Color.FromArgb(139, 0, 0);     // #8b0000

        /// <summary>Light green for success backgrounds</summary>
        public static readonly Color STATUS_GREEN_LIGHT = Color.FromArgb(144, 238, 144); // #90ee90

        /// <summary>Orange/Amber for warning backgrounds</summary>
        public static readonly Color STATUS_ORANGE = Color.FromArgb(255, 152, 0);     // #ff9800

        // ============================================
        // HUD SPECIFIC COLORS
        // ============================================

        /// <summary>Sky blue for artificial horizon upper hemisphere</summary>
        public static readonly Color SKY_BLUE = Color.FromArgb(0, 100, 200);          // #0064c8

        /// <summary>Ground brown for artificial horizon lower hemisphere</summary>
        public static readonly Color GROUND_BROWN = Color.FromArgb(139, 69, 19);      // #8b4513

        /// <summary>Aircraft red for center symbol</summary>
        public static readonly Color AIRCRAFT_RED = Color.Red;                        // #ff0000

        /// <summary>Compass rose color</summary>
        public static readonly Color COMPASS_COLOR = Color.FromArgb(200, 168, 101);   // Gold

        /// <summary>Speed tape color</summary>
        public static readonly Color SPEED_TAPE_COLOR = Color.FromArgb(40, 45, 60);   // Charcoal

        /// <summary>Altitude tape color</summary>
        public static readonly Color ALTITUDE_TAPE_COLOR = Color.FromArgb(40, 45, 60); // Charcoal

        // ============================================
        // ALPHA/TRANSPARENCY VARIANTS
        // ============================================

        /// <summary>Gold with 50% opacity for glass morphism effects</summary>
        public static readonly Color GOLD_TRANSPARENT_50 = Color.FromArgb(128, 200, 168, 101);

        /// <summary>Gold with 30% opacity for subtle highlights</summary>
        public static readonly Color GOLD_TRANSPARENT_30 = Color.FromArgb(77, 200, 168, 101);

        /// <summary>Charcoal with 50% opacity for overlay backgrounds</summary>
        public static readonly Color CHARCOAL_TRANSPARENT_50 = Color.FromArgb(128, 40, 45, 60);

        /// <summary>Dark navy with 80% opacity for semi-transparent panels</summary>
        public static readonly Color DARK_NAVY_TRANSPARENT_80 = Color.FromArgb(204, 26, 31, 46);

        // ============================================
        // BUTTON COLORS
        // ============================================

        /// <summary>ARM button color - Yellow/Warning</summary>
        public static readonly Color BTN_ARM = Color.FromArgb(255, 193, 7);           // #ffc107

        /// <summary>DISARM button color - Red/Danger</summary>
        public static readonly Color BTN_DISARM = Color.FromArgb(244, 67, 54);        // #f44336

        /// <summary>TAKEOFF button color - Green/Success</summary>
        public static readonly Color BTN_TAKEOFF = Color.FromArgb(76, 175, 80);       // #4caf50

        /// <summary>LAND button color - Red/Danger</summary>
        public static readonly Color BTN_LAND = Color.FromArgb(244, 67, 54);          // #f44336

        /// <summary>RTL button color - Yellow/Caution</summary>
        public static readonly Color BTN_RTL = Color.FromArgb(255, 193, 7);           // #ffc107

        /// <summary>LOITER button color - Gold/Accent</summary>
        public static readonly Color BTN_LOITER = Color.FromArgb(200, 168, 101);      // #c8a865

        /// <summary>AUTO button color - Green/Success</summary>
        public static readonly Color BTN_AUTO = Color.FromArgb(76, 175, 80);          // #4caf50

        /// <summary>SET HOME button color - Gold/Accent</summary>
        public static readonly Color BTN_SET_HOME = Color.FromArgb(200, 168, 101);    // #c8a865

        // ============================================
        // TELEMETRY CARD COLORS
        // ============================================

        /// <summary>Altitude card accent</summary>
        public static readonly Color CARD_ALTITUDE = Color.FromArgb(33, 150, 243);    // #2196f3 (Blue)

        /// <summary>Speed card accent</summary>
        public static readonly Color CARD_SPEED = Color.FromArgb(76, 175, 80);        // #4caf50 (Green)

        /// <summary>Heading card accent</summary>
        public static readonly Color CARD_HEADING = Color.FromArgb(200, 168, 101);    // #c8a865 (Gold)

        /// <summary>Battery card accent - changes color based on percentage</summary>
        public static readonly Color CARD_BATTERY = Color.FromArgb(76, 175, 80);      // #4caf50 (Green by default)

        /// <summary>GPS card accent</summary>
        public static readonly Color CARD_GPS = Color.FromArgb(76, 175, 80);          // #4caf50 (Green)

        /// <summary>Distance card accent</summary>
        public static readonly Color CARD_DISTANCE = Color.FromArgb(255, 193, 7);     // #ffc107 (Amber)

        /// <summary>Vertical Speed card accent</summary>
        public static readonly Color CARD_VERT_SPEED = Color.FromArgb(33, 150, 243);  // #2196f3 (Blue)

        // ============================================
        // GRID AND BACKGROUND COLORS
        // ============================================

        /// <summary>Grid line color - very subtle on dark background</summary>
        public static readonly Color GRID_COLOR = Color.FromArgb(30, 200, 168, 101);  // Gold at ~10% opacity

        /// <summary>Subtle grid background overlay</summary>
        public static readonly Color GRID_OVERLAY = Color.FromArgb(20, 255, 255, 255); // White at ~8% opacity

        // ============================================
        // HELPER METHODS
        // ============================================

        /// <summary>
        /// Create a color with custom alpha/transparency
        /// </summary>
        /// <param name="color">Base color</param>
        /// <param name="alpha">Alpha value (0-255)</param>
        /// <returns>Color with specified transparency</returns>
        public static Color WithAlpha(Color color, int alpha)
        {
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        /// <summary>
        /// Create a color with transparency as percentage (0-100)
        /// </summary>
        /// <param name="color">Base color</param>
        /// <param name="alphaPercent">Opacity percentage (0-100)</param>
        /// <returns>Color with specified transparency</returns>
        public static Color WithAlphaPercent(Color color, double alphaPercent)
        {
            int alpha = (int)(255 * (alphaPercent / 100.0));
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        /// <summary>
        /// Get battery color based on remaining percentage
        /// Green (100%) → Yellow (30%) → Red (0%)
        /// </summary>
        /// <param name="percentRemaining">Battery percentage (0-100)</param>
        /// <returns>Appropriate status color</returns>
        public static Color GetBatteryColor(double percentRemaining)
        {
            if (percentRemaining > 50)
                return STATUS_GREEN;
            else if (percentRemaining > 20)
                return STATUS_YELLOW;
            else
                return STATUS_RED;
        }

        /// <summary>
        /// Get connection status color based on link quality
        /// Green (>80%) → Yellow (50-80%) → Red (<50%)
        /// </summary>
        /// <param name="linkQualityPercent">Link quality percentage (0-100)</param>
        /// <returns>Appropriate status color</returns>
        public static Color GetLinkQualityColor(double linkQualityPercent)
        {
            if (linkQualityPercent >= 80)
                return STATUS_GREEN;
            else if (linkQualityPercent >= 50)
                return STATUS_YELLOW;
            else
                return STATUS_RED;
        }
    }
}
