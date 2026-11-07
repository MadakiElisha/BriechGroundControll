using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;
using MissionPlanner.Properties;
using MissionPlanner.Utilities;

namespace MissionPlanner
{
    public partial class Splash : Form
    {
        public Splash()
        {
            InitializeComponent();

            ApplyBranding();

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

        protected override void OnBackgroundImageChanged(EventArgs e)
        {
            // Mono cannot draw part of a 24 bit image, which the transparent labels need,
            // so they show as blank boxes. A 32 bit copy draws everywhere. (from upstream)
            if (BackgroundImage is Bitmap bitmap && bitmap.PixelFormat != PixelFormat.Format32bppArgb)
            {
                BackgroundImage = new Bitmap(bitmap);
                return;
            }

            base.OnBackgroundImageChanged(e);
        }

        private void ApplyBranding()
        {
            BackColor = Color.FromArgb(10, 14, 20);

            if (titanLogo != null)
            {
                titanLogo.BackColor = Color.Transparent;
                titanLogo.SizeMode = PictureBoxSizeMode.Zoom;
                titanLogo.Image = GetSplashImage() ?? titanLogo.Image;
            }
        }

        private static Image GetSplashImage()
        {
            if (Resources.splashdark != null)
                return (Image)Resources.splashdark.Clone();

            if (Resources.logo_dark != null)
                return (Image)Resources.logo_dark.Clone();

            return null;
        }
    }
}
