using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
using SystemInformation = System.Windows.Forms.SystemInformation;

namespace VigilApp
{
    public class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // The sensor library and its friends are packed inside the exe: one file to download, like Sentinel.
            AppDomain.CurrentDomain.AssemblyResolve += Libs.Resolve;
            return Run(args);
        }

        // Kept apart from Main: the JIT must not meet a library type before the resolver is in place.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int Run(string[] args)
        {
            if (Array.IndexOf(args, "--selftest") >= 0) return SelfTest.Run();

            // --restart: the old Vigil is closing to hand over to an elevated one, wait for it
            bool restart = Array.IndexOf(args, "--restart") >= 0;
            Mutex single = null;
            bool owned = false;
            try
            {
                single = new Mutex(false, "Local\\VigilApp");
                owned = single.WaitOne(restart ? 15000 : 0);
            }
            catch (AbandonedMutexException) { owned = true; }
            catch (UnauthorizedAccessException) { }   // the running Vigil is elevated: it's there all the same
            bool fromStartup = Array.IndexOf(args, "--startup") >= 0;
            if (!owned)
            {
                // at logon both the Run key and the elevated task may fire: the second one just leaves
                if (!fromStartup) MessageBox.Show(L.T("Vigil è già acceso: lo trovi nell'area di notifica, vicino all'orologio.",
                                    "Vigil is already running: look for it in the notification area, next to the clock."),
                                "Vigil", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new VigilContext(fromStartup));
            }
            finally { single.ReleaseMutex(); }
            return 0;
        }
    }

    public static class Libs
    {
        static readonly Dictionary<string, Assembly> Loaded = new Dictionary<string, Assembly>();

        public static Assembly Resolve(object sender, ResolveEventArgs e)
        {
            string file = new AssemblyName(e.Name).Name + ".dll";
            lock (Loaded)
            {
                Assembly a;
                if (Loaded.TryGetValue(file, out a)) return a;
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(file))
                {
                    if (s == null) return null;
                    var m = new MemoryStream();
                    s.CopyTo(m);
                    a = Assembly.Load(m.ToArray());
                }
                Loaded[file] = a;
                return a;
            }
        }
    }

    // Italian if Windows speaks Italian, English otherwise. Same as Sentinel.
    public static class L
    {
        static readonly bool It = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it";
        public static string T(string it, string en) { return It ? it : en; }

        public static Icon AppIcon(Size size)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Vigil.ico"))
                return s == null ? SystemIcons.Application : new Icon(s, size);
        }
    }

    // One sensor at one moment, copied out of the library so the window never touches it from another thread.
    public class Reading
    {
        public string Key, GroupKey, Group, Kind, Name, Type;
        public int GroupRank, Index;
        public float? Value, Min, Max;
        public bool DefaultHidden;
    }

    // Everything that decides what is shown, in one place, checked by --selftest.
    public static class Rules
    {
        static readonly string[] TypeOrder = { "Temperature", "Fan", "Control", "Power", "Load", "Clock", "Voltage", "Current" };
        static readonly string[] DriverKinds = { "Cpu", "Motherboard", "SuperIO", "EmbeddedController" };
        static readonly string[] MainNames = { "CPU Package", "Core (Tctl/Tdie)", "Core (Tctl)", "Core (Tdie)", "GPU Core" };

        public static bool Visible(Reading r, bool full)
        {
            if (r.Value == null || r.DefaultHidden) return false;
            // Without the driver these chips answer 0 (temperatures, watts) or a made-up number
            // (a Ryzen's VID reads 1.55 V): only their load is real.
            if (!full && Array.IndexOf(DriverKinds, r.Kind) >= 0 && r.Type != "Load") return false;
            if (r.Type == "Temperature" && (r.Value <= 0 || r.Value >= 125)) return false;   // unplugged probes read -55 or 127
            if (r.Type == "Fan" && r.Kind.StartsWith("Gpu")) return true;   // a GPU fan stopped at idle is still a fan
            return r.Max > 0 || r.Min < 0;   // never moved from 0 since start: nothing connected
        }

        // The temperature that stands for a chip: the package of a CPU, the core of a GPU, else its hottest.
        // Hot spots run 10-20 degrees above and would ring the alarm in every game.
        public static List<Reading> MainTemps(IEnumerable<Reading> all, bool full)
        {
            var res = new List<Reading>();
            foreach (var g in all.Where(r => r.Type == "Temperature" && (r.Kind == "Cpu" || r.Kind.StartsWith("Gpu")) && Visible(r, full))
                                 .GroupBy(r => r.GroupKey))
                res.Add(g.FirstOrDefault(r => Array.IndexOf(MainNames, r.Name) >= 0) ?? g.OrderByDescending(r => r.Value).First());
            return res.OrderBy(r => r.GroupRank).ToList();
        }

        public static int KindRank(string kind)
        {
            if (kind == "Cpu") return 0;
            if (kind.StartsWith("Gpu")) return 1;
            if (kind == "Memory") return 2;
            if (kind == "Motherboard") return 3;
            if (kind == "Storage") return 4;
            if (kind == "Battery" || kind == "Psu" || kind == "PowerMonitor") return 5;
            if (kind == "Cooler") return 6;
            return 7;   // network and the rest
        }

        public static int TypeRank(string type)
        {
            int i = Array.IndexOf(TypeOrder, type);
            return i < 0 ? TypeOrder.Length : i;
        }

        // 0 fine, 1 warm (within 10 degrees of the limit), 2 over the limit
        public static int Level(float t, int limit) { return t >= limit ? 2 : t >= limit - 10 ? 1 : 0; }

        public static string Format(string type, float v)
        {
            switch (type)
            {
                case "Temperature": return v.ToString("0.0") + " °C";
                case "Fan":         return v.ToString("0") + " RPM";
                case "Control":
                case "Load":
                case "Level":
                case "Humidity":    return v.ToString("0.0") + " %";
                case "Power":       return v.ToString("0.0") + " W";
                case "Clock":       return v.ToString("0") + " MHz";
                case "Voltage":     return v.ToString("0.000") + " V";
                case "Current":     return v.ToString("0.000") + " A";
                case "Data":        return v.ToString("0.00") + " GB";
                case "SmallData":   return v.ToString("0") + " MB";
                case "Throughput":  return v >= 1048576 ? (v / 1048576).ToString("0.0") + " MB/s" : (v / 1024).ToString("0.0") + " KB/s";
                case "Frequency":   return v.ToString("0") + " Hz";
                case "Energy":      return v.ToString("0") + " mWh";
                case "Flow":        return v.ToString("0.0") + " L/h";
                case "Noise":       return v.ToString("0") + " dBA";
                case "TimeSpan":    return v.ToString("0") + " s";
                default:            return v.ToString("0.##");
            }
        }
    }

    // Rings once per episode: over the limit for a whole minute. Re-armed 5 degrees under the limit,
    // so a temperature hovering on the line doesn't ring every minute.
    public class Alarm
    {
        public const int HoldSeconds = 60, Rearm = 5;
        DateTime? since;
        bool fired;

        public bool Step(float value, int limit, DateTime now)
        {
            if (value >= limit)
            {
                if (since == null) since = now;
                if (fired || (now - since.Value).TotalSeconds < HoldSeconds) return false;
                fired = true;
                return true;
            }
            since = null;
            if (value <= limit - Rearm) fired = false;
            return false;
        }
    }

    public static class SelfTest
    {
        static Reading R(string kind, string type, string name, float? value, float? max)
        {
            var r = new Reading();
            r.Kind = kind; r.Type = type; r.Name = name; r.Value = value; r.Min = value; r.Max = max;
            r.GroupKey = "/" + kind; r.Group = kind; r.Key = "/" + kind + "/" + name; r.GroupRank = Rules.KindRank(kind);
            return r;
        }

        static void Check(bool ok, string what) { if (!ok) throw new Exception("self-test failed: " + what); }

        public static int Run()
        {
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

                Check(!Rules.Visible(R("Cpu", "Temperature", "Core (Tctl/Tdie)", 0, 0), false), "base mode hides the CPU's zero temperature");
                Check(!Rules.Visible(R("Cpu", "Voltage", "Core #1 VID", 1.55f, 1.55f), false), "base mode hides the CPU's made-up voltage");
                Check(Rules.Visible(R("Cpu", "Load", "CPU Total", 12, 40), false), "base mode shows the CPU load");
                Check(Rules.Visible(R("Cpu", "Temperature", "Core (Tctl/Tdie)", 48, 60), true), "full mode shows the CPU temperature");
                Check(!Rules.Visible(R("SuperIO", "Temperature", "Temperature #3", 127, 127), true), "an unplugged probe stays hidden");
                Check(!Rules.Visible(R("SuperIO", "Fan", "Fan #4", 0, 0), true), "an empty fan header stays hidden");
                Check(Rules.Visible(R("GpuAmd", "Fan", "GPU Fan", 0, 0), false), "a GPU fan at rest is shown");
                Check(!Rules.Visible(R("Network", "Throughput", "Upload Speed", 0, 0), false), "an idle network adapter stays hidden");
                Check(!Rules.Visible(R("GpuAmd", "Clock", "GPU Core", null, null), false), "no value, no row");

                var all = new List<Reading> {
                    R("GpuAmd", "Temperature", "GPU Hot Spot", 70, 70), R("GpuAmd", "Temperature", "GPU Core", 55, 55),
                    R("Cpu", "Temperature", "Core (CCD1)", 50, 50), R("Cpu", "Temperature", "Core (Tctl/Tdie)", 62, 62),
                    R("Storage", "Temperature", "Temperature", 40, 40) };
                var main = Rules.MainTemps(all, true);
                Check(main.Count == 2 && main[0].Name == "Core (Tctl/Tdie)" && main[1].Name == "GPU Core", "main temperatures: package and GPU core, CPU first");
                Check(Rules.MainTemps(all, false).Count == 1, "base mode: only the GPU has a temperature");
                var odd = new List<Reading> { R("GpuIntel", "Temperature", "A", 40, 40), R("GpuIntel", "Temperature", "B", 45, 45) };
                Check(Rules.MainTemps(odd, true)[0].Name == "B", "no known name: the hottest");

                Check(Rules.Format("Throughput", 35065.77f) == "34.2 KB/s", "throughput in KB/s");
                Check(Rules.Format("Throughput", 3 * 1048576f) == "3.0 MB/s", "throughput in MB/s");
                Check(Rules.Format("Temperature", 55) == "55.0 °C", "temperature");
                Check(Rules.Level(79, 90) == 0 && Rules.Level(80, 90) == 1 && Rules.Level(90, 90) == 2, "tray colour levels");

                var a = new Alarm();
                var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
                Check(!a.Step(91, 90, t0), "not at once");
                Check(!a.Step(92, 90, t0.AddSeconds(59)), "not before a minute");
                Check(a.Step(92, 90, t0.AddSeconds(60)), "after a minute");
                Check(!a.Step(93, 90, t0.AddSeconds(120)), "once per episode");
                Check(!a.Step(87, 90, t0.AddSeconds(130)), "dip under the limit");
                Check(!a.Step(91, 90, t0.AddSeconds(200)), "back over without re-arming: silent");
                Check(!a.Step(91, 90, t0.AddSeconds(300)), "still silent");
                Check(!a.Step(85, 90, t0.AddSeconds(310)), "re-armed 5 degrees under");
                Check(!a.Step(91, 90, t0.AddSeconds(320)), "new episode starts");
                Check(a.Step(91, 90, t0.AddSeconds(380)), "new episode rings");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
    }

    // Reads every sensor once a second on its own thread and hands out copies.
    public class Sensors
    {
        public Action<List<Reading>> Updated;
        public volatile bool Driver, Ready;
        public volatile string Summary = "";
        readonly ManualResetEvent stop = new ManualResetEvent(false);
        Thread thread;

        public void Start()
        {
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "Vigil sensors";
            thread.Start();
        }

        public void Stop()
        {
            stop.Set();
            if (thread != null) thread.Join(3000);
        }

        void Loop()
        {
            var c = new Computer();
            c.IsCpuEnabled = c.IsGpuEnabled = c.IsMemoryEnabled = c.IsMotherboardEnabled = c.IsControllerEnabled =
                c.IsNetworkEnabled = c.IsStorageEnabled = c.IsBatteryEnabled = c.IsPsuEnabled = true;
            try { Driver = LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled; } catch { Driver = false; }
            try { c.Open(); } catch { }
            Summary = Describe(c);
            Ready = true;
            do
            {
                var list = new List<Reading>();
                foreach (IHardware h in c.Hardware.ToList()) Collect(h, h, list);
                var cb = Updated;
                if (cb != null) cb(list);
            } while (!stop.WaitOne(1000));
            try { c.Close(); } catch { }
        }

        static void Collect(IHardware top, IHardware h, List<Reading> list)
        {
            try { h.Update(); } catch { return; }   // one chip hiccups, the others carry on
            string group = h == top ? h.Name : top.Name + " › " + h.Name;
            foreach (ISensor s in h.Sensors)
            {
                var r = new Reading();
                r.Key = s.Identifier.ToString();
                r.GroupKey = h.Identifier.ToString();
                r.Group = group;
                r.Kind = h.HardwareType.ToString();
                r.GroupRank = Rules.KindRank(top.HardwareType.ToString());
                r.Name = s.Name;
                r.Type = s.SensorType.ToString();
                r.Index = s.Index;
                r.Value = s.Value; r.Min = s.Min; r.Max = s.Max;
                r.DefaultHidden = s.IsDefaultHidden;
                list.Add(r);
            }
            foreach (IHardware sub in h.SubHardware) Collect(top, sub, list);
        }

        // "What's inside", in words: chips from the library, RAM and disks from Windows (no admin needed).
        static string Describe(Computer c)
        {
            var chips = new List<string>();
            string board = null;
            foreach (IHardware h in c.Hardware)
            {
                string k = h.HardwareType.ToString();
                if (k == "Cpu" || k.StartsWith("Gpu")) chips.Add(h.Name);
                if (k == "Motherboard") board = h.Name;
            }
            var lines = new List<string>();
            if (chips.Count > 0) lines.Add(string.Join("  ·  ", chips));
            string second = string.Join("  ·  ", new[] { Ram(), board }.Where(x => !string.IsNullOrEmpty(x)));
            if (second != "") lines.Add(second);
            string disks = Disks();
            if (disks != "") lines.Add(disks);
            return string.Join("\n", lines);
        }

        static IEnumerable<ManagementBaseObject> Wmi(string query)
        {
            using (var s = new ManagementObjectSearcher(query))
                return s.Get().Cast<ManagementBaseObject>().ToList();
        }

        static string Ram()
        {
            try
            {
                var sizes = new List<long>();
                long speed = 0;
                string type = "";
                foreach (var m in Wmi("SELECT Capacity, ConfiguredClockSpeed, Speed, SMBIOSMemoryType FROM Win32_PhysicalMemory"))
                {
                    sizes.Add(Convert.ToInt64(m["Capacity"]) >> 30);
                    long s = Convert.ToInt64(m["ConfiguredClockSpeed"] ?? 0L);
                    if (s == 0) s = Convert.ToInt64(m["Speed"] ?? 0L);
                    if (s > speed) speed = s;
                    switch (Convert.ToInt32(m["SMBIOSMemoryType"] ?? 0))
                    {
                        case 24: type = "DDR3"; break;
                        case 26: type = "DDR4"; break;
                        case 30: type = "LPDDR4"; break;
                        case 34: type = "DDR5"; break;
                        case 35: type = "LPDDR5"; break;
                    }
                }
                if (sizes.Count == 0) return null;
                int slots = Wmi("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray").Sum(m => Convert.ToInt32(m["MemoryDevices"] ?? 0));
                string sticks = sizes.Distinct().Count() == 1 ? sizes.Count + " × " + sizes[0] + " GB" : string.Join(" + ", sizes) + " GB";
                string used = slots >= sizes.Count ? string.Format(L.T(", {0} slot su {1}", ", {0} of {1} slots"), sizes.Count, slots) : "";
                string at = speed > 0 ? string.Format(L.T(" a {0} MT/s", " at {0} MT/s"), speed) : "";
                return string.Format("RAM {0} GB {1}{2} ({3}{4})", sizes.Sum(), type, at, sticks, used).Replace("  ", " ");
            }
            catch { return null; }
        }

        static string Disks()
        {
            try
            {
                var names = Wmi("SELECT Model, Size FROM Win32_DiskDrive").Where(m => Convert.ToDouble(m["Size"] ?? 0.0) > 0).Select(m =>
                {   // no size: an empty card reader
                    double gb = Convert.ToDouble(m["Size"] ?? 0) / 1e9;
                    string size = gb >= 1000 ? (gb / 1000).ToString("0.#") + " TB" : gb.ToString("0") + " GB";
                    return Convert.ToString(m["Model"]).Trim() + " (" + size + ")";
                }).ToList();
                return names.Count == 0 ? "" : L.T("Dischi: ", "Disks: ") + string.Join(", ", names);
            }
            catch { return ""; }
        }
    }

    // The tray shows the hottest main temperature as a number, on dark, amber or red.
    public static class TrayIcons
    {
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        public static Icon Number(int t, int level)
        {
            int w = SystemInformation.SmallIconSize.Width;
            Color bg = level == 2 ? Color.FromArgb(0xdc, 0x26, 0x26) : level == 1 ? Color.FromArgb(0xd9, 0x77, 0x06) : Color.FromArgb(0x27, 0x27, 0x2a);
            using (var bmp = new Bitmap(w, w))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                int r = w / 4;
                using (var path = new GraphicsPath())
                using (var b = new SolidBrush(bg))
                {
                    path.AddArc(0, 0, r * 2, r * 2, 180, 90);
                    path.AddArc(w - 1 - r * 2, 0, r * 2, r * 2, 270, 90);
                    path.AddArc(w - 1 - r * 2, w - 1 - r * 2, r * 2, r * 2, 0, 90);
                    path.AddArc(0, w - 1 - r * 2, r * 2, r * 2, 90, 90);
                    path.CloseFigure();
                    g.FillPath(b, path);
                }
                string s = t.ToString();
                using (var f = new Font("Segoe UI", w * (s.Length > 2 ? 0.5f : 0.7f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(s, f, Brushes.White, new RectangleF(-1, 0, w + 2, w + 1), sf);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        public static void Free(Icon icon)
        {
            if (icon == null) return;
            DestroyIcon(icon.Handle);
            icon.Dispose();
        }
    }

    public class VigilContext : ApplicationContext
    {
        public static readonly bool Admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        const string RegistryPath = "SOFTWARE\\VigilApp";
        const string RunPath = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
        const string TaskName = "Vigil";

        // Portable version: a Vigil.ini next to the exe. Settings live there, the registry is never touched.
        static readonly string IniPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Vigil.ini");
        public static readonly bool Portable = File.Exists(IniPath);
        const string IniHeader = "; Vigil settings. While this file sits next to Vigil.exe, nothing goes to the registry.";

        readonly NotifyIcon tray;
        readonly MainForm form;
        readonly Sensors sensors = new Sensors();
        readonly Icon appIcon;
        readonly Dictionary<string, Alarm> alarms = new Dictionary<string, Alarm>();
        Icon numberIcon;
        string iconKey = "";
        bool modeShown;

        bool alertsOn = true;
        int alertAt = 90;

        public VigilContext(bool fromStartup)
        {
            LoadSettings();
            appIcon = L.AppIcon(SystemInformation.SmallIconSize);

            tray = new NotifyIcon();
            tray.Icon = appIcon;
            tray.Text = "Vigil";
            tray.Visible = true;
            tray.MouseDoubleClick += (s, e) => ShowForm();

            var menu = new ContextMenuStrip();
            var header = new ToolStripMenuItem("Vigil");
            header.Enabled = false;
            header.Font = new Font(menu.Font, FontStyle.Bold);
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("Apri Vigil", "Open Vigil"), null, (s, e) => ShowForm());
            if (!Admin) menu.Items.Add(L.T("Riavvia come amministratore", "Restart as administrator"), null, (s, e) => Restart(true));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("Esci", "Exit"), null, (s, e) => Exit());
            tray.ContextMenuStrip = menu;

            bool startup = !Portable && IsStartupEnabled();
            if (startup) RefreshStartup();

            form = new MainForm(startup, alertsOn, alertAt);
            if (Portable) form.ShowPortable();
            form.StartupChanged += (s, e) => { SetStartup(form.chkStartup.Checked); form.SetStartupQuietly(IsStartupEnabled()); };
            form.AlertsChanged += (s, e) =>
            {
                alertsOn = form.chkAlerts.Checked;
                alertAt = (int)form.nudAlertAt.Value;
                alarms.Clear();
                iconKey = "";
                SaveSettings();
            };
            form.RestartAsAdmin += (s, e) => Restart(true);
            form.RestartPlain += (s, e) => Restart(false);
            var handle = form.Handle;   // the window must exist, even hidden, to receive the readings

            sensors.Updated = list =>
            {
                try { form.BeginInvoke((Action)(() => OnReadings(list))); }
                catch (InvalidOperationException) { }   // closing
            };
            sensors.Start();

            if (!fromStartup) ShowForm();   // at login it stays quiet in the tray
        }

        void OnReadings(List<Reading> list)
        {
            bool full = Admin && sensors.Driver;
            if (!modeShown && sensors.Ready)
            {
                form.SetMode(Admin, sensors.Driver, sensors.Summary);
                modeShown = true;
            }

            var mains = Rules.MainTemps(list, full);
            UpdateTray(mains);
            if (alertsOn)
                foreach (var r in mains)
                {
                    Alarm a;
                    if (!alarms.TryGetValue(r.GroupKey, out a)) alarms[r.GroupKey] = a = new Alarm();
                    if (a.Step(r.Value.Value, alertAt, DateTime.Now))
                        tray.ShowBalloonTip(15000, "Vigil",
                            string.Format(L.T("{0}: {1:0} °C da più di un minuto (il tuo limite è {2} °C).",
                                              "{0}: {1:0} °C for over a minute (your limit is {2} °C)."), r.Group, r.Value.Value, alertAt),
                            ToolTipIcon.Warning);
                }
            form.ShowReadings(list, full);
        }

        void UpdateTray(List<Reading> mains)
        {
            string tip = "Vigil";
            if (mains.Count == 0)
            {
                if (iconKey != "app") { tray.Icon = appIcon; TrayIcons.Free(numberIcon); numberIcon = null; iconKey = "app"; }
            }
            else
            {
                float hottest = mains.Max(r => r.Value.Value);
                int t = (int)Math.Round(hottest);
                int level = Rules.Level(hottest, alertAt);
                string key = t + ":" + level;
                if (key != iconKey)
                {
                    Icon old = numberIcon;
                    numberIcon = TrayIcons.Number(t, level);
                    tray.Icon = numberIcon;
                    TrayIcons.Free(old);
                    iconKey = key;
                }
                tip = "Vigil: " + string.Join(", ", mains.Select(r => (r.Kind == "Cpu" ? "CPU " : "GPU ") + r.Value.Value.ToString("0") + " °C"));
            }
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        void ShowForm()
        {
            if (form.Visible) { if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal; form.Activate(); }
            else form.Show();
        }

        // Elevated: through UAC. Plain: the same rights as now (after installing PawnIO).
        void Restart(bool elevate)
        {
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, "--restart");
                psi.UseShellExecute = true;
                if (elevate) psi.Verb = "runas";
                Process.Start(psi);
                Exit();
            }
            catch (Win32Exception) { }   // UAC said no: stay as we are
        }

        void Exit()
        {
            sensors.Updated = null;
            sensors.Stop();   // closes the library: drivers and chips are left as they were
            tray.Visible = false;
            TrayIcons.Free(numberIcon);
            form.Dispose();
            Application.Exit();
        }

        // ── Start with Windows ───────────────────────────────────────────────────
        // Base mode: the Run key, like Sentinel. As administrator: a scheduled task "with highest
        // privileges", the only way to start elevated at logon without a UAC prompt every morning.

        bool RunKeySet()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunPath, false)) return k != null && k.GetValue("Vigil") != null; }
            catch { return false; }
        }

        void SetRunKey(bool on)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunPath, true))
            {
                if (on) k.SetValue("Vigil", "\"" + Application.ExecutablePath + "\" --startup");   // quoted: folders with spaces
                else k.DeleteValue("Vigil", false);
            }
        }

        static int Schtasks(string args, bool elevate)
        {
            var psi = new ProcessStartInfo("schtasks.exe", args);
            if (elevate) { psi.UseShellExecute = true; psi.Verb = "runas"; psi.WindowStyle = ProcessWindowStyle.Hidden; }
            else { psi.UseShellExecute = false; psi.CreateNoWindow = true; }
            using (var p = Process.Start(psi))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        static bool TaskExists() { return Schtasks("/Query /TN " + TaskName, false) == 0; }

        static void CreateTask()
        {
            // Through XML: schtasks' own switches would stop Vigil on battery and after 72 hours.
            string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
                "  <RegistrationInfo><Description>Vigil: all sensors at logon, without a UAC prompt.</Description></RegistrationInfo>\n" +
                "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId><Delay>PT15S</Delay></LogonTrigger></Triggers>\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\n" +
                "  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>7</Priority></Settings>\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(Application.ExecutablePath) + "</Command><Arguments>--startup</Arguments></Exec></Actions>\n" +
                "</Task>\n";
            string file = Path.Combine(Path.GetTempPath(), "vigil-task.xml");
            File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
            try
            {
                if (Schtasks("/Create /TN " + TaskName + " /XML \"" + file + "\" /F", false) != 0)
                    throw new Exception(L.T("attività pianificata non creata", "scheduled task not created"));
            }
            finally { File.Delete(file); }
        }

        bool IsStartupEnabled() { return RunKeySet() || TaskExists(); }

        // At every start: the exe may have moved, the entry must follow it
        void RefreshStartup()
        {
            try
            {
                if (Admin && TaskExists()) CreateTask();
                if (RunKeySet()) SetRunKey(true);
            }
            catch { }
        }

        void SetStartup(bool on)
        {
            try
            {
                if (on && Admin) { CreateTask(); SetRunKey(false); }
                else if (on) { if (!TaskExists()) SetRunKey(true); }   // an elevated task is already there: it's the better one
                else
                {
                    SetRunKey(false);
                    if (TaskExists() && Schtasks("/Delete /TN " + TaskName + " /F", !Admin) != 0)
                        throw new Exception(L.T("attività pianificata non rimossa", "scheduled task not removed"));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(L.T("Avvio con Windows non cambiato: ", "Start with Windows not changed: ") + ex.Message,
                                "Vigil", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ── Settings: registry, or Vigil.ini in the portable version ─────────────

        void LoadSettings()
        {
            try
            {
                var v = ReadValues();
                string s;
                if (v.TryGetValue("AlertsEnabled", out s)) alertsOn = Convert.ToBoolean(s);
                if (v.TryGetValue("AlertAt", out s)) alertAt = Math.Max(50, Math.Min(110, Convert.ToInt32(s)));
            }
            catch { }   // broken values: defaults win
        }

        Dictionary<string, string> ReadValues()
        {
            var d = new Dictionary<string, string>();
            if (Portable)
            {
                foreach (string line in File.ReadAllLines(IniPath))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0 && !line.StartsWith(";")) d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                return d;
            }
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                if (key != null)
                    foreach (string name in key.GetValueNames())
                        d[name] = Convert.ToString(key.GetValue(name), CultureInfo.InvariantCulture);
            return d;
        }

        void SaveSettings()
        {
            var values = new Dictionary<string, object> { { "AlertsEnabled", alertsOn }, { "AlertAt", alertAt } };
            try
            {
                if (Portable)
                {
                    var lines = new List<string> { IniHeader };
                    foreach (var kv in values) lines.Add(kv.Key + "=" + Convert.ToString(kv.Value, CultureInfo.InvariantCulture));
                    File.WriteAllLines(IniPath, lines.ToArray());
                    return;
                }
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                    foreach (var kv in values) key.SetValue(kv.Key, kv.Value);
            }
            catch { }   // read-only stick or locked key: the change doesn't stick, Vigil keeps working
        }
    }
}
