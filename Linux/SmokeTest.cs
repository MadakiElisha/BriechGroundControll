using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MissionPlanner.Controls.PreFlight;
using System.Windows.Forms;
using System.Xml.Serialization;
using MissionPlanner;
using MissionPlanner.Utilities;

// Run in the extracted package, with an isolated profile and an X11 test display.
class LinuxSmokeTest
{
    private static Timer timer;
    // This is a startup test in a disposable process. Mono 6.8 can hang while
    // aborting background plugin/network threads during Environment.Exit.
    [DllImport("libc", EntryPoint = "_exit")]
    private static extern void Exit(int status);

    private static void Finish(int status)
    {
        Console.Out.Flush();
        Console.Error.Flush();
        Exit(status);
    }

    private static void CheckChecklistResize()
    {
        using (var checklist = new CheckListControl())
        {
            // A resize can arrive before the timer has drawn any rows.
            checklist.Controls_Resize(checklist, EventArgs.Empty);
            checklist.CheckListItems.Clear();
            checklist.CheckListItems.Add(new CheckListItem { Description = "Test item", Text = "Test" });
            checklist.Draw();
            checklist.Controls_Resize(checklist, EventArgs.Empty);
            checklist.CheckListItems.Clear();
            checklist.Draw();
            checklist.Controls_Resize(checklist, EventArgs.Empty);
        }
    }

