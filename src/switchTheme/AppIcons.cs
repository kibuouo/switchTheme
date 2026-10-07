using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SwitchTheme
{
    internal static class AppIcons
    {
        public static Icon CreateTrayIcon()
        {
            // Match the original WPF tray icon, which differs from app.ico.
            using (var bitmap = new Bitmap(32, 32))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var disk = new SolidBrush(Color.FromArgb(23, 23, 23)))
            using (var moon = new SolidBrush(Color.FromArgb(244, 244, 244)))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                graphics.FillEllipse(disk, 1, 1, 30, 30);
                graphics.FillEllipse(moon, 7, 5, 20, 22);
                graphics.FillEllipse(disk, 14, 2, 16, 17);
                var handle = bitmap.GetHicon();
                try { using (var icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
