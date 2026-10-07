using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SwitchTheme
{
    internal sealed class Colors
    {
        public readonly Color Page, Text, Muted, Line, Track, Hover, Accent, ActionText;
        private Colors(bool dark)
        {
            Page = Gray(dark ? 16 : 244); Text = Gray(dark ? 244 : 21);
            Muted = Gray(dark ? 160 : 116); Line = Gray(dark ? 48 : 218);
            Track = Gray(dark ? 68 : 199); Hover = Gray(dark ? 36 : 232); Accent = Gray(dark ? 244 : 23);
            ActionText = Gray(dark ? 20 : 255);
        }
        public static Color Gray(int value) { return Color.FromArgb(value, value, value); }
        public static readonly Colors Light = new Colors(false), Dark = new Colors(true);
    }
    internal static class Shapes
    {
        public static GraphicsPath Rounded(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath(); var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure(); return path;
        }
        public static void Card(Graphics g, RectangleF bounds, float radius, Color fill, Color border, float thickness)
        {
            using (var shape = Rounded(bounds, radius))
            using (var brush = new SolidBrush(fill)) g.FillPath(brush, shape);
            if (border == fill) return;
            // Keep the entire antialiased stroke inside the control's bounds.
            var inset = bounds; inset.Inflate(-thickness / 2, -thickness / 2);
            using (var outline = Rounded(inset, Math.Max(1, radius - thickness / 2)))
            using (var pen = new Pen(border, thickness)) g.DrawPath(pen, outline);
        }
        public static void Icon(Graphics g, string kind, RectangleF rect, Color ink, Color background, float scale)
        {
            var cx = rect.X + rect.Width / 2; var cy = rect.Y + rect.Height / 2;
            using (var pen = new Pen(ink, 1.8f * scale))
            using (var brush = new SolidBrush(ink))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                if (kind == "sun")
                {
                    g.DrawEllipse(pen, cx - 3 * scale, cy - 3 * scale, 6 * scale, 6 * scale);
                    for (var i = 0; i < 8; i++) { var angle = Math.PI * i / 4; g.DrawLine(pen,
                        cx + (float)Math.Cos(angle) * 6 * scale, cy + (float)Math.Sin(angle) * 6 * scale,
                        cx + (float)Math.Cos(angle) * 8 * scale, cy + (float)Math.Sin(angle) * 8 * scale); }
                }
                else if (kind == "moon")
                {
                    var saved = g.Save(); g.SetClip(rect);
                    g.FillEllipse(brush, rect.X, rect.Y, rect.Width, rect.Height);
                    using (var cut = new SolidBrush(background)) g.FillEllipse(cut, rect.X + 5 * scale, rect.Y - 3 * scale, rect.Width, rect.Height);
                    g.Restore(saved);
                }
                else if (kind == "min") g.DrawLine(pen, cx - 5 * scale, cy, cx + 5 * scale, cy);
                else if (kind == "close")
                { g.DrawLine(pen, cx - 4 * scale, cy - 4 * scale, cx + 4 * scale, cy + 4 * scale); g.DrawLine(pen, cx + 4 * scale, cy - 4 * scale, cx - 4 * scale, cy + 4 * scale); }
                else if (kind == "check")
                { g.DrawLine(pen, cx - 5 * scale, cy, cx - 2 * scale, cy + 3 * scale); g.DrawLine(pen, cx - 2 * scale, cy + 3 * scale, cx + 5 * scale, cy - 4 * scale); }
            }
        }
    }
    internal sealed class RoundButton : Button
    {
        public Colors Palette = Colors.Light;
        public string Glyph;
        public bool Selected, IsSave, IsWindowControl;
        public float DpiScale = 1;
        private bool hover;
        public RoundButton()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void WndProc(ref Message message)
        {
            Diagnostics.Count("button-msg-" + message.Msg.ToString("X"));
            base.WndProc(ref message);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Diagnostics.Count("button-paint");
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            // All controls share the same surface; only transient hover feedback fills a rectangle.
            var fill = hover && Enabled ? Palette.Hover : Palette.Page;
            g.Clear(fill);
            var ink = IsSave && !Enabled ? Palette.Muted : Palette.Text;
            if (Selected || Focused)
            {
                using (var marker = new SolidBrush(ink))
                    g.FillRectangle(marker, 7 * DpiScale, Height - 2 * DpiScale, Width - 14 * DpiScale, Selected ? 2 * DpiScale : DpiScale);
            }
            var glyph = IsSave && !Enabled ? "check" : Glyph;
            if (IsWindowControl) { Shapes.Icon(g, glyph, new RectangleF(Width / 2f - 7 * DpiScale, Height / 2f - 7 * DpiScale, 14 * DpiScale, 14 * DpiScale), ink, fill, DpiScale); return; }
            var textWidth = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            var groupWidth = textWidth + (glyph == null ? 0 : 20 * DpiScale);
            var left = (Width - groupWidth) / 2;
            if (glyph != null) Shapes.Icon(g, glyph, new RectangleF(left, Height / 2f - 7 * DpiScale, 14 * DpiScale, 14 * DpiScale), ink, fill, DpiScale);
            TextRenderer.DrawText(g, Text, Font, new Point((int)(left + (glyph == null ? 0 : 20 * DpiScale)), (Height - Font.Height) / 2), ink, TextFormatFlags.NoPadding);
        }
    }
    internal sealed class RoundSwitch : CheckBox
    {
        public Colors Palette = Colors.Light;
        public float DpiScale = 1;
        public RoundSwitch()
        { Cursor = Cursors.Hand; AutoSize = false; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Diagnostics.Count("switch-paint");
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.Clear(Palette.Page);
            Shapes.Card(g, new RectangleF(DpiScale / 2, DpiScale / 2, Width - DpiScale, Height - DpiScale), Height / 2f,
                Checked ? Palette.Accent : Palette.Track, Focused ? Palette.Text : Checked ? Palette.Accent : Palette.Track, DpiScale);
            using (var knob = new SolidBrush(Checked ? Palette.ActionText : Color.White))
                g.FillEllipse(knob, Checked ? Width - 22 * DpiScale : 3 * DpiScale, 3 * DpiScale, 19 * DpiScale, 19 * DpiScale);
        }
    }
    public sealed class SettingsWindow : Form
    {
        private readonly AppRuntime runtime;
        private readonly Icon windowIcon;
        private readonly TextBox lightTime = new TextBox(), darkTime = new TextBox();
        private readonly RoundSwitch enabled = new RoundSwitch(), startup = new RoundSwitch();
        private readonly RoundButton light = new RoundButton(), dark = new RoundButton(), minimize = new RoundButton(), close = new RoundButton(), save = new RoundButton();
        private readonly ToolTip tips = new ToolTip { InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 6000 };
        private Font titleFont, labelFont, buttonFont, saveFont, timeFont;
        private float scale = 1;
        private Colors palette;
        private bool initialized, startupMismatch;
        public SettingsWindow(AppRuntime runtime)
        {
            this.runtime = runtime;
            windowIcon = (Icon)runtime.WindowIcon.Clone(); Icon = windowIcon;
            Text = "switchTheme · 定时主题"; AccessibleName = Text;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            light.Text = "浅色"; light.Glyph = "sun"; light.AccessibleName = "切换 Windows 浅色主题";
            dark.Text = "深色"; dark.Glyph = "moon"; dark.AccessibleName = "切换 Windows 深色主题";
            minimize.Glyph = "min"; minimize.IsWindowControl = true; minimize.TabStop = false; minimize.AccessibleName = "最小化";
            close.Glyph = "close"; close.IsWindowControl = true; close.TabStop = false; close.AccessibleName = "关闭并驻留托盘";
            save.IsSave = true; save.AccessibleName = "保存并应用";
            lightTime.AccessibleName = "浅色开始时间"; darkTime.AccessibleName = "深色开始时间";
            foreach (var input in new[] { lightTime, darkTime })
            { input.BorderStyle = BorderStyle.None; input.MaxLength = 5; input.TextAlign = HorizontalAlignment.Center; input.AutoSize = false; input.TextChanged += DraftChanged; input.Enter += delegate { Invalidate(); }; input.Leave += delegate { Invalidate(); }; }
            enabled.AccessibleName = "定时切换"; startup.AccessibleName = "开机静默启动";
            enabled.CheckedChanged += DraftChanged; startup.CheckedChanged += DraftChanged;
            Controls.AddRange(new Control[] { light, dark, minimize, close, lightTime, darkTime, enabled, startup, save });
            var initial = runtime.DraftCache ?? runtime.Active;
            lightTime.Text = initial.LightTime; darkTime.Text = initial.DarkTime;
            enabled.Checked = initial.Enabled; startup.Checked = initial.StartWithWindows;
            try { startupMismatch = runtime.Active.StartWithWindows && !StartupRegistration.IsRegistered(); } catch (Exception ex) { runtime.Store.Log(ex.ToString()); }
            light.Click += delegate { Manual(ThemeMode.Light); }; dark.Click += delegate { Manual(ThemeMode.Dark); };
            minimize.Click += delegate { WindowState = FormWindowState.Minimized; };
            close.Click += delegate { Close(); };
            save.Click += delegate {
                try
                {
                    if (!runtime.Save(Draft())) MessageBox.Show(this, runtime.Message, "switchTheme", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    else startupMismatch = false;
                }
                catch (Exception ex) { runtime.Store.Log(ex.ToString()); MessageBox.Show(this, "保存失败：" + ex.Message, "switchTheme", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                UpdateSavedState();
            };
            tips.SetToolTip(minimize, "最小化"); tips.SetToolTip(close, "关闭窗口后继续在托盘运行");
            tips.SetToolTip(light, "切换 Windows 系统与应用为浅色"); tips.SetToolTip(dark, "切换 Windows 系统与应用为深色");
            tips.SetToolTip(lightTime, "白天开始时间，例如 07:00"); tips.SetToolTip(darkTime, "白天结束时间，例如 19:00；开始晚于结束时跨午夜");
            tips.SetToolTip(enabled, "每天按时间切换主题"); tips.SetToolTip(startup, "Windows 登录后只在托盘运行");
            initialized = true; LayoutControls();
            runtime.Changed += RuntimeChanged; RuntimeChanged(); UpdateSavedState();
        }
        private int S(float value) { return (int)Math.Round(value * scale); }
        private Rectangle Rect(float x, float y, float w, float h) { return new Rectangle(S(x), S(y), S(w), S(h)); }
        private void LayoutControls()
        {
            if (!initialized) return;
            SuspendLayout();
            if (titleFont != null) { titleFont.Dispose(); labelFont.Dispose(); buttonFont.Dispose(); saveFont.Dispose(); timeFont.Dispose(); }
            titleFont = new Font("Microsoft YaHei UI", 14 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            labelFont = new Font("Microsoft YaHei UI", 13 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            buttonFont = new Font("Microsoft YaHei UI", 12 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            saveFont = new Font("Microsoft YaHei UI", 12 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            timeFont = new Font("Segoe UI", 16 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            ClientSize = new Size(S(320), S(257));
            light.Bounds = Rect(116, 11, 60, 34); dark.Bounds = Rect(180, 11, 60, 34);
            minimize.Bounds = Rect(249, 13, 28, 30); close.Bounds = Rect(277, 13, 28, 30);
            lightTime.Bounds = Rect(94, 68, 62, 23); darkTime.Bounds = Rect(176, 68, 62, 23);
            enabled.Bounds = Rect(264, 120, 44, 25); startup.Bounds = Rect(264, 162, 44, 25);
            save.Bounds = Rect(12, 210, 296, 34);
            foreach (var button in new[] { light, dark, minimize, close, save }) { button.Font = buttonFont; button.DpiScale = scale; }
            save.Font = saveFont;
            lightTime.Font = darkTime.Font = timeFont; enabled.DpiScale = startup.DpiScale = scale;
            ResumeLayout(); UpdateWindowCorners(); Invalidate(true);
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e); UpdateWindowCorners();
        }
        private void UpdateWindowCorners()
        {
            if (!IsHandleCreated || Width < 1 || Height < 1) return;
            var old = Region; Region = null; if (old != null) old.Dispose();
            var smallCorners = 3;
            if (DwmSetWindowAttribute(Handle, 33, ref smallCorners, sizeof(int)) == 0) return;
            // Older Windows versions fall back to a much smaller clip radius.
            using (var outline = Shapes.Rounded(new RectangleF(0, 0, Width, Height), S(8))) Region = new Region(outline);
        }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        private void UpdateFrameColors()
        {
            if (!IsHandleCreated || palette == null) return;
            var darkFrame = ReferenceEquals(palette, Colors.Dark) ? 1 : 0;
            var border = ColorTranslator.ToWin32(palette.Line);
            DwmSetWindowAttribute(Handle, 20, ref darkFrame, sizeof(int));
            DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int));
        }
        private Settings Draft()
        { return new Settings { LightTime = lightTime.Text, DarkTime = darkTime.Text, Enabled = enabled.Checked, StartWithWindows = startup.Checked }; }
        private void DraftChanged(object sender, EventArgs e) { if (initialized) UpdateSavedState(); }
        private void UpdateSavedState()
        {
            var draft = Draft();
            save.Enabled = startupMismatch || !draft.SameAs(runtime.Active) || !System.IO.File.Exists(runtime.Store.FilePath);
            var text = save.Enabled ? "保存并应用" : "设置已保存";
            if (save.Text != text) { save.Text = text; save.AccessibleName = text; }
            var tip = save.Enabled ? draft.Validate() ?? "有未保存的更改。" : runtime.Message;
            if (tips.GetToolTip(save) != tip) tips.SetToolTip(save, tip);
        }
        private void Manual(ThemeMode mode)
        {
            try { runtime.Manual(mode); }
            catch (Exception ex) { runtime.Store.Log(ex.ToString()); MessageBox.Show(this, "切换失败：" + ex.Message, "switchTheme", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void RuntimeChanged()
        {
            var next = runtime.ActualMode == ThemeMode.Dark ? Colors.Dark : Colors.Light;
            var paletteChanged = !ReferenceEquals(palette, next);
            palette = next;
            var selected = runtime.ActualSystemMode == ThemeMode.Light;
            var selectionChanged = light.Selected != selected || dark.Selected == selected;
            light.Selected = selected; dark.Selected = !selected;
            if (paletteChanged)
            {
                BackColor = palette.Page;
                foreach (var button in new[] { light, dark, minimize, close, save }) button.Palette = palette;
                enabled.Palette = startup.Palette = palette;
                lightTime.BackColor = darkTime.BackColor = palette.Page; lightTime.ForeColor = darkTime.ForeColor = palette.Text;
                UpdateFrameColors(); Invalidate(true);
            }
            else if (selectionChanged) { light.Invalidate(); dark.Invalidate(); }
            UpdateSavedState();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Diagnostics.Count("window-paint");
            base.OnPaint(e); if (!initialized || palette == null) return;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            // Pixel-centered, one-device-pixel divider keeps the custom caption distinct.
            using (var divider = new Pen(palette.Line, 1)) g.DrawLine(divider, 0, S(50) + 0.5f, Width, S(50) + 0.5f);
            using (var divider = new Pen(palette.Line, 1)) g.DrawLine(divider, S(243) + 0.5f, S(17), S(243) + 0.5f, S(39));
            Label(g, "主题", Rect(12, 8, 50, 40), titleFont);
            Label(g, "白天时间：", Rect(12, 56, 74, 44), labelFont);
            Label(g, "-", Rect(157, 56, 16, 44), labelFont);
            Label(g, "定时切换", Rect(12, 119, 160, 25), labelFont);
            Label(g, "开机静默启动", Rect(12, 161, 180, 25), labelFont);
            using (var divider = new Pen(palette.Line, 1))
                foreach (var y in new[] { 104, 151, 198 }) g.DrawLine(divider, S(12), S(y) + 0.5f, S(308), S(y) + 0.5f);
            using (var pen = new Pen(palette.Text, 1.5f * scale))
                foreach (var input in new[] { lightTime, darkTime }) if (input.Focused) g.DrawLine(pen, input.Left, input.Bottom, input.Right, input.Bottom);
        }
        private void Label(Graphics graphics, string text, Rectangle bounds, Font font)
        { TextRenderer.DrawText(graphics, text, font, bounds, palette.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding); }
        protected override void WndProc(ref Message message)
        {
            Diagnostics.Count("window-msg-" + message.Msg.ToString("X"));
            if (message.Msg == 0x84 && initialized)
            {
                var packed = message.LParam.ToInt64(); var point = PointToClient(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)));
                if (point.Y >= 0 && point.Y < S(50) && !light.Bounds.Contains(point) && !dark.Bounds.Contains(point) && !minimize.Bounds.Contains(point) && !close.Bounds.Contains(point))
                { message.Result = new IntPtr(2); return; }
            }
            base.WndProc(ref message);
            if (message.Msg == 0x2e0 && initialized)
            { scale = (message.WParam.ToInt64() & 0xffff) / 96f; LayoutControls(); }
        }
        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        { if (keyData == Keys.Enter && save.Enabled) { save.PerformClick(); return true; } return base.ProcessCmdKey(ref message, keyData); }
        protected override void OnFormClosing(FormClosingEventArgs e)
        { runtime.DraftCache = Draft(); base.OnFormClosing(e); }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                runtime.Changed -= RuntimeChanged; tips.Dispose();
                windowIcon.Dispose();
                if (titleFont != null) { titleFont.Dispose(); labelFont.Dispose(); buttonFont.Dispose(); saveFont.Dispose(); timeFont.Dispose(); }
            }
            base.Dispose(disposing);
        }
    }
}