    static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var grandChild in Descendants(child))
                yield return grandChild;
        }
    }

    // Optional: BRIECH_SCREENSHOT_DIR makes the test also capture Modern Fly screens for PR review,
    // and checks that a tool shown in Modern Fly goes back to classic Fly intact.
    static void Pause(int ms)
    {
        var until = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < until)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(30);
        }
    }

    static void Shot(string dir, string name)
    {
        Pause(1200);
        var form = MainV2.instance;
        var bounds = form.Bounds;
        using (var bmp = new System.Drawing.Bitmap(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height)))
        {
            try
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                    g.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
            }
            catch (Exception ex)
            {
                Console.WriteLine("CopyFromScreen failed, using DrawToBitmap: " + ex.Message);
                form.DrawToBitmap(bmp, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bounds.Size));
            }
            bmp.Save(Path.Combine(dir, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        Console.WriteLine("screenshot " + name);
    }

    static void ModernFlyTools(string dir)
    {
        var main = MainV2.instance;
        MainV2.View.ShowScreen("ModernFlightData");
        Shot(dir, "1-modern-fly");

        var panel = Descendants(main).FirstOrDefault(c => c.GetType().Name == "PanelQuickActions");
        if (panel == null)
            throw new Exception("Modern Fly has no operations panel.");
        var type = panel.GetType();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var tabEnum = type.GetNestedType("OperationsTab", System.Reflection.BindingFlags.NonPublic);
        type.GetMethod("SelectTab", flags).Invoke(panel, new[] { Enum.Parse(tabEnum, "Tools") });
        Shot(dir, "2-tools");

        var open = type.GetMethod("OpenTool", flags);
        foreach (var key in new[] { "status", "tuning", "servo" })
        {
            open.Invoke(panel, new object[] { key });
            if (main.FlightData.BorrowedToolKey != key)
                throw new Exception("Tool did not open in Modern Fly: " + key);
            Shot(dir, "3-tool-" + key);
        }

        // switching to classic Fly must hand the page back with its controls
        MainV2.View.ShowScreen("FlightData");
        Pause(800);
        if (main.FlightData.BorrowedToolKey != null)
            throw new Exception("Classic Fly did not take its tool back.");
        var servoPage = (Control)typeof(MissionPlanner.GCSViews.FlightData)
            .GetField("tabServo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .GetValue(main.FlightData);
        var servoPanel = (Control)typeof(MissionPlanner.GCSViews.FlightData)
            .GetField("flowLayoutPanelServos", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .GetValue(main.FlightData);
        Console.WriteLine($"servo page children {servoPage.Controls.Count}, servo panel parent {servoPanel.Parent?.Name}");
        if (servoPanel.Parent != servoPage)
            throw new Exception("Servo controls did not go back to classic Fly.");
        Shot(dir, "4-classic-after-return");
    }

    [STAThread]
    static void Main()
    {
        Settings.CustomUserDataDirectory = Path.Combine(Path.GetTempPath(), "missionplanner-smoke-" + Guid.NewGuid());
        Directory.CreateDirectory(Settings.GetUserDataDirectory());
        Settings.Instance["update_check"] = DateTime.Now.ToShortDateString();
        Settings.Instance["MainWidth"] = "1200";
        Settings.Instance["MainHeight"] = "900";
        Settings.Instance["AutoConnect"] = "[]";
        Settings.Instance.Save();
        timer = new Timer { Interval = 10000 };
        timer.Tick += (sender, args) => {
            timer.Stop();
            try
            {
                var flightData = MainV2.instance.FlightData;
                // Briech GCS draws its own tab strip and hosts the actions in a FlightDataActions control
                // (speed, altitude, loiter radius); select the Actions tab when it is reachable.
                var tabs = flightData.Controls.Find("tabControlactions", true).OfType<TabControl>().FirstOrDefault();
                var actionsTab = flightData.Controls.Find("tabActions", true).OfType<TabPage>().FirstOrDefault();
                if (tabs != null && actionsTab != null && tabs.TabPages.Contains(actionsTab))
                    tabs.SelectedTab = actionsTab;
                var actions = Descendants(flightData).OfType<MissionPlanner.Controls.FlightDataActions>().FirstOrDefault();
                if (actions == null)
                    throw new Exception("FlightData has no actions panel.");
                var combo = actions.CMB_action;
                if (!MainV2.instance.Visible || combo.Items.Count == 0)
                    throw new Exception("FlightData did not load its actions.");
                var modifiers = Descendants(actions).OfType<MissionPlanner.Controls.ModifyandSet>().ToArray();
                if (modifiers.Length < 3)
                    throw new Exception("Missing numeric action controls.");
                foreach (var modify in modifiers)
                {
                    Console.WriteLine($"{modify.Name}: width {modify.Width}, box top {modify.NumericUpDown.Top} " +
                                      $"right {modify.NumericUpDown.Right}, button top {modify.Button.Top} right {modify.Button.Right}");
                    // Briech centres the box and button vertically in their own cells, so only clipping fails the test
                    if (modify.Button.Right > modify.Width || modify.NumericUpDown.Right > modify.Width)
                        throw new Exception("Clipped action control: " + modify.Name);
                }
                CheckChecklistResize();
                // Check the packaged native library and its managed ABI together.
                using (var bitmap = new SkiaSharp.SKBitmap(8, 8))
                using (var canvas = new SkiaSharp.SKCanvas(bitmap))
                {
                    canvas.Clear(SkiaSharp.SKColors.Red);
                    if (bitmap.GetPixel(0, 0) != SkiaSharp.SKColors.Red)
                        throw new Exception("Native Skia rendering failed.");
                }
                // Theme serialization can fall back to Mono's compiler helper.
                var serializer = new XmlSerializer(typeof(ThemeColorTable));
                var theme = new ThemeColorTable();
                theme.InitColors();
                var xml = new StringWriter();
                serializer.Serialize(xml, theme);
                var restored = (ThemeColorTable)serializer.Deserialize(new StringReader(xml.ToString()));
                if (restored.colors.Count != theme.colors.Count)
                    throw new Exception("Theme serialization failed.");
                var shots = Environment.GetEnvironmentVariable("BRIECH_SCREENSHOT_DIR");
                if (!string.IsNullOrEmpty(shots))
                {
                    Directory.CreateDirectory(shots);
                    ModernFlyTools(shots);
                }
                Console.WriteLine("LINUX_SMOKE_TEST_PASS");
                Finish(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                Finish(1);
            }
        };
        timer.Start();
        Program.Main(new string[0]);
    }
}
