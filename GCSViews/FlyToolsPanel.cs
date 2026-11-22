using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using log4net;
using MissionPlanner.Controls;
using MissionPlanner.Controls.Fly;
using MissionPlanner.Utilities;
using T = MissionPlanner.Utilities.DesignTokens;

namespace MissionPlanner.GCSViews
{
    /// <summary>
    /// The Tools view for the centre of the new Fly screen: a grid of every classic Fly tool page (Dashboard,
    /// Status, Gauges, Servo / Relay, Parameters, ...), and the open tool with "All tools" and "Open in window".
    /// The tool pages are the classic ones, borrowed from <see cref="FlightData"/> (only one place owns them at a
    /// time, so every handler keeps working); while shown here their plain backgrounds and text follow the tokens.
    /// </summary>
    [PreventTheming]
    public class FlyToolsPanel : Panel
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(FlyToolsPanel));

        private readonly Panel picker, toolView, toolHost;
        private readonly Label pickerNote, toolTitle;
        private readonly TableLayoutPanel grid;
        private readonly TokenButton back, popOut;
        private readonly List<ToolTile> tiles = new List<ToolTile>();
        private Form toolWindow;
        private FlyToolPage nativePage, windowPage;

        /// <summary>Key of the tool open in this panel or its window, or null when the grid is showing.</summary>
        public string OpenToolKey { get; private set; }

        public FlyToolsPanel()
        {
            DoubleBuffered = true;
            Padding = new Padding(0);

            // ---- grid of tools ----
            picker = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(T.Space4) };
            pickerNote = new Label
            {
                Dock = DockStyle.Top,
                Height = 28,
                Font = T.Detail,
                Text = "Every tool from Classic Fly. Pick one to open it here, or open it in its own window.",
                AutoEllipsis = true
            };
            grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                Padding = new Padding(0, T.Space2, 0, T.Space2),
                BackColor = Color.Transparent
            };
            for (int c = 0; c < 3; c++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));

            int i = 0;
            foreach (var tool in FlightData.ModernTools)
            {
                var tile = new ToolTile
                {
                    Title = tool.Title,
                    Detail = tool.Detail,
                    Tag = tool.Key,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, T.Space2, T.Space2),
                    AccessibleName = tool.Title,
                    AccessibleDescription = tool.Detail
                };
                tile.Click += (s, e) => OpenTool((string)((Control)s).Tag);
                tiles.Add(tile);
                if (i % 3 == 0)
                    grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
                grid.Controls.Add(tile, i % 3, i / 3);
                i++;
            }

            picker.Controls.Add(grid);
            picker.Controls.Add(pickerNote);

            // ---- open tool ----
            toolView = new Panel { Dock = DockStyle.Fill, Visible = false, Padding = new Padding(0) };
            var bar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 44 + T.Space2,
                ColumnCount = 3,
                Padding = new Padding(T.Space2, T.Space2, T.Space2, 0),
                BackColor = Color.Transparent
            };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 164));
            back = new TokenButton { Text = "‹  All tools", Dock = DockStyle.Fill, Margin = new Padding(0), Height = 40 };
            back.Click += (s, e) => CloseTool();
            toolTitle = new Label
            {
                Dock = DockStyle.Fill,
                Font = T.Title,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(T.Space3, 0, 0, 0),
                AutoEllipsis = true
            };
            popOut = new TokenButton { Text = "Open in window", Dock = DockStyle.Fill, Margin = new Padding(0), Height = 40 };
            popOut.Click += (s, e) => PopOutTool();
            bar.Controls.Add(back, 0, 0);
            bar.Controls.Add(toolTitle, 1, 0);
            bar.Controls.Add(popOut, 2, 0);

            toolHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(T.Space2) };
            toolView.Controls.Add(toolHost);
            toolView.Controls.Add(bar);

            Controls.Add(toolView);
            Controls.Add(picker);
            ApplyTokens();
        }

        public void ApplyTokens()
        {
            var p = T.Current;
            BackColor = p.Surface;
            picker.BackColor = p.Surface;
            toolView.BackColor = p.Surface;
            toolHost.BackColor = p.Surface;
            pickerNote.BackColor = p.Surface;
            pickerNote.ForeColor = p.TextMuted;
            toolTitle.BackColor = p.Surface;
            toolTitle.ForeColor = p.TextPrimary;
            foreach (var tile in tiles)
                tile.Invalidate();
            if (OpenToolKey != null && toolWindow == null && nativePage == null)
                Retint(toolHost);
            nativePage?.ApplyTokens();
            if (windowPage != null && !windowPage.IsDisposed)
            {
                windowPage.ApplyTokens();
                if (windowPage.Parent != null)
                    windowPage.Parent.BackColor = p.Surface;
                var form = windowPage.FindForm();
                if (form != null)
                    form.BackColor = p.Surface;
            }
        }

        /// <summary>Open a tool in this panel. Returns false when Classic Fly can't lend it.</summary>
        public bool OpenTool(string key)
        {
            var classic = MainV2.instance?.FlightData;
            if (classic == null)
                return false;

            ClosePopOut();
            GiveBack(toolHost);
            DisposeNative();

            // Dashboard, Status, Gauges and Servo / Relay are drawn in the new design; the rest are borrowed
            var page = FlyToolPage.Create(key);
            if (page != null)
            {
                nativePage = page;
                page.Dock = DockStyle.Fill;
                toolHost.Controls.Add(page);
            }
            else
            {
                if (!classic.BorrowTool(key, toolHost))
                    return false;
                Retint(toolHost);
            }

            OpenToolKey = key;
            toolTitle.Text = TitleOf(key);
            picker.Visible = false;
            toolView.Visible = true;
            toolView.BringToFront();
            return true;
        }

        /// <summary>Hand the open tool back to Classic Fly and show the grid.</summary>
        public void CloseTool()
        {
            ClosePopOut();
            GiveBack(toolHost);
            DisposeNative();
            ShowPicker();
        }

        private void DisposeNative()
        {
            var page = nativePage;
            nativePage = null;
            if (page != null && !page.IsDisposed)
            {
                toolHost.Controls.Remove(page);
                page.Dispose();
            }
        }

        /// <summary>Refresh the drawn pages (in place and in their own window); called from the Fly screen's timer.</summary>
        public void Tick(CurrentState cs, bool connected)
        {
            try
            {
                if (nativePage != null && !nativePage.IsDisposed && Visible)
                    nativePage.UpdateView(cs, connected);
                if (windowPage != null && !windowPage.IsDisposed)
                    windowPage.UpdateView(cs, connected);
            }
            catch (Exception ex)
            {
                log.Debug(ex);
            }
        }

        /// <summary>Classic Fly takes its tools back when it is shown; follow that here.</summary>
        public void SyncWithClassic()
        {
            if (OpenToolKey == null || nativePage != null)
                return;
            if (MainV2.instance?.FlightData?.BorrowedToolKey != OpenToolKey)
            {
                ClosePopOut();
                ShowPicker();
            }
        }

        private void ShowPicker()
        {
            OpenToolKey = null;
            toolView.Visible = false;
            picker.Visible = true;
        }

        private static string TitleOf(string key)
        {
            foreach (var tool in FlightData.ModernTools)
                if (tool.Key == key)
                    return tool.Title;
            return key;
        }

        /// <summary>Return the borrowed controls, then put the classic theme back on them.</summary>
        private void GiveBack(Control host)
        {
            var classic = MainV2.instance?.FlightData;
            if (classic == null || classic.BorrowedToolKey == null)
                return;
            // nothing borrowed here (empty, or one of the drawn pages): leave Classic Fly's tool where it is
            if (host.Controls.Count == 0 || host.Controls.Cast<Control>().All(c => c is FlyToolPage))
                return;
            var children = new Control[host.Controls.Count];
            host.Controls.CopyTo(children, 0);
            classic.ReturnTool();
            foreach (var child in children)
            {
                try
                {
                    if (!child.IsDisposed)
                        ThemeManager.ApplyThemeTo(child);
                }
                catch (Exception ex)
                {
                    log.Debug(ex);
                }
            }
        }

        private void PopOutTool()
        {
            if (OpenToolKey == null || toolWindow != null)
                return;
            var classic = MainV2.instance?.FlightData;
            if (classic == null)
                return;

            var key = OpenToolKey;
            var p = T.Current;
            var form = new Form
            {
                Text = TitleOf(key),
                StartPosition = FormStartPosition.CenterScreen,
                Size = new Size(900, 640),
                ShowInTaskbar = true,
                BackColor = p.Surface,
                Icon = FindForm()?.Icon
            };
            var host = new Panel { Dock = DockStyle.Fill, BackColor = p.Surface, Padding = new Padding(T.Space2) };
            form.Controls.Add(host);
            if (nativePage != null)
            {
                // a drawn page simply moves into the window and keeps being refreshed there
                var page = nativePage;
                nativePage = null;
                toolHost.Controls.Remove(page);
                host.Controls.Add(page);
                windowPage = page;
                toolWindow = form;
                form.FormClosing += (s, e) =>
                {
                    if (toolWindow == form)
                        toolWindow = null;
                    if (windowPage == page)
                        windowPage = null;
                };
                OpenToolKey = null;
                toolView.Visible = false;
                picker.Visible = true;
                form.Show(FindForm());
                return;
            }

            if (!classic.BorrowTool(key, host))
            {
                form.Dispose();
                classic.BorrowTool(key, toolHost);
                return;
            }

            Retint(host);
            toolWindow = form;
            // FormClosing, not FormClosed: the classic controls must go back before the window disposes them
            form.FormClosing += (s, e) =>
            {
                if (toolWindow != form)
                    return;
                toolWindow = null;
                if (MainV2.instance?.FlightData?.BorrowedToolKey == key)
                    GiveBack(host);
                ShowPicker();
            };

            toolView.Visible = false;
            picker.Visible = true;
            form.Show(FindForm());
        }

        private void ClosePopOut()
        {
            var form = toolWindow;
            if (form == null)
                return;
            toolWindow = null;
            windowPage = null;
            // hand the controls back first so closing the window can't dispose them
            foreach (Control c in form.Controls)
                GiveBack(c);
            if (!form.IsDisposed)
                form.Close();
        }

        /// <summary>
        /// Plain containers and text follow the tokens while a classic page is shown here. Custom-drawn controls
        /// (gauges, the dashboard tiles, grids, the HUD) keep their own colours.
        /// </summary>
        private static void Retint(Control root)
        {
            var p = T.Current;
            foreach (Control c in root.Controls)
            {
                try
                {
                    switch (c)
                    {
                        case TableLayoutPanel _:
                        case FlowLayoutPanel _:
                        case SplitContainer _:
                        case TabPage _:
                        case GroupBox _:
                            c.BackColor = p.Surface;
                            c.ForeColor = p.TextPrimary;
                            break;
                        case Label _:
                        case CheckBox _:
                        case RadioButton _:
                            c.BackColor = Color.Transparent;
                            c.ForeColor = p.TextPrimary;
                            break;
                        default:
                            if (c.GetType() == typeof(Panel) || c.GetType() == typeof(UserControl))
                            {
                                c.BackColor = p.Surface;
                                c.ForeColor = p.TextPrimary;
                            }

                            break;
                    }

                    if (c.HasChildren)
                        Retint(c);
                }
                catch (Exception ex)
                {
                    log.Debug(ex);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClosePopOut();
                GiveBack(toolHost);
            }

            base.Dispose(disposing);
        }

        /// <summary>A tool in the grid: name over a one-line description, styled like the other Fly buttons.</summary>
        private class ToolTile : TokenControl
        {
            private bool hover, pressed;
            public string Title { get; set; }
            public string Detail { get; set; }

            public ToolTile()
            {
                Cursor = Cursors.Hand;
                TabStop = true;
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override void OnKeyUp(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Space)
                    OnClick(EventArgs.Empty);
                base.OnKeyUp(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                FillParentBackground(g);
                Smooth(g);
                var p = T.Current;
                var fill = pressed ? ControlPaint.Dark(p.SurfaceRaised, 0.05f)
                    : hover ? ControlPaint.Light(p.SurfaceRaised, 0.08f)
                    : p.SurfaceRaised;
                var border = hover || Focused ? p.Brand : p.BorderControl;
                using (var path = T.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), T.ControlRadius))
                using (var b = new SolidBrush(fill))
                using (var pen = new Pen(border, hover || Focused ? 2f : 1f))
                {
                    g.FillPath(b, path);
                    g.DrawPath(pen, path);
                }

                var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
                int half = Height / 2;
                DrawText(g, Title, T.BodyStrong, p.TextPrimary, new Rectangle(T.Space3, 6, Width - T.Space3 * 2, half - 4), flags);
                DrawText(g, Detail, T.Detail, p.TextMuted, new Rectangle(T.Space3, half, Width - T.Space3 * 2, half - 6), flags);
            }
        }
    }
}
