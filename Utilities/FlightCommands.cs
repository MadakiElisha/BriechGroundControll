using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using log4net;
using MissionPlanner.ArduPilot;
using MissionPlanner.Controls;
using Family = MissionPlanner.ArduPilot.FlightCommandProfile.Family;
using TakeoffMethod = MissionPlanner.ArduPilot.FlightCommandProfile.TakeoffMethod;

namespace MissionPlanner.Utilities
{
    /// <summary>
    /// Flight commands shared by the header strip, Modern Fly and classic Fly so they behave the same way:
    /// the command is checked against the vehicle type, the result is confirmed from telemetry rather than assumed,
    /// and destructive actions in the air need a deliberate press-and-hold.
    /// </summary>
    public static class FlightCommands
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(FlightCommands));

        private const int ModeConfirmTimeoutMs = 3000;
        private const int ArmConfirmTimeoutMs = 3000;

        public static Family CurrentFamily()
        {
            var mav = MainV2.comPort?.MAV;
            if (mav == null)
                return Family.Unknown;
            bool q = false;
            try
            {
                q = mav.param.ContainsKey("Q_ENABLE") && mav.param["Q_ENABLE"].Value > 0.5;
            }
            catch
            {
            }

            return FlightCommandProfile.Classify(mav.aptype, q);
        }

        public static bool CurrentlyAirborne()
        {
            var cs = MainV2.comPort?.MAV?.cs;
            if (cs == null)
                return false;
            double heightM = CurrentState.multiplieralt != 0 ? cs.alt / CurrentState.multiplieralt : cs.alt;
            double speedMs = CurrentState.multiplierspeed != 0 ? cs.groundspeed / CurrentState.multiplierspeed : cs.groundspeed;
            return FlightCommandProfile.LooksAirborne(cs.armed, cs.landed_state, heightM, speedMs);
        }

        private static bool Connected()
        {
            return MainV2.comPort?.BaseStream != null && MainV2.comPort.BaseStream.IsOpen;
        }

        private static bool ModeIs(string mode)
        {
            var current = MainV2.comPort?.MAV?.cs?.mode;
            return current != null && string.Equals(current.Trim(), mode.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public enum ModeResult
        {
            /// <summary>the vehicle reported the new mode in its heartbeat</summary>
            Confirmed,

            /// <summary>this vehicle has no such mode (e.g. LAND on a plane); nothing was sent</summary>
            NotAvailable,

            /// <summary>sent, but the heartbeat never showed the new mode (refused, or link lost)</summary>
            NotConfirmed,
        }

        /// <summary>
        /// Send a mode change and wait for the vehicle's heartbeat to show it. Safe to call from a background thread.
        /// </summary>
        public static ModeResult SetModeAndConfirm(string mode, int timeoutMs = ModeConfirmTimeoutMs,
            CancellationToken cancel = default(CancellationToken))
        {
            var port = MainV2.comPort;
            var mav = port?.MAV;
            if (mav == null || string.IsNullOrWhiteSpace(mode))
                return ModeResult.NotAvailable;

            if (ModeIs(mode))
                return ModeResult.Confirmed;

            var check = new MAVLink.mavlink_set_mode_t();
            if (!port.translateMode(mav.sysid, mav.compid, mode, ref check))
                return ModeResult.NotAvailable;

            log.Info("SetModeAndConfirm " + mode);
            port.setMode(mav.sysid, mav.compid, mode);

            var sw = Stopwatch.StartNew();
            int resendAt = 1000;
            while (sw.ElapsedMilliseconds < timeoutMs && !cancel.IsCancellationRequested)
            {
                if (ModeIs(mode))
                    return ModeResult.Confirmed;
                if (sw.ElapsedMilliseconds > resendAt)
                {
                    // one resend covers a dropped packet on a lossy link
                    port.setMode(mav.sysid, mav.compid, mode);
                    resendAt = int.MaxValue;
                }

                Thread.Sleep(100);
            }

            return ModeIs(mode) ? ModeResult.Confirmed : ModeResult.NotConfirmed;
        }

        public static Task<ModeResult> SetModeAndConfirmAsync(string mode, int timeoutMs = ModeConfirmTimeoutMs)
        {
            return Task.Run(() => SetModeAndConfirm(mode, timeoutMs));
        }

        /// <summary>
        /// UI helper for mode buttons: changes mode, then tells the pilot if the vehicle did not follow.
        /// Returns true when the vehicle confirmed the mode.
        /// </summary>
        public static async Task<bool> SetModeInteractiveAsync(IWin32Window owner, string mode)
        {
            if (!Connected())
                return false;

            var result = await SetModeAndConfirmAsync(mode).ConfigureAwait(true);
            switch (result)
            {
                case ModeResult.Confirmed:
                    return true;
                case ModeResult.NotAvailable:
                    System.CustomMessageBox.Show($"{mode} is not a mode on this vehicle ({CurrentFamily()}). Nothing was sent.",
                        "Mode not available");
                    return false;
                default:
                    var now = MainV2.comPort?.MAV?.cs?.mode ?? "unknown";
                    System.CustomMessageBox.Show(
                        $"The vehicle did not switch to {mode} (still {now}).\nCheck the Messages tab for the reason.",
                        "Mode change not confirmed");
                    return false;
            }
        }

        private static int CaptureStatusText(StringBuilder sb)
        {
            return MainV2.comPort.SubscribeToPacketType(MAVLink.MAVLINK_MSG_ID.STATUSTEXT, message =>
            {
                lock (sb)
                    sb.AppendLine(Encoding.ASCII.GetString(((MAVLink.mavlink_statustext_t)message.data).text)
                        .TrimEnd('\0'));
                return true;
            }, MainV2.comPort.sysidcurrent, (byte)MainV2.comPort.compidcurrent);
        }

        private static bool WaitArmed(bool armed, int timeoutMs, CancellationToken cancel = default(CancellationToken))
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs && !cancel.IsCancellationRequested)
            {
                if (MainV2.comPort?.MAV?.cs?.armed == armed)
                    return true;
                Thread.Sleep(100);
            }

            return MainV2.comPort?.MAV?.cs?.armed == armed;
        }

        /// <summary>
        /// Arm or disarm from any arm button. Arming is unchanged (confirmation is the caller's choice).
        /// Disarming always asks; in the air it goes straight to a press-and-hold FORCE disarm because ArduPilot
        /// refuses a normal disarm while flying, and that refusal used to be followed by a one-click
        /// "Force disarm?" Yes/No that could drop the aircraft.
        /// </summary>
        public static void ArmOrDisarmInteractive(IWin32Window owner)
        {
            if (!Connected())
                return;

            bool isArmed = MainV2.comPort.MAV.cs.armed;
            string action = isArmed ? "Disarm" : "Arm";

            if (isArmed)
            {
                if (CurrentlyAirborne())
                {
                    if (!HoldToConfirm.Ask(owner, "Vehicle is flying",
                            "The vehicle looks airborne. Disarming stops the motors and it will fall.\n\n" +
                            "Only do this in an emergency (fly-away, uncontrolled spin). For anything else use RTL or Land.",
                            "Hold to FORCE DISARM", 2000))
                        return;

                    SendArm(false, true, "Force disarm");
                    return;
                }

                if (System.CustomMessageBox.Show("Disarm the vehicle?", "Disarm",
                        System.CustomMessageBox.MessageBoxButtons.YesNo, System.CustomMessageBox.MessageBoxIcon.Question,
                        "Disarm", "Cancel") != System.CustomMessageBox.DialogResult.Yes)
                    return;
            }

            var sb = new StringBuilder();
            var sub = CaptureStatusText(sb);
            bool ok;
            try
            {
                ok = MainV2.comPort.doARM(!isArmed);
            }
            finally
            {
                MainV2.comPort.UnSubscribeToPacketType(sub);
            }

            if (ok)
                return;

            string reasons;
            lock (sb)
                reasons = sb.ToString().Trim();

            // re-check: the vehicle may have taken off between the click and now
            if (isArmed && CurrentlyAirborne())
            {
                System.CustomMessageBox.Show("Disarm refused by the vehicle.\n" + reasons, Strings.ERROR);
                return;
            }

            // Yes/No texts are swapped on purpose so the first (default) button is Cancel and the risky
            // choice needs a deliberate click on the second button.
            if (System.CustomMessageBox.Show(
                    action + " failed.\n" + reasons + "\n\nForce " + action +
                    " bypasses the vehicle's safety checks, which can lead to a crash and serious injury.",
                    Strings.ERROR, System.CustomMessageBox.MessageBoxButtons.YesNo,
                    System.CustomMessageBox.MessageBoxIcon.Exclamation, "Cancel", "Force " + action) !=
                System.CustomMessageBox.DialogResult.No)
                return;

            SendArm(!isArmed, true, "Force " + action.ToLowerInvariant());
        }

        private static void SendArm(bool arm, bool force, string what)
        {
            try
            {
                if (!MainV2.comPort.doARM(arm, force))
                    System.CustomMessageBox.Show(what + ": " + Strings.ErrorRejectedByMAV, Strings.ERROR);
            }
            catch (Exception ex)
            {
                System.CustomMessageBox.Show(what + " failed: " + ex.Message, Strings.ERROR);
            }
        }

        /// <summary>
        /// Takeoff for the connected vehicle type. Copter/heli/quadplane: GUIDED, arm (checked), NAV_TAKEOFF (checked).
        /// Fixed-wing: TKOFF_ALT, TAKEOFF mode, arm. Rover/boat/sub: refused.
        /// Returns null on success, otherwise the reason it stopped. Runs on a background thread.
        /// </summary>
        public static string Takeoff(double altitudeM, CancellationToken cancel = default(CancellationToken))
        {
            if (!Connected())
                return "Not connected.";
            if (altitudeM <= 0)
                return "Takeoff altitude must be above zero.";

            var family = CurrentFamily();
            var method = FlightCommandProfile.Takeoff(family);
            var port = MainV2.comPort;
            var mav = port.MAV;

            switch (method)
            {
                case TakeoffMethod.GuidedNavTakeoff:
                {
                    var mode = SetModeAndConfirm("GUIDED", ModeConfirmTimeoutMs, cancel);
                    if (mode != ModeResult.Confirmed)
                        return "The vehicle did not enter GUIDED (" + mode + ").";

                    var err = ArmChecked(cancel);
                    if (err != null)
                        return err;

                    if (!port.doCommand(mav.sysid, mav.compid, MAVLink.MAV_CMD.TAKEOFF, 0, 0, 0, 0, 0, 0,
                            (float)altitudeM))
                        return family == Family.QuadPlane
                            ? "Takeoff refused. A quadplane needs GUIDED, armed, and Q_GUIDED_MODE/VTOL takeoff allowed."
                            : "Takeoff refused by the vehicle.";
                    return null;
                }
                case TakeoffMethod.PlaneTakeoffMode:
                {
                    if (mav.param.ContainsKey("TKOFF_ALT"))
                        port.setParam(mav.sysid, mav.compid, "TKOFF_ALT", altitudeM);

                    var mode = SetModeAndConfirm("TAKEOFF", ModeConfirmTimeoutMs, cancel);
                    if (mode != ModeResult.Confirmed)
                        return "The aircraft did not enter TAKEOFF mode (" + mode + ").";

                    return ArmChecked(cancel);
                }
                default:
                    return $"There is no takeoff command for a {family}.";
            }
        }

        /// <summary>UI wrapper for <see cref="Takeoff"/>: asks before arming, runs off the UI thread, reports failure.</summary>
        public static async Task<bool> TakeoffInteractiveAsync(IWin32Window owner, double altitudeM)
        {
            if (!Connected())
                return false;

            var family = CurrentFamily();
            if (FlightCommandProfile.Takeoff(family) == TakeoffMethod.None)
            {
                System.CustomMessageBox.Show($"There is no takeoff command for a {family}.", "Takeoff");
                return false;
            }

            if (!MainV2.comPort.MAV.cs.armed)
            {
                var how = FlightCommandProfile.Takeoff(family) == TakeoffMethod.PlaneTakeoffMode
                    ? "switch to TAKEOFF mode and ARM. The aircraft will launch"
                    : "switch to GUIDED, ARM and climb";
                if (System.CustomMessageBox.Show(
                        $"The vehicle is disarmed. This will {how} to {altitudeM:0.#} m.",
                        "Takeoff", System.CustomMessageBox.MessageBoxButtons.YesNo,
                        System.CustomMessageBox.MessageBoxIcon.Warning, "Arm and take off", "Cancel") !=
                    System.CustomMessageBox.DialogResult.Yes)
                    return false;
            }

            var err = await Task.Run(() => Takeoff(altitudeM)).ConfigureAwait(true);
            if (err == null)
                return true;
            System.CustomMessageBox.Show(err, "Takeoff");
            return false;
        }

        private static string ArmChecked(CancellationToken cancel)
        {
            if (MainV2.comPort.MAV.cs.armed)
                return null;

            var sb = new StringBuilder();
            var sub = CaptureStatusText(sb);
            try
            {
                if (!MainV2.comPort.doARM(true) || !WaitArmed(true, ArmConfirmTimeoutMs, cancel))
                {
                    lock (sb)
                        return "Arming refused.\n" + sb.ToString().Trim();
                }
            }
            finally
            {
                MainV2.comPort.UnSubscribeToPacketType(sub);
            }

            return null;
        }

        /// <summary>
        /// Resume a mission from a waypoint: rewrites the mission without the already-flown legs, then, if the
        /// vehicle is on the ground and can take off by command, takes off to the resume waypoint's altitude, then AUTO.
        /// Runs in a cancellable progress dialog; nothing is retried in a loop.
        /// Returns true if the mission was rewritten (the caller should refresh the Plan view).
        /// </summary>
        public static bool ResumeMissionInteractive(IWin32Window owner)
        {
            if (!Connected())
                return false;

            string lastwp = MainV2.comPort.MAV.cs.lastautowp.ToString();
            if (lastwp == "-1")
                lastwp = "1";
            if (InputBox.Show("Resume mission", "Resume the mission at waypoint #", ref lastwp) != DialogResult.OK)
                return false;
            if (!int.TryParse(lastwp, out var resumeAt) || resumeAt < 1)
            {
                System.CustomMessageBox.Show("Enter a waypoint number of 1 or more.", "Resume mission");
                return false;
            }

            var family = CurrentFamily();
            bool airborne = CurrentlyAirborne();
            bool takeoffFirst = !airborne && FlightCommandProfile.Takeoff(family) == TakeoffMethod.GuidedNavTakeoff;

            var plan = takeoffFirst
                ? $"This rewrites the mission from waypoint {resumeAt}, then ARMS the vehicle, takes off in GUIDED and switches to AUTO."
                : airborne
                    ? $"This rewrites the mission from waypoint {resumeAt} and switches to AUTO."
                    : $"This rewrites the mission from waypoint {resumeAt} and switches to AUTO. Arm the aircraft yourself when ready.";

            if (System.CustomMessageBox.Show(plan, "Resume mission", System.CustomMessageBox.MessageBoxButtons.YesNo,
                    System.CustomMessageBox.MessageBoxIcon.Warning, "Resume", "Cancel") !=
                System.CustomMessageBox.DialogResult.Yes)
                return false;

            bool rewritten = false;
            var dlg = new ProgressReporterDialogue
            {
                StartPosition = FormStartPosition.CenterScreen,
                Text = "Resume mission"
            };
            dlg.DoWork += sender =>
            {
                var args = sender.doWorkArgs;
                var cts = new CancellationTokenSource();
                args.CancelRequestChanged += (s, e) =>
                {
                    if (args.CancelRequested)
                        cts.Cancel();
                };

                var err = ResumeWork(sender, resumeAt, takeoffFirst, ref rewritten, cts.Token);
                if (cts.IsCancellationRequested)
                {
                    args.CancelAcknowledged = true;
                    return;
                }

                if (err != null)
                    args.ErrorMessage = err;
            };
            ThemeManager.ApplyThemeTo(dlg);
            dlg.UpdateProgressAndStatus(-1, "Reading mission");
            dlg.RunBackgroundOperationAsync();
            dlg.Dispose();
            return rewritten;
        }

        private static string ResumeWork(IProgressReporterDialogue ui, int resumeAt, bool takeoffFirst,
            ref bool rewritten, CancellationToken cancel)
        {
            var port = MainV2.comPort;
            var mav = port.MAV;

            ui.UpdateProgressAndStatus(5, "Reading mission");
            var count = port.getWPCount(mav.sysid, mav.compid);
            if (resumeAt >= count)
                return $"The mission has {count - 1} items; waypoint {resumeAt} does not exist.";

            var items = new List<Locationwp>();
            for (ushort a = 0; a < count; a++)
            {
                if (cancel.IsCancellationRequested)
                    return null;
                items.Add(port.getWP(mav.sysid, mav.compid, a));
                ui.UpdateProgressAndStatus(5 + 30 * a / Math.Max(1, (int)count), $"Reading item {a}");
            }

            var ids = items.ConvertAll(i => i.id);
            var keep = FlightCommandProfile.ResumeMissionItems(ids, resumeAt);
            var target = items[resumeAt];

            if (cancel.IsCancellationRequested)
                return null;

            ui.UpdateProgressAndStatus(40, "Writing shortened mission");
            port.setWPTotal((ushort)keep.Count);
            ushort seq = 0;
            foreach (var index in keep)
            {
                var loc = items[index];
                var ans = port.setWP(loc, seq, (MAVLink.MAV_FRAME)loc.frame);
                if (ans != MAVLink.MAV_MISSION_RESULT.MAV_MISSION_ACCEPTED)
                    return $"Upload failed at item {seq} ({(MAVLink.MAV_CMD)loc.id}): {ans}";
                seq++;
            }

            port.setWPACK();
            rewritten = true;
            port.setWPCurrent(mav.sysid, mav.compid, 1);

            if (takeoffFirst)
            {
                ui.UpdateProgressAndStatus(60, "Taking off");
                var climbTo = Math.Max(2.0, target.alt);
                var err = Takeoff(climbTo, cancel);
                if (err != null)
                    return err;

                var sw = Stopwatch.StartNew();
                var heightM = new Func<double>(() =>
                    CurrentState.multiplieralt != 0 ? mav.cs.alt / CurrentState.multiplieralt : mav.cs.alt);
                while (heightM() < climbTo - 2)
                {
                    if (cancel.IsCancellationRequested)
                        return null; // stays in GUIDED, holding
                    if (!mav.cs.armed)
                        return "The vehicle disarmed during the climb.";
                    if (sw.Elapsed > TimeSpan.FromSeconds(90))
                        return $"Still climbing after 90 s ({heightM():0} of {climbTo:0} m). The vehicle is left in GUIDED.";
                    ui.UpdateProgressAndStatus(60 + (int)(30 * Math.Max(0, heightM()) / climbTo),
                        $"Climbing {heightM():0} / {climbTo:0} m");
                    Thread.Sleep(250);
                }
            }

            ui.UpdateProgressAndStatus(95, "Switching to AUTO");
            var mode = SetModeAndConfirm("AUTO", 5000, cancel);
            if (mode != ModeResult.Confirmed && !cancel.IsCancellationRequested)
                return "The mission was written, but the vehicle did not enter AUTO (" + mode + ").";

            return null;
        }

        /// <summary>
        /// Press-and-hold confirmation for actions that cannot be undone in flight. Enter and Escape cancel;
        /// the confirm control is never a default button, so a stray key press can't trigger it.
        /// </summary>
        public sealed class HoldToConfirm : Form
        {
            private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 30 };
            private readonly Stopwatch held = new Stopwatch();
            private readonly int holdMs;
            private readonly ProgressBar bar;

            public static bool Ask(IWin32Window owner, string title, string message, string holdText, int holdMs)
            {
                using (var f = new HoldToConfirm(title, message, holdText, holdMs))
                {
                    ThemeManager.ApplyThemeTo(f);
                    return f.ShowDialog(owner) == DialogResult.OK;
                }
            }

            private HoldToConfirm(string title, string message, string holdText, int holdMs)
            {
                this.holdMs = Math.Max(500, holdMs);
                Text = title;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MinimizeBox = false;
                MaximizeBox = false;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.CenterParent;
                AutoScaleMode = AutoScaleMode.Dpi;
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                Padding = new Padding(16);

                var layout = new TableLayoutPanel
                {
                    ColumnCount = 1,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Dock = DockStyle.Fill
                };

                var label = new Label
                {
                    Text = message,
                    AutoSize = true,
                    MaximumSize = new Size(440, 0),
                    Margin = new Padding(0, 0, 0, 12)
                };

                var hold = new Button
                {
                    Text = holdText,
                    AutoSize = false,
                    Size = new Size(440, 48),
                    BackColor = Color.FromArgb(0x8B, 0x1A, 0x10),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
                    TabStop = false
                };

                bar = new ProgressBar
                {
                    Size = new Size(440, 8),
                    Maximum = 1000,
                    Margin = new Padding(3, 4, 3, 12)
                };

                var cancel = new Button
                {
                    Text = "Cancel",
                    DialogResult = DialogResult.Cancel,
                    Size = new Size(120, 36),
                    Anchor = AnchorStyles.Right
                };

                hold.MouseDown += (s, e) => Start();
                hold.MouseUp += (s, e) => Stop();
                hold.MouseLeave += (s, e) => Stop();
                timer.Tick += (s, e) => Tick();

                layout.Controls.Add(label);
                layout.Controls.Add(hold);
                layout.Controls.Add(bar);
                layout.Controls.Add(cancel);
                Controls.Add(layout);

                AcceptButton = cancel;
                CancelButton = cancel;
                ActiveControl = cancel;
            }

            private void Start()
            {
                held.Restart();
                timer.Start();
            }

            private void Stop()
            {
                timer.Stop();
                held.Reset();
                bar.Value = 0;
            }

            private void Tick()
            {
                var v = (int)Math.Min(1000, held.ElapsedMilliseconds * 1000 / holdMs);
                bar.Value = v;
                if (v >= 1000)
                {
                    timer.Stop();
                    DialogResult = DialogResult.OK;
                    Close();
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    timer.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
