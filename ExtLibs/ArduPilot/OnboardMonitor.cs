using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MissionPlanner.ArduPilot
{
    /// <summary>
    /// What is on board besides the autopilot: every MAVLink component that sends a heartbeat (companion computer,
    /// camera, gimbal, Remote ID, radios...), the companion's health from ONBOARD_COMPUTER_STATUS, the custom
    /// values it publishes with NAMED_VALUE_FLOAT / NAMED_VALUE_INT, and its STATUSTEXT messages.
    /// Pure logic fed from the link's receive thread; the Fly screen's Onboard view only reads snapshots.
    /// </summary>
    public class OnboardMonitor
    {
        /// <summary>A component counts as lost after this long without a heartbeat.</summary>
        public static readonly TimeSpan HeartbeatLost = TimeSpan.FromSeconds(5);

        /// <summary>A heartbeat older than this is late (shown as a caution).</summary>
        public static readonly TimeSpan HeartbeatLate = TimeSpan.FromSeconds(2.5);

        public const int MaxMessages = 50;

        public enum HeartbeatState
        {
            Ok,
            Late,
            Lost
        }

        public class Component
        {
            public uint SysId;
            public byte CompId;
            public byte MavType;
            public byte Autopilot;
            public DateTime LastHeartbeat;
            public string Name;
            public bool IsCompanion;
        }

        public class ComputerStatus
        {
            public uint SysId;
            public byte CompId;
            public DateTime Received;
            public TimeSpan Uptime;
            public int CpuCores;          // cores that report a load
            public double? CpuLoadPct;    // average over reporting cores
            public double? CpuMaxPct;
            public int? TempCoreC;        // hottest reporting core
            public int? TempBoardC;
            public double? RamUsedMiB;
            public double? RamTotalMiB;
            public double? StorageUsedMiB;
            public double? StorageTotalMiB;

            public double? RamPct => RamUsedMiB.HasValue && RamTotalMiB > 0 ? RamUsedMiB * 100 / RamTotalMiB : null;
            public double? StoragePct => StorageUsedMiB.HasValue && StorageTotalMiB > 0 ? StorageUsedMiB * 100 / StorageTotalMiB : null;
        }

        public class NamedValue
        {
            public uint SysId;
            public byte CompId;
            public string Name;
            public double Value;
            public bool IsInteger;
            public DateTime Received;
        }

        public class Message
        {
            public uint SysId;
            public byte CompId;
            public byte Severity;
            public string Text;
            public DateTime Received;
        }

        public class Snapshot
        {
            public List<(Component component, HeartbeatState state, TimeSpan age)> Components = new List<(Component, HeartbeatState, TimeSpan)>();
            public ComputerStatus Computer;
            public bool ComputerFresh;
            public List<NamedValue> Values = new List<NamedValue>();
            public List<Message> Messages = new List<Message>();
            public (uint sysid, byte compid)? Companion;
        }

        private readonly object locker = new object();
        private readonly Dictionary<(uint, byte), Component> components = new Dictionary<(uint, byte), Component>();
        private readonly Dictionary<(uint, byte), ComputerStatus> computers = new Dictionary<(uint, byte), ComputerStatus>();
        private readonly Dictionary<(uint, byte, string), NamedValue> values = new Dictionary<(uint, byte, string), NamedValue>();
        private readonly List<Message> messages = new List<Message>();

        /// <summary>Only this vehicle's components are kept (0 = any).</summary>
        public uint SysId { get; set; }

        public void Clear()
        {
            lock (locker)
            {
                components.Clear();
                computers.Clear();
                values.Clear();
                messages.Clear();
            }
        }

        private bool Wanted(uint sysid)
        {
            return SysId == 0 || sysid == SysId;
        }

        public void IngestHeartbeat(uint sysid, byte compid, byte mavType, byte autopilot, DateTime now)
        {
            if (!Wanted(sysid))
                return;
            lock (locker)
            {
                if (!components.TryGetValue((sysid, compid), out var c))
                    components[(sysid, compid)] = c = new Component { SysId = sysid, CompId = compid };
                c.MavType = mavType;
                c.Autopilot = autopilot;
                c.LastHeartbeat = now;
                c.IsCompanion = IsCompanion(compid, mavType);
                c.Name = Describe(compid, mavType, autopilot);
            }
        }

        public void IngestComputerStatus(uint sysid, byte compid, uint uptimeMs, byte[] cpuCores, sbyte[] tempCores,
            sbyte tempBoard, uint ramUsage, uint ramTotal, uint[] storageUsage, uint[] storageTotal, DateTime now)
        {
            if (!Wanted(sysid))
                return;

            var s = new ComputerStatus { SysId = sysid, CompId = compid, Received = now, Uptime = TimeSpan.FromMilliseconds(uptimeMs) };

            // UINT8_MAX / INT8_MAX mark "not used" in the message
            var loads = (cpuCores ?? new byte[0]).Where(v => v != byte.MaxValue && v <= 100).Select(v => (double)v).ToList();
            s.CpuCores = loads.Count;
            if (loads.Count > 0)
            {
                s.CpuLoadPct = loads.Average();
                s.CpuMaxPct = loads.Max();
            }

            var temps = (tempCores ?? new sbyte[0]).Where(v => v != sbyte.MaxValue).Select(v => (int)v).ToList();
            if (temps.Count > 0)
                s.TempCoreC = temps.Max();
            if (tempBoard != sbyte.MaxValue && tempBoard != 0)
                s.TempBoardC = tempBoard;

            if (ramTotal > 0 && ramTotal != uint.MaxValue)
            {
                s.RamTotalMiB = ramTotal;
                s.RamUsedMiB = ramUsage == uint.MaxValue ? (double?)null : ramUsage;
            }

            double used = 0, total = 0;
            var su = storageUsage ?? new uint[0];
            var st = storageTotal ?? new uint[0];
            for (int i = 0; i < Math.Min(su.Length, st.Length); i++)
            {
                if (st[i] == 0 || st[i] == uint.MaxValue || su[i] == uint.MaxValue)
                    continue;
                used += su[i];
                total += st[i];
            }

            if (total > 0)
            {
                s.StorageUsedMiB = used;
                s.StorageTotalMiB = total;
            }

            lock (locker)
            {
                computers[(sysid, compid)] = s;
                // a component that reports computer status is a companion, even without a heartbeat yet
                if (!components.ContainsKey((sysid, compid)))
                    components[(sysid, compid)] = new Component
                    {
                        SysId = sysid, CompId = compid, IsCompanion = true, Name = "Companion computer",
                        LastHeartbeat = DateTime.MinValue
                    };
            }
        }

        public void IngestNamedValue(uint sysid, byte compid, string name, double value, bool isInteger, DateTime now)
        {
            if (!Wanted(sysid) || string.IsNullOrWhiteSpace(name))
                return;
            name = name.Trim();
            lock (locker)
            {
                values[(sysid, compid, name)] = new NamedValue
                    { SysId = sysid, CompId = compid, Name = name, Value = value, IsInteger = isInteger, Received = now };
            }
        }

        /// <summary>STATUSTEXT from anything but the autopilot (the Fly screen already lists those).</summary>
        public void IngestText(uint sysid, byte compid, byte severity, string text, DateTime now)
        {
            if (!Wanted(sysid) || compid == 1 || string.IsNullOrWhiteSpace(text))
                return;
            lock (locker)
            {
                messages.Add(new Message { SysId = sysid, CompId = compid, Severity = severity, Text = text.Trim(), Received = now });
                if (messages.Count > MaxMessages)
                    messages.RemoveRange(0, messages.Count - MaxMessages);
            }
        }

        /// <summary>Feed any received packet; anything not about on-board components is ignored.</summary>
        public void Ingest(MAVLink.MAVLinkMessage msg)
        {
            if (msg == null || msg.data == null)
                return;
            var now = msg.rxtime == DateTime.MinValue ? DateTime.Now : msg.rxtime;
            try
            {
                switch (msg.msgid)
                {
                    case (uint)MAVLink.MAVLINK_MSG_ID.HEARTBEAT:
                    {
                        var hb = (MAVLink.mavlink_heartbeat_t)msg.data;
                        IngestHeartbeat(msg.sysid, msg.compid, hb.type, hb.autopilot, now);
                        break;
                    }
                    case (uint)MAVLink.MAVLINK_MSG_ID.ONBOARD_COMPUTER_STATUS:
                    {
                        var s = (MAVLink.mavlink_onboard_computer_status_t)msg.data;
                        IngestComputerStatus(msg.sysid, msg.compid, s.uptime, s.cpu_cores, s.temperature_core,
                            s.temperature_board, s.ram_usage, s.ram_total, s.storage_usage, s.storage_total, now);
                        break;
                    }
                    case (uint)MAVLink.MAVLINK_MSG_ID.NAMED_VALUE_FLOAT:
                    {
                        var v = (MAVLink.mavlink_named_value_float_t)msg.data;
                        IngestNamedValue(msg.sysid, msg.compid, CString(v.name), v.value, false, now);
                        break;
                    }
                    case (uint)MAVLink.MAVLINK_MSG_ID.NAMED_VALUE_INT:
                    {
                        var v = (MAVLink.mavlink_named_value_int_t)msg.data;
                        IngestNamedValue(msg.sysid, msg.compid, CString(v.name), v.value, true, now);
                        break;
                    }
                    case (uint)MAVLink.MAVLINK_MSG_ID.STATUSTEXT:
                    {
                        var t = (MAVLink.mavlink_statustext_t)msg.data;
                        IngestText(msg.sysid, msg.compid, t.severity, CString(t.text), now);
                        break;
                    }
                }
            }
            catch (InvalidCastException)
            {
            }
        }

        private static string CString(byte[] bytes)
        {
            if (bytes == null)
                return "";
            var s = Encoding.UTF8.GetString(bytes);
            var end = s.IndexOf('\0');
            return end >= 0 ? s.Substring(0, end) : s;
        }

        public Snapshot GetSnapshot(DateTime now)
        {
            var snap = new Snapshot();
            lock (locker)
            {
                foreach (var c in components.Values.OrderBy(c => c.SysId).ThenBy(c => c.CompId))
                {
                    var age = c.LastHeartbeat == DateTime.MinValue ? TimeSpan.MaxValue : now - c.LastHeartbeat;
                    var state = age >= HeartbeatLost ? HeartbeatState.Lost : age >= HeartbeatLate ? HeartbeatState.Late : HeartbeatState.Ok;
                    snap.Components.Add((c, state, age));
                }

                // the companion to show: one that reports computer status, else the first companion by id
                var withStatus = computers.Values.OrderByDescending(s => s.Received).FirstOrDefault();
                if (withStatus != null)
                {
                    snap.Companion = (withStatus.SysId, withStatus.CompId);
                    snap.Computer = withStatus;
                    snap.ComputerFresh = now - withStatus.Received < HeartbeatLost;
                }
                else
                {
                    var comp = components.Values.Where(c => c.IsCompanion).OrderBy(c => c.CompId).FirstOrDefault();
                    if (comp != null)
                        snap.Companion = (comp.SysId, comp.CompId);
                }

                // the autopilot's own named values belong to it; everything else's are "custom telemetry"
                snap.Values = values.Values.OrderBy(v => v.CompId).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
                snap.Messages = messages.OrderByDescending(m => m.Received).ToList();
            }

            return snap;
        }

        // ---- naming ----

        // MAV_TYPE values used below (MAVLink common)
        private const byte TypeGcs = 6, TypeOnboardController = 18, TypeGimbal = 26, TypeAdsb = 27, TypeCamera = 30,
            TypeChargingStation = 31, TypeFlarm = 32, TypeServo = 33, TypeOdid = 34, TypeBattery = 36, TypeParachute = 37,
            TypeLog = 38, TypeOsd = 39, TypeImu = 40, TypeGps = 41, TypeWinch = 42;

        public static bool IsCompanion(byte compid, byte mavType)
        {
            return mavType == TypeOnboardController || (compid >= 191 && compid <= 194);
        }

        public static string Describe(byte compid, byte mavType, byte autopilot)
        {
            // MAV_AUTOPILOT_INVALID = 8: anything else that heartbeats as an autopilot is the flight controller
            if (autopilot != 8 && mavType != TypeGcs && mavType != TypeOnboardController)
                return compid == 1 ? "Autopilot" : "Autopilot (component " + compid + ")";

            switch (mavType)
            {
                case TypeOnboardController: return "Companion computer";
                case TypeGimbal: return "Gimbal";
                case TypeAdsb: return "ADS-B";
                case TypeCamera: return "Camera";
                case TypeFlarm: return "FLARM";
                case TypeServo: return "Servo";
                case TypeOdid: return "Remote ID";
                case TypeBattery: return "Battery";
                case TypeParachute: return "Parachute";
                case TypeLog: return "Logger";
                case TypeOsd: return "OSD";
                case TypeImu: return "IMU";
                case TypeGps: return "GPS";
                case TypeWinch: return "Winch";
                case TypeChargingStation: return "Charging station";
                case TypeGcs: return "Ground station";
            }

            if (compid >= 191 && compid <= 194) return "Companion computer";
            if (compid >= 100 && compid <= 105) return "Camera";
            if (compid == 154 || (compid >= 171 && compid <= 176)) return "Gimbal";
            if (compid >= 236 && compid <= 238) return "Remote ID";
            if (compid == 68) return "Telemetry radio";
            if (compid == 156) return "ADS-B";
            if (compid == 220 || compid == 221) return "GPS";
            if (compid >= 240 && compid <= 242) return "MAVLink bridge";
            return "Component " + compid;
        }

        /// <summary>Plain value text: integers without decimals, others with up to two.</summary>
        public static string FormatValue(NamedValue v)
        {
            if (v == null)
                return "--";
            if (v.IsInteger || Math.Abs(v.Value - Math.Round(v.Value)) < 1e-9 && Math.Abs(v.Value) < 1e9)
                return Math.Round(v.Value).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            return v.Value.ToString(Math.Abs(v.Value) >= 100 ? "0.0" : "0.00", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
