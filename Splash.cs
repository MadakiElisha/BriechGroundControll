using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using MissionPlanner.Utilities;

namespace MissionPlanner
{
    public partial class Splash : Form
    {
        public Splash()
        {
            InitializeComponent();

            if (Program.IconFile is Bitmap iconBitmap)
                this.Icon = Icon.FromHandle(iconBitmap.GetHicon());

            TXT_version.Visible = false; //Added to hide version info on splash screen.

            string strVersion = typeof(Splash).GetType().Assembly.GetName().Version.ToString();

            TXT_version.Text = "Version: Titan " + Application.ProductVersion; // +" Build " + strVersion;

            // Use theme color for bottom line instead of hardcoded green
            label1.ForeColor = ThemeManager.BannerColor2;

            Console.WriteLine(strVersion);

            Console.WriteLine("Splash .ctor");
        }
    }
}
