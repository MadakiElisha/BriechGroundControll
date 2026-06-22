using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews
{
    public class ControlArtificialHorizon : Control
    {
        public enum HudVitalTargetType
        {
            Core,
            System,
            Edge
        }

        public sealed class VitalDisplayOverride
        {
            public VitalDisplayOverride(string label, string value, string detail, Color accent)
            {
                Label = label;
                Value = value;
                Detail = detail;
                Accent = accent;
            }

            public string Label { get; }
            public string Value { get; }
            public string Detail { get; }
            public Color Accent { get; }
        }

        public sealed class HudVitalEditRequestedEventArgs : EventArgs
        {
            public HudVitalEditRequestedEventArgs(HudVitalTargetType targetType, int slotIndex)
            {
                TargetType = targetType;
                SlotIndex = slotIndex;
            }

            public HudVitalTargetType TargetType { get; }
            public bool IsCoreVital => TargetType == HudVitalTargetType.Core;
            public bool IsSystemVital => TargetType == HudVitalTargetType.System;
            public bool IsEdgeVital => TargetType == HudVitalTargetType.Edge;
            public int SlotIndex { get; }
        }

        public float Pitch { get; set; }
        public float Roll { get; set; }
        public float Heading { get; set; }
        public float GroundCourse { get; set; }
        public float TargetHeading { get; set; }
        public float Altitude { get; set; }
        public float TargetAltitude { get; set; }
        public float GroundSpeed { get; set; }
        public float AirSpeed { get; set; }
        public float TargetSpeed { get; set; }
        public float VerticalSpeed { get; set; }
        public float AngleOfAttack { get; set; }
        public float CriticalAngleOfAttack { get; set; }
        public float DistanceToWaypoint { get; set; }
        public int WaypointNumber { get; set; }
        public float DistanceToHome { get; set; }
        public float DistanceTravelled { get; set; }
        public float AzToMav { get; set; }
        public int BatteryRemaining { get; set; }
        public float BatteryVoltage { get; set; }
        public float BatteryPowerWatts { get; set; }
        public double EstimatedRemainingSeconds { get; set; }
        public double TimeInAirSeconds { get; set; }
        public float LinkQuality { get; set; }
        public int GpsSatellites { get; set; }
        public float GpsFixType { get; set; }
        public float EfiRpm { get; set; }
        public float EfiFuelFlow { get; set; }
        public float EfiFuelConsumed { get; set; }
        public float EfiFuelRemaining { get; set; } = -1f;
        public string Mode { get; set; } = "STANDBY";
        public bool Armed { get; set; }
        public bool Connected { get; set; }
        public VitalDisplayOverride[] CoreVitalOverrides { get; set; }
        public VitalDisplayOverride[] SystemVitalOverrides { get; set; }
        public VitalDisplayOverride[] EdgeVitalOverrides { get; set; }
        public event EventHandler<HudVitalEditRequestedEventArgs> VitalEditRequested;

        private readonly Color ribbonSurface = Color.FromArgb(15, 21, 32);
        private readonly Color tapeSurface = Color.FromArgb(17, 24, 35);
        private readonly Color tapeBorder = Color.FromArgb(72, 84, 106);
        private readonly Color centerBoxSurface = Color.FromArgb(8, 11, 18);
        private readonly Color centerBoxBorder = Color.FromArgb(118, 131, 156);
        private readonly Color skyColor = Color.FromArgb(18, 74, 174);
        private readonly Color groundColor = Color.FromArgb(118, 92, 62);
        private readonly Color horizonLineColor = Color.FromArgb(214, 179, 92);
        private readonly Color majorScaleColor = Color.FromArgb(228, 232, 238);
        private readonly Color minorScaleColor = Color.FromArgb(130, 143, 165);
        private readonly Color targetBugColor = Color.FromArgb(230, 174, 68);
        private readonly Color homeBugColor = Color.FromArgb(92, 202, 142);
        private readonly Color aircraftColor = Color.FromArgb(245, 82, 71);
        private readonly Color aoaSafeColor = Color.FromArgb(78, 182, 117);
        private readonly Color aoaCautionColor = Color.FromArgb(221, 182, 74);
        private readonly Color aoaDangerColor = Color.FromArgb(223, 92, 82);
        private readonly Color textPrimary = Color.FromArgb(232, 236, 242);
        private readonly Color textSecondary = Color.FromArgb(150, 160, 178);
        private readonly Color statusDanger = Color.FromArgb(232, 82, 71);
        private readonly Color statusSafe = Color.FromArgb(232, 82, 71);
        private readonly Color systemsStripSurface = Color.FromArgb(15, 21, 32);
        private readonly Color systemsStripBorder = Color.FromArgb(58, 70, 92);
        private readonly Color systemsStripDivider = Color.FromArgb(38, 47, 64);

        private readonly Pen horizonPen;
        private readonly Pen majorLadderPen;
        private readonly Pen minorLadderPen;
        private readonly Pen tapeTickPen;
        private readonly Font headingLabelFont;
        private readonly Font headingValueFont;
        private readonly Font bankLabelFont;
        private readonly Font tapeScaleFont;
        private readonly Font tapeValueFont;
        private readonly Font tapeBadgeFont;
        private readonly Font footerFont;
        private readonly Font batteryFont;
        private readonly Font systemsLabelFont;
        private readonly Font systemsValueFont;
        private readonly Font systemsValueCompactFont;
        private readonly Font systemsDetailFont;
        private readonly Font systemsDetailCompactFont;
        private readonly Font coreVitalLabelFont;
        private readonly Font coreVitalValueFont;
        private readonly Font overlayLabelFont;
        private readonly Font overlayValueFont;
        private readonly Font overlayDetailFont;
        private readonly Rectangle[] coreVitalHitAreas = new Rectangle[4];
        private readonly Rectangle[] systemVitalHitAreas = new Rectangle[9];
        private readonly Rectangle[] edgeVitalHitAreas = new Rectangle[2];

        public ControlArtificialHorizon()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            DoubleBuffered = true;
            BackColor = Color.FromArgb(10, 14, 20);

            horizonPen = new Pen(horizonLineColor, 2.4f);
            majorLadderPen = new Pen(ModernUiPainter.WithAlpha(horizonLineColor, 230), 1.45f);
            minorLadderPen = new Pen(ModernUiPainter.WithAlpha(horizonLineColor, 170), 1f);
            tapeTickPen = new Pen(ModernUiPainter.WithAlpha(majorScaleColor, 210), 1.2f);
            headingLabelFont = new Font("Segoe UI", 8.2f, FontStyle.Bold);
            headingValueFont = new Font("Segoe UI", 10.2f, FontStyle.Bold);
            bankLabelFont = new Font("Segoe UI", 9.2f, FontStyle.Bold);
            tapeScaleFont = new Font("Segoe UI", 11f, FontStyle.Bold);
            tapeValueFont = new Font("Segoe UI", 10f, FontStyle.Bold);
            tapeBadgeFont = new Font("Segoe UI", 8.1f, FontStyle.Bold);
            footerFont = new Font("Segoe UI", 9f, FontStyle.Bold);
            batteryFont = new Font("Segoe UI", 9.1f, FontStyle.Bold);
            systemsLabelFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            systemsValueFont = new Font("Segoe UI", 13.8f, FontStyle.Bold);
            systemsValueCompactFont = new Font("Segoe UI", 12.1f, FontStyle.Bold);
            systemsDetailFont = new Font("Segoe UI", 7.9f, FontStyle.Regular);
            systemsDetailCompactFont = new Font("Segoe UI", 7.1f, FontStyle.Regular);
            coreVitalLabelFont = new Font("Segoe UI", 8.1f, FontStyle.Bold);
            coreVitalValueFont = new Font("Segoe UI", 10.6f, FontStyle.Bold);
            overlayLabelFont = new Font("Segoe UI", 8.0f, FontStyle.Bold);
            overlayValueFont = new Font("Segoe UI", 10.6f, FontStyle.Bold);
            overlayDetailFont = new Font("Segoe UI", 8.3f, FontStyle.Regular);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var graphics = e.Graphics;
            graphics.Clear(BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            ClearVitalHitAreas();

            if (Width < 220 || Height < 220)
                return;

            int outerPadding = ClampInt(Math.Min(Height / 22, Width / 30), 8, 14);
            int ribbonHeight = ClampInt(Height / 9, 40, 54);
            int coreVitalsHeight = 0;
            int coreVitalsGap = 0;
            int systemsStripHeight = ClampInt(Height / 3, 168, 210);
            int systemsStripGap = ClampInt(Height / 60, 6, 10);
            int instrumentTop = outerPadding + ribbonHeight + 8;
            int instrumentBottom = Height - outerPadding - systemsStripHeight - systemsStripGap - coreVitalsHeight - coreVitalsGap;
            int instrumentHeight = Math.Max(160, instrumentBottom - instrumentTop);
            int arcReserve = ClampInt(instrumentHeight / 10, 24, 34);
            int tapeWidth = ClampInt(Width / 8, 66, 90);
            int aoaStripWidth = ClampInt(Width / 38, 18, 24);
            int aoaStripGap = ClampInt(Width / 80, 6, 10);
            int gap = ClampInt(Width / 60, 8, 14);

            var headingRect = new Rectangle(outerPadding, outerPadding, Width - outerPadding * 2, ribbonHeight);
            var systemsStripRect = new Rectangle(
                outerPadding,
                Height - outerPadding - systemsStripHeight,
                Width - outerPadding * 2,
                systemsStripHeight);
            var coreVitalsRect = new Rectangle(
                outerPadding,
                systemsStripRect.Top - coreVitalsGap - coreVitalsHeight,
                Width - outerPadding * 2,
                coreVitalsHeight);

            int centerAvailableWidth = Width - outerPadding * 2 - tapeWidth * 2 - gap * 2 - aoaStripWidth - aoaStripGap;
            int centerAvailableHeight = instrumentHeight - arcReserve - 12;
            int globeDiameter = Math.Max(220, Math.Min(centerAvailableWidth, centerAvailableHeight));
            globeDiameter = Math.Min(globeDiameter, Math.Min(centerAvailableWidth, centerAvailableHeight));

            int globeLeft = outerPadding + tapeWidth + gap + Math.Max(0, (centerAvailableWidth - globeDiameter) / 2);
            int globeTop = instrumentTop + arcReserve + Math.Max(0, (centerAvailableHeight - globeDiameter) / 2);
            var globeRect = new Rectangle(globeLeft, globeTop, globeDiameter, globeDiameter);

            int tapeTop = instrumentTop + arcReserve + 8;
            int tapeHeight = Math.Max(160, instrumentBottom - tapeTop - 4);
            var leftTapeRect = new Rectangle(outerPadding + 4, tapeTop, tapeWidth, tapeHeight);
            var rightTapeRect = new Rectangle(
                Width - outerPadding - aoaStripWidth - aoaStripGap - tapeWidth - 4,
                tapeTop,
                tapeWidth,
                tapeHeight);
            var aoaRect = new Rectangle(rightTapeRect.Right + aoaStripGap, tapeTop + 10, aoaStripWidth, Math.Max(80, tapeHeight - 20));

            DrawHeadingRibbon(graphics, headingRect);
            DrawRollScale(graphics, globeRect);
            DrawHorizonGlobe(graphics, globeRect);
            DrawSpeedTape(graphics, leftTapeRect);
            DrawAltitudeTape(graphics, rightTapeRect);
            DrawAoaEnvelope(graphics, aoaRect);
            DrawArmingAnnunciator(graphics, globeRect);
            DrawLegacyEdgeVitals(graphics, globeRect, leftTapeRect, rightTapeRect, aoaRect, systemsStripRect);
            DrawSystemsStrip(graphics, systemsStripRect);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = HitTestVital(e.Location) != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Cursor = Cursors.Default;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            var hit = HitTestVital(e.Location);
            if (hit == null)
                return;

            VitalEditRequested?.Invoke(this, hit);
        }

        private void DrawHeadingRibbon(Graphics graphics, Rectangle bounds)
        {
            ModernUiPainter.FillRoundedRectangle(graphics, ribbonSurface, bounds, 16);
            ModernUiPainter.DrawRoundedRectangle(graphics, Color.FromArgb(56, 69, 92), 1f, bounds, 16);

            int centerX = bounds.Left + bounds.Width / 2;
            var centerWindow = new Rectangle(centerX - 26, bounds.Top + 4, 52, bounds.Height - 8);
            ModernUiPainter.FillRoundedRectangle(graphics, Color.FromArgb(24, 31, 45), centerWindow, 10);
            ModernUiPainter.DrawRoundedRectangle(graphics, Color.FromArgb(86, 101, 128), 1f, centerWindow, 10);

            float pixelsPerDegree = (bounds.Width - 42) / 120f;
            float ribbonBottom = bounds.Bottom - 9;

            using (var majorTickPen = new Pen(ModernUiPainter.WithAlpha(majorScaleColor, 210), 1.2f))
            using (var minorTickPen = new Pen(ModernUiPainter.WithAlpha(majorScaleColor, 130), 1f))
            using (var labelBrush = new SolidBrush(majorScaleColor))
            using (var numberBrush = new SolidBrush(textSecondary))
            using (var headingBrush = new SolidBrush(textPrimary))
            using (var headingBugPen = new Pen(targetBugColor, 2.4f))
            using (var coursePen = new Pen(Color.FromArgb(90, 160, 208), 2f))
            using (var homeBrush = new SolidBrush(homeBugColor))
            {
                for (int offset = -60; offset <= 60; offset += 5)
                {
                    float x = centerX + offset * pixelsPerDegree;
                    bool isMajor = offset % 15 == 0;
                    float tickTop = ribbonBottom - (isMajor ? 12 : 7);
                    graphics.DrawLine(isMajor ? majorTickPen : minorTickPen, x, tickTop, x, ribbonBottom);

                    if (!isMajor)
                        continue;

                    int displayHeading = (int)Math.Round(NormalizeHeading(Heading + offset));
                    string label = GetHeadingLabel(displayHeading);
                    SizeF labelSize = graphics.MeasureString(label, headingLabelFont);
                    Brush brush = label.Length <= 2 ? labelBrush : numberBrush;
                    graphics.DrawString(label, headingLabelFont, brush, x - labelSize.Width / 2f, bounds.Top + 8);
                }

                float courseOffset = NormalizeRelativeAngle(GroundCourse - Heading);
                if (Connected && Math.Abs(courseOffset) <= 60f)
                {
                    float courseX = centerX + courseOffset * pixelsPerDegree;
                    graphics.DrawLine(coursePen, courseX, bounds.Top + 8, courseX, ribbonBottom);
                }

                float targetOffset = NormalizeRelativeAngle(TargetHeading - Heading);
                float bugX = targetOffset <= -60f ? bounds.Left + 8 : targetOffset >= 60f ? bounds.Right - 8 : centerX + targetOffset * pixelsPerDegree;
                graphics.DrawLine(headingBugPen, bugX, bounds.Top + 8, bugX, ribbonBottom);

                if (Connected && DistanceToHome > 0.1f)
                {
                    float homeHeading = NormalizeHeading(AzToMav + 180f);
                    float homeOffset = NormalizeRelativeAngle(homeHeading - Heading);
                    if (Math.Abs(homeOffset) <= 60f)
                    {
                        float homeX = centerX + homeOffset * pixelsPerDegree;
                        SizeF homeSize = graphics.MeasureString("H", headingLabelFont);
                        graphics.DrawString("H", headingLabelFont, homeBrush, homeX - homeSize.Width / 2f, bounds.Top + bounds.Height - 22);
                    }
                }

                string headingValue = ((int)Math.Round(NormalizeHeading(Heading))).ToString("000");
                SizeF headingSize = graphics.MeasureString(headingValue, headingValueFont);
                graphics.DrawString(headingValue, headingValueFont, headingBrush,
                    centerWindow.Left + (centerWindow.Width - headingSize.Width) / 2f,
                    centerWindow.Top + (centerWindow.Height - headingSize.Height) / 2f - 1f);
            }

            Point[] centerMarker =
            {
                new Point(centerX, bounds.Bottom - 3),
                new Point(centerX - 7, bounds.Bottom - 15),
                new Point(centerX + 7, bounds.Bottom - 15)
            };
            using (var markerBrush = new SolidBrush(targetBugColor))
            {
                graphics.FillPolygon(markerBrush, centerMarker);
            }
        }

        private void DrawRollScale(Graphics graphics, Rectangle globeRect)
        {
            PointF center = new PointF(globeRect.Left + globeRect.Width / 2f, globeRect.Top + globeRect.Height / 2f);
            float radius = globeRect.Width * 0.60f;

            using (var arcPen = new Pen(ModernUiPainter.WithAlpha(horizonLineColor, 230), 2f))
            using (var tickPen = new Pen(ModernUiPainter.WithAlpha(horizonLineColor, 210), 1.4f))
            using (var labelBrush = new SolidBrush(horizonLineColor))
            using (var pointerBrush = new SolidBrush(aircraftColor))
            {
                PointF previous = RollArcPoint(center, radius, -60f);
                for (int angle = -59; angle <= 60; angle++)
                {
                    PointF current = RollArcPoint(center, radius, angle);
                    graphics.DrawLine(arcPen, previous, current);
                    previous = current;
                }

                int[] tickAngles = { -60, -45, -30, -15, 0, 15, 30, 45, 60 };
                foreach (int tick in tickAngles)
                {
                    float outerRadius = radius;
                    float innerRadius = tick % 30 == 0 ? radius - 13 : radius - 9;
                    PointF outer = RollArcPoint(center, outerRadius, tick);
                    PointF inner = RollArcPoint(center, innerRadius, tick);
                    graphics.DrawLine(tickPen, outer, inner);

                    if (tick == 0)
                        continue;

                    string label = Math.Abs(tick).ToString();
                    SizeF labelSize = graphics.MeasureString(label, bankLabelFont);
                    PointF labelPoint = RollArcPoint(center, radius + 20, tick);
                    graphics.DrawString(label, bankLabelFont, labelBrush,
                        labelPoint.X - labelSize.Width / 2f, labelPoint.Y - labelSize.Height / 2f);
                }

                PointF index = RollArcPoint(center, radius + 3, 0f);
                graphics.FillPolygon(pointerBrush, new[]
                {
                    new Point((int)Math.Round(index.X), (int)Math.Round(index.Y) - 1),
                    new Point((int)Math.Round(index.X) - 8, (int)Math.Round(index.Y) - 12),
                    new Point((int)Math.Round(index.X) + 8, (int)Math.Round(index.Y) - 12)
                });

                float clampedRoll = Clamp(Roll, -60f, 60f);
                PointF rollPointer = RollArcPoint(center, radius - 3, clampedRoll);
                PointF rollLeft = RollArcPoint(center, radius - 16, clampedRoll - 3.2f);
                PointF rollRight = RollArcPoint(center, radius - 16, clampedRoll + 3.2f);
                graphics.FillPolygon(pointerBrush, new[] { Point.Round(rollPointer), Point.Round(rollLeft), Point.Round(rollRight) });
            }
        }

        private void DrawHorizonGlobe(Graphics graphics, Rectangle hudRect)
        {
            int centerX = hudRect.Left + hudRect.Width / 2;
            int centerY = hudRect.Top + hudRect.Height / 2;
            int radius = hudRect.Width / 2;
            float pixelsPerDegree = Math.Max(2.75f, radius / 42f);

            using (var clipPath = new GraphicsPath())
            {
                clipPath.AddEllipse(hudRect);
                GraphicsState clipState = graphics.Save();
                graphics.SetClip(clipPath);

                GraphicsState rotatedState = graphics.Save();
                graphics.TranslateTransform(centerX, centerY);
                graphics.RotateTransform(-Roll);

                float pitchOffset = -Pitch * pixelsPerDegree;
                using (var skyBrush = new SolidBrush(skyColor))
                using (var groundBrush = new SolidBrush(groundColor))
                using (var labelBrush = new SolidBrush(majorScaleColor))
                {
                    graphics.FillRectangle(skyBrush, -radius * 2f, -radius * 2f + pitchOffset, radius * 4f, radius * 4f);
                    graphics.FillRectangle(groundBrush, -radius * 2f, pitchOffset, radius * 4f, radius * 4f);
                    graphics.DrawLine(horizonPen, -radius * 2f, pitchOffset, radius * 2f, pitchOffset);

                    for (int ladderPitch = -40; ladderPitch <= 40; ladderPitch += 5)
                    {
                        if (ladderPitch == 0)
                            continue;

                        float y = (ladderPitch - Pitch) * pixelsPerDegree;
                        bool isMajor = ladderPitch % 10 == 0;
                        int lineLength = isMajor ? (int)(radius * 0.34f) : (int)(radius * 0.22f);
                        Pen pen = isMajor ? majorLadderPen : minorLadderPen;
                        graphics.DrawLine(pen, -lineLength, y, lineLength, y);

                        if (!isMajor)
                            continue;

                        string label = Math.Abs(ladderPitch).ToString();
                        SizeF labelSize = graphics.MeasureString(label, tapeValueFont);
                        graphics.DrawString(label, tapeValueFont, labelBrush, lineLength + 8, y - labelSize.Height / 2f);
                        graphics.DrawString(label, tapeValueFont, labelBrush, -lineLength - labelSize.Width - 8, y - labelSize.Height / 2f);
                    }
                }

                graphics.Restore(rotatedState);
                graphics.Restore(clipState);
            }

            using (var borderPen = new Pen(horizonLineColor, 2.8f))
            using (var wingPen = new Pen(aircraftColor, 3f))
            using (var aircraftBrush = new SolidBrush(aircraftColor))
            {
                graphics.DrawEllipse(borderPen, hudRect);

                int wingSpan = Math.Max(22, radius / 6);
                int centerPodWidth = Math.Max(12, radius / 12);
                graphics.DrawLine(wingPen, centerX - wingSpan, centerY, centerX - centerPodWidth, centerY);
                graphics.DrawLine(wingPen, centerX + centerPodWidth, centerY, centerX + wingSpan, centerY);
                graphics.DrawLine(wingPen, centerX, centerY - 6, centerX, centerY + 6);
                graphics.FillEllipse(aircraftBrush, centerX - 4, centerY - 4, 8, 8);
            }
        }

        private void DrawSpeedTape(Graphics graphics, Rectangle tapeRect)
        {
            DrawTapeShell(graphics, tapeRect);

            float displaySpeed = AirSpeed > 0.1f ? AirSpeed : GroundSpeed;
            float visibleRange = GetSpeedRange();
            float start = displaySpeed - visibleRange / 2f;
            float end = displaySpeed + visibleRange / 2f;
            float pixelsPerUnit = tapeRect.Height / visibleRange;

            using (var scaleBrush = new SolidBrush(majorScaleColor))
            using (var targetPen = new Pen(targetBugColor, 2.4f))
            {
                for (int value = (int)Math.Floor(start); value <= (int)Math.Ceiling(end); value++)
                {
                    float y = tapeRect.Bottom - ((value - start) * pixelsPerUnit);
                    if (y < tapeRect.Top || y > tapeRect.Bottom)
                        continue;

                    bool isMajor = value % 5 == 0;
                    int tickLength = isMajor ? tapeRect.Width / 3 : tapeRect.Width / 5;
                    graphics.DrawLine(tapeTickPen, tapeRect.Right - 1, y, tapeRect.Right - tickLength, y);

                    if (isMajor)
                    {
                        string label = value.ToString();
                        SizeF labelSize = graphics.MeasureString(label, tapeScaleFont);
                        graphics.DrawString(label, tapeScaleFont, scaleBrush,
                            tapeRect.Left + 10, y - labelSize.Height / 2f);
                    }
                }

                if (TargetSpeed > 0.1f)
                {
                    float bugY = tapeRect.Bottom - ((TargetSpeed - start) * pixelsPerUnit);
                    if (bugY >= tapeRect.Top && bugY <= tapeRect.Bottom)
                        graphics.DrawLine(targetPen, tapeRect.Left + 6, bugY, tapeRect.Right - 6, bugY);
                }

                DrawTapeValueBox(graphics, new Rectangle(tapeRect.Left - 4, tapeRect.Top + tapeRect.Height / 2 - 17, tapeRect.Width + 14, 34),
                    string.Format("{0:0}", displaySpeed), false);

                var airBadgeRect = new Rectangle(Math.Max(2, tapeRect.Left - 52), tapeRect.Top + tapeRect.Height / 2 - 24, 48, 20);
                var groundBadgeRect = new Rectangle(Math.Max(2, tapeRect.Left - 52), tapeRect.Top + tapeRect.Height / 2 - 1, 48, 20);
                DrawTelemetryBadge(graphics, airBadgeRect, "A", AirSpeed, ModernUiPainter.GetBatteryColor(BatteryRemaining));
                DrawTelemetryBadge(graphics, groundBadgeRect, "G", GroundSpeed, Color.FromArgb(88, 182, 225));
            }
        }

        private void DrawAltitudeTape(Graphics graphics, Rectangle tapeRect)
        {
            DrawTapeShell(graphics, tapeRect);

            float visibleRange = GetAltitudeRange();
            float start = Altitude - visibleRange / 2f;
            float end = Altitude + visibleRange / 2f;
            float pixelsPerUnit = tapeRect.Height / visibleRange;

            using (var scaleBrush = new SolidBrush(majorScaleColor))
            using (var targetPen = new Pen(homeBugColor, 2.4f))
            using (var accentPen = new Pen(ModernUiPainter.WithAlpha(targetBugColor, 220), 1.6f))
            {
                for (int value = (int)Math.Floor(start); value <= (int)Math.Ceiling(end); value++)
                {
                    float y = tapeRect.Bottom - ((value - start) * pixelsPerUnit);
                    if (y < tapeRect.Top || y > tapeRect.Bottom)
                        continue;

                    bool isMajor = value % 5 == 0;
                    int tickLength = isMajor ? tapeRect.Width / 3 : tapeRect.Width / 5;
                    graphics.DrawLine(tapeTickPen, tapeRect.Left, y, tapeRect.Left + tickLength, y);

                    if (isMajor)
                    {
                        string label = value.ToString();
                        SizeF labelSize = graphics.MeasureString(label, tapeScaleFont);
                        graphics.DrawString(label, tapeScaleFont, scaleBrush,
                            tapeRect.Right - labelSize.Width - 10, y - labelSize.Height / 2f);
                    }
                }

                if (TargetAltitude != 0)
                {
                    float bugY = tapeRect.Bottom - ((TargetAltitude - start) * pixelsPerUnit);
                    if (bugY >= tapeRect.Top && bugY <= tapeRect.Bottom)
                        graphics.DrawLine(targetPen, tapeRect.Left + 6, bugY, tapeRect.Right - 6, bugY);
                }

                DrawTapeValueBox(graphics,
                    new Rectangle(tapeRect.Left - 10, tapeRect.Top + tapeRect.Height / 2 - 17, tapeRect.Width + 18, 34),
                    string.Format("{0:0}{1}", Altitude, GetAltUnit()),
                    true);

                int vsiWidth = 18;
                var vsiRect = new Rectangle(tapeRect.Left - vsiWidth - 8, tapeRect.Top + 6, vsiWidth, tapeRect.Height - 12);
                float vsRange = GetVerticalSpeedRange();
                float vsValue = Clamp(VerticalSpeed, -vsRange / 2f, vsRange / 2f);
                float centerY = vsiRect.Top + vsiRect.Height / 2f;
                float pixelsPerVsUnit = vsiRect.Height / vsRange;
                float tipY = centerY - (vsValue * pixelsPerVsUnit);
                PointF[] wedge =
                {
                    new PointF(vsiRect.Right, centerY),
                    new PointF(vsiRect.Left + 3, centerY),
                    new PointF(vsiRect.Left + 3, tipY),
                    new PointF(vsiRect.Right, tipY - Math.Sign(vsValue == 0 ? 1 : vsValue) * 6f)
                };

                using (var fillBrush = new SolidBrush(ModernUiPainter.WithAlpha(targetBugColor, 120)))
                {
                    graphics.FillPolygon(fillBrush, wedge);
                }

                graphics.DrawPolygon(accentPen, wedge);
            }
        }

        private void DrawAoaEnvelope(Graphics graphics, Rectangle bounds)
        {
            if (bounds.Width < 14 || bounds.Height < 72)
                return;

            bool hasAoAData = Connected && CriticalAngleOfAttack > 0.1f;
            float aoaRatio = hasAoAData
                ? Clamp(AngleOfAttack / CriticalAngleOfAttack, 0f, 1f)
                : 0f;

            ModernUiPainter.FillRoundedRectangle(graphics, Color.FromArgb(13, 18, 27), bounds, 10);
            ModernUiPainter.DrawRoundedRectangle(graphics, Color.FromArgb(86, 99, 122), 1f, bounds, 10);

            var barRect = new Rectangle(
                bounds.Left + Math.Max(4, bounds.Width / 3),
                bounds.Top + 8,
                Math.Max(7, bounds.Width / 3),
                Math.Max(40, bounds.Height - 16));

            using (var safeBrush = new SolidBrush(ModernUiPainter.WithAlpha(aoaSafeColor, hasAoAData ? 220 : 90)))
            using (var cautionBrush = new SolidBrush(ModernUiPainter.WithAlpha(aoaCautionColor, hasAoAData ? 220 : 90)))
            using (var dangerBrush = new SolidBrush(ModernUiPainter.WithAlpha(aoaDangerColor, hasAoAData ? 220 : 90)))
            using (var barBorderPen = new Pen(Color.FromArgb(218, 225, 235), 1f))
            {
                float redHeight = barRect.Height * 0.15f;
                float yellowHeight = barRect.Height * 0.25f;
                float greenHeight = barRect.Height - redHeight - yellowHeight;

                graphics.FillRectangle(dangerBrush,
                    new RectangleF(barRect.Left, barRect.Top, barRect.Width, redHeight));
                graphics.FillRectangle(cautionBrush,
                    new RectangleF(barRect.Left, barRect.Top + redHeight, barRect.Width, yellowHeight));
                graphics.FillRectangle(safeBrush,
                    new RectangleF(barRect.Left, barRect.Top + redHeight + yellowHeight, barRect.Width, greenHeight));
                graphics.DrawRectangle(barBorderPen, barRect);
            }

            float pointerY = barRect.Bottom - (barRect.Height * aoaRatio);
            pointerY = Clamp(pointerY, barRect.Top + 3, barRect.Bottom - 3);

            Color pointerAccent = !hasAoAData
                ? textSecondary
                : aoaRatio >= 0.85f
                    ? aoaDangerColor
                    : aoaRatio >= 0.60f
                        ? aoaCautionColor
                        : aoaSafeColor;

            PointF[] pointer =
            {
                new PointF(barRect.Left - 1, pointerY),
                new PointF(bounds.Left + 2, pointerY - Math.Max(4, bounds.Width / 3f)),
                new PointF(bounds.Left + 2, pointerY + Math.Max(4, bounds.Width / 3f))
            };

            using (var pointerFill = new SolidBrush(Color.FromArgb(16, 20, 30)))
            using (var pointerPen = new Pen(pointerAccent, 1.4f))
            {
                graphics.FillPolygon(pointerFill, pointer);
                graphics.DrawPolygon(pointerPen, pointer);
            }
        }

        private void DrawWaypointReadout(Graphics graphics, Rectangle bounds, StringAlignment alignment = StringAlignment.Center, Color? colorOverride = null)
        {
            if (bounds.Width < 40 || bounds.Height < 16)
                return;

            string footer = FormatWaypointFooter();
            using (var textBrush = new SolidBrush(colorOverride ?? textSecondary))
            {
                var textRect = new RectangleF(bounds.Left + 2, bounds.Top, bounds.Width - 4, bounds.Height);
                var format = new StringFormat
                {
                    Alignment = alignment,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                };

                graphics.DrawString(footer, footerFont, textBrush, textRect, format);
            }
        }

        private void DrawBatteryBadge(Graphics graphics, Rectangle bounds, VitalDisplayOverride vitalOverride)
        {
            Color accent = vitalOverride?.Accent ?? (Connected ? ModernUiPainter.GetBatteryColor(BatteryRemaining) : textSecondary);
            string label = vitalOverride?.Label ?? "BAT";
            string value = vitalOverride?.Value ?? (Connected ? $"{BatteryRemaining:0}%" : "--");
            ModernUiPainter.FillRoundedRectangle(graphics, Color.FromArgb(16, 20, 30), bounds, 11);
            ModernUiPainter.DrawRoundedRectangle(graphics, ModernUiPainter.WithAlpha(accent, 140), 1f, bounds, 11);

            int iconWidth = 18;
            var body = new Rectangle(bounds.Left + 8, bounds.Top + 5, iconWidth, bounds.Height - 10);
            var tip = new Rectangle(body.Right, body.Top + body.Height / 3, 4, body.Height / 3);
            int fillWidth = Math.Max(2, (int)Math.Round((body.Width - 4) * Clamp(BatteryRemaining / 100f, 0f, 1f)));

            using (var borderPen = new Pen(accent, 1.2f))
            using (var fillBrush = new SolidBrush(accent))
            using (var labelBrush = new SolidBrush(textSecondary))
            using (var textBrush = new SolidBrush(textPrimary))
            {
                graphics.DrawRectangle(borderPen, body);
                graphics.DrawRectangle(borderPen, tip);
                graphics.FillRectangle(fillBrush, new Rectangle(body.Left + 2, body.Top + 2, fillWidth, Math.Max(1, body.Height - 3)));
                graphics.DrawString(label, overlayLabelFont, labelBrush, body.Right + 8, bounds.Top + 2);
                graphics.DrawString(value, overlayValueFont, textBrush, body.Right + 8, bounds.Top + 10);
            }
        }

        private void DrawArmingAnnunciator(Graphics graphics, Rectangle globeRect)
        {
            string label = Armed ? "ARMED" : "DISARMED";
            Color accent = Armed ? statusDanger : statusDanger;
            float fontSize = Clamp(globeRect.Width / 12.5f, 18f, 28f);

            using (var statusTextFont = new Font("Segoe UI", fontSize, FontStyle.Bold))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(65, 0, 0, 0)))
            using (var textBrush = new SolidBrush(accent))
            {
                SizeF size = graphics.MeasureString(label, statusTextFont);
                float x = globeRect.Left + (globeRect.Width - size.Width) / 2f;
                float y = globeRect.Top + globeRect.Height * 0.64f;
                graphics.DrawString(label, statusTextFont, shadowBrush, x + 1.5f, y + 1.5f);
                graphics.DrawString(label, statusTextFont, textBrush, x, y);
            }
        }

        private void DrawLegacyEdgeVitals(Graphics graphics, Rectangle globeRect, Rectangle leftTapeRect, Rectangle rightTapeRect, Rectangle aoaRect, Rectangle systemsStripRect)
        {
            int upperBadgeHeight = 24;
            int upperBadgeTop = Math.Min(leftTapeRect.Top - upperBadgeHeight - 12, globeRect.Top - upperBadgeHeight - 8);
            upperBadgeTop = Math.Max(6, upperBadgeTop);

            var linkRect = new Rectangle(leftTapeRect.Left + 2, upperBadgeTop, 82, 22);
            int batteryWidth = Math.Max(74, aoaRect.Left - rightTapeRect.Left - 18);
            var batteryRect = new Rectangle(
                Math.Max(globeRect.Right - 4, aoaRect.Left - batteryWidth - 6),
                upperBadgeTop,
                batteryWidth,
                upperBadgeHeight);

            int lowerBandTop = globeRect.Bottom + 6;
            int lowerBandBottom = systemsStripRect.Top - 8;
            int lowerBandHeight = Math.Max(0, lowerBandBottom - lowerBandTop);
            int lineHeight = lowerBandHeight >= 44 ? 16 : lowerBandHeight >= 34 ? 13 : 11;
            int rowGap = lowerBandHeight >= 44 ? 3 : 2;
            int leftBlockWidth = Math.Max(126, Math.Min(176, globeRect.Width / 2));
            int rightBlockWidth = 132;
            int rightBlockLeft = globeRect.Right - rightBlockWidth - 8;
            int leftBlockLeft = globeRect.Left + 10;
            int groupHeight = (lineHeight * 2) + rowGap;
            int groupTop = lowerBandTop + Math.Max(0, (lowerBandHeight - groupHeight) / 2);

            var fuelUsedRect = new Rectangle(leftBlockLeft, groupTop, leftBlockWidth, lineHeight);
            var fuelRemRect = new Rectangle(leftBlockLeft, groupTop + lineHeight + rowGap, leftBlockWidth, lineHeight);
            var waypointRect = new Rectangle(rightBlockLeft, groupTop, rightBlockWidth, lineHeight);
            var rpmRect = new Rectangle(rightBlockLeft, groupTop + lineHeight + rowGap, rightBlockWidth, lineHeight);

            edgeVitalHitAreas[0] = linkRect;
            edgeVitalHitAreas[1] = batteryRect;

            DrawLinkQualityBadge(graphics, linkRect, GetVitalOverride(EdgeVitalOverrides, 0));
            DrawBatteryBadge(graphics, batteryRect, GetVitalOverride(EdgeVitalOverrides, 1));
            DrawCompactTelemetryLine(graphics, fuelUsedRect, "FUEL USED", FormatFuelUsedValue(), Color.FromArgb(230, 174, 68), StringAlignment.Near);
            DrawCompactTelemetryLine(graphics, fuelRemRect, "FUEL REM", FormatFuelRemainingValue(), Color.FromArgb(92, 202, 142), StringAlignment.Near);
            DrawWaypointReadout(graphics, waypointRect, StringAlignment.Far, textPrimary);
            DrawCompactTelemetryLine(graphics, rpmRect, "RPM", FormatRpmValue(), Color.FromArgb(235, 239, 245), StringAlignment.Far);
        }

        private void DrawLinkQualityBadge(Graphics graphics, Rectangle bounds, VitalDisplayOverride vitalOverride)
        {
            Color accent = vitalOverride?.Accent ?? ModernUiPainter.GetLinkColor(Connected, (int)Math.Round(LinkQuality));
            string label = vitalOverride?.Label ?? "LQ";
            string value = vitalOverride?.Value ?? (Connected ? $"{LinkQuality:0}%" : "--");

            using (var accentBrush = new SolidBrush(accent))
            using (var labelBrush = new SolidBrush(textSecondary))
            using (var valueBrush = new SolidBrush(textPrimary))
            {
                graphics.FillRectangle(accentBrush, bounds.Left + 2, bounds.Top + 6, 2, Math.Max(8, bounds.Height - 12));
                graphics.DrawString(label, overlayLabelFont, labelBrush, bounds.Left + 8, bounds.Top + 3);
                graphics.DrawString(value, overlayValueFont, valueBrush, bounds.Left + 24, bounds.Top + 1);
            }
        }

        private void DrawCompactTelemetryLine(Graphics graphics, Rectangle bounds, string label, string value, Color accent, StringAlignment alignment)
        {
            using (var labelBrush = new SolidBrush(ModernUiPainter.WithAlpha(accent, 180)))
            using (var valueBrush = new SolidBrush(accent))
            {
                string safeLabel = string.IsNullOrWhiteSpace(label) ? string.Empty : label.Trim();
                string safeValue = string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();
                float inset = 3f;
                float gap = 8f;
                float baselineY = bounds.Top + Math.Max(0f, (bounds.Height - overlayValueFont.GetHeight(graphics)) / 2f) - 1f;
                SizeF labelSize = graphics.MeasureString(safeLabel, overlayLabelFont, int.MaxValue, StringFormat.GenericTypographic);
                SizeF valueSize = graphics.MeasureString(safeValue, overlayValueFont, int.MaxValue, StringFormat.GenericTypographic);

                if (alignment == StringAlignment.Far)
                {
                    float valueX = bounds.Right - inset - valueSize.Width;
                    float labelX = Math.Max(bounds.Left + inset, valueX - gap - labelSize.Width);
                    graphics.DrawString(safeLabel, overlayLabelFont, labelBrush, labelX, baselineY + 1f, StringFormat.GenericTypographic);
                    graphics.DrawString(safeValue, overlayValueFont, valueBrush, valueX, baselineY, StringFormat.GenericTypographic);
                }
                else
                {
                    float labelX = bounds.Left + inset;
                    float valueX = Math.Min(bounds.Right - inset - valueSize.Width, labelX + labelSize.Width + gap);
                    graphics.DrawString(safeLabel, overlayLabelFont, labelBrush, labelX, baselineY + 1f, StringFormat.GenericTypographic);
                    graphics.DrawString(safeValue, overlayValueFont, valueBrush, valueX, baselineY, StringFormat.GenericTypographic);
                }
            }
        }

        private void DrawSystemsStrip(Graphics graphics, Rectangle bounds)
        {
            if (bounds.Width < 300 || bounds.Height < 40)
                return;

            ModernUiPainter.FillRoundedRectangle(graphics, systemsStripSurface, bounds, 16);
            ModernUiPainter.DrawRoundedRectangle(graphics, systemsStripBorder, 1f, bounds, 16);

            var vitals = new[]
            {
                new SystemVital(
                    "MODE",
                    string.IsNullOrWhiteSpace(Mode) ? "STBY" : Mode.ToUpperInvariant(),
                    Connected ? $"LINK {LinkQuality:0}%  |  {(Armed ? "ARMED" : "SAFE")}" : "Awaiting vehicle link",
                    Connected ? (Armed ? statusDanger : Color.FromArgb(100, 184, 224)) : textSecondary),
                new SystemVital(
                    "BATTERY",
                    Connected ? $"{BatteryRemaining:0}%" : "--",
                    Connected ? $"{BatteryVoltage:0.0} V  |  {BatteryPowerWatts:0} W" : "Awaiting power telemetry",
                    Connected ? ModernUiPainter.GetBatteryColor(BatteryRemaining) : textSecondary),
                new SystemVital(
                    "EST. REM",
                    EstimatedRemainingSeconds > 0 ? ModernUiPainter.FormatDuration(EstimatedRemainingSeconds) : "--",
                    EstimatedRemainingSeconds > 0 ? "Predicted endurance" : "Awaiting battery estimate",
                    Color.FromArgb(230, 174, 68)),
                new SystemVital(
                    "ALTITUDE",
                    $"{Altitude:0}{GetAltUnit()}",
                    Connected ? $"V/S {FormatVerticalSpeedValue()}" : "Awaiting altitude telemetry",
                    Color.FromArgb(94, 199, 230)),
                new SystemVital(
                    "GROUND SPD",
                    FormatSpeedValue(GroundSpeed),
                    Connected ? $"CRS {GroundCourse:000}" : "Awaiting groundspeed telemetry",
                    Color.FromArgb(88, 146, 232)),
                new SystemVital(
                    "AIR SPEED",
                    FormatSpeedValue(AirSpeed),
                    Connected ? $"TGT {FormatSpeedValue(TargetSpeed)}" : "Awaiting airspeed telemetry",
                    Color.FromArgb(226, 179, 82)),
                new SystemVital(
                    "TIME AIR",
                    TimeInAirSeconds > 1 ? ModernUiPainter.FormatDuration(TimeInAirSeconds) : "00:00",
                    $"TRACK {FormatDistanceValue(DistanceTravelled)}",
                    Color.FromArgb(104, 129, 232)),
                new SystemVital(
                    "HOME",
                    FormatDistanceValue(DistanceToHome),
                    Connected ? $"BRG {NormalizeHeading(AzToMav + 180f):000}" : "Awaiting home reference",
                    Color.FromArgb(92, 202, 142)),
                new SystemVital(
                    "NAV",
                    FormatNavigationValue(),
                    Connected ? $"GPS {GpsSatellites:0} SAT  |  FIX {GpsFixType:0}" : "Awaiting nav telemetry",
                    Color.FromArgb(162, 110, 232))
            };

            for (int i = 0; i < vitals.Length; i++)
            {
                var vitalOverride = GetVitalOverride(SystemVitalOverrides, i);
                if (vitalOverride != null)
                    vitals[i] = new SystemVital(vitalOverride.Label, vitalOverride.Value, vitalOverride.Detail, vitalOverride.Accent);
            }

            int columns = 3;
            int rows = (int)Math.Ceiling(vitals.Length / (double)columns);
            int rowHeight = bounds.Height / rows;
            int rowRemainder = bounds.Height - (rowHeight * rows);
            int segmentWidth = bounds.Width / columns;
            int segmentRemainder = bounds.Width - (segmentWidth * columns);

            for (int row = 0, y = bounds.Top; row < rows; row++)
            {
                int height = rowHeight + (row == rows - 1 ? rowRemainder : 0);

                for (int column = 0, x = bounds.Left; column < columns; column++)
                {
                    int index = (row * columns) + column;
                    if (index >= vitals.Length)
                        break;

                    int width = segmentWidth + (column == columns - 1 ? segmentRemainder : 0);
                    var segmentRect = new Rectangle(x, y, width, height);
                    systemVitalHitAreas[index] = segmentRect;
                    bool drawDivider = column < columns - 1;
                    bool drawBottomDivider = row < rows - 1;
                    DrawSystemVitalCell(graphics, segmentRect, vitals[index], drawDivider, drawBottomDivider);
                    x += width;
                }

                y += height;
            }
        }

        private void DrawCoreVitalsRibbon(Graphics graphics, Rectangle bounds)
        {
            if (bounds.Width < 220 || bounds.Height < 24)
                return;

            ModernUiPainter.FillRoundedRectangle(graphics, Color.FromArgb(13, 18, 28), bounds, 12);
            ModernUiPainter.DrawRoundedRectangle(graphics, Color.FromArgb(50, 63, 84), 1f, bounds, 12);

            var vitals = new[]
            {
                new SystemVital(
                    "RPM",
                    Connected && EfiRpm > 0 ? $"{EfiRpm:0}" : "--",
                    string.Empty,
                    Color.FromArgb(226, 179, 82)),
                new SystemVital(
                    "FUEL FLOW",
                    Connected && EfiFuelFlow > 0 ? $"{EfiFuelFlow:0} cc/min" : "--",
                    string.Empty,
                    Color.FromArgb(92, 202, 142)),
                new SystemVital(
                    "BATTERY",
                    Connected ? $"{BatteryRemaining:0}%  {BatteryVoltage:0.0}V" : "--",
                    string.Empty,
                    Connected ? ModernUiPainter.GetBatteryColor(BatteryRemaining) : textSecondary),
                new SystemVital(
                    "PWR",
                    Connected ? $"{BatteryPowerWatts:0} W" : "--",
                    string.Empty,
                    Color.FromArgb(88, 182, 225))
            };

            for (int i = 0; i < vitals.Length; i++)
            {
                var vitalOverride = GetVitalOverride(CoreVitalOverrides, i);
                if (vitalOverride != null)
                    vitals[i] = new SystemVital(vitalOverride.Label, vitalOverride.Value, string.Empty, vitalOverride.Accent);
            }

            int segmentWidth = bounds.Width / vitals.Length;
            int remainder = bounds.Width - (segmentWidth * vitals.Length);

            using (var dividerPen = new Pen(Color.FromArgb(34, 46, 64), 1f))
            {
                for (int i = 0, x = bounds.Left; i < vitals.Length; i++)
                {
                    int width = segmentWidth + (i == vitals.Length - 1 ? remainder : 0);
                    var segmentRect = new Rectangle(x, bounds.Top, width, bounds.Height);
                    coreVitalHitAreas[i] = segmentRect;
                    DrawCoreVitalCell(graphics, segmentRect, vitals[i]);

                    if (i < vitals.Length - 1)
                        graphics.DrawLine(dividerPen, segmentRect.Right, segmentRect.Top + 8, segmentRect.Right, segmentRect.Bottom - 8);

                    x += width;
                }
            }
        }

        private void DrawCoreVitalCell(Graphics graphics, Rectangle bounds, SystemVital vital)
        {
            using (var accentBrush = new SolidBrush(vital.Accent))
            using (var labelBrush = new SolidBrush(textSecondary))
            using (var valueBrush = new SolidBrush(vital.Accent))
            {
                int contentLeft = bounds.Left + 12;
                int contentWidth = Math.Max(40, bounds.Width - 24);
                graphics.FillRectangle(accentBrush, contentLeft, bounds.Top + 7, Math.Min(26, contentWidth), 2);

                var format = new StringFormat
                {
                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                };

                graphics.DrawString(vital.Label, coreVitalLabelFont, labelBrush,
                    new RectangleF(contentLeft, bounds.Top + 10, contentWidth, 12), format);
                graphics.DrawString(vital.Value, coreVitalValueFont, valueBrush,
                    new RectangleF(contentLeft, bounds.Top + 18, contentWidth, bounds.Height - 22), format);
            }
        }

        private void DrawSystemVitalCell(Graphics graphics, Rectangle bounds, SystemVital vital, bool drawDivider, bool drawBottomDivider)
        {
            int contentLeft = bounds.Left + 11;
            int contentWidth = Math.Max(44, bounds.Width - 22);
            bool compact = bounds.Height < 68;
            float labelTop = compact ? bounds.Top + 12 : bounds.Top + 15;
            float valueTop = compact ? bounds.Top + 24 : bounds.Top + 29;
            float detailTop = compact ? bounds.Bottom - 18 : bounds.Bottom - 22;
            Font valueFont = compact ? systemsValueCompactFont : systemsValueFont;
            Font detailFont = compact ? systemsDetailCompactFont : systemsDetailFont;

            using (var accentBrush = new SolidBrush(vital.Accent))
            using (var labelBrush = new SolidBrush(textSecondary))
            using (var valueBrush = new SolidBrush(vital.Accent))
            using (var detailBrush = new SolidBrush(Color.FromArgb(184, 193, 208)))
            using (var dividerPen = new Pen(systemsStripDivider, 1f))
            {
                graphics.FillRectangle(accentBrush, contentLeft, bounds.Top + 8, Math.Min(42, contentWidth), 3);

                var labelRect = new RectangleF(contentLeft, labelTop, contentWidth, 12);
                var valueRect = new RectangleF(contentLeft, valueTop, contentWidth, compact ? 16 : 18);
                var detailRect = new RectangleF(contentLeft, detailTop, contentWidth, 14);
                var format = new StringFormat
                {
                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                };

                graphics.DrawString(vital.Label, systemsLabelFont, labelBrush, labelRect, format);
                graphics.DrawString(vital.Value, valueFont, valueBrush, valueRect, format);
                graphics.DrawString(vital.Detail, detailFont, detailBrush, detailRect, format);

                if (drawDivider)
                    graphics.DrawLine(dividerPen, bounds.Right, bounds.Top + 12, bounds.Right, bounds.Bottom - 12);

                if (drawBottomDivider)
                    graphics.DrawLine(dividerPen, bounds.Left + 12, bounds.Bottom, bounds.Right - 12, bounds.Bottom);
            }
        }

        private void DrawTapeShell(Graphics graphics, Rectangle bounds)
        {
            ModernUiPainter.FillRoundedRectangle(graphics, tapeSurface, bounds, 14);
            ModernUiPainter.DrawRoundedRectangle(graphics, tapeBorder, 1.2f, bounds, 14);
        }

        private void DrawTapeValueBox(Graphics graphics, Rectangle bounds, string value, bool rightAligned)
        {
            ModernUiPainter.FillRoundedRectangle(graphics, centerBoxSurface, bounds, 10);
            ModernUiPainter.DrawRoundedRectangle(graphics, centerBoxBorder, 1f, bounds, 10);

            using (var valueBrush = new SolidBrush(textPrimary))
            {
                SizeF valueSize = graphics.MeasureString(value, tapeValueFont);
                float x = rightAligned ? bounds.Right - valueSize.Width - 10 : bounds.Left + 10;
                graphics.DrawString(value, tapeValueFont, valueBrush, x, bounds.Top + (bounds.Height - valueSize.Height) / 2f - 1f);
            }
        }

        private void DrawTelemetryBadge(Graphics graphics, Rectangle bounds, string prefix, float value, Color accent)
        {
            ModernUiPainter.FillRoundedRectangle(graphics, Color.FromArgb(12, 16, 24), bounds, 8);
            ModernUiPainter.DrawRoundedRectangle(graphics, ModernUiPainter.WithAlpha(accent, 120), 1f, bounds, 8);

            using (var brush = new SolidBrush(textPrimary))
            {
                string label = string.Format("{0}:{1:0.0}{2}", prefix, value, GetSpeedUnit());
                graphics.DrawString(label, tapeBadgeFont, brush,
                    new RectangleF(bounds.Left + 5, bounds.Top + 2, bounds.Width - 10, bounds.Height - 4));
            }
        }

        private string FormatWaypointFooter()
        {
            string prefix = WaypointNumber > 0 ? string.Format("WP {0} <", WaypointNumber) : "WP <";
            return string.Format("{0} {1}", prefix, FormatDistanceValue(DistanceToWaypoint));
        }

        private string FormatFuelUsedValue()
        {
            return Connected && EfiFuelConsumed > 0 ? $"{EfiFuelConsumed:0} cc" : "--";
        }

        private string FormatFuelRemainingValue()
        {
            return Connected && EfiFuelRemaining >= 0 ? $"{EfiFuelRemaining:0} cc" : "--";
        }

        private string FormatRpmValue()
        {
            return Connected && EfiRpm > 0 ? $"{EfiRpm:0}" : "--";
        }

        private string FormatNavigationValue()
        {
            if (!Connected)
                return "--";

            if (WaypointNumber > 0)
                return string.Format("WP {0}  {1}", WaypointNumber, FormatDistanceValue(DistanceToWaypoint));

            return FormatDistanceValue(DistanceToWaypoint);
        }

        private string FormatSpeedValue(float speed)
        {
            return string.Format("{0:0.0}{1}", speed, GetSpeedUnit());
        }

        private string FormatVerticalSpeedValue()
        {
            return string.Format("{0:+0.0;-0.0;0.0}{1}", VerticalSpeed, GetSpeedUnit());
        }

        private static string FormatDistanceValue(float distance)
        {
            string unit = GetDistUnit();
            float absolute = Math.Abs(distance);

            if (unit == "m" && absolute >= 1000f)
                return string.Format("{0:0.0} km", distance / 1000f);

            if (unit == "ft" && absolute >= 5280f)
                return string.Format("{0:0.0} mi", distance / 5280f);

            return string.Format("{0:0}{1}", distance, unit);
        }

        private static string GetHeadingLabel(int heading)
        {
            int normalized = ((heading % 360) + 360) % 360;
            switch (normalized)
            {
                case 0:
                    return "N";
                case 45:
                    return "NE";
                case 90:
                    return "E";
                case 135:
                    return "SE";
                case 180:
                    return "S";
                case 225:
                    return "SW";
                case 270:
                    return "W";
                case 315:
                    return "NW";
                default:
                    return normalized.ToString("000");
            }
        }

        private static PointF RollArcPoint(PointF center, float radius, float bankDegrees)
        {
            double radians = Math.PI * (bankDegrees - 90.0) / 180.0;
            return new PointF(
                center.X + (float)(Math.Cos(radians) * radius),
                center.Y + (float)(Math.Sin(radians) * radius));
        }

        private static float NormalizeHeading(float heading)
        {
            while (heading < 0)
                heading += 360f;
            while (heading >= 360f)
                heading -= 360f;
            return heading;
        }

        private static float NormalizeRelativeAngle(float angle)
        {
            while (angle > 180f)
                angle -= 360f;
            while (angle < -180f)
                angle += 360f;
            return angle;
        }

        private static float GetSpeedRange()
        {
            switch (GetSpeedUnit())
            {
                case "fps":
                case "kts":
                case "mph":
                    return 50f;
                case "kph":
                    return 80f;
                default:
                    return 26f;
            }
        }

        private static float GetAltitudeRange()
        {
            return GetAltUnit() == "ft" ? 100f : 26f;
        }

        private static float GetVerticalSpeedRange()
        {
            switch (GetSpeedUnit())
            {
                case "fps":
                case "kts":
                case "mph":
                    return 24f;
                case "kph":
                    return 40f;
                default:
                    return 12f;
            }
        }

        private static string GetSpeedUnit()
        {
            return string.IsNullOrWhiteSpace(CurrentState.SpeedUnit) ? "m/s" : CurrentState.SpeedUnit;
        }

        private static string GetAltUnit()
        {
            return string.IsNullOrWhiteSpace(CurrentState.AltUnit) ? "m" : CurrentState.AltUnit;
        }

        private static string GetDistUnit()
        {
            return string.IsNullOrWhiteSpace(CurrentState.DistanceUnit) ? "m" : CurrentState.DistanceUnit;
        }

        private void ClearVitalHitAreas()
        {
            for (int i = 0; i < coreVitalHitAreas.Length; i++)
                coreVitalHitAreas[i] = Rectangle.Empty;

            for (int i = 0; i < systemVitalHitAreas.Length; i++)
                systemVitalHitAreas[i] = Rectangle.Empty;

            for (int i = 0; i < edgeVitalHitAreas.Length; i++)
                edgeVitalHitAreas[i] = Rectangle.Empty;
        }

        private HudVitalEditRequestedEventArgs HitTestVital(Point location)
        {
            for (int i = 0; i < edgeVitalHitAreas.Length; i++)
            {
                if (edgeVitalHitAreas[i].Contains(location))
                    return new HudVitalEditRequestedEventArgs(HudVitalTargetType.Edge, i);
            }

            for (int i = 0; i < coreVitalHitAreas.Length; i++)
            {
                if (coreVitalHitAreas[i].Contains(location))
                    return new HudVitalEditRequestedEventArgs(HudVitalTargetType.Core, i);
            }

            for (int i = 0; i < systemVitalHitAreas.Length; i++)
            {
                if (systemVitalHitAreas[i].Contains(location))
                    return new HudVitalEditRequestedEventArgs(HudVitalTargetType.System, i);
            }

            return null;
        }

        private static VitalDisplayOverride GetVitalOverride(VitalDisplayOverride[] overrides, int index)
        {
            if (overrides == null || index < 0 || index >= overrides.Length)
                return null;

            return overrides[index];
        }

        private static int ClampInt(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static float Clamp(float value, float min, float max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                horizonPen?.Dispose();
                majorLadderPen?.Dispose();
                minorLadderPen?.Dispose();
                tapeTickPen?.Dispose();
                headingLabelFont?.Dispose();
                headingValueFont?.Dispose();
                bankLabelFont?.Dispose();
                tapeScaleFont?.Dispose();
                tapeValueFont?.Dispose();
                tapeBadgeFont?.Dispose();
                footerFont?.Dispose();
                batteryFont?.Dispose();
                systemsLabelFont?.Dispose();
                systemsValueFont?.Dispose();
                systemsValueCompactFont?.Dispose();
                systemsDetailFont?.Dispose();
                systemsDetailCompactFont?.Dispose();
                coreVitalLabelFont?.Dispose();
                coreVitalValueFont?.Dispose();
                overlayLabelFont?.Dispose();
                overlayValueFont?.Dispose();
                overlayDetailFont?.Dispose();
            }

            base.Dispose(disposing);
        }

        private readonly struct SystemVital
        {
            public SystemVital(string label, string value, string detail, Color accent)
            {
                Label = label;
                Value = value;
                Detail = detail;
                Accent = accent;
            }

            public string Label { get; }
            public string Value { get; }
            public string Detail { get; }
            public Color Accent { get; }
        }
    }
}
