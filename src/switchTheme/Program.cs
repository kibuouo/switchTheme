using System;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("switchTheme · 本机轻量版")]
[assembly: AssemblyProduct("switchTheme")]
[assembly: AssemblyVersion("0.3.6.0")]
[assembly: AssemblyFileVersion("0.3.6.0")]

namespace SwitchTheme
{
    internal static class Program
    {
        [STAThread] private static void Main(string[] args)
        {
            try
            {
                using (var process = Process.GetCurrentProcess())
                    StartupLog.Write("start version=0.3.6 pid=" + process.Id + " mode=" +
                        (args.Contains("--startup") ? "logon" : args.Contains("--background") ? "background" : "manual"));
                if (args.Contains("--repair-startup")) { StartupRegistration.Set(new SettingsStore().Load().StartWithWindows); return; }
                if (args.Contains("--startup") && !new SettingsStore().Load().StartWithWindows)
                { StartupLog.Write("logon skipped: startup disabled in settings"); return; }
                Run(args);
                StartupLog.Write("exit");
            }
            catch (Exception ex) { StartupLog.Write("fatal: " + ex); Environment.ExitCode = 1; }
        }
        private static void Run(string[] args)
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
            var background = args.Contains("--background", StringComparer.OrdinalIgnoreCase);
            var resumeArgument = args.FirstOrDefault(arg => arg.StartsWith("--resume=", StringComparison.Ordinal));
            ResumeState resume = null;
            if (resumeArgument != null)
            { try { resume = ResumeState.Decode(resumeArgument.Substring(9)); } catch { return; } }
            var suffix = WindowsIdentity.GetCurrent().User.Value;
            bool first;
            using (var instance = new Mutex(true, @"Local\ThemeShift.Instance." + suffix, out first))
            using (var activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ThemeShift.Activate." + suffix))
            {
                if (!first && resume != null)
                {
                    try { first = instance.WaitOne(TimeSpan.FromSeconds(10)); }
                    catch (AbandonedMutexException) { first = true; }
                }
                if (!first) { StartupLog.Write("existing instance detected"); if (!background) activation.Set(); return; }
                try
                {
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                    using (var runtime = new AppRuntime(activation, resume))
                    {
                        StartupLog.Write("runtime ready");
                        Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { runtime.Store.Log(e.Exception.ToString()); };
                        if (!background) runtime.OpenWindow();
#if PROFILE
                        if (args.Contains("--trace-window")) Diagnostics.StartWindowTrace();
                        if (args.Contains("--profile-lifecycle", StringComparer.OrdinalIgnoreCase)) Diagnostics.StartLifecycle(runtime, !args.Contains("--profile-no-window"));
#endif
                        Application.Run(runtime);
                    }
                }
                finally { instance.ReleaseMutex(); }
            }
        }
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr awareness);
    }
    public sealed class AppRuntime : ApplicationContext
    {
        public readonly SettingsStore Store = new SettingsStore();
        public Settings Active { get; private set; }
        public Settings DraftCache;
        public ThemeMode ActualMode { get; private set; }
        public ThemeMode ActualSystemMode { get; private set; }
        public string Message { get; private set; }
        public DateTimeOffset? PausedUntil { get; private set; }
        private readonly WindowsTheme theme;
        private readonly UiDispatcher ui;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly NotifyIcon tray;
        private readonly System.Drawing.Icon icon;
        private readonly System.Drawing.Icon trayIcon;
        public System.Drawing.Icon WindowIcon { get { return icon; } }
        private readonly RegisteredWaitHandle activationListener;
        private RegistryWatcher watcher;
        private SettingsWindow window;
        private bool disposed; private int refreshQueued;
        private DateTimeOffset retryAfter;
        public event Action Changed;
        public AppRuntime(EventWaitHandle activation, ResumeState resume = null)
        {
            Active = Store.Load(); Message = Store.LoadWarning ?? "设置已保存。";
            if (resume != null) { DraftCache = resume.Draft; PausedUntil = resume.PausedUntil; }
            theme = new WindowsTheme(ex => Store.Log(ex.ToString()));
            ui = new UiDispatcher(ex => Store.Log(ex.ToString()));
            ui.SystemClockChanged = delegate { TimeZoneInfo.ClearCachedData(); Tick(); };
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico")) icon = new System.Drawing.Icon(stream);
            trayIcon = AppIcons.CreateTrayIcon();
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开switchTheme", null, delegate { OpenWindow(); });
            menu.Items.Add(new ToolStripSeparator());
            var pause = menu.Items.Add("暂停自动切换 1 小时", null, delegate {
                PausedUntil = IsPaused ? (DateTimeOffset?)null : DateTimeOffset.Now.AddHours(1); Tick();
            });
            menu.Opening += delegate { pause.Enabled = Active.Enabled; pause.Text = IsPaused ? "恢复自动切换" : "暂停自动切换 1 小时"; };
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitThread(); });
            tray = new NotifyIcon { Icon = trayIcon, Text = "switchTheme · 定时主题", Visible = true, ContextMenuStrip = menu };
            tray.DoubleClick += delegate { OpenWindow(); };
            activationListener = ThreadPool.RegisterWaitForSingleObject(activation, delegate { ui.Post(OpenWindow); }, null, Timeout.Infinite, false);
            timer.Tick += delegate { Tick(); };
            try { watcher = new RegistryWatcher(WindowsTheme.Personalize, QueueRefresh, ex => Store.Log(ex.ToString())); }
            catch (Exception ex) { Store.Log(ex.ToString()); }
            Tick();
            Diagnostics.Snapshot("background initialized");
        }
        public bool IsPaused { get { return PausedUntil.HasValue && PausedUntil.Value > DateTimeOffset.Now; } }
        public void OpenWindow()
        {
            if (disposed) return;
            if (window == null || window.IsDisposed)
            {
                window = new SettingsWindow(this);
                window.FormClosed += delegate { window = null; RestartInBackground(); };
            }
            window.Show(); window.WindowState = FormWindowState.Normal; window.Activate();
            Diagnostics.Snapshot("window opened");
        }
        private void QueueRefresh()
        {
            if (Interlocked.Exchange(ref refreshQueued, 1) != 0) return;
            ui.Post(delegate { Interlocked.Exchange(ref refreshQueued, 0); if (!disposed) Refresh(); });
        }
        private void RestartInBackground()
        {
            if (disposed) return;
            try
            {
                var state = new ResumeState { Draft = DraftCache, PausedUntil = PausedUntil };
                var start = new ProcessStartInfo(Application.ExecutablePath, "--background --resume=" + state.Encode())
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                using (var replacement = Process.Start(start)) { }
                ExitThread();
            }
            catch (Exception ex) { Store.Log("后台交接失败，保留当前实例：" + ex); }
        }
        private void Refresh(bool force = false)
        {
            Diagnostics.Count("runtime-refresh");
            var actual = theme.Read(); var system = theme.ReadSystem();
            var changed = actual != ActualMode || system != ActualSystemMode;
            ActualMode = actual; ActualSystemMode = system;
            if ((force || changed) && Changed != null) Changed();
            Diagnostics.Snapshot("theme refreshed");
        }
        private void Tick(bool forceRefresh = false)
        {
            if (disposed) return;
            timer.Stop();
            try
            {
                if (Active.Enabled && !IsPaused && DateTimeOffset.Now >= retryAfter) { PausedUntil = null; theme.Apply(Scheduler.Evaluate(Active, DateTimeOffset.Now, TimeZoneInfo.Local).Current); }
                var text = "switchTheme · " + (!Active.Enabled ? "定时切换未启用" : IsPaused ? "已暂停" : "定时切换已开启");
                if (tray.Text != text) tray.Text = text;
                Refresh(forceRefresh);
            }
            catch (Exception ex) { retryAfter = DateTimeOffset.Now.AddMinutes(1); Store.Log(ex.ToString()); Message = "切换失败：" + ex.Message; }
            ArmTimer();
        }
        private void ArmTimer()
        {
            timer.Stop();
            var delay = Scheduler.NextCheckDelay(Active, DateTimeOffset.Now, TimeZoneInfo.Local, PausedUntil, retryAfter, watcher != null);
            if (!delay.HasValue) return;
            timer.Interval = (int)Math.Max(1, Math.Min(int.MaxValue, Math.Ceiling(delay.Value.TotalMilliseconds)));
            timer.Start();
        }
        public void Manual(ThemeMode mode)
        {
            theme.Apply(mode);
            PausedUntil = Active.Enabled ? (DateTimeOffset?)Scheduler.Evaluate(Active, DateTimeOffset.Now, TimeZoneInfo.Local).Next.At : null;
            Message = "Windows 已切换为" + (mode == ThemeMode.Light ? "浅色" : "深色") + "。";
            Refresh(true); ArmTimer();
        }
        public bool Save(Settings draft)
        {
            var error = draft.Validate();
            if (error != null) { Message = error; return false; }
            var oldStartup = StartupRegistration.Capture();
            try
            {
                StartupRegistration.Set(draft.StartWithWindows); Store.Save(draft);
                Active = draft.Copy(); DraftCache = null; PausedUntil = null; retryAfter = default(DateTimeOffset);
                Message = Active.StartWithWindows ? "已保存 · 登录后静默运行。" : "设置已保存。";
                Tick(true); return true;
            }
            catch (Exception ex)
            {
                try { StartupRegistration.Restore(oldStartup); } catch (Exception rollback) { Store.Log(rollback.ToString()); }
                Store.Log(ex.ToString()); Message = "保存失败：" + ex.Message; return false;
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true; activationListener.Unregister(null); timer.Dispose();
                if (watcher != null) watcher.Dispose();
                if (window != null) window.Dispose();
                tray.Visible = false; tray.ContextMenuStrip.Dispose(); tray.Dispose(); trayIcon.Dispose(); icon.Dispose(); ui.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
