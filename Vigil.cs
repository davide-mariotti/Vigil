using System;
using System.Collections;
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
using System.Web.Script.Serialization;
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
            // The libraries are packed inside the exe: one file to download, like Sentinel.
            AppDomain.CurrentDomain.AssemblyResolve += Libs.Resolve;
            return Run(args);
        }

        // Kept apart from Main: the JIT must not meet a library type before the resolver is in place.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int Run(string[] args)
        {
            if (Array.IndexOf(args, "--selftest") >= 0) return SelfTest.Run();

            // --restart: the old Vigil is closing to hand over to a new one, wait for it
            bool restart = Array.IndexOf(args, "--restart") >= 0;
            bool fromStartup = Array.IndexOf(args, "--startup") >= 0;
            Mutex single = null;
            bool owned = false;
            try
            {
                single = new Mutex(false, "Local\\VigilApp");
                owned = single.WaitOne(restart ? 15000 : 0);
            }
            catch (AbandonedMutexException) { owned = true; }
            catch (UnauthorizedAccessException) { }   // the running Vigil is elevated: it's there all the same
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
                // any crash goes through AppDomain.UnhandledException, where the fans are handed back to the BIOS
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
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

        static byte[] Resource(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return null;
                var m = new MemoryStream();
                s.CopyTo(m);
                return m.ToArray();
            }
        }

        public static Stream Open(string name) { return Assembly.GetExecutingAssembly().GetManifestResourceStream(name); }

        public static Assembly Resolve(object sender, ResolveEventArgs e)
        {
            string file = new AssemblyName(e.Name).Name + ".dll";
            lock (Loaded)
            {
                Assembly a;
                if (Loaded.TryGetValue(file, out a)) return a;
                byte[] bytes = Resource(file);
                if (bytes == null) return null;
                a = Assembly.Load(bytes);
                Loaded[file] = a;
                return a;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibrary(string path);

        // A native DLL can't be loaded from memory: it goes to disk once, then into the process by full path.
        public static void LoadNative(string name, string folder)
        {
            byte[] bytes = Resource(name);
            string path = Path.Combine(folder, name);
            Directory.CreateDirectory(folder);
            if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length) File.WriteAllBytes(path, bytes);
            if (LoadLibrary(path) == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    // Italian if Windows speaks Italian, English otherwise. Same as Sentinel.
    public static class L
    {
        public static readonly bool It = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it";
        public static string T(string it, string en) { return It ? it : en; }

        public static Icon AppIcon(Size size)
        {
            using (var s = Libs.Open("Vigil.ico"))
                return s == null ? SystemIcons.Application : new Icon(s, size);
        }
    }

    public static class Json
    {
        static readonly JavaScriptSerializer S = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        public static string Write(object o) { return S.Serialize(o); }
        public static T Read<T>(string s) { return S.Deserialize<T>(s); }
    }

    // One sensor at one moment, copied out of the library so nothing else ever touches it from another thread.
    public class Reading
    {
        public string Key, GroupKey, Group, Kind, Name, Type, CtlMode;
        public int GroupRank, Index;
        public float? Value, Min, Max;
        public bool DefaultHidden, Ctl;
    }

    // Everything that decides what is shown, in one place, checked by --selftest.
    public static class Rules
    {
        static readonly string[] DriverKinds = { "Cpu", "Motherboard", "SuperIO", "EmbeddedController" };
        static readonly string[] MainNames = { "CPU Package", "Core (Tctl/Tdie)", "Core (Tctl)", "Core (Tdie)", "GPU Core" };

        public static bool Visible(Reading r, bool full)
        {
            if (r.Value == null || r.DefaultHidden) return false;
            // Without the driver these chips answer 0 (temperatures, watts) or a made-up number
            // (a Ryzen's VID reads 1.55 V): only their load is real.
            if (!full && Array.IndexOf(DriverKinds, r.Kind) >= 0 && r.Type != "Load") return false;
            // unplugged probes read -55 or 127; a RAM stick's "Temperature Sensor Resolution" reads 0.25: nothing in a PC is under 5 °C
            if (r.Type == "Temperature" && (r.Value < 5 || r.Value >= 125)) return false;
            if ((r.Type == "Fan" || r.Type == "Control") && r.Kind.StartsWith("Gpu")) return true;   // a GPU fan stopped at idle is still a fan
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

        // 0 fine, 1 warm (within 10 degrees of the limit), 2 over the limit
        public static int Level(float t, int limit) { return t >= limit ? 2 : t >= limit - 10 ? 1 : 0; }
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

    // What the user asked for one fan. Never changed after creation: the sensor thread reads it while the window writes a new one.
    public class FanPlan
    {
        public string Mode = "auto", Source, Name;
        public int Pct = 50;
        public int[] Temps, Pcts;
    }

    // The fan rules, all of them safety rules first. Checked by --selftest.
    public static class Fans
    {
        public const int MinPct = 20;   // a fan you command never goes under this, whatever the curve says
        public const int StepDown = 2;  // % per second when slowing down: no fan that hunts up and down

        static int Clamp(int v, int lo, int hi) { return Math.Max(lo, Math.Min(hi, v)); }

        // Whatever comes from the window is checked here: bad values become safe ones, never an exception.
        public static FanPlan Clean(FanPlan p)
        {
            var c = new FanPlan();
            c.Mode = p != null && (p.Mode == "fixed" || p.Mode == "curve") ? p.Mode : "auto";
            if (p == null) return c;
            c.Pct = Clamp(p.Pct, MinPct, 100);
            c.Source = p.Source;
            c.Name = p.Name == null ? null : p.Name.Trim().Length == 0 ? null : p.Name.Trim().Substring(0, Math.Min(40, p.Name.Trim().Length));
            if (p.Temps != null && p.Pcts != null && p.Temps.Length == p.Pcts.Length && p.Temps.Length >= 2 && p.Temps.Length <= 8)
            {
                var pts = p.Temps.Select((t, i) => new[] { Clamp(t, 0, 120), Clamp(p.Pcts[i], MinPct, 100) }).OrderBy(x => x[0]).ToList();
                for (int i = 1; i < pts.Count; i++) if (pts[i][0] <= pts[i - 1][0]) pts[i][0] = pts[i - 1][0] + 1;   // strictly rising
                c.Temps = pts.Select(x => x[0]).ToArray();
                c.Pcts = pts.Select(x => x[1]).ToArray();
            }
            else { c.Temps = new[] { 40, 60, 75, 85 }; c.Pcts = new[] { 30, 45, 70, 100 }; }
            if (c.Mode == "curve" && string.IsNullOrEmpty(c.Source)) c.Mode = "auto";
            return c;
        }

        public static float Curve(int[] temps, int[] pcts, float t)
        {
            if (t <= temps[0]) return pcts[0];
            for (int i = 1; i < temps.Length; i++)
                if (t <= temps[i]) return pcts[i - 1] + (pcts[i] - pcts[i - 1]) * (t - temps[i - 1]) / (temps[i] - temps[i - 1]);
            return pcts[pcts.Length - 1];
        }

        // null = hand it to the BIOS (or the graphics driver)
        public static float? Target(FanPlan p, IDictionary<string, float> temps, bool emergency)
        {
            if (p == null || p.Mode == "auto") return null;
            if (emergency) return 100;
            if (p.Mode == "fixed") return Math.Max(MinPct, Math.Min(100, p.Pct));
            float t;
            if (p.Source == null || !temps.TryGetValue(p.Source, out t)) return null;   // the probe is gone: the BIOS knows better than a guess
            return Math.Max(MinPct, Math.Min(100, Curve(p.Temps, p.Pcts, t)));
        }

        // Up at once, down slowly
        public static float Smooth(float? prev, float target)
        {
            if (prev == null || target >= prev.Value) return target;
            return Math.Max(target, prev.Value - StepDown);
        }

        // CPU or GPU over the limit: every fan you command goes to 100% until it's 5 degrees under
        public static bool Emergency(bool was, float hottest, int limit)
        {
            return was ? hottest > limit - Alarm.Rearm : hottest >= limit;
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
                Check(!Rules.Visible(R("Cpu", "Temperature", "Core (Tctl/Tdie)", 0, 0), false), "base mode hides the CPU's zero temperature");
                Check(!Rules.Visible(R("Cpu", "Voltage", "Core #1 VID", 1.55f, 1.55f), false), "base mode hides the CPU's made-up voltage");
                Check(Rules.Visible(R("Cpu", "Load", "CPU Total", 12, 40), false), "base mode shows the CPU load");
                Check(Rules.Visible(R("Cpu", "Temperature", "Core (Tctl/Tdie)", 48, 60), true), "full mode shows the CPU temperature");
                Check(!Rules.Visible(R("SuperIO", "Temperature", "Temperature #3", 127, 127), true), "an unplugged probe stays hidden");
                Check(!Rules.Visible(R("SuperIO", "Fan", "Fan #4", 0, 0), true), "an empty fan header stays hidden");
                Check(!Rules.Visible(R("Memory", "Temperature", "Temperature Sensor Resolution", 0.25f, 0.25f), true), "a probe's resolution is not a temperature");
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
                Check(Rules.Level(79, 90) == 0 && Rules.Level(80, 90) == 1 && Rules.Level(90, 90) == 2, "tray colour levels");

                var a = new Alarm();
                var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
                Check(!a.Step(91, 90, t0), "not at once");
                Check(!a.Step(92, 90, t0.AddSeconds(59)), "not before a minute");
                Check(a.Step(92, 90, t0.AddSeconds(60)), "after a minute");
                Check(!a.Step(93, 90, t0.AddSeconds(120)), "once per episode");
                Check(!a.Step(87, 90, t0.AddSeconds(130)), "dip under the limit");
                Check(!a.Step(91, 90, t0.AddSeconds(200)), "back over without re-arming: silent");
                Check(!a.Step(85, 90, t0.AddSeconds(310)), "re-armed 5 degrees under");
                Check(!a.Step(91, 90, t0.AddSeconds(320)), "new episode starts");
                Check(a.Step(91, 90, t0.AddSeconds(380)), "new episode rings");

                // fans: the curve, the floor, the emergency, the slow way down, the BIOS when in doubt
                int[] temps = { 40, 60, 80 }, pcts = { 30, 50, 100 };
                Check(Fans.Curve(temps, pcts, 20) == 30 && Fans.Curve(temps, pcts, 50) == 40 && Fans.Curve(temps, pcts, 90) == 100, "curve: flat ends, straight lines between");
                var curve = new FanPlan { Mode = "curve", Source = "/cpu/t", Temps = temps, Pcts = pcts };
                var t = new Dictionary<string, float> { { "/cpu/t", 70 } };
                Check(Fans.Target(curve, t, false) == 75, "curve follows its probe");
                Check(Fans.Target(curve, new Dictionary<string, float>(), false) == null, "probe gone: back to the BIOS");
                Check(Fans.Target(curve, t, true) == 100, "emergency: 100%");
                Check(Fans.Target(new FanPlan { Mode = "auto" }, t, true) == null, "a fan left to the BIOS stays there, even in an emergency");
                Check(Fans.Target(new FanPlan { Mode = "fixed", Pct = 5 }, t, false) == Fans.MinPct, "never under the floor");
                Check(Fans.Target(null, t, false) == null, "no plan: BIOS");
                Check(Fans.Smooth(40, 70) == 70 && Fans.Smooth(70, 40) == 68 && Fans.Smooth(null, 40) == 40, "up at once, down 2% a second");
                Check(Fans.Emergency(false, 90, 90) && Fans.Emergency(true, 86, 90) && !Fans.Emergency(true, 85, 90) && !Fans.Emergency(false, 89, 90), "emergency in at the limit, out 5 under");
                var dirty = Fans.Clean(new FanPlan { Mode = "rocket", Pct = 300, Temps = new[] { 90, 30, 30 }, Pcts = new[] { 0, 10, 200 } });
                Check(dirty.Mode == "auto" && dirty.Pct == 100, "unknown mode: auto; percent clamped");
                Check(dirty.Temps.SequenceEqual(new[] { 30, 31, 90 }) && dirty.Pcts.All(p => p >= Fans.MinPct && p <= 100), "curve points sorted, rising, clamped");
                Check(Fans.Clean(new FanPlan { Mode = "curve" }).Mode == "auto", "a curve without a probe is auto");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
    }

    // Reads every sensor once a second on its own thread, drives the fans, hands out copies.
    // All library calls happen on this thread.
    public class Sensors
    {
        public Action<List<Reading>> Updated;
        public volatile bool Driver, Ready, InEmergency;
        public volatile Dictionary<string, object> Pc = new Dictionary<string, object>();
        public volatile Dictionary<string, FanPlan> Plans = new Dictionary<string, FanPlan>();
        public volatile int AlertAt = 90;

        readonly ManualResetEvent stop = new ManualResetEvent(false);
        readonly Dictionary<string, IControl> controls = new Dictionary<string, IControl>();
        readonly Dictionary<string, float> sent = new Dictionary<string, float>();
        readonly HashSet<string> touched = new HashSet<string>();
        Thread thread;

        public bool Full { get { return VigilContext.Admin && Driver; } }

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
            Pc = Describe(c);
            Ready = true;
            do
            {
                var list = new List<Reading>();
                controls.Clear();
                foreach (IHardware h in c.Hardware.ToList()) Collect(h, h, list);
                ApplyFans(list);
                var cb = Updated;
                if (cb != null) cb(list);
            } while (!stop.WaitOne(1000));
            RestoreFans();
            try { c.Close(); } catch { }
        }

        void Collect(IHardware top, IHardware h, List<Reading> list)
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
                if (s.Control != null)
                {
                    r.Ctl = true;
                    r.CtlMode = s.Control.ControlMode.ToString();
                    controls[r.Key] = s.Control;
                }
                list.Add(r);
            }
            foreach (IHardware sub in h.SubHardware) Collect(top, sub, list);
        }

        void ApplyFans(List<Reading> list)
        {
            var plans = Plans;
            if (plans.Count == 0 && touched.Count == 0) return;
            var temps = new Dictionary<string, float>();
            foreach (var r in list) if (r.Type == "Temperature" && r.Value != null && Rules.Visible(r, Full)) temps[r.Key] = r.Value.Value;
            var mains = Rules.MainTemps(list, Full);
            InEmergency = Fans.Emergency(InEmergency, mains.Count > 0 ? mains.Max(r => r.Value.Value) : 0, AlertAt);

            foreach (var kv in controls)
            {
                FanPlan p;
                plans.TryGetValue(kv.Key, out p);
                float? target = Fans.Target(p, temps, InEmergency);
                IControl ctl = kv.Value;
                try
                {
                    if (target == null)
                    {
                        if (touched.Remove(kv.Key)) { ctl.SetDefault(); sent.Remove(kv.Key); }
                        continue;
                    }
                    float prev;
                    float next = Fans.Smooth(sent.TryGetValue(kv.Key, out prev) ? prev : (float?)null, target.Value);
                    next = Math.Max(ctl.MinSoftwareValue, Math.Min(ctl.MaxSoftwareValue, next));
                    ctl.SetSoftware(next);   // every second: after sleep the BIOS may have taken it back
                    sent[kv.Key] = next;
                    touched.Add(kv.Key);
                }
                catch { }   // a chip that refuses: it stays as it is, the others carry on
            }
        }

        // Closing or crashing: every fan Vigil touched goes back to the BIOS
        public void RestoreFans()
        {
            foreach (var key in touched.ToList())
            {
                IControl ctl;
                if (controls.TryGetValue(key, out ctl)) try { ctl.SetDefault(); } catch { }
            }
            touched.Clear();
        }

        // What's inside, as data: chips from the library, RAM and disks from Windows (no admin needed). The window writes the words.
        static Dictionary<string, object> Describe(Computer c)
        {
            var d = new Dictionary<string, object>();
            var cpu = new List<string>();
            var gpu = new List<string>();
            foreach (IHardware h in c.Hardware)
            {
                string k = h.HardwareType.ToString();
                if (k == "Cpu") cpu.Add(h.Name);
                if (k.StartsWith("Gpu")) gpu.Add(h.Name);
                if (k == "Motherboard") d["board"] = h.Name;
            }
            d["cpu"] = cpu;
            d["gpu"] = gpu;
            d["ram"] = Ram();
            d["disks"] = Disks();
            return d;
        }

        static List<ManagementBaseObject> Wmi(string query)
        {
            using (var s = new ManagementObjectSearcher(query))
                return s.Get().Cast<ManagementBaseObject>().ToList();
        }

        static Dictionary<string, object> Ram()
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
                var d = new Dictionary<string, object>();
                d["sticks"] = sizes;
                d["speed"] = speed;
                d["type"] = type;
                d["slots"] = Wmi("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray").Sum(m => Convert.ToInt32(m["MemoryDevices"] ?? 0));
                return d;
            }
            catch { return null; }
        }

        static List<Dictionary<string, object>> Disks()
        {
            try
            {
                return Wmi("SELECT Model, Size FROM Win32_DiskDrive").Where(m => Convert.ToDouble(m["Size"] ?? 0.0) > 0)   // no size: an empty card reader
                    .Select(m => new Dictionary<string, object> { { "name", Convert.ToString(m["Model"]).Trim() }, { "bytes", Convert.ToDouble(m["Size"]) } })
                    .ToList();
            }
            catch { return new List<Dictionary<string, object>>(); }
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
            Color bg = level == 2 ? Color.FromArgb(0xdc, 0x26, 0x26) : level == 1 ? Color.FromArgb(0xd9, 0x77, 0x06) : Color.FromArgb(0x0b, 0x0f, 0x0d);
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
                    if (level == 0) using (var edge = new Pen(Color.FromArgb(0x2f, 0xe3, 0x7f), Math.Max(1, w / 16))) g.DrawPath(edge, path);
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
        const int HistorySize = 600;   // one reading a second: ten minutes

        // Portable version: a Vigil.ini next to the exe. Settings live there, the registry is never touched.
        static readonly string IniPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Vigil.ini");
        public static readonly bool Portable = File.Exists(IniPath);
        const string IniHeader = "; Vigil settings. While this file sits next to Vigil.exe, nothing goes to the registry.";

        readonly NotifyIcon tray;
        readonly MainForm form;
        readonly Sensors sensors = new Sensors();
        readonly Icon appIcon;
        readonly Dictionary<string, Alarm> alarms = new Dictionary<string, Alarm>();
        readonly Dictionary<string, List<float>> history = new Dictionary<string, List<float>>();
        Icon numberIcon;
        string iconKey = "";
        bool startup;

        bool alertsOn = true;
        int alertAt = 90;

        public VigilContext(bool fromStartup)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => sensors.RestoreFans();
            LoadSettings();
            sensors.AlertAt = alertAt;
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

            startup = !Portable && IsStartupEnabled();
            if (startup) RefreshStartup();

            form = new MainForm(DataFolder());
            form.Message += OnMessage;
            var handle = form.Handle;   // the window must exist, even hidden, to receive the readings

            sensors.Updated = list =>
            {
                try { form.BeginInvoke((Action)(() => OnReadings(list))); }
                catch (InvalidOperationException) { }   // closing
            };
            sensors.Start();

            if (!fromStartup) ShowForm();   // at login it stays quiet in the tray
        }

        // WebView2 keeps its cache here. Portable: in TEMP, so the stick stays clean.
        // Elevated and plain Vigil get separate folders: WebView2 refuses one made with other rights.
        static string DataFolder()
        {
            string root = Portable ? Path.GetTempPath() : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "Vigil", Admin ? "web-admin" : "web");
        }

        List<Reading> lastRows = new List<Reading>();

        void OnReadings(List<Reading> list)
        {
            bool full = sensors.Full;
            var rows = list.Where(r => Rules.Visible(r, full)).ToList();
            foreach (var r in rows)
            {
                List<float> h;
                if (!history.TryGetValue(r.Key, out h)) history[r.Key] = h = new List<float>(HistorySize + 1);
                h.Add(r.Value.Value);
                if (h.Count > HistorySize) h.RemoveAt(0);   // ponytail: O(n) shift, 600 floats a sensor, nothing to measure
            }
            lastRows = rows;

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
            if (!form.PageReady) return;
            if (!initWithPc) SendInit();   // the page opened before the sensors were ready: now it gets the whole picture
            else form.Post(Data(rows, mains));
        }

        bool initWithPc;

        static object Row(Reading r)
        {
            var d = new Dictionary<string, object> {
                { "k", r.Key }, { "gk", r.GroupKey }, { "g", r.Group }, { "kind", r.Kind }, { "rank", r.GroupRank },
                { "n", r.Name }, { "type", r.Type }, { "i", r.Index },
                { "v", Math.Round((double)r.Value.Value, 3) },
                { "min", r.Min.HasValue ? Math.Round((double)r.Min.Value, 3) : (object)null },
                { "max", r.Max.HasValue ? Math.Round((double)r.Max.Value, 3) : (object)null } };
            if (r.Ctl) { d["ctl"] = true; d["mode"] = r.CtlMode; }
            return d;
        }

        object Data(List<Reading> rows, List<Reading> mains)
        {
            return new Dictionary<string, object> {
                { "t", "data" }, { "full", sensors.Full }, { "emergency", sensors.InEmergency },
                { "rows", rows.Select(r => Row(r)).ToList() }, { "main", mains.Select(r => r.Key).ToList() } };
        }

        object Settings()
        {
            return new Dictionary<string, object> {
                { "t", "settings" }, { "startup", startup }, { "alertsOn", alertsOn }, { "alertAt", alertAt },
                { "fans", sensors.Plans } };
        }

        void SendInit()
        {
            initWithPc = sensors.Ready;
            form.Post(new Dictionary<string, object> {
                { "t", "init" }, { "lang", L.It ? "it" : "en" },
                { "version", Assembly.GetExecutingAssembly().GetName().Version.ToString(3) },
                { "admin", Admin }, { "driver", sensors.Driver }, { "ready", sensors.Ready }, { "portable", Portable },
                { "pc", sensors.Pc }, { "minPct", Fans.MinPct },
                { "history", lastRows.Where(r => history.ContainsKey(r.Key)).ToDictionary(r => r.Key, r => (object)history[r.Key].Select(v => Math.Round((double)v, 2)).ToList()) } });
            form.Post(Settings());
            if (lastRows.Count > 0) form.Post(Data(lastRows, Rules.MainTemps(lastRows, sensors.Full)));
        }

        // Everything the window asks for. The page is ours, but what it sends is still checked here.
        void OnMessage(Dictionary<string, object> m)
        {
            object v;
            switch (Convert.ToString(m.TryGetValue("t", out v) ? v : ""))
            {
                case "ready": SendInit(); break;
                case "admin": Restart(true); break;
                case "restart": Restart(false); break;
                case "pawnio": OpenLink("https://pawnio.eu/"); break;
                case "open":
                    string url = m.TryGetValue("url", out v) ? Convert.ToString(v) : "";
                    if (url == "https://github.com/davide-mariotti/Vigil" || url == "https://ko-fi.com/Z0X32ATNHL" || url == "https://davide-mariotti.github.io/apps/vigil")
                        OpenLink(url);
                    break;
                case "set":
                    if (m.TryGetValue("startup", out v) && !Portable) { SetStartup(Convert.ToBoolean(v)); startup = IsStartupEnabled(); }
                    if (m.TryGetValue("alertsOn", out v)) alertsOn = Convert.ToBoolean(v);
                    if (m.TryGetValue("alertAt", out v)) alertAt = Math.Max(50, Math.Min(110, Convert.ToInt32(v)));
                    sensors.AlertAt = alertAt;
                    alarms.Clear();
                    iconKey = "";
                    SaveSettings();
                    form.Post(Settings());
                    break;
                case "fan":
                    string key = m.TryGetValue("k", out v) ? Convert.ToString(v) : null;
                    if (string.IsNullOrEmpty(key)) break;
                    var p = new FanPlan();
                    if (m.TryGetValue("mode", out v)) p.Mode = Convert.ToString(v);
                    if (m.TryGetValue("pct", out v)) p.Pct = Convert.ToInt32(v);
                    if (m.TryGetValue("src", out v) && v != null) p.Source = Convert.ToString(v);
                    if (m.TryGetValue("name", out v) && v != null) p.Name = Convert.ToString(v);
                    if (m.TryGetValue("temps", out v) && v is IEnumerable) p.Temps = ((IEnumerable)v).Cast<object>().Select(x => Convert.ToInt32(x)).ToArray();
                    if (m.TryGetValue("pcts", out v) && v is IEnumerable) p.Pcts = ((IEnumerable)v).Cast<object>().Select(x => Convert.ToInt32(x)).ToArray();
                    var plans = new Dictionary<string, FanPlan>(sensors.Plans);
                    plans[key] = Fans.Clean(p);
                    sensors.Plans = plans;   // a new dictionary each time: the sensor thread never sees one half written
                    SaveSettings();
                    form.Post(Settings());
                    break;
            }
        }

        // Through Explorer: a browser started by an elevated Vigil would run as administrator too
        static void OpenLink(string url)
        {
            try { Process.Start("explorer.exe", "\"" + url + "\""); } catch { }
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
            if (sensors.InEmergency) tip += L.T(" · ventole al 100%", " · fans at 100%");
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
            sensors.Stop();   // fans back to the BIOS, then the library closes
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
                if (v.TryGetValue("Fans", out s))
                    sensors.Plans = Json.Read<Dictionary<string, FanPlan>>(s).ToDictionary(kv => kv.Key, kv => Fans.Clean(kv.Value));
            }
            catch { }   // broken values: defaults win, fans stay with the BIOS
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
            var values = new Dictionary<string, object> { { "AlertsEnabled", alertsOn }, { "AlertAt", alertAt }, { "Fans", Json.Write(sensors.Plans) } };
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
