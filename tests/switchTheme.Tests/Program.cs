using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SwitchTheme;

class LocalTests
{
    private static int passed;
    private static void Check(bool result, string label)
    { if (!result) throw new Exception("FAIL: " + label); passed++; Console.WriteLine("PASS: " + label); }
    private static DateTimeOffset At(int hour, int minute = 0)
    { return new DateTimeOffset(2026, 10, 7, hour, minute, 0, TimeSpan.FromHours(8)); }
    private static void Main()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test China", TimeSpan.FromHours(8), "Test China", "Test China");
        var settings = new Settings { LightTime = "07:00", DarkTime = "19:00" };
        Check(Scheduler.Evaluate(settings, At(6, 59), zone).Current == ThemeMode.Dark, "before morning");
        Check(Scheduler.Evaluate(settings, At(7), zone).Current == ThemeMode.Light, "exact morning");
        Check(Scheduler.Evaluate(settings, At(18, 59), zone).Current == ThemeMode.Light, "before evening");
        Check(Scheduler.Evaluate(settings, At(19), zone).Current == ThemeMode.Dark, "exact evening");
        Check(Scheduler.Evaluate(settings, At(23), zone).Next.At == At(7).AddDays(1), "next day boundary");
        Check(Enumerable.Range(0, 24).All(hour => Scheduler.Evaluate(settings, At(hour), zone).Current == (hour >= 7 && hour < 19 ? ThemeMode.Light : ThemeMode.Dark)), "whole day schedule");
        Check(!Scheduler.NextCheckDelay(settings, At(8), zone, null, default(DateTimeOffset), true).HasValue, "disabled automation has no timer with registry events");
        Check(Scheduler.NextCheckDelay(settings, At(8), zone, null, default(DateTimeOffset), false) == TimeSpan.FromSeconds(15), "missing registry watcher retains fallback");
        settings.Enabled = true;
        Check(Scheduler.NextCheckDelay(settings, At(8), zone, null, default(DateTimeOffset), true) == TimeSpan.FromHours(11), "timer sleeps until next evening boundary");
        Check(Scheduler.NextCheckDelay(settings, At(19), zone, null, default(DateTimeOffset), true) == TimeSpan.FromHours(12), "exact boundary arms next day morning");
        Check(Scheduler.NextCheckDelay(settings, At(18, 59).AddSeconds(59.5), zone, null, default(DateTimeOffset), true) == TimeSpan.FromMilliseconds(500), "timer retains subsecond remainder");
        Check(Scheduler.NextCheckDelay(settings, At(8), zone, At(9), default(DateTimeOffset), true) == TimeSpan.FromHours(1), "one hour pause arms its expiry");
        Check(Scheduler.NextCheckDelay(settings, At(18), zone, At(20), default(DateTimeOffset), true) == TimeSpan.FromHours(2), "pause spanning a boundary holds until expiry");
        Check(Scheduler.NextCheckDelay(settings, At(8), zone, At(7), default(DateTimeOffset), true) == TimeSpan.FromHours(11), "expired pause returns to schedule");
        Check(Scheduler.NextCheckDelay(settings, At(8), zone, null, At(8, 1), true) == TimeSpan.FromMinutes(1), "failed theme application retries before boundary");
        Check(Scheduler.NextCheckDelay(settings, At(18, 59).AddSeconds(59), zone, null, default(DateTimeOffset), false) == TimeSpan.FromSeconds(1), "fallback does not delay imminent boundary");
        settings.Enabled = false;
        settings.LightTime = "19:00"; settings.DarkTime = "07:00";
        Check(Scheduler.Evaluate(settings, At(23), zone).Current == ThemeMode.Light, "overnight light");
        Check(Scheduler.Evaluate(settings, At(8), zone).Current == ThemeMode.Dark, "overnight daytime");
        settings.DarkTime = "19:00";
        Check(settings.Validate() != null, "equal times rejected");
        settings.LightTime = "25:00"; Check(settings.Validate() != null, "invalid hour rejected");
        settings.LightTime = "07:60"; Check(settings.Validate() != null, "invalid minute rejected");
        settings.LightTime = "7:00"; Check(settings.Validate() != null, "strict format retained");
        settings.LightTime = "00:00"; settings.DarkTime = "23:59";
        Check(Scheduler.Evaluate(settings, At(0), zone).Current == ThemeMode.Light, "midnight");
        Check(Scheduler.Evaluate(settings, At(23, 59), zone).Current == ThemeMode.Dark, "last minute");
        var us = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var missing = Scheduler.LocalTime(new DateTime(2026, 3, 8), TimeSpan.FromMinutes(150), us);
        Check(TimeZoneInfo.ConvertTime(missing, us).Hour == 3, "DST missing time");
        var repeated = Scheduler.LocalTime(new DateTime(2026, 11, 1), TimeSpan.FromMinutes(90), us);
        Check(repeated.Offset == TimeSpan.FromHours(-4), "DST first repeated occurrence");
        settings.LightTime = "02:15"; settings.DarkTime = "02:45";
        Check(Scheduler.Evaluate(settings, new DateTimeOffset(2026, 3, 8, 3, 0, 0, TimeSpan.FromHours(-4)), us).Current == ThemeMode.Dark, "DST collapsed boundaries retain order");
        Check(StartupRegistration.BuildCommand(@"C:\My Apps\昼夜\ThemeShift.exe") == "\"C:\\My Apps\\昼夜\\ThemeShift.exe\" --background", "silent startup command");
        var draft = settings.Copy(); draft.LightTime = "08:00";
        Check(settings.LightTime == "02:15" && !draft.SameAs(settings), "draft isolation");
        draft.LightTime = settings.LightTime; Check(draft.SameAs(settings), "reverting a draft clears its dirty state");
        var pauseUntil = new DateTimeOffset(2026, 10, 7, 19, 0, 0, TimeSpan.FromHours(8));
        var resume = new ResumeState { Draft = new Settings { LightTime = "bad", StartWithWindows = true }, PausedUntil = pauseUntil };
        var restored = ResumeState.Decode(resume.Encode());
        Check(restored.Draft.LightTime == "bad" && restored.Draft.StartWithWindows, "handoff preserves unsaved input without saving it");
        Check(restored.PausedUntil == pauseUntil && restored.PausedUntil.Value.Offset == pauseUntil.Offset, "handoff preserves manual pause and offset");
        Check(ResumeState.Decode(new ResumeState().Encode()).Draft == null, "handoff accepts empty transient state");
        var invalidResume = false; try { ResumeState.Decode(new string('x', 4097)); } catch (ArgumentException) { invalidResume = true; }
        Check(invalidResume, "oversized handoff argument rejected");
        var temporary = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ThemeShift-local-tests-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(temporary);
        try
        {
            var store = new SettingsStore(temporary);
            File.WriteAllText(store.FilePath, "{\"SchemaVersion\":2,\"Enabled\":false,\"StartWithWindows\":true,\"LightTime\":\"08:00\",\"DarkTime\":\"21:00\"}");
            var existing = store.Load();
            Check(existing.StartWithWindows && existing.LightTime == "08:00" && existing.DarkTime == "21:00", "WPF settings compatible with local build");
            File.WriteAllText(store.FilePath, "{\"Enabled\":true,\"UseSolar\":true,\"CityId\":\"wuhan\",\"OffsetMinutes\":30,\"ChangeApps\":false,\"StartWithWindows\":true,\"LightTime\":\"08:00\",\"DarkTime\":\"21:00\"}");
            var migrated = store.Load();
            Check(migrated.LightTime == "08:00" && migrated.DarkTime == "21:00" && migrated.StartWithWindows, "legacy preferences retained");
            Check(!migrated.Enabled && store.LoadWarning != null, "legacy solar automation waits for confirmation");
            Check(File.Exists(Path.Combine(temporary, "settings.previous.json")), "legacy backup preserved");
            var minimal = File.ReadAllText(store.FilePath);
            Check(Regex.Matches(minimal, "\"[^\"]+\":").Count == 5 && !minimal.Contains("UseSolar") && !minimal.Contains("CityId"), "only five settings written");
            File.WriteAllText(store.FilePath, "{\"Enabled\":true,\"UseSolar\":false,\"LightTime\":\"06:00\",\"DarkTime\":\"18:00\"}");
            Check(new SettingsStore(temporary).Load().Enabled, "legacy fixed schedule stays enabled");
            store.Save(new Settings { Enabled = true, LightTime = "09:00", DarkTime = "20:00" });
            var loaded = new SettingsStore(temporary).Load();
            Check(loaded.Enabled && loaded.LightTime == "09:00" && loaded.DarkTime == "20:00", "atomic save and reload");
            Check(!File.Exists(store.FilePath + ".tmp"), "atomic save leaves no temporary file");
            var saved = File.ReadAllText(store.FilePath); var rejected = false;
            try { store.Save(new Settings { LightTime = "25:00" }); } catch (ArgumentException) { rejected = true; }
            Check(rejected && File.ReadAllText(store.FilePath) == saved, "invalid draft cannot overwrite saved settings");
            File.WriteAllText(store.FilePath, "{broken"); var broken = new SettingsStore(temporary);
            Check(!broken.Load().Enabled && broken.LoadWarning != null && File.ReadAllText(store.FilePath) == "{broken", "corrupt source retained");
            File.WriteAllText(store.FilePath, "{}"); var defaults = new SettingsStore(temporary).Load();
            Check(defaults.LightTime == "07:00" && defaults.DarkTime == "19:00" && !defaults.Enabled, "missing fields use safe defaults");
        }
        finally
        {
            if (!temporary.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(temporary).StartsWith("ThemeShift-local-tests-")) throw new Exception("Unsafe test cleanup path");
            Directory.Delete(temporary, true);
        }
        using (var started = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        using (var finished = new ManualResetEventSlim())
        {
            var count = 0;
            var queue = new NotificationQueue(delegate { if (Interlocked.Increment(ref count) == 1) { started.Set(); release.Wait(TimeSpan.FromSeconds(10)); } else finished.Set(); }, null);
            queue.Request();
            try
            {
                Check(started.Wait(TimeSpan.FromSeconds(5)), "notification worker starts");
                var requests = Task.Factory.StartNew(delegate { for (int i = 0; i < 64; i++) queue.Request(); });
                Check(requests.Wait(TimeSpan.FromSeconds(2)), "slow recipient does not block requests");
                Check(count == 1, "broadcasts do not run concurrently");
            }
            finally { release.Set(); }
            Check(finished.Wait(TimeSpan.FromSeconds(5)) && count == 2, "rapid notifications coalesce");
        }
        using (var error = new ManualResetEventSlim())
        using (var recovered = new ManualResetEventSlim())
        {
            var attempts = 0;
            var queue = new NotificationQueue(delegate { if (Interlocked.Increment(ref attempts) == 1) throw new InvalidOperationException(); recovered.Set(); }, delegate { error.Set(); });
            queue.Request(); Check(error.Wait(TimeSpan.FromSeconds(5)), "notification error reported");
            queue.Request(); Check(recovered.Wait(TimeSpan.FromSeconds(5)), "worker recovers after failure");
        }
        var testPath = @"Software\ThemeShift\Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(testPath))
            using (var changed = new AutoResetEvent(false))
            using (var failed = new ManualResetEventSlim())
            {
                key.SetValue("Mode", 1);
                using (var watcher = new RegistryWatcher(testPath, delegate { changed.Set(); }, delegate { failed.Set(); }))
                {
                    key.SetValue("Mode", 0); Check(changed.WaitOne(TimeSpan.FromSeconds(2)), "registry notification without polling");
                    key.SetValue("Mode", 1); Check(changed.WaitOne(TimeSpan.FromSeconds(2)), "registry watcher rearms");
                    watcher.Dispose(); watcher.Dispose(); key.SetValue("Mode", 2);
                    Check(!changed.WaitOne(150) && !failed.IsSet, "registry watcher disposes cleanly");
                }
            }
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(testPath, false); }
        Console.WriteLine(passed + " local checks passed.");
    }
}
