using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MissionPlanner;
using MissionPlanner.Comms;
using MissionPlanner.GCSViews;
using MissionPlanner.Utilities;
using Timer = System.Windows.Forms.Timer;

// Windows UI check, run by CI after the build with a quadplane SITL listening on BRIECH_SITL_TCP.
// It starts the real app, connects it to the simulator, opens Fly screen tools the way a pilot does and
// reports what it finds (UICHECK lines), with screenshots in BRIECH_SCREENSHOT_DIR. It never fails the build:
// the lines and screenshots are the evidence for review.
static class UiCheck
{
    static Timer timer;
    static string shots;
    static int problems;

    static DateTime lastSay = DateTime.Now;

    static void Say(string text)
    {
        lastSay = DateTime.Now;
        Console.WriteLine("UICHECK " + text);
        Console.Out.Flush();
    }

    static void Problem(string text)
    {
        problems++;
        Say("PROBLEM " + text);
    }

    static void Pause(int ms)
    {
        var until = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < until)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }

    static void Shot(string name)
    {
        Pause(800);
        var form = MainV2.instance;
        using (var bmp = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height)))
        {
            // the form itself, not the screen: the build machine's desktop has other windows on top
            form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));

            bmp.Save(Path.Combine(shots, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }

        Say("screenshot " + name);
    }

    /// <summary>Paint a control into a bitmap, as Windows would on screen, and report any exception.</summary>
    static bool PaintCheck(Control c, string what)
    {
        try
        {
            using (var bmp = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height)))
                c.DrawToBitmap(bmp, new Rectangle(Point.Empty, c.Size));
            // every visible control on its own: an exception in one control's paint can be hidden by its parent's
            foreach (var child in Descendants(c).Where(x => x.Visible && x.Width > 0 && x.Height > 0))
            {
                try
                {
                    using (var bmp = new Bitmap(child.Width, child.Height))
                    using (var g = Graphics.FromImage(bmp))
                    {
                        var paint = typeof(Control).GetMethod("OnPaint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        paint.Invoke(child, new object[] { new PaintEventArgs(g, new Rectangle(Point.Empty, child.Size)) });
                    }
                }
                catch (System.Reflection.TargetInvocationException ex)
                {
                    var inner = ex.InnerException ?? ex;
                    Problem($"{what}: {child.GetType().Name} '{child.Name}' failed to paint: {inner.GetType().Name}: {inner.Message}");
                    Say(inner.ToString().Replace("\r", "").Replace("\n", " | "));
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Problem($"{what} failed to paint: {ex.GetType().Name}: {ex.Message}");
            Say(ex.ToString().Replace("\r", "").Replace("\n", " | "));
            return false;
        }
    }

    static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var g in Descendants(child))
                yield return g;
        }
    }

    static void OnUiException(object sender, ThreadExceptionEventArgs e)
    {
        Problem("unhandled UI exception: " + e.Exception.GetType().Name + ": " + e.Exception.Message);
        Say(e.Exception.ToString().Replace("\r", "").Replace("\n", " | "));
        Console.Out.Flush();
        Environment.Exit(problems == 0 ? 1 : problems);
    }

    static void Run()
    {
        Application.ThreadException += OnUiException;
        Say("app started, connecting");
        var target = Environment.GetEnvironmentVariable("BRIECH_SITL_TCP") ?? "127.0.0.1:5760";
        var i = target.LastIndexOf(':');
        MainV2.instance.WindowState = FormWindowState.Normal;
        MainV2.instance.Bounds = new Rectangle(0, 0, 1280, 800);
        // be the active window, as the app is for a pilot (the build machine's console is otherwise in front)
        MainV2.instance.TopMost = true;
        SetForegroundWindow(MainV2.instance.Handle);
        MainV2.instance.Activate();

        // connect like a pilot picking TCP, without the host/port prompts
        var connected = false;
        for (int attempt = 0; attempt < 20 && !connected; attempt++)
        {
            var port = MainV2.comPort;
            Say("connect attempt " + (attempt + 1));
            try
            {
                port.BaseStream = new TcpSerial { Host = target.Substring(0, i), Port = target.Substring(i + 1) };
                var t = Task.Run(() => MainV2.instance.doConnect(port, "preset", "0", true, false));
                var until = DateTime.Now.AddSeconds(90);
                while (!t.IsCompleted && DateTime.Now < until)
                    Pause(200);
                connected = port.BaseStream.IsOpen && port.MAV.sysid != 0;
            }
            catch (Exception ex)
            {
                Say("waiting for SITL: " + ex.Message);
            }

            if (!connected)
                Pause(3000);
        }

        if (!connected)
        {
            Problem("could not connect to SITL at " + target);
            return;
        }

        Say($"link open, {MainV2.comPort.MAV.param.Count}/{MainV2.comPort.MAV.param.TotalReported} params so far");
        // a pilot often opens Parameters while the list is still downloading: open it now, check it once loaded
        MainV2.View.ShowScreen("FlyView");
        Pause(1500);
        var early = MainV2.instance.FlyView;
        early.ShowTools(true);
        early.Tools.OpenTool("params");
        Say($"Parameters opened early: {MainV2.comPort.MAV.param.Count}/{MainV2.comPort.MAV.param.TotalReported} loaded");

        // let parameters finish and telemetry settle
        var deadline = DateTime.Now.AddSeconds(120);
        while (DateTime.Now < deadline && (MainV2.comPort.MAV.param.TotalReported == 0 || MainV2.comPort.MAV.param.Count < MainV2.comPort.MAV.param.TotalReported))
            Pause(500);
        Pause(3000);
        Pause(2000);
        Say("Parameters opened early, after loading:");
        CheckParamsScroll(early.Tools);
        Shot("w-params-early");
        early.Tools.CloseTool();
        early.ShowTools(false);

        var cs = MainV2.comPort.MAV.cs;
        Say($"connected: sysid {MainV2.comPort.MAV.sysid}, {MainV2.comPort.MAV.param.Count}/{MainV2.comPort.MAV.param.TotalReported} params, mode {cs.mode}, " +
            $"airspeed {cs.airspeed:0.0}, alt {cs.alt:0.0}, climb {cs.climbrate:0.00}, yaw {cs.yaw:0}");

        // the new Fly screen
        MainV2.View.ShowScreen("FlyView");
        Pause(2000);
        var fly = MainV2.instance.FlyView;
        Shot("w-fly");

        // the Tools grid: every classic tool should be listed
        fly.ShowTools(true);
        Pause(1000);
        Shot("w-tools");
        var listed = FlightData.ModernTools.Select(t => t.Title).ToList();
        Say("tools listed: " + string.Join(", ", listed));
        var classicTabs = Descendants(MainV2.instance.FlightData).OfType<TabPage>().Select(p => p.Text).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        Say("classic Fly tabs: " + string.Join(", ", classicTabs));
        if (!listed.Any(t => t.IndexOf("log", StringComparison.OrdinalIgnoreCase) >= 0))
            Problem("Tools has no Logs entry (Classic Fly has a Telemetry Logs tab)");

        // each tool: open it, let it run, paint it, screenshot it
        foreach (var tool in FlightData.ModernTools)
        {
            bool opened;
            try
            {
                opened = fly.Tools.OpenTool(tool.Key);
            }
            catch (Exception ex)
            {
                Problem($"{tool.Title} threw when opened: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            if (!opened)
            {
                Problem(tool.Title + " did not open");
                continue;
            }

            Pause(2500);
            PaintCheck(fly.Tools, tool.Title);
            Shot("w-tool-" + tool.Key);

            if (tool.Key == "params")
                CheckParamsScroll(fly.Tools);
        }

        fly.Tools.CloseTool();
        fly.ShowTools(false);

        GaugesNearZero();
    }

    /// <summary>
    /// On the ground the climb rate and airspeed hover around zero. Paint the Gauges page with tiny values
    /// (tiny arcs) the way the timer would, and report a paint exception (Windows shows a red X and stops drawing).
    /// </summary>
    static void GaugesNearZero()
    {
        using (var form = new Form { Width = 900, Height = 700, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) })
        {
            var page = FlightDataToolPage("gauges");
            if (page == null)
            {
                Problem("Gauges: page could not be created");
                return;
            }

            page.Dock = DockStyle.Fill;
            form.Controls.Add(page);
            form.Show();
            Pause(300);
            var cs = new CurrentState();
            var values = new[] { 0.0, 1e-7, -1e-7, 1e-5, 0.0004, -0.0004, 0.004, 0.02, -0.02, 0.3 };
            var failures = 0;
            foreach (var v in values)
            {
                Say($"Gauges: value {v}");
                cs.climbrate = (float)v;
                cs.airspeed = (float)Math.Abs(v);
                cs.alt = (float)v;
                page.UpdateView(cs, true);
                if (!PaintCheck(page, $"Gauges at {v}"))
                    failures++;
            }

            Say($"Gauges near zero: {values.Length - failures}/{values.Length} values painted");
            form.Close();
        }
    }

    static FlyToolPage FlightDataToolPage(string key)
    {
        try
        {
            return FlyToolPage.Create(key);
        }
        catch (Exception ex)
        {
            Say("create " + key + ": " + ex.Message);
            return null;
        }
    }

    /// <summary>The parameter grid must be scrollable straight away when it has more rows than fit.</summary>
    static void CheckParamsScroll(Control host)
    {
        var grid = Descendants(host).OfType<DataGridView>().FirstOrDefault(g => g.Name == "Params");
        if (grid == null)
        {
            Problem("Parameters: no parameter grid found");
            return;
        }

        var vbar = grid.Controls.OfType<VScrollBar>().FirstOrDefault();
        var visibleRows = grid.DisplayedRowCount(false);
        Say($"Parameters: {grid.Rows.Count} rows, {visibleRows} visible, grid {grid.Width}x{grid.Height}, grid enabled {grid.Enabled}, " +
            $"scroll bar visible {vbar?.Visible} enabled {vbar?.Enabled} max {vbar?.Maximum}, can focus {grid.CanFocus}");
        if (!grid.Enabled || (vbar != null && !vbar.Enabled))
            Problem("Parameters: the list or its scroll bar is disabled");
        if (grid.Rows.Count > visibleRows + 1 && (vbar == null || !vbar.Visible))
            Problem("Parameters: more rows than fit but no vertical scroll bar");

        // the mouse wheel must move the list too
        var before = grid.FirstDisplayedScrollingRowIndex;
        try
        {
            grid.Focus();
            // the wheel goes to the focused window (unless Windows' hover scrolling is on): send it there, as a pilot's
            // first scroll after opening the tool would be
            Pause(500);
            var focused = GetFocus();
            var focusedControl = Control.FromHandle(focused);
            Say($"Parameters: app is the active window {GetForegroundWindow() == MainV2.instance.Handle}");
            Say($"Parameters: focus is on {(focusedControl == null ? "nothing in the app" : focusedControl.GetType().Name + " '" + focusedControl.Name + "'")}, grid has focus {grid.ContainsFocus}");
            var centre = grid.PointToScreen(new Point(grid.Width / 2, grid.Height / 2));
            if (focused != IntPtr.Zero)
                SendMessage(focused, 0x020A, (IntPtr)((-360) << 16), (IntPtr)((centre.Y << 16) | (centre.X & 0xFFFF)));
            Pause(300);
        }
        catch (Exception ex)
        {
            Say("wheel: " + ex.Message);
        }

        var after = grid.FirstDisplayedScrollingRowIndex;
        Say($"Parameters: wheel scroll moved first row {before} -> {after}");
        if (grid.Rows.Count > visibleRows + 1 && after == before)
        {
            if (GetForegroundWindow() != MainV2.instance.Handle)
                Say("Parameters: wheel-to-focus not measurable here (the build machine won't make the app the active window); grid focus requested: " + grid.Focused);
            else
                Problem("Parameters: mouse wheel does not scroll the list");
        }

        // with Windows' hover scrolling (on by default) the wheel goes to the list under the mouse: that must work
        var hoverBefore = grid.FirstDisplayedScrollingRowIndex;
        var at = grid.PointToScreen(new Point(grid.Width / 2, grid.Height / 2));
        SendMessage(grid.Handle, 0x020A, (IntPtr)((-360) << 16), (IntPtr)((at.Y << 16) | (at.X & 0xFFFF)));
        Pause(300);
        Say($"Parameters: wheel over the list moved first row {hoverBefore} -> {grid.FirstDisplayedScrollingRowIndex}");
        if (grid.Rows.Count > visibleRows + 1 && grid.FirstDisplayedScrollingRowIndex == hoverBefore)
            Problem("Parameters: the list does not scroll with the wheel over it");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr GetFocus();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int max);

    /// <summary>
    /// If the UI thread stops making progress (a dialog nobody answers, a hang), say which of our windows are open
    /// and grab the whole screen, from a separate thread, so a hang is visible in the log rather than a silent timeout.
    /// </summary>
    static void StartWatchdog()
    {
        var t = new Thread(() =>
        {
            int n = 0;
            while (true)
            {
                Thread.Sleep(15000);
                if (DateTime.Now - lastSay < TimeSpan.FromSeconds(75))
                    continue;
                n++;
                var me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                var titles = new System.Collections.Generic.List<string>();
                EnumWindows((h, l) =>
                {
                    uint pid;
                    GetWindowThreadProcessId(h, out pid);
                    if (pid == me && IsWindowVisible(h))
                    {
                        var sb = new System.Text.StringBuilder(256);
                        GetWindowText(h, sb, 256);
                        titles.Add("'" + sb + "'");
                    }

                    return true;
                }, IntPtr.Zero);
                Console.WriteLine($"UICHECK STALLED {n}: no progress for {(DateTime.Now - lastSay).TotalSeconds:0} s; open windows: {string.Join(", ", titles)}");
                try
                {
                    var b = Screen.PrimaryScreen.Bounds;
                    using (var bmp = new Bitmap(b.Width, b.Height))
                    {
                        using (var g = Graphics.FromImage(bmp))
                            g.CopyFromScreen(b.Location, Point.Empty, b.Size);
                        bmp.Save(Path.Combine(shots, "stalled-" + n + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("UICHECK stalled screenshot failed: " + ex.Message);
                }

                Console.Out.Flush();
                if (n >= 3)
                    return;
            }
        }) { IsBackground = true };
        t.Start();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [STAThread]
    static void Main()
    {
        shots = Environment.GetEnvironmentVariable("BRIECH_SCREENSHOT_DIR") ?? Path.Combine(Path.GetTempPath(), "briech-uicheck");
        Directory.CreateDirectory(shots);
        Settings.CustomUserDataDirectory = Path.Combine(Path.GetTempPath(), "briech-uicheck-" + Guid.NewGuid());
        Directory.CreateDirectory(Settings.GetUserDataDirectory());
        Settings.Instance["update_check"] = DateTime.Now.ToShortDateString();
        Settings.Instance["AutoConnect"] = "[]";
        Settings.Instance["AA_CheckEnableAltitudeAngel"] = "False"; // no first-run airspace prompt
        Settings.Instance.Save();

        // report UI-thread exceptions here (the app's own handler shows a dialog nobody can close). WinForms keeps
        // one handler per thread and the app sets its own at start-up, so ours is set again once the app runs.
        Application.ThreadException += OnUiException;


        Say("starting the app");
        StartWatchdog();
        timer = new Timer { Interval = 8000 };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            try
            {
                Run();
            }
            catch (Exception ex)
            {
                Problem("check crashed: " + ex);
            }

            Say(problems == 0 ? "RESULT no problems found" : $"RESULT {problems} problem(s)");
            Console.Out.Flush();
            Environment.Exit(0);
        };
        timer.Start();
        Program.Main(new string[0]);
    }
}
