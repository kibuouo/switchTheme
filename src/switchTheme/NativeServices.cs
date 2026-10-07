using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SwitchTheme
{
    public sealed class NotificationQueue
    {
        private readonly object gate = new object();
        private readonly Action notify; private readonly Action<Exception> error;
        private bool running, pending;
        public NotificationQueue(Action notify, Action<Exception> error) { this.notify = notify; this.error = error; }
        public void Request()
        {
            lock (gate) { pending = true; if (running) return; running = true; }
            ThreadPool.QueueUserWorkItem(delegate {
                while (true)
                {
                    lock (gate) { if (!pending) { running = false; return; } pending = false; }
                    try { notify(); } catch (Exception ex) { try { if (error != null) error(ex); } catch { } }
                }
            });
        }
    }
    public sealed class WindowsTheme
    {
        public const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private readonly NotificationQueue notifications;
        public WindowsTheme(Action<Exception> error)
        {
            notifications = new NotificationQueue(delegate {
                var ownProcess = (uint)Process.GetCurrentProcess().Id;
                EnumWindows(delegate(IntPtr window, IntPtr parameter) {
                    uint owner; GetWindowThreadProcessId(window, out owner);
                    // Our custom palette already refreshed from the registry. Avoid
                    // needlessly rebuilding WinForms system-theme caches in this process.
                    if (owner != ownProcess)
                    { IntPtr ignored; SendMessageTimeout(window, 0x1a, IntPtr.Zero, "ImmersiveColorSet", 2, 1500, out ignored); }
                    return true;
                }, IntPtr.Zero);
            }, error);
        }
        private static int ReadValue(string name)
        { using (var key = Registry.CurrentUser.OpenSubKey(Personalize)) { var value = key == null ? null : key.GetValue(name); return value is int ? (int)value : 1; } }
        public ThemeMode Read() { return ReadValue("AppsUseLightTheme") == 0 ? ThemeMode.Dark : ThemeMode.Light; }
        public ThemeMode ReadSystem() { return ReadValue("SystemUsesLightTheme") == 0 ? ThemeMode.Dark : ThemeMode.Light; }
        public void Apply(ThemeMode mode)
        {
            var value = mode == ThemeMode.Light ? 1 : 0; var changed = false;
            using (var key = Registry.CurrentUser.CreateSubKey(Personalize))
                foreach (var name in new[] { "AppsUseLightTheme", "SystemUsesLightTheme" })
                {
                    var current = key.GetValue(name);
                    if (!(current is int) || (int)current != value)
                    { key.SetValue(name, value, RegistryValueKind.DWord); changed = true; }
                }
            if (changed) notifications.Request();
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
        private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    }
    public sealed class RegistryWatcher : IDisposable
    {
        private readonly object gate = new object(); private readonly RegistryKey key;
        private readonly AutoResetEvent signal = new AutoResetEvent(false);
        private readonly RegisteredWaitHandle wait; private bool disposed;
        public RegistryWatcher(string path, Action changed, Action<Exception> error)
        {
            key = Registry.CurrentUser.OpenSubKey(path, false);
            if (key == null) { signal.Dispose(); throw new InvalidOperationException("主题设置项不存在。"); }
            try
            {
                Arm();
                wait = ThreadPool.RegisterWaitForSingleObject(signal, delegate {
                    try { lock (gate) { if (disposed) return; Arm(); } changed(); }
                    catch (Exception ex) { try { error(ex); } catch { } }
                }, null, Timeout.Infinite, false);
            }
            catch { key.Dispose(); signal.Dispose(); throw; }
        }
        private void Arm()
        {
            var result = RegNotifyChangeKeyValue(key.Handle, false, 0x10000004, signal.SafeWaitHandle, true);
            if (result != 0) throw new Win32Exception(result);
        }
        public void Dispose()
        { lock (gate) { if (disposed) return; disposed = true; wait.Unregister(null); key.Dispose(); signal.Dispose(); } }
        [DllImport("advapi32.dll")]
        private static extern int RegNotifyChangeKeyValue(Microsoft.Win32.SafeHandles.SafeRegistryHandle key,
            [MarshalAs(UnmanagedType.Bool)] bool subtree, uint filter, Microsoft.Win32.SafeHandles.SafeWaitHandle signal,
            [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
    }
    public static class StartupRegistration
    {
        private const string Path = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public sealed class State { public string LegacyCommand, TaskXml; }
        public static string BuildCommand(string executable)
        { if (string.IsNullOrWhiteSpace(executable) || executable.Contains("\"")) throw new ArgumentException("启动路径无效。"); return "\"" + executable + "\" --background"; }
        public static State Capture()
        {
            string command;
            using (var key = Registry.CurrentUser.OpenSubKey(Path)) command = key == null ? null : key.GetValue("ThemeShift") as string;
            using (var task = new StartupTask()) return new State { LegacyCommand = command, TaskXml = task.ReadXml() };
        }
        public static bool IsRegistered()
        {
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            using (var task = new StartupTask()) return StartupTask.Matches(task.ReadXml(), Application.ExecutablePath, identity.User.Value);
        }
        private static void SetLegacy(string command)
        { using (var key = Registry.CurrentUser.CreateSubKey(Path)) { if (command == null) key.DeleteValue("ThemeShift", false); else key.SetValue("ThemeShift", command, RegistryValueKind.String); } }
        public static void Set(bool enabled)
        {
            var before = Capture();
            try
            {
                using (var task = new StartupTask())
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                { if (enabled) task.Register(StartupTask.BuildXml(Application.ExecutablePath, identity.User.Value)); else task.Delete(); }
                SetLegacy(null);
                StartupLog.Write(enabled ? "logon task registered" : "logon task removed");
            }
            catch (Exception failure)
            { try { Restore(before); } catch (Exception rollback) { throw new AggregateException(failure, rollback); } throw; }
        }
        public static void Restore(State state)
        {
            using (var task = new StartupTask()) { if (state.TaskXml == null) task.Delete(); else task.Register(state.TaskXml); }
            SetLegacy(state.LegacyCommand);
        }
    }
    public sealed class UiDispatcher : NativeWindow, IDisposable
    {
        private readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
        private readonly Action<Exception> error; private volatile bool disposed;
        public Action SystemClockChanged;
        public UiDispatcher(Action<Exception> error)
        { this.error = error; CreateHandle(new CreateParams { Caption = "ThemeShift.Dispatcher" }); }
        public void Post(Action action) { if (disposed) return; queue.Enqueue(action); PostMessage(Handle, 0x802a, IntPtr.Zero, IntPtr.Zero); }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x802a)
            { Action action; while (queue.TryDequeue(out action)) { if (!disposed) try { action(); } catch (Exception ex) { error(ex); } } }
            else if (message.Msg == 0x1e || (message.Msg == 0x218 && (message.WParam.ToInt64() == 7 || message.WParam.ToInt64() == 18)))
            { if (SystemClockChanged != null) SystemClockChanged(); }
            base.WndProc(ref message);
        }
        public void Dispose() { disposed = true; DestroyHandle(); }
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    }
}
