using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Linq;

namespace SwitchTheme
{
    internal static class Diagnostics
    {
#if PROFILE
        private static readonly ConcurrentDictionary<string, long> counts = new ConcurrentDictionary<string, long>();
        public static void StartWindowTrace()
        {
            var samples = 0;
            var timer = new System.Windows.Forms.Timer { Interval = 5000 };
            timer.Tick += delegate {
                using (var process = Process.GetCurrentProcess())
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "window-trace.log"),
                        DateTime.Now.ToString("HH:mm:ss.fff") + " cpu=" + process.TotalProcessorTime.TotalSeconds + " " +
                        string.Join(" ", counts.OrderByDescending(item => item.Value).Take(20).Select(item => item.Key + "=" + item.Value)) + Environment.NewLine);
                if (++samples == 24) timer.Dispose();
            };
            timer.Start();
        }
#endif
        [Conditional("PROFILE")]
        public static void Count(string name)
        {
#if PROFILE
            counts.AddOrUpdate(name, 1, delegate(string key, long value) { return value + 1; });
#endif
        }
#if PROFILE
        public static void StartLifecycle(AppRuntime runtime, bool openWindow)
        {
            var step = 0;
            var timer = new System.Windows.Forms.Timer { Interval = 6000 };
            timer.Tick += delegate {
                step++;
                if (step == 1 && openWindow) runtime.OpenWindow();
                else if (step == 2) runtime.Manual(ThemeMode.Dark);
                else if (step == 3) runtime.Manual(ThemeMode.Light);
                else if (step == 4 && System.Windows.Forms.Application.OpenForms.Count != 0)
                    System.Windows.Forms.Application.OpenForms[0].Close();
                Snapshot("lifecycle " + step);
                if (step == 5) timer.Dispose();
            };
            timer.Start();
        }
#endif
        [Conditional("PROFILE")]
        public static void Snapshot(string stage)
        {
            var process = GetCurrentProcess();
            var memory = new MemoryCounters(); memory.Size = (uint)Marshal.SizeOf(memory);
            GetProcessMemoryInfo(process, out memory, memory.Size);
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "memory-profile.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + stage +
                    " managed=" + GC.GetTotalMemory(false) / 1024 + "KiB private=" + memory.PrivateUsage.ToUInt64() / 1024 +
                    "KiB gdi=" + GetGuiResources(process, 0) +
                    " user=" + GetGuiResources(process, 1) + Environment.NewLine);
        }
        [StructLayout(LayoutKind.Sequential)] private struct MemoryCounters
        {
            public uint Size, PageFaults;
            public UIntPtr PeakWorkingSet, WorkingSet, QuotaPeakPagedPool, QuotaPagedPool, QuotaPeakNonPagedPool, QuotaNonPagedPool, Pagefile, PeakPagefile, PrivateUsage;
        }
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("psapi.dll")] private static extern bool GetProcessMemoryInfo(IntPtr process, out MemoryCounters counters, uint size);
        [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flag);
    }
}
