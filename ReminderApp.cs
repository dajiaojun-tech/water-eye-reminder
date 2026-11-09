// 喝水 / 护眼 定时提醒
// 单文件常驻程序：右下角自定义卡片提醒，自动消失，不抢焦点，无声音。
// 目标框架 .NET Framework 4.x（C# 5 语法），用 Windows 自带 csc.exe 编译。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WaterEye
{
    enum Kind { Water, Eye }

    static class Native
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr h);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr h);

        [DllImport("user32.dll")]
        static extern int GetDpiForSystem();

        public static void TryDpiAware()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch (Exception) { }
            try { SetProcessDPIAware(); } catch (Exception) { }
        }

        public static int SystemDpi()
        {
            try
            {
                int d = GetDpiForSystem();
                if (d > 0) return d;
            }
            catch (Exception) { }
            return 96;
        }
    }

    static class Ui
    {
        public static float Scale = 1f;

        public static int S(float v)
        {
            return (int)Math.Round(v * Scale);
        }

        public static Font F(string family, float px, FontStyle style)
        {
            try { return new Font(family, px * Scale, style, GraphicsUnit.Pixel); }
            catch (Exception) { return new Font("Microsoft Sans Serif", px * Scale, style, GraphicsUnit.Pixel); }
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2f;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    static class Theme
    {
        public static Color Accent(Kind k)
        {
            return k == Kind.Water ? Color.FromArgb(0x2D, 0x86, 0xF0) : Color.FromArgb(0x22, 0xA0, 0x6C);
        }

        public static Color AccentDeep(Kind k)
        {
            return k == Kind.Water ? Color.FromArgb(0x1B, 0x6F, 0xD6) : Color.FromArgb(0x18, 0x87, 0x59);
        }

        public static Color Bg(Kind k)
        {
            return k == Kind.Water ? Color.FromArgb(0xF6, 0xFA, 0xFF) : Color.FromArgb(0xF5, 0xFC, 0xF7);
        }

        public static Color Border(Kind k)
        {
            return k == Kind.Water ? Color.FromArgb(0xD8, 0xE6, 0xF8) : Color.FromArgb(0xD5, 0xEE, 0xE0);
        }

        public static Color Title = Color.FromArgb(0x1E, 0x26, 0x33);
        public static Color Sub = Color.FromArgb(0x60, 0x6C, 0x7C);
    }

    static class Cfg
    {
        public static int WaterMin = 30;
        public static int EyeMin = 20;
        public static int WaterSec = 25;
        public static int EyeSec = 20;

        public const string DefWaterTitle = "该喝水了";
        public const string DefWaterText = "起身接杯水，慢慢喝完（约 400 毫升）";
        public const string DefEyeTitle = "该护眼了";
        public const string DefEyeText = "看向 6 米外的远处，保持 20 秒";

        public static string WaterTitle = DefWaterTitle;
        public static string WaterText = DefWaterText;
        public static string EyeTitle = DefEyeTitle;
        public static string EyeText = DefEyeText;

        // true = 对齐钟点（:00 / :30 这种）；false = 从启动那一刻起按间隔倒计时
        public static bool AlignClock = true;

        const string Template =
"# ==========================================\n" +
"# 喝水 / 护眼提醒 · 配置（改完退出程序再打开）\n" +
"# ==========================================\n" +
"# ---- 间隔（分钟）----\n" +
"# 喝水间隔；建议能整除 60，这样才会落在整点上\n" +
"water_minutes=30\n" +
"# 护眼间隔\n" +
"eye_minutes=20\n" +
"# true = 对齐钟点（:00、:30）；false = 从启动那一刻开始数\n" +
"align_clock=true\n" +
"\n" +
"# ---- 卡片停留时间（秒）----\n" +
"water_seconds=25\n" +
"eye_seconds=20\n" +
"\n" +
"# ---- 卡片文案（随便改；文字太长会自动缩字号）----\n" +
"water_title=该喝水了\n" +
"water_text=起身接杯水，慢慢喝完（约 400 毫升）\n" +
"eye_title=该护眼了\n" +
"eye_text=看向 6 米外的远处，保持 20 秒\n";

        public static string Path = "";
        public static void Load(string path)
        {
            Path = path;
            try
            {
                if (!File.Exists(path))
                {
                    try { File.WriteAllText(path, Template, new System.Text.UTF8Encoding(false)); }
                    catch (Exception) { }
                    return;
                }

                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim().TrimStart('\uFEFF');
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    if (val.Length == 0) continue;

                    int n;
                    if (key == "water_minutes") { if (int.TryParse(val, out n) && n > 0) WaterMin = n; continue; }
                    if (key == "eye_minutes") { if (int.TryParse(val, out n) && n > 0) EyeMin = n; continue; }
                    if (key == "water_seconds") { if (int.TryParse(val, out n) && n > 0) WaterSec = n; continue; }
                    if (key == "eye_seconds") { if (int.TryParse(val, out n) && n > 0) EyeSec = n; continue; }

                    if (key == "align_clock")
                    {
                        string v = val.ToLowerInvariant();
                        AlignClock = !(v == "0" || v == "false" || v == "no" || v == "off");
                        continue;
                    }

                    if (key == "water_title") { WaterTitle = val; continue; }
                    if (key == "water_text") { WaterText = val; continue; }
                    if (key == "eye_title") { EyeTitle = val; continue; }
                    if (key == "eye_text") { EyeText = val; continue; }
                }
            }
            catch (Exception) { }
        }

        // 只改写四行文案，注释和其他设置都保留
        public static void SaveTexts(string wt, string wx, string et, string ex)
        {
            try
            {
                if (string.IsNullOrEmpty(Path) || !File.Exists(Path)) Load(Path);
                string[] lines = File.ReadAllLines(Path);
                string[] keys = { "water_title", "water_text", "eye_title", "eye_text" };
                string[] vals = { wt, wx, et, ex };
                for (int i = 0; i < lines.Length; i++)
                {
                    int eq = lines[i].IndexOf('=');
                    if (eq <= 0) continue;
                    string k = lines[i].Substring(0, eq).Trim().ToLowerInvariant();
                    int idx = Array.IndexOf(keys, k);
                    if (idx >= 0) lines[i] = keys[idx] + "=" + vals[idx];
                }
                File.WriteAllLines(Path, lines, new System.Text.UTF8Encoding(false));
            }
            catch (Exception) { }

            WaterTitle = wt; WaterText = wx; EyeTitle = et; EyeText = ex;
        }
    }

    // ---------------------------------------------------------------- 卡片

    class ToastForm : Form
    {
        static readonly List<ToastForm> live = new List<ToastForm>();

        readonly Kind kind;
        readonly string titleText, subText;
        readonly int w, h;
        readonly Font fTitle, fSub;
        readonly Stopwatch sw = new Stopwatch();
        readonly System.Windows.Forms.Timer anim;
        int totalMs;
        bool done;

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE  不抢焦点
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW  不进 Alt+Tab
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW  淡淡投影
                return cp;
            }
        }

        public ToastForm(Kind kind, int durationMs)
        {
            this.kind = kind;
            totalMs = Math.Max(2000, durationMs);

            titleText = kind == Kind.Water ? Cfg.WaterTitle : Cfg.EyeTitle;
            subText = kind == Kind.Water ? Cfg.WaterText : Cfg.EyeText;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            w = Ui.S(424);
            h = Ui.S(122);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Bg(kind);
            MinimizeBox = false;
            MaximizeBox = false;
            Cursor = Cursors.Default;
            Size = new Size(w, h);
            Opacity = 0.0;

            int textLeft = Ui.S(95);
            int textWidth = w - textLeft - Ui.S(22);
            using (Bitmap probe = new Bitmap(1, 1))
            using (Graphics pg = Graphics.FromImage(probe))
            {
                fTitle = Fit(pg, titleText, 19f, FontStyle.Bold, textWidth, 13f);
                fSub = Fit(pg, subText, 13.5f, FontStyle.Regular, textWidth, 9.5f);
            }

            Place();

            try
            {
                using (GraphicsPath gp = Ui.Round(new RectangleF(0, 0, w, h), Ui.S(13)))
                {
                    Region = new Region(gp);
                }
            }
            catch (Exception) { }

            MouseClick += OnClickAnywhere;

            anim = new System.Windows.Forms.Timer();
            anim.Interval = 30;
            anim.Tick += OnTick;
            sw.Start();
            anim.Start();

            live.Add(this);
        }

        public ToastForm(Kind kind, int durationMs, string title, string sub)
            : this(kind, durationMs)
        {
            titleText = title;
            subText = sub;
        }

        // 文案可编辑，太长就自动往小字号里挑一个放得下的
        static Font Fit(Graphics g, string text, float maxPx, FontStyle style, int maxWidth, float minPx)
        {
            for (float px = maxPx; px > minPx; px -= 0.5f)
            {
                Font f = Ui.F("Microsoft YaHei UI", px, style);
                Size sz = TextRenderer.MeasureText(g, text, f, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                if (sz.Width <= maxWidth) return f;
                f.Dispose();
            }
            return Ui.F("Microsoft YaHei UI", minPx, style);
        }
        void Place()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int margin = Ui.S(18), gap = Ui.S(12);
            int y = wa.Bottom - margin - h;

            foreach (ToastForm t in live)
            {
                if (y < t.Top + t.Height + gap && y + h > t.Top - gap)
                    y = t.Top - gap - h;
            }
            if (y < wa.Top + margin) y = wa.Top + margin;

            Location = new Point(wa.Right - margin - w, y);
        }

        void OnClickAnywhere(object sender, MouseEventArgs e)
        {
            if (done) return;
            int left = totalMs - (int)sw.ElapsedMilliseconds;
            if (left > 320) totalMs = (int)sw.ElapsedMilliseconds + 320;
        }

        void OnTick(object sender, EventArgs e)
        {
            long t = sw.ElapsedMilliseconds;

            if (t >= totalMs)
            {
                Finish();
                return;
            }

            const int fadeIn = 200, fadeOut = 420;
            float op;
            if (t < fadeIn) op = (float)t / fadeIn;
            else if (t > totalMs - fadeOut) op = (float)(totalMs - t) / fadeOut;
            else op = 1f;

            op *= 0.97f;
            if (op < 0) op = 0;
            if (op > 0.97f) op = 0.97f;
            Opacity = op;

            Invalidate();
        }

        void Finish()
        {
            if (done) return;
            done = true;
            anim.Stop();
            anim.Dispose();
            live.Remove(this);
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (fTitle != null) fTitle.Dispose();
                if (fSub != null) fSub.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            Color accent = Theme.Accent(kind);
            RectangleF box = new RectangleF(0.5f, 0.5f, w - 1f, h - 1f);

            using (GraphicsPath gp = Ui.Round(box, Ui.S(13)))
            {
                using (SolidBrush b = new SolidBrush(Theme.Bg(kind))) g.FillPath(b, gp);

                // 左侧色条（跟随圆角裁切）
                GraphicsState st = g.Save();
                g.SetClip(gp);
                using (SolidBrush ab = new SolidBrush(accent)) g.FillRectangle(ab, 0, 0, Ui.S(6), h);
                g.Restore(st);

                using (Pen p = new Pen(Theme.Border(kind), 1f)) g.DrawPath(p, gp);
            }

            int cx = Ui.S(54), cy = Ui.S(58), r = Ui.S(25);
            using (SolidBrush cb = new SolidBrush(accent)) g.FillEllipse(cb, cx - r, cy - r, r * 2, r * 2);

            if (kind == Kind.Water) DrawDrop(g, cx, cy, r * 0.60f);
            else DrawEye(g, cx, cy, r * 0.52f);

            int left = Ui.S(95);
            string title = titleText;
            string sub = subText;

            TextRenderer.DrawText(g, title, fTitle, new Point(left, Ui.S(27)), Theme.Title,
                                  TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, sub, fSub, new Point(left, Ui.S(64)), Theme.Sub,
                                  TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            // 底部倒计时进度条
            int px = Ui.S(95), py = Ui.S(h - 20), pw = w - px - Ui.S(22), ph = Ui.S(3);
            using (GraphicsPath tp = Ui.Round(new RectangleF(px, py, pw, ph), ph / 2f))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(40, accent)))
                g.FillPath(tb, tp);

            float remain = 1f - (float)sw.ElapsedMilliseconds / totalMs;
            if (remain < 0) remain = 0;
            float fw = pw * remain;
            if (fw > 1)
            {
                using (GraphicsPath fp = Ui.Round(new RectangleF(px, py, fw, ph), ph / 2f))
                using (SolidBrush fb = new SolidBrush(accent))
                    g.FillPath(fb, fp);
            }
        }

        static void DrawDrop(Graphics g, float cx, float cy, float k)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                p.StartFigure();
                p.AddBezier(new PointF(0f, -1f), new PointF(0.30f, -0.52f), new PointF(0.62f, -0.06f), new PointF(0.62f, 0.28f));
                p.AddArc(new RectangleF(-0.62f, -0.34f, 1.24f, 1.24f), 0, 180);
                p.AddBezier(new PointF(-0.62f, 0.28f), new PointF(-0.62f, -0.06f), new PointF(-0.30f, -0.52f), new PointF(0f, -1f));
                p.CloseFigure();

                GraphicsState st = g.Save();
                g.TranslateTransform(cx, cy);
                g.ScaleTransform(k, k);
                using (SolidBrush b = new SolidBrush(Color.White)) g.FillPath(b, p);
                g.Restore(st);
            }

            // 高光
            using (SolidBrush b = new SolidBrush(Color.FromArgb(150, Color.White)))
                g.FillEllipse(b, cx - k * 0.30f, cy + k * 0.05f, k * 0.26f, k * 0.34f);
        }

        static void DrawEye(Graphics g, float cx, float cy, float k)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                p.StartFigure();
                p.AddBezier(new PointF(-1.05f, 0f), new PointF(-0.50f, -0.92f), new PointF(0.50f, -0.92f), new PointF(1.05f, 0f));
                p.AddBezier(new PointF(1.05f, 0f), new PointF(0.50f, 0.92f), new PointF(-0.50f, 0.92f), new PointF(-1.05f, 0f));
                p.CloseFigure();

                GraphicsState st = g.Save();
                g.TranslateTransform(cx, cy);
                g.ScaleTransform(k, k);
                using (SolidBrush b = new SolidBrush(Color.White)) g.FillPath(b, p);
                using (SolidBrush b = new SolidBrush(Theme.AccentDeep(Kind.Eye)))
                    g.FillEllipse(b, -0.34f, -0.34f, 0.68f, 0.68f);
                using (SolidBrush b = new SolidBrush(Color.White))
                    g.FillEllipse(b, -0.17f, -0.17f, 0.20f, 0.20f);
                g.Restore(st);
            }
        }
    }

    // ---------------------------------------------------------------- 托盘 + 调度

    class TrayApp : ApplicationContext
    {
        readonly NotifyIcon ni;
        readonly System.Windows.Forms.Timer tick;
        readonly Icon trayIcon;
        readonly ToolStripMenuItem miWaterNext, miEyeNext, miPause, miAutostart;
        readonly EventWaitHandle showEvent;
        ContextMenuStrip menu;

        DateTime nextWater, nextEye;
        bool paused;

        public TrayApp(EventWaitHandle evt)
        {
            showEvent = evt;

            Reset();

            trayIcon = MakeIcon();

            menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;

            ToolStripMenuItem head = new ToolStripMenuItem(
                "喝水 " + HourList(Cfg.WaterMin) + " · 护眼 " + HourList(Cfg.EyeMin));
            head.Enabled = false;
            menu.Items.Add(head);
            menu.Items.Add(new ToolStripSeparator());

            miWaterNext = new ToolStripMenuItem("下次喝水：--");
            miEyeNext = new ToolStripMenuItem("下次护眼：--");
            miWaterNext.Enabled = false;
            miEyeNext.Enabled = false;
            menu.Items.Add(miWaterNext);
            menu.Items.Add(miEyeNext);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miW = new ToolStripMenuItem("现在弹一次：喝水");
            miW.Click += delegate { Show(Kind.Water); };
            ToolStripMenuItem miE = new ToolStripMenuItem("现在弹一次：护眼");
            miE.Click += delegate { Show(Kind.Eye); };
            menu.Items.Add(miW);
            menu.Items.Add(miE);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miEdit = new ToolStripMenuItem("编辑提醒词…");
            miEdit.Click += delegate { OpenSettings(); };
            menu.Items.Add(miEdit);

            ToolStripMenuItem miReload = new ToolStripMenuItem("重新读取配置");
            miReload.Click += delegate { ReloadConfig(); };
            menu.Items.Add(miReload);
            menu.Items.Add(new ToolStripSeparator());

            miPause = new ToolStripMenuItem("暂停提醒");
            miPause.Click += delegate
            {
                paused = !paused;
                miPause.Checked = paused;
                miPause.Text = paused ? "恢复提醒" : "暂停提醒";
                if (!paused) Reset();
                Refresh();
            };
            menu.Items.Add(miPause);

            miAutostart = new ToolStripMenuItem("开机自动启动");
            miAutostart.Checked = Autostart.IsOn();
            miAutostart.Click += delegate
            {
                try
                {
                    Autostart.Set(!Autostart.IsOn());
                    miAutostart.Checked = Autostart.IsOn();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("设置开机启动失败：" + ex.Message, "提醒");
                }
            };
            menu.Items.Add(miAutostart);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miExit = new ToolStripMenuItem("退出");
            miExit.Click += delegate { ExitThread(); };
            menu.Items.Add(miExit);

            menu.Opening += delegate { Refresh(); };

            ni = new NotifyIcon();
            ni.Icon = trayIcon;
            ni.Text = "喝水 / 护眼提醒";
            ni.Visible = true;
            ni.ContextMenuStrip = menu;
            ni.MouseDoubleClick += delegate { menu.Show(Cursor.Position); };

            Refresh();

            tick = new System.Windows.Forms.Timer();
            tick.Interval = 1000;
            tick.Tick += OnTick;
            tick.Start();

        }

        static string HourList(int step)
        {
            System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>();
            for (int m = 0; m < 60; m += step) parts.Add(":" + m.ToString("00"));
            return string.Join(" / ", parts.ToArray());
        }

        // 已经有一个实例在跑时，给用户一个明确反馈
        void OpenSettings()
        {
            try
            {
                using (SettingsForm f = new SettingsForm())
                {
                    if (f.ShowDialog() == DialogResult.OK)
                    {
                        Refresh();
                        ToastForm t = new ToastForm(Kind.Water, 2600, "提醒词已更新", "下一个提醒就用新文案");
                        t.Show();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开设置失败：" + ex.Message, "提醒");
            }
        }

        void OpenConfig()
        {
            try
            {
                if (!File.Exists(Cfg.Path)) Cfg.Load(Cfg.Path);
                Process.Start(Cfg.Path);
            }
            catch (Exception)
            {
                try { Process.Start("notepad.exe", "\"" + Cfg.Path + "\""); }
                catch (Exception ex) { MessageBox.Show("打不开配置文件：" + ex.Message, "提醒"); }
            }
        }

        void ReloadConfig()
        {
            try
            {
                Cfg.Load(Cfg.Path);
                Reset();
                Refresh();
                ToastForm f = new ToastForm(Kind.Water, 3000, "配置已重新读取",
                    "喝水 " + HourList(Cfg.WaterMin) + " · 护眼 " + HourList(Cfg.EyeMin));
                f.Show();
            }
            catch (Exception) { }
        }

        void ShowStatus()
        {
            try
            {
                ToastForm f = new ToastForm(Kind.Water, 3500, "提醒正在运行",
                    "下次喝水 " + nextWater.ToString("HH:mm") + " · 下次护眼 " + nextEye.ToString("HH:mm"));
                f.Show();
            }
            catch (Exception) { }
        }

        void Reset()
        {
            DateTime now = DateTime.Now;
            nextWater = NextDue(now, Cfg.WaterMin);
            nextEye = NextDue(now, Cfg.EyeMin);
        }

        // 按配置决定下一次的时间点
        static DateTime NextDue(DateTime now, int step)
        {
            return Cfg.AlignClock ? NextAligned(now, step) : now.AddMinutes(step);
        }

        // 对齐到整点的刻度：比如 30 分钟 → 下一个 :00 或 :30
        static DateTime NextAligned(DateTime now, int step)
        {
            int minutes = now.Hour * 60 + now.Minute;
            int next = (minutes / step + 1) * step;
            DateTime t = now.Date.AddMinutes(next);
            if (t <= now) t = t.AddMinutes(step);
            return t;
        }

        void Refresh()
        {
            miWaterNext.Text = "下次喝水：" + Fmt(nextWater);
            miEyeNext.Text = "下次护眼：" + Fmt(nextEye);
        }

        static string Fmt(DateTime t)
        {
            TimeSpan d = t - DateTime.Now;
            if (d.TotalSeconds < 0) d = TimeSpan.Zero;
            string left = d.TotalMinutes >= 1
                ? string.Format("{0} 分钟后", (int)Math.Ceiling(d.TotalMinutes))
                : string.Format("{0} 秒后", Math.Max(1, (int)Math.Ceiling(d.TotalSeconds)));
            return string.Format("{0:HH:mm}（{1}）", t, left);
        }

        void OnTick(object sender, EventArgs e)
        {
            try { if (showEvent.WaitOne(0)) ShowStatus(); } catch (Exception) { }



            DateTime now = DateTime.Now;

            if (now >= nextWater)
            {
                Show(Kind.Water);
                nextWater = NextDue(now, Cfg.WaterMin);
            }
            if (now >= nextEye)
            {
                Show(Kind.Eye);
                nextEye = NextDue(now, Cfg.EyeMin);
            }
            Refresh();
        }

        static void Show(Kind k)
        {
            int sec = k == Kind.Water ? Cfg.WaterSec : Cfg.EyeSec;
            ToastForm f = new ToastForm(k, sec * 1000);
            f.Show();
        }

        Icon MakeIcon()
        {
            // 优先用同目录下的 app.ico（16px 帧最清晰）
            try
            {
                string ico = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(ico)) return new Icon(ico, SystemInformation.SmallIconSize);
            }
            catch (Exception) { }

            // 退而求其次：exe 内嵌图标
            try
            {
                Icon ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (ico != null) return ico;
            }
            catch (Exception) { }

            return (Icon)SystemIcons.Application.Clone();
        }

        protected override void ExitThreadCore()
        {
            tick.Stop();
            ni.Visible = false;
            ni.Dispose();
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && trayIcon != null)
            {
                trayIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // ---------------------------------------------------------------- 开机自启

    static class Autostart
    {
        public static string LinkPath
        {
            get
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                return Path.Combine(dir, "喝水护眼提醒.lnk");
            }
        }

        // 有 启动提醒.cmd 就让它当启动器（不经过资源管理器，不会触发 SmartScreen）
        public static string LauncherPath
        {
            get
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                if (dir == null) return Application.ExecutablePath;
                string cmd = Path.Combine(dir, "启动提醒.cmd");
                return File.Exists(cmd) ? cmd : Application.ExecutablePath;
            }
        }

        public static bool IsOn()
        {
            try
            {
                if (!File.Exists(LinkPath)) return false;
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return true;
                object shell = Activator.CreateInstance(shellType);
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { LinkPath });
                string target = (string)link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null);
                Marshal.ReleaseComObject(link);
                Marshal.ReleaseComObject(shell);
                if (target == null) return true;
                return string.Equals(target, LauncherPath, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(target, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return File.Exists(LinkPath);
            }
        }

        public static void Set(bool on)
        {
            if (!on)
            {
                if (File.Exists(LinkPath)) File.Delete(LinkPath);
                return;
            }

            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new InvalidOperationException("系统缺少 WScript.Shell");

            object shell = Activator.CreateInstance(shellType);
            object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { LinkPath });
            Type lt = link.GetType();

            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { LauncherPath });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(Application.ExecutablePath) });
            lt.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "喝水 / 护眼定时提醒（右下角卡片）" });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);

            Marshal.ReleaseComObject(link);
            Marshal.ReleaseComObject(shell);
        }
    }

    // ---------------------------------------------------------------- 入口

    // ---------------------------------------------------------------- 设置窗口

    class SettingsForm : Form
    {
        readonly TextBox tbWT, tbWX, tbET, tbEX;

        public SettingsForm()
        {
            Text = "提醒词设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(480, 332);
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Color.White;

            int x = 22, w = 436, y = 18;

            Label(x, "喝水 · 大字", ref y);
            tbWT = Box(x, ref y, w);
            Label(x, "喝水 · 小字", ref y);
            tbWX = Box(x, ref y, w);
            Label(x, "护眼 · 大字", ref y);
            tbET = Box(x, ref y, w);
            Label(x, "护眼 · 小字", ref y);
            tbEX = Box(x, ref y, w);

            tbWT.Text = Cfg.WaterTitle;
            tbWX.Text = Cfg.WaterText;
            tbET.Text = Cfg.EyeTitle;
            tbEX.Text = Cfg.EyeText;

            y += 12;

            Button btnDefault = new Button();
            btnDefault.Text = "恢复默认";
            btnDefault.Location = new Point(x, y);
            btnDefault.Size = new Size(96, 30);
            btnDefault.Click += delegate
            {
                tbWT.Text = Cfg.DefWaterTitle;
                tbWX.Text = Cfg.DefWaterText;
                tbET.Text = Cfg.DefEyeTitle;
                tbEX.Text = Cfg.DefEyeText;
            };
            Controls.Add(btnDefault);

            Button btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.Location = new Point(x + w - 176, y);
            btnCancel.Size = new Size(80, 30);
            btnCancel.DialogResult = DialogResult.Cancel;
            Controls.Add(btnCancel);

            Button btnSave = new Button();
            btnSave.Text = "保存并生效";
            btnSave.Location = new Point(x + w - 88, y);
            btnSave.Size = new Size(88, 30);
            btnSave.Click += delegate
            {
                Cfg.SaveTexts(Value(tbWT, Cfg.DefWaterTitle), Value(tbWX, Cfg.DefWaterText),
                              Value(tbET, Cfg.DefEyeTitle), Value(tbEX, Cfg.DefEyeText));
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(btnSave);

            AcceptButton = btnSave;
            CancelButton = btnCancel;
        }

        void Label(int x, string text, ref int y)
        {
            System.Windows.Forms.Label l = new System.Windows.Forms.Label();
            l.Text = text;
            l.Location = new Point(x, y);
            l.AutoSize = true;
            l.ForeColor = Color.FromArgb(0x50, 0x5A, 0x68);
            Controls.Add(l);
            y += 22;
        }

        TextBox Box(int x, ref int y, int w)
        {
            TextBox tb = new TextBox();
            tb.Location = new Point(x, y);
            tb.Width = w;
            tb.Font = new Font("Microsoft YaHei UI", 10f);
            Controls.Add(tb);
            y += 40;
            return tb;
        }

        static string Value(TextBox tb, string fallback)
        {
            string s = (tb.Text ?? "").Trim();
            return s.Length == 0 ? fallback : s;
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try { Run(args); }
            catch (Exception ex) { LogCrash(ex); throw; }
        }

        static void LogCrash(Exception ex)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "watereye_crash.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + "\r\n\r\n");
            }
            catch (Exception) { }
        }

        static void Run(string[] args)
        {
            Native.TryDpiAware();
            Ui.Scale = Native.SystemDpi() / 96f;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            if (dir != null) Cfg.Load(Path.Combine(dir, "ReminderApp.ini"));

            string a0 = args.Length > 0 ? args[0].ToLowerInvariant() : "";

            if (a0 == "--test" || a0 == "--preview")
            {
                string which = args.Length > 1 ? args[1].ToLowerInvariant() : "water";
                int sec = args.Length > 2 ? ParseInt(args[2], 8) : 8;
                Application.Run(new PreviewContext(which, sec * 1000));
                return;
            }

            if (a0 == "--demo")
            {
                int ms = args.Length > 1 ? ParseInt(args[1], 12) * 1000 : 12000;
                Application.Run(new DemoContext(ms));
                return;
            }

            if (a0 == "--enable-autostart")
            {
                try { Autostart.Set(true); }
                catch (Exception ex) { MessageBox.Show("设置开机启动失败：\r\n" + ex.Message, "喝水 / 护眼提醒"); }
                return;
            }
            if (a0 == "--disable-autostart")
            {
                try { Autostart.Set(false); }
                catch (Exception ex) { MessageBox.Show("取消开机启动失败：\r\n" + ex.Message, "喝水 / 护眼提醒"); }
                return;
            }
            if (a0 == "--status")
            {
                Console.WriteLine("autostart=" + (Autostart.IsOn() ? "on" : "off"));
                Console.WriteLine("link=" + Autostart.LinkPath);
                return;
            }

            bool createdNew = true;
            EventWaitHandle evt = null;
            try
            {
                evt = new EventWaitHandle(false, EventResetMode.AutoReset,
                                          "Local\\WaterEyeReminder.ShowMenu", out createdNew);
            }
            catch (Exception)
            {
                // 残留实例（可能权限不同）占着这个名字时，别为了单实例把进程崩掉
                createdNew = true;
                evt = new EventWaitHandle(false, EventResetMode.AutoReset);
            }
            if (!createdNew)
            {
                // 已经在跑了：叫它把菜单弹出来
                try { evt.Set(); } catch (Exception) { }
                return;
            }

            Application.Run(new TrayApp(evt));
        }

        static int ParseInt(string s, int fallback)
        {
            int n;
            return int.TryParse(s, out n) && n > 0 ? n : fallback;
        }
    }

    class PreviewContext : ApplicationContext
    {
        public PreviewContext(string which, int ms)
        {
            Kind k = which.StartsWith("e") ? Kind.Eye : Kind.Water;
            ToastForm f = new ToastForm(k, ms);
            f.FormClosed += delegate { ExitThread(); };
            f.Show();
        }
    }

    // 同时展示两张卡片，用于检查叠放位置
    class DemoContext : ApplicationContext
    {
        int open = 2;

        public DemoContext(int ms)
        {
            ToastForm eye = new ToastForm(Kind.Eye, ms);
            eye.FormClosed += Closed;
            eye.Show();

            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 400;
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                ToastForm water = new ToastForm(Kind.Water, ms);
                water.FormClosed += Closed;
                water.Show();
            };
            t.Start();
        }

        void Closed(object sender, FormClosedEventArgs e)
        {
            if (--open <= 0) ExitThread();
        }
    }
}
