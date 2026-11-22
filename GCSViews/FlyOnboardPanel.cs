using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using log4net;
using MissionPlanner.ArduPilot;
using MissionPlanner.Controls;
using MissionPlanner.Controls.Fly;
using MissionPlanner.Utilities;
using T = MissionPlanner.Utilities.DesignTokens;

namespace MissionPlanner.GCSViews
{
    /// <summary>
    /// The Onboard view for the centre of the new Fly screen (design canvas board 8): everything on the aircraft
    /// besides the autopilot. Components with their heartbeat, the companion computer's health
    /// (ONBOARD_COMPUTER_STATUS), custom values it publishes (NAMED_VALUE_FLOAT / _INT), commands to it and its
    /// messages. Works with any companion software (Pi or Jetson); a section with nothing reported says how to
    /// get it rather than showing blanks.
    /// </summary>
    [PreventTheming]
    public class FlyOnboardPanel : Panel
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(FlyOnboardPanel));

        public const string KeyCommands = "onboard_commands";   // "label=command:param1;label=command:param1"
        public const string KeyWebConsole = "onboard_web_console";
        private const int CustomCommandBase = 31010;             // MAV_CMD_USER_1

        /// <summary>One monitor per app, fed from the active link's receive thread.</summary>
        public static readonly OnboardMonitor Monitor = new OnboardMonitor();

        private static MAVLinkInterface subscribedPort;
        private static EventHandler<MAVLink.MAVLinkMessage> packetHandler;

        private readonly TableLayoutPanel grid;
        private readonly TokenCard componentsCard, healthCard, valuesCard, commandsCard;
        private readonly TableLayoutPanel componentRows;
        private readonly Label componentsEmpty;
        private readonly ReadoutCard cpu, temp, ram, storage;
        private readonly Label healthNote;
        private readonly LinkLabel webConsole;
        private readonly FlowLayoutPanel valueTiles;
        private readonly Label valuesNote;
        private readonly FlowLayoutPanel commandButtons;
        private readonly Label commandResult, messagesTitle;
        private readonly Label[] messageLines = new Label[5];
        private readonly ToolTip tips = new ToolTip();
        private readonly List<ReadoutCard> valueCards = new List<ReadoutCard>();
        private int lastComponentCount = -1;
        private bool commandBusy;

        public FlyOnboardPanel()
        {
            DoubleBuffered = true;
            AutoScroll = true;
            Padding = new Padding(0);

            grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                BackColor = Color.Transparent,
                Padding = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            // ---- components ----
            componentsCard = MakeCard("Components on board");
            componentRows = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                BackColor = Color.Transparent
            };
            componentRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            componentRows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
            componentRows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
            componentsEmpty = MakeNote("Connect a vehicle to see what's on board.");
            componentsCard.Controls.Add(componentRows);
            componentsCard.Controls.Add(componentsEmpty);

            // ---- companion health ----
            healthCard = MakeCard("Companion health");
            var healthTiles = new TableLayoutPanel { Dock = DockStyle.Top, Height = 104, ColumnCount = 4, BackColor = Color.Transparent };
            for (int i = 0; i < 4; i++)
                healthTiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            cpu = MakeTile();
            temp = MakeTile();
            ram = MakeTile();
            storage = MakeTile();
            healthTiles.Controls.Add(cpu, 0, 0);
            healthTiles.Controls.Add(temp, 1, 0);
            healthTiles.Controls.Add(ram, 2, 0);
            healthTiles.Controls.Add(storage, 3, 0);
            healthNote = MakeNote("");
            healthNote.Dock = DockStyle.Top;
            healthNote.Height = 44;
            webConsole = new LinkLabel { Text = "Open web console", AutoSize = true, Font = T.Detail, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            webConsole.LinkClicked += (s, e) => OpenWebConsole();
            healthCard.Controls.Add(healthNote);
            healthCard.Controls.Add(healthTiles);
            healthCard.Controls.Add(webConsole);
            healthCard.Resize += (s, e) => webConsole.Location = new Point(healthCard.Width - webConsole.Width - 14, 12);

            // ---- custom telemetry ----
            valuesCard = MakeCard("Custom telemetry");
            valueTiles = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
            valuesNote = MakeNote("");
            valuesNote.Height = 40;
            valuesCard.Controls.Add(valuesNote);
            valuesCard.Controls.Add(valueTiles);
            valuesCard.Resize += (s, e) => LayoutValueTiles();

            // ---- commands + messages ----
            commandsCard = MakeCard("Commands to the companion");
            commandButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
            commandResult = MakeNote("");
            commandResult.Height = 30;
            messagesTitle = new Label { Dock = DockStyle.Top, Height = 30, Font = T.Label, Text = "COMPANION MESSAGES", TextAlign = ContentAlignment.BottomLeft };
            var msgList = new TableLayoutPanel { Dock = DockStyle.Top, Height = messageLines.Length * 22, ColumnCount = 1, BackColor = Color.Transparent };
            for (int i = 0; i < messageLines.Length; i++)
            {
                msgList.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
                messageLines[i] = new Label { Dock = DockStyle.Fill, Font = T.Detail, AutoEllipsis = true, UseMnemonic = false };
                msgList.Controls.Add(messageLines[i], 0, i);
            }

            commandsCard.Controls.Add(msgList);
            commandsCard.Controls.Add(messagesTitle);
            commandsCard.Controls.Add(commandResult);
            commandsCard.Controls.Add(commandButtons);
            BuildCommandButtons();

            Controls.Add(grid);
            Resize += (s, e) => ArrangeCards();
            ArrangeCards();
            ApplyTokens();
        }

        // ---------------- link ----------------

        /// <summary>Keep the monitor fed from whichever link is active; call from the UI timer.</summary>
        public static void FollowLink(MAVLinkInterface port)
        {
            if (port == subscribedPort)
            {
                if (port != null && port.MAV != null && Monitor.SysId != port.MAV.sysid && port.MAV.sysid != 0)
                {
                    Monitor.Clear();
                    Monitor.SysId = port.MAV.sysid;
                }

                return;
            }

            try
            {
                if (subscribedPort != null && packetHandler != null)
                    subscribedPort.OnPacketReceived -= packetHandler;
            }
            catch (Exception ex)
            {
                log.Debug(ex);
            }

            subscribedPort = port;
            Monitor.Clear();
            Monitor.SysId = port?.MAV?.sysid ?? 0;
            if (port == null)
                return;
            packetHandler = (s, msg) => Monitor.Ingest(msg);
            port.OnPacketReceived += packetHandler;
        }

        /// <summary>Sample data for the design preview and the Linux screenshots.</summary>
        public static void LoadPreviewSample()
        {
            subscribedPort = null;
            var m = Monitor;
            m.Clear();
            m.SysId = 1;
            var now = DateTime.Now;
            m.IngestHeartbeat(1, 1, 1, 3, now.AddSeconds(-0.2));
            m.IngestHeartbeat(1, 191, 18, 8, now.AddSeconds(-0.6));
            m.IngestHeartbeat(1, 100, 30, 8, now.AddSeconds(-0.9));
            m.IngestHeartbeat(1, 154, 26, 8, now.AddSeconds(-0.4));
            m.IngestHeartbeat(1, 236, 34, 8, now.AddSeconds(-3.1));
            var cores = Enumerable.Repeat(byte.MaxValue, 8).ToArray();
            cores[0] = 52; cores[1] = 41; cores[2] = 47; cores[3] = 48;
            var temps = Enumerable.Repeat(sbyte.MaxValue, 8).ToArray();
            temps[0] = 78;
            m.IngestComputerStatus(1, 191, 5_400_000, cores, temps, sbyte.MaxValue, 2150, 8192,
                new uint[] { 18700, 0, 0, 0 }, new uint[] { 30600, 0, 0, 0 }, now);
            m.IngestNamedValue(1, 191, "det_count", 14, true, now);
            m.IngestNamedValue(1, 191, "infer_fps", 23.5, false, now);
            m.IngestNamedValue(1, 191, "pl_temp", 41, false, now);
            m.IngestText(1, 191, 6, "detector: model loaded (yolo-briech-v3)", now.AddMinutes(-2));
            m.IngestText(1, 191, 4, "thermal: CPU 78 °C, throttling soon", now.AddMinutes(-1));
        }

        // ---------------- layout ----------------

        private void ArrangeCards()
        {
            var twoColumns = ClientSize.Width >= 820;
            var cards = new Control[] { componentsCard, healthCard, valuesCard, commandsCard };
            grid.SuspendLayout();
            grid.Controls.Clear();
            grid.ColumnCount = twoColumns ? 2 : 1;
            grid.ColumnStyles.Clear();
            for (int c = 0; c < grid.ColumnCount; c++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / grid.ColumnCount));
            grid.RowStyles.Clear();
            for (int i = 0; i < cards.Length; i++)
                grid.Controls.Add(cards[i], twoColumns ? i % 2 : 0, twoColumns ? i / 2 : i);
            grid.ResumeLayout(true);
        }

        private void SizeCard(TokenCard card)
        {
            // auto-size cards to their content: the title band plus every docked-top child
            int h = card.Padding.Vertical;
            foreach (Control c in card.Controls)
                if (c.Visible && c.Dock == DockStyle.Top)
                    h += c.Height;
            if (card.Height != h)
                card.Height = h;
        }

        private void LayoutValueTiles()
        {
            var w = valueTiles.ClientSize.Width;
            if (w <= 0)
                return;
            var perRow = Math.Max(1, Math.Min(3, w / 150));
            var tileW = (w - (perRow - 1) * T.Space2) / perRow - 1;
            for (int i = 0; i < valueCards.Count; i++)
            {
                valueCards[i].Width = tileW;
                valueCards[i].Margin = new Padding(0, 0, (i + 1) % perRow == 0 ? 0 : T.Space2, T.Space2);
            }
        }

        private static TokenCard MakeCard(string title)
        {
            return new TokenCard { Title = title, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, Margin = new Padding(0, 0, T.Space3, T.Space3), Height = 200 };
        }

        private static Label MakeNote(string text)
        {
            return new Label { Dock = DockStyle.Top, Height = 24, Font = T.Detail, Text = text, AutoEllipsis = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft };
        }

        private static ReadoutCard MakeTile()
        {
            return new ReadoutCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, T.Space2, 0) };
        }

        public void ApplyTokens()
        {
            var p = T.Current;
            BackColor = p.AppBackground;
            grid.BackColor = p.AppBackground;
            foreach (var l in new[] { componentsEmpty, healthNote, valuesNote, commandResult })
            {
                l.BackColor = p.Surface;
                l.ForeColor = p.TextMuted;
            }

            messagesTitle.BackColor = p.Surface;
            messagesTitle.ForeColor = p.TextMuted;
            foreach (var l in messageLines)
                l.BackColor = p.Surface;
            webConsole.BackColor = p.Surface;
            webConsole.LinkColor = p.Brand;
            webConsole.ActiveLinkColor = p.Brand;
            foreach (Control c in componentRows.Controls)
            {
                c.BackColor = p.Surface;
                if (c is Label)
                    c.ForeColor = p.TextPrimary;
            }

            Invalidate(true);
        }

        // ---------------- update ----------------

        public void UpdateView(bool connected)
        {
            var p = T.Current;
            var snap = Monitor.GetSnapshot(DateTime.Now);

            // components
            if (snap.Components.Count != lastComponentCount)
                RebuildComponentRows(snap.Components.Count);
            componentsEmpty.Visible = snap.Components.Count == 0;
            componentsEmpty.Text = connected ? "Waiting for heartbeats…" : "Connect a vehicle to see what's on board.";
            for (int i = 0; i < snap.Components.Count; i++)
            {
                var (c, state, age) = snap.Components[i];
                var name = (Label)componentRows.GetControlFromPosition(0, i);
                var id = (Label)componentRows.GetControlFromPosition(1, i);
                var chip = (HealthChip)componentRows.GetControlFromPosition(2, i);
                name.Text = c.Name;
                id.Text = c.CompId.ToString(CultureInfo.InvariantCulture);
                var ageText = age == TimeSpan.MaxValue ? "no heartbeat" : age.TotalSeconds < 60 ? $"{age.TotalSeconds:0.0} s" : "lost";
                chip.Set(ageText, state == OnboardMonitor.HeartbeatState.Ok ? T.State.Ok
                    : state == OnboardMonitor.HeartbeatState.Late ? T.State.Caution : T.State.Warning);
                tips.SetToolTip(chip, state == OnboardMonitor.HeartbeatState.Lost ? "No heartbeat for 5 s or more" : "Time since its last heartbeat");
            }

            componentsCard.TitleAside = snap.Companion.HasValue ? $"system {snap.Companion.Value.sysid}" : null;

            // health
            var s = snap.Computer;
            if (s != null)
            {
                var stale = !snap.ComputerFresh;
                cpu.Set("CPU", s.CpuLoadPct.HasValue ? $"{s.CpuLoadPct:0}%" : "--",
                    s.CpuCores > 0 ? $"{s.CpuCores} cores" : "not reported",
                    stale ? T.State.Neutral : s.CpuMaxPct >= 95 ? T.State.Caution : T.State.Neutral);
                var t = s.TempCoreC ?? s.TempBoardC;
                temp.Set("Core temp", t.HasValue ? $"{t} °C" : "--", t.HasValue ? (t >= 85 ? "throttling" : t >= 75 ? "near throttling" : "normal") : "not reported",
                    stale || !t.HasValue ? T.State.Neutral : t >= 85 ? T.State.Warning : t >= 75 ? T.State.Caution : T.State.Ok);
                ram.Set("RAM", s.RamUsedMiB.HasValue ? GiB(s.RamUsedMiB.Value) : "--",
                    s.RamTotalMiB.HasValue ? $"of {GiB(s.RamTotalMiB.Value)}" : "not reported",
                    stale ? T.State.Neutral : s.RamPct >= 90 ? T.State.Caution : T.State.Neutral);
                storage.Set("Storage", s.StoragePct.HasValue ? $"{s.StoragePct:0}%" : "--",
                    s.StorageTotalMiB.HasValue ? $"{GiB(s.StorageTotalMiB.Value - s.StorageUsedMiB.Value)} free" : "not reported",
                    stale ? T.State.Neutral : s.StoragePct >= 95 ? T.State.Warning : s.StoragePct >= 85 ? T.State.Caution : T.State.Neutral);
                var ago = (DateTime.Now - s.Received).TotalSeconds;
                healthNote.Text = stale
                    ? $"Last report {ago:0} s ago: values may be out of date."
                    : $"Component {s.CompId} · up {Uptime(s.Uptime)} · updated {Math.Max(0, ago):0} s ago";
                healthNote.ForeColor = stale ? p.Caution : p.TextMuted;
            }
            else
            {
                foreach (var tile in new[] { cpu, temp, ram, storage })
                    tile.Set(tile == cpu ? "CPU" : tile == temp ? "Core temp" : tile == ram ? "RAM" : "Storage", "--", "not reported", T.State.Neutral);
                healthNote.Text = snap.Companion.HasValue
                    ? "The companion isn't sending ONBOARD_COMPUTER_STATUS. Enable it in its MAVLink software to see CPU, temperature, RAM and storage."
                    : "No companion computer found on this link.";
                healthNote.ForeColor = p.TextMuted;
            }

            // custom values (12 at most; the rest are a scroll away in the Tools > Inspector page)
            var values = snap.Values.Take(12).ToList();
            while (valueCards.Count < values.Count)
            {
                var card = new ReadoutCard { Height = 92 };
                valueCards.Add(card);
                valueTiles.Controls.Add(card);
            }

            while (valueCards.Count > values.Count)
            {
                var last = valueCards[valueCards.Count - 1];
                valueCards.RemoveAt(valueCards.Count - 1);
                valueTiles.Controls.Remove(last);
                last.Dispose();
            }

            for (int i = 0; i < values.Count; i++)
            {
                var v = values[i];
                var ageS = (DateTime.Now - v.Received).TotalSeconds;
                valueCards[i].Set(v.Name, OnboardMonitor.FormatValue(v),
                    (v.CompId == 1 ? "autopilot" : "component " + v.CompId) + (ageS > 10 ? $" · {ageS:0} s old" : ""),
                    ageS > 10 ? T.State.Caution : T.State.Neutral);
            }

            LayoutValueTiles();
            valuesNote.Text = values.Count == 0
                ? "Nothing yet. Values sent with NAMED_VALUE_FLOAT or NAMED_VALUE_INT (e.g. detections, model FPS, payload temperature) appear here."
                : snap.Values.Count > values.Count ? $"Showing 12 of {snap.Values.Count}. All are in Tools › Inspector." : "From NAMED_VALUE_FLOAT / NAMED_VALUE_INT · names as sent";

            // messages
            var msgs = snap.Messages.Take(messageLines.Length).ToList();
            for (int i = 0; i < messageLines.Length; i++)
            {
                if (i < msgs.Count)
                {
                    var m = msgs[i];
                    messageLines[i].Text = $"{m.Received:HH:mm:ss}  {m.Text}";
                    messageLines[i].ForeColor = m.Severity <= (byte)MAVLink.MAV_SEVERITY.ERROR ? p.Warning
                        : m.Severity == (byte)MAVLink.MAV_SEVERITY.WARNING ? p.Caution : p.TextPrimary;
                }
                else
                {
                    messageLines[i].Text = i == 0 && msgs.Count == 0 ? "No messages from on-board components yet." : "";
                    messageLines[i].ForeColor = p.TextMuted;
                }
            }

            foreach (Control c in commandButtons.Controls)
                c.Enabled = connected && snap.Companion.HasValue && !commandBusy || c.Tag as string == "add";

            foreach (var card in new[] { componentsCard, healthCard, valuesCard, commandsCard })
            {
                SizeCard(card);
                card.Invalidate();
            }
        }

        private void RebuildComponentRows(int count)
        {
            lastComponentCount = count;
            componentRows.SuspendLayout();
            foreach (Control c in componentRows.Controls.Cast<Control>().ToList())
                c.Dispose();
            componentRows.Controls.Clear();
            componentRows.RowStyles.Clear();
            componentRows.RowCount = Math.Max(1, count);
            var p = T.Current;
            for (int i = 0; i < count; i++)
            {
                componentRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                componentRows.Controls.Add(new Label { Dock = DockStyle.Fill, Font = T.Body, ForeColor = p.TextPrimary, BackColor = p.Surface, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, UseMnemonic = false }, 0, i);
                componentRows.Controls.Add(new Label { Dock = DockStyle.Fill, Font = T.DetailMono, ForeColor = p.TextPrimary, BackColor = p.Surface, TextAlign = ContentAlignment.MiddleLeft }, 1, i);
                componentRows.Controls.Add(new HealthChip { Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 0, 3) }, 2, i);
            }

            componentRows.Height = count * 38;
            componentRows.ResumeLayout(true);
        }

        private static string GiB(double mib)
        {
            return mib >= 1024 ? $"{mib / 1024:0.0} GB" : $"{mib:0} MB";
        }

        private static string Uptime(TimeSpan t)
        {
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : $"{t.Minutes} min";
        }

        // ---------------- commands ----------------

        private class CommandButton
        {
            public string Label;
            public int Command;
            public float Param1;
        }

        private static List<CommandButton> LoadCommands()
        {
            var list = new List<CommandButton>();
            var raw = Settings.Instance[KeyCommands] ?? "";
            foreach (var entry in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = entry.LastIndexOf('=');
                if (eq <= 0)
                    continue;
                var parts = entry.Substring(eq + 1).Split(':');
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cmd))
                    continue;
                float p1 = 0;
                if (parts.Length > 1)
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out p1);
                list.Add(new CommandButton { Label = entry.Substring(0, eq).Trim(), Command = cmd, Param1 = p1 });
            }

            return list;
        }

        private static void SaveCommands(List<CommandButton> list)
        {
            Settings.Instance[KeyCommands] = string.Join(";", list.Select(c =>
                c.Label.Replace(";", ",").Replace("=", "-") + "=" + c.Command.ToString(CultureInfo.InvariantCulture) + ":" +
                c.Param1.ToString(CultureInfo.InvariantCulture)));
            Settings.Instance.Save();
        }

        private void BuildCommandButtons()
        {
            commandButtons.SuspendLayout();
            foreach (Control c in commandButtons.Controls.Cast<Control>().ToList())
                c.Dispose();
            commandButtons.Controls.Clear();

            foreach (var cmd in LoadCommands())
            {
                var b = new TokenButton { Text = cmd.Label, Width = 164, Height = 48, Margin = new Padding(0, 0, T.Space2, T.Space2) };
                var captured = cmd;
                b.Click += async (s, e) => await SendAsync(captured.Label, (MAVLink.MAV_CMD)captured.Command, captured.Param1, 0, 0);
                var menu = new ContextMenuStrip();
                menu.Items.Add("Remove this button", null, (s, e) =>
                {
                    var list = LoadCommands();
                    list.RemoveAll(x => x.Label == captured.Label && x.Command == captured.Command);
                    SaveCommands(list);
                    BuildCommandButtons();
                });
                b.ContextMenuStrip = menu;
                tips.SetToolTip(b, $"Sends command {captured.Command} (param 1 = {captured.Param1}) to the companion. Right-click to remove.");
                commandButtons.Controls.Add(b);
            }

            var reboot = new TokenButton { Text = "Reboot companion…", Style = TokenButton.Kind.Danger, Width = 164, Height = 48, Margin = new Padding(0, 0, T.Space2, T.Space2) };
            reboot.Click += async (s, e) =>
            {
                if (CustomMessageBox.Show("Reboot the companion computer?\n\nDetection, recording and any LTE link through it stop until it is back.",
                        "Reboot companion", CustomMessageBox.MessageBoxButtons.YesNo, CustomMessageBox.MessageBoxIcon.Warning,
                        "Cancel", "Reboot") != CustomMessageBox.DialogResult.No)
                    return;
                // MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN, param 3 = 1: reboot the onboard computer
                await SendAsync("Reboot companion", MAVLink.MAV_CMD.PREFLIGHT_REBOOT_SHUTDOWN, 0, 0, 1);
            };
            commandButtons.Controls.Add(reboot);

            var add = new TokenButton { Text = "Add a button…", Width = 164, Height = 48, Margin = new Padding(0, 0, T.Space2, T.Space2), Tag = "add" };
            add.Click += (s, e) => AddCommand();
            tips.SetToolTip(add, "Add a button that sends a MAVLink command to the companion (e.g. start detection, start recording).");
            commandButtons.Controls.Add(add);
            commandButtons.ResumeLayout(true);
        }

        private void AddCommand()
        {
            string label = "";
            if (InputBox.Show("Add a button", "Button label (e.g. Start detection)", ref label) != DialogResult.OK || string.IsNullOrWhiteSpace(label))
                return;
            string cmd = (CustomCommandBase + LoadCommands().Count % 5).ToString(CultureInfo.InvariantCulture);
            if (InputBox.Show("Add a button", "MAVLink command number your companion listens for.\n31010–31014 are MAV_CMD_USER_1–5, free for your own use.", ref cmd) != DialogResult.OK)
                return;
            if (!int.TryParse(cmd.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number <= 0 || number > 65535)
            {
                CustomMessageBox.Show("That isn't a command number (1–65535).", "Add a button");
                return;
            }

            string p1 = "1";
            if (InputBox.Show("Add a button", "Param 1 (optional, e.g. 1 = start, 0 = stop)", ref p1) != DialogResult.OK)
                return;
            float.TryParse(p1.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var param1);

            var list = LoadCommands();
            list.Add(new CommandButton { Label = label.Trim(), Command = number, Param1 = param1 });
            SaveCommands(list);
            BuildCommandButtons();
        }

        private async Task SendAsync(string label, MAVLink.MAV_CMD command, float p1, float p2, float p3)
        {
            var port = MainV2.comPort;
            var target = Monitor.GetSnapshot(DateTime.Now).Companion;
            if (port?.BaseStream == null || !port.BaseStream.IsOpen || !target.HasValue || commandBusy)
                return;

            commandBusy = true;
            commandResult.Text = $"{label} · sending to component {target.Value.compid}…";
            commandResult.ForeColor = T.Current.TextMuted;
            var started = DateTime.Now;
            bool ok;
            string error = null;
            try
            {
                ok = await Task.Run(() => port.doCommand(target.Value.sysid, target.Value.compid, command, p1, p2, p3, 0, 0, 0, 0));
            }
            catch (Exception ex)
            {
                ok = false;
                error = ex.Message;
            }

            commandBusy = false;
            if (IsDisposed)
                return;
            var secs = (DateTime.Now - started).TotalSeconds;
            commandResult.Text = ok
                ? $"{label} · accepted by component {target.Value.compid} ({secs:0.0} s)"
                : $"{label} · not accepted{(string.IsNullOrEmpty(error) ? "" : ": " + error)}. Check the companion software handles command {(int)command}.";
            commandResult.ForeColor = ok ? T.Current.Ok : T.Current.Warning;
        }

        private void OpenWebConsole()
        {
            string url = Settings.Instance[KeyWebConsole] ?? "";
            if (string.IsNullOrWhiteSpace(url) || ModifierKeys.HasFlag(Keys.Shift))
            {
                if (InputBox.Show("Companion web console", "Address of the companion's web page (e.g. http://192.168.144.10:8080). Shift-click the link to change it later.", ref url) != DialogResult.OK
                    || string.IsNullOrWhiteSpace(url))
                    return;
                url = url.Trim();
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    url = "http://" + url;
                Settings.Instance[KeyWebConsole] = url;
                Settings.Instance.Save();
            }

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("Couldn't open " + url + "\n" + ex.Message, "Companion web console");
            }
        }
    }
}
