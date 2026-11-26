using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using log4net;
using MissionPlanner.Controls;
using MissionPlanner.Utilities;
using T = MissionPlanner.Utilities.DesignTokens;

namespace MissionPlanner.GCSViews
{
    /// <summary>
    /// The 3D view for the centre of the Fly screen: the vehicle's model (picked from the airframe, quadplanes
    /// included), its flight trail and the mission over 3D terrain. The OpenGL view is only created the first
    /// time the pilot switches to 3D, and it stops drawing whenever it is hidden, so the 2D map costs nothing extra.
    /// Until there is a valid position it shows a plain message in its place.
    /// </summary>
    [PreventTheming]
    public class Fly3DPanel : Panel
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(Fly3DPanel));

        private Map3D view;
        private bool triedCreate;
        private string createError;
        private bool running;
        private readonly Label message;

        public Fly3DPanel()
        {
            DoubleBuffered = true;
            message = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = T.Body,
                Text = "Connect a vehicle to see it in 3D"
            };
            Controls.Add(message);
            ApplyTokens();
        }

        /// <summary>True when the OpenGL view exists and is drawing (for tests).</summary>
        public bool ViewAvailable => view != null;

        public void ApplyTokens()
        {
            var p = T.Current;
            BackColor = p.Surface;
            message.BackColor = p.Surface;
            message.ForeColor = p.TextMuted;
        }

        private void EnsureView()
        {
            if (triedCreate)
                return;
            triedCreate = true;
            try
            {
                view = new Map3D { Dock = DockStyle.Fill, BackColor = Color.Black, Name = "flyView3D", Visible = false };
                Controls.Add(view);
                view.BringToFront();
            }
            catch (Exception ex)
            {
                // no OpenGL on this PC (or a remote desktop session): say so instead of failing the screen
                view = null;
                createError = ex.Message;
                log.Error("Fly screen 3D view failed to start", ex);
            }
        }

        /// <summary>Start drawing; called when the 3D view is shown on an active Fly screen.</summary>
        public void Start()
        {
            EnsureView();
            if (view == null || running)
                return;
            try
            {
                view.Activate();
                running = true;
            }
            catch (Exception ex)
            {
                log.Debug(ex);
            }
        }

        /// <summary>Stop drawing and free tile textures; called when hidden or when the Fly screen is left.</summary>
        public void Stop()
        {
            if (view == null || !running)
                return;
            running = false;
            try
            {
                view.Deactivate();
            }
            catch (Exception ex)
            {
                log.Debug(ex);
            }
        }

        public void UpdateTelemetry(CurrentState cs, bool connected)
        {
            bool hasPosition = cs != null && Math.Abs(cs.lat) > 1e-6 && Math.Abs(cs.lng) > 1e-6
                               && Math.Abs(cs.lat) <= 90 && Math.Abs(cs.lng) <= 180;
            bool live = view != null && connected && hasPosition;

            if (view != null && view.Unsupported)
            {
                // the driver can't run the 3D view (no GPU driver, remote desktop): say so, and stop drawing it
                message.Text = "3D view needs a graphics driver with OpenGL 2.0 or newer.\r\nThis computer offers OpenGL " +
                               view.GlVersion + ". Install the graphics driver from the PC maker.\r\nThe 2D map still works.";
                message.Visible = true;
                view.Visible = false;
                return;
            }

            message.Text = view == null && triedCreate
                ? "3D view isn't available on this computer (no OpenGL)." +
                  (string.IsNullOrEmpty(createError) ? "" : "\r\n" + createError) + "\r\nThe 2D map still works."
                : !connected
                    ? "Connect a vehicle to see it in 3D"
                    : !hasPosition
                        ? "Waiting for a GPS position"
                        : "";
            message.Visible = !live;
            if (view == null)
                return;
            view.Visible = live;
            if (!live)
                return;

            try
            {
                view.rpy = new Vector3(cs.roll, cs.pitch, cs.yaw);
                double terrainAlt = srtm.getAltitude(cs.lat, cs.lng).alt;
                double relativeAlt = CurrentState.multiplieralt == 0 ? cs.alt : cs.alt / CurrentState.multiplieralt;
                view.LocationCenter = new PointLatLngAlt(cs.lat, cs.lng, terrainAlt + relativeAlt, "here");
                view.Velocity = new Vector3(cs.vx, cs.vy, cs.vz);

                var waypoints = MainV2.comPort?.MAV?.wps?.Values;
                view.WPs = waypoints != null ? waypoints.Select(item => (Locationwp)item).ToList() : new List<Locationwp>();
            }
            catch (Exception ex)
            {
                log.Debug(ex);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Stop();
            base.Dispose(disposing);
        }
    }
}
