using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SwitchTheme
{
    [DataContract]
    public sealed class Settings
    {
        [DataMember(Order = 0)] public int SchemaVersion = 2;
        [DataMember(Order = 1)] public bool Enabled;
        [DataMember(Order = 2)] public bool StartWithWindows;
        [DataMember(Order = 3)] public string LightTime = "07:00";
        [DataMember(Order = 4)] public string DarkTime = "19:00";
        [DataMember(EmitDefaultValue = false)] public bool? UseSolar;
        [OnDeserializing] private void Defaults(StreamingContext context)
        { SchemaVersion = 2; LightTime = "07:00"; DarkTime = "19:00"; }
        public Settings Copy() { return (Settings)MemberwiseClone(); }
        public bool SameAs(Settings other)
        { return Enabled == other.Enabled && StartWithWindows == other.StartWithWindows && LightTime == other.LightTime && DarkTime == other.DarkTime; }
        public static bool TryTime(string value, out TimeSpan time)
        { return TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out time); }
        public string Validate()
        {
            TimeSpan light, dark;
            if (!TryTime(LightTime, out light) || !TryTime(DarkTime, out dark)) return "请输入有效时间，例如 07:00 和 19:00。";
            return light == dark ? "浅色与深色时间不能相同。" : null;
        }
    }
    public sealed class SettingsStore
    {
        public readonly string DirectoryPath;
        public string FilePath { get { return Path.Combine(DirectoryPath, "settings.json"); } }
        public string LoadWarning { get; private set; }
        public SettingsStore(string directory = null)
        {
            DirectoryPath = directory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (directory == null && !File.Exists(FilePath))
            {
                var previous = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThemeShift", "settings.json");
                if (File.Exists(previous))
                {
                    try { Directory.CreateDirectory(DirectoryPath); File.Copy(previous, FilePath, false); }
                    catch (IOException ex) { LoadWarning = "旧设置迁移失败：" + ex.Message; }
                    catch (UnauthorizedAccessException ex) { LoadWarning = "旧设置迁移失败：" + ex.Message; }
                }
            }
        }
        public Settings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new Settings();
                Settings settings;
                using (var stream = File.OpenRead(FilePath))
                    settings = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);
                if (settings == null || settings.Validate() != null) throw new InvalidDataException("设置内容无效。");
                if (settings.UseSolar.HasValue)
                {
                    var backup = Path.Combine(DirectoryPath, "settings.previous.json");
                    if (!File.Exists(backup)) File.Copy(FilePath, backup);
                    if (settings.UseSolar.Value) settings.Enabled = false;
                    settings.UseSolar = null; settings.SchemaVersion = 2;
                    Save(settings); LoadWarning = "旧设置已迁移，请确认每日时间。";
                }
                return settings;
            }
            catch (Exception ex)
            {
                LoadWarning = "设置无法读取，已使用默认值。原文件保留。";
                Log(ex.ToString()); return new Settings();
            }
        }
        public void Save(Settings settings)
        {
            if (settings.Validate() != null) throw new ArgumentException(settings.Validate());
            Directory.CreateDirectory(DirectoryPath);
            var temporary = FilePath + ".tmp";
            var minimal = settings.Copy(); minimal.SchemaVersion = 2; minimal.UseSolar = null;
            using (var stream = File.Create(temporary)) new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, minimal);
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
            else File.Move(temporary, FilePath);
        }
        public void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                var path = Path.Combine(DirectoryPath, "activity.log");
                if (File.Exists(path) && new FileInfo(path).Length > 512000)
                { var old = path + ".old"; if (File.Exists(old)) File.Delete(old); File.Move(path, old); }
                File.AppendAllText(path, DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    public enum ThemeMode { Light, Dark }
    public sealed class Transition
    {
        public DateTimeOffset At; public ThemeMode Mode; public TimeSpan WallTime;
        public Transition(DateTimeOffset at, ThemeMode mode, TimeSpan wallTime) { At = at; Mode = mode; WallTime = wallTime; }
    }
    public sealed class ScheduleState
    {
        public ThemeMode Current; public Transition Next;
        public ScheduleState(ThemeMode current, Transition next) { Current = current; Next = next; }
    }
    public static class Scheduler
    {
        public static TimeSpan? NextCheckDelay(Settings settings, DateTimeOffset now, TimeZoneInfo zone,
            DateTimeOffset? pausedUntil, DateTimeOffset retryAfter, bool watchingTheme)
        {
            TimeSpan? delay = null;
            if (settings.Enabled)
            {
                var next = pausedUntil.HasValue && pausedUntil.Value > now ? pausedUntil.Value : Evaluate(settings, now, zone).Next.At;
                if (retryAfter > now && retryAfter < next) next = retryAfter;
                delay = next - now;
            }
            // Keep a small fallback only if the event subscription could not be established.
            if (!watchingTheme && (!delay.HasValue || delay.Value > TimeSpan.FromSeconds(15))) delay = TimeSpan.FromSeconds(15);
            return delay;
        }
        public static ScheduleState Evaluate(Settings settings, DateTimeOffset now, TimeZoneInfo zone)
        {
            if (settings.Validate() != null) throw new ArgumentException(settings.Validate());
            TimeSpan light, dark; Settings.TryTime(settings.LightTime, out light); Settings.TryTime(settings.DarkTime, out dark);
            var date = TimeZoneInfo.ConvertTime(now, zone).Date;
            var events = Enumerable.Range(-2, 5).SelectMany(offset => new[] {
                new Transition(LocalTime(date.AddDays(offset), light, zone), ThemeMode.Light, light),
                new Transition(LocalTime(date.AddDays(offset), dark, zone), ThemeMode.Dark, dark)
            }).OrderBy(item => item.At).ThenBy(item => item.WallTime).ToArray();
            var next = events.First(item => item.At > now);
            return new ScheduleState(events.Last(item => item.At <= now).Mode,
                new Transition(TimeZoneInfo.ConvertTime(next.At, zone), next.Mode, next.WallTime));
        }
        public static DateTimeOffset LocalTime(DateTime date, TimeSpan time, TimeZoneInfo zone)
        {
            var local = DateTime.SpecifyKind(date.Date + time, DateTimeKind.Unspecified);
            while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
            var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
            return new DateTimeOffset(local, offset);
        }
    }
}
