using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Xml;

namespace SwitchTheme
{
    internal sealed class StartupTask : IDisposable
    {
        internal const string Name = "switchTheme";
        private object service, folder;
        public StartupTask()
        {
            try
            {
                service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
                Call(service, "Connect"); folder = Call(service, "GetFolder", "\\");
            }
            catch { Dispose(); throw; }
        }
        private static object Call(object target, string name, params object[] arguments)
        {
            try { return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod | BindingFlags.OptionalParamBinding, null, target, arguments); }
            catch (TargetInvocationException ex)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw(); throw; }
        }
        private static object Get(object target, string name)
        { return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null); }
        private static void Release(object value)
        { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
        public string ReadXml()
        {
            object task = null;
            try { task = Call(folder, "GetTask", Name); return (string)Get(task, "Xml"); }
            catch (FileNotFoundException) { return null; }
            catch (COMException ex) { if (ex.ErrorCode == unchecked((int)0x80070002)) return null; throw; }
            finally { Release(task); }
        }
        public void Register(string xml)
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                // TASK_CREATE_OR_UPDATE, TASK_LOGON_INTERACTIVE_TOKEN: current user's desktop only.
                var task = Call(folder, "RegisterTask", Name, xml, 6, identity.User.Value, null, 3, null);
                Release(task);
            }
        }
        public void Delete()
        {
            try { Call(folder, "DeleteTask", Name, 0); }
            catch (FileNotFoundException) { }
            catch (COMException ex) { if (ex.ErrorCode != unchecked((int)0x80070002)) throw; }
        }
        public static string BuildXml(string executable, string sid)
        {
            StartupRegistration.BuildCommand(executable);
            if (string.IsNullOrWhiteSpace(sid)) throw new ArgumentException("登录用户无效。");
            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                "<RegistrationInfo><Description>switchTheme：当前用户登录后静默运行定时主题。</Description></RegistrationInfo>" +
                "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + SecurityElement.Escape(sid) + "</UserId><Delay>PT10S</Delay></LogonTrigger></Triggers>" +
                "<Principals><Principal id=\"User\"><UserId>" + SecurityElement.Escape(sid) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>true</StartWhenAvailable>" +
                "<RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable><IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>" +
                "<AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>true</Hidden><ExecutionTimeLimit>PT0S</ExecutionTimeLimit></Settings>" +
                "<Actions Context=\"User\"><Exec><Command>" + SecurityElement.Escape(executable) + "</Command><Arguments>--background --startup</Arguments>" +
                "<WorkingDirectory>" + SecurityElement.Escape(Path.GetDirectoryName(executable)) + "</WorkingDirectory></Exec></Actions></Task>";
        }
        public static bool Matches(string xml, string executable, string sid)
        {
            if (xml == null) return false;
            var document = new XmlDocument { XmlResolver = null }; document.LoadXml(xml);
            var ns = new XmlNamespaceManager(document.NameTable); ns.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");
            Func<string, string> value = path => { var node = document.SelectSingleNode("/t:Task/" + path, ns); return node == null ? null : node.InnerText; };
            return value("t:Settings/t:Enabled") != "false" &&
                value("t:Triggers/t:LogonTrigger/t:Enabled") != "false" && SameUser(value("t:Triggers/t:LogonTrigger/t:UserId"), sid) &&
                SameUser(value("t:Principals/t:Principal/t:UserId"), sid) && value("t:Principals/t:Principal/t:LogonType") == "InteractiveToken" &&
                (value("t:Principals/t:Principal/t:RunLevel") == null || value("t:Principals/t:Principal/t:RunLevel") == "LeastPrivilege") &&
                string.Equals(value("t:Actions/t:Exec/t:Command"), executable, StringComparison.OrdinalIgnoreCase) &&
                value("t:Actions/t:Exec/t:Arguments") == "--background --startup" && value("t:Settings/t:ExecutionTimeLimit") == "PT0S";
        }
        private static bool SameUser(string value, string sid)
        {
            if (string.Equals(value, sid, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.IsNullOrEmpty(value)) return false;
            try { return ((SecurityIdentifier)new NTAccount(value).Translate(typeof(SecurityIdentifier))).Value == sid; }
            catch (IdentityNotMappedException) { return false; }
        }
        public void Dispose() { Release(folder); Release(service); folder = service = null; }
    }
    internal static class StartupLog
    {
        public static void Write(string message)
        {
            try
            {
                var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                Directory.CreateDirectory(directory); var path = Path.Combine(directory, "startup.log");
                if (File.Exists(path) && new FileInfo(path).Length > 128 * 1024) File.WriteAllText(path, "");
                File.AppendAllText(path, DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (SecurityException) { }
        }
    }
}
