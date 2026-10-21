using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ClaudeTrafficLight
{
    /// <summary>
    /// 一套按 DPI 缩放过的字体。
    /// 用像素单位而不是磅：TextRenderer 走 GDI，磅值是按系统 DPI 换算的，
    /// 跨不同缩放的显示器不会自己变；像素值才是确定的。
    /// </summary>
    internal sealed class UiFonts
    {
        public readonly Font F12B, F10B, F95B, F85, F8, F8B, F75;

        public UiFonts(float scale)
        {
            F12B = Mk(12f, FontStyle.Bold, scale);
            F10B = Mk(10f, FontStyle.Bold, scale);
            F95B = Mk(9.5f, FontStyle.Bold, scale);
            F85 = Mk(8.5f, FontStyle.Regular, scale);
            F8 = Mk(8f, FontStyle.Regular, scale);
            F8B = Mk(8f, FontStyle.Bold, scale);
            F75 = Mk(7.5f, FontStyle.Regular, scale);
        }

        private static Font Mk(float pt, FontStyle style, float scale)
        {
            float px = pt * 96f / 72f * scale;
            if (px < 1f) px = 1f;
            try { return new Font("Microsoft YaHei UI", px, style, GraphicsUnit.Pixel); }
            catch
            {
                try { return new Font("Microsoft YaHei", px, style, GraphicsUnit.Pixel); }
                catch { return new Font(FontFamily.GenericSansSerif, px, style, GraphicsUnit.Pixel); }
            }
        }
    }

    internal static class Ui
    {
        /// <summary>启动时的兜底缩放（屏幕/系统 DPI）；各窗口按自己所在显示器的 DPI 再覆盖。</summary>
        public static float Scale = 1f;

        public static int Px(int v) { return (int)Math.Round(v * Scale); }

        private static readonly Dictionary<int, UiFonts> _fontCache = new Dictionary<int, UiFonts>();

        /// <summary>按缩放取一套字号，同一档位复用（窗口跨屏时只会有几档）。</summary>
        public static UiFonts Fonts(float scale)
        {
            int key = (int)Math.Round(scale * 100f);
            if (key < 50) key = 50;
            UiFonts f;
            if (!_fontCache.TryGetValue(key, out f))
            {
                f = new UiFonts(key / 100f);
                _fontCache[key] = f;
            }
            return f;
        }

        // 配色（深色）
        public static readonly Color Bg = Color.FromArgb(17, 19, 24);
        public static readonly Color Bg2 = Color.FromArgb(26, 29, 36);
        public static readonly Color Bg3 = Color.FromArgb(35, 39, 48);
        public static readonly Color Line = Color.FromArgb(47, 52, 63);
        public static readonly Color Text = Color.FromArgb(233, 236, 241);
        public static readonly Color TextDim = Color.FromArgb(150, 157, 170);
        public static readonly Color TextFaint = Color.FromArgb(104, 111, 125);

        public static readonly Color Red = Color.FromArgb(240, 68, 56);
        public static readonly Color Amber = Color.FromArgb(245, 165, 36);
        public static readonly Color Green = Color.FromArgb(34, 197, 94);
        public static readonly Color Magenta = Color.FromArgb(217, 70, 239);
        public static readonly Color Gray = Color.FromArgb(110, 117, 130);

        // 兼容旧调用：按全局 Scale 取字体；窗口内部用各自的 _fonts
        public static Font F12B { get { return Fonts(Scale).F12B; } }
        public static Font F10B { get { return Fonts(Scale).F10B; } }
        public static Font F95B { get { return Fonts(Scale).F95B; } }
        public static Font F85 { get { return Fonts(Scale).F85; } }
        public static Font F8 { get { return Fonts(Scale).F8; } }
        public static Font F8B { get { return Fonts(Scale).F8B; } }
        public static Font F75 { get { return Fonts(Scale).F75; } }

        public static Color StateColor(SessState s)
        {
            switch (s)
            {
                case SessState.Working: return Amber;
                case SessState.NeedsInput: return Red;
                case SessState.Done: return Green;
                case SessState.Error: return Magenta;
                default: return Gray;
            }
        }

        public static string ProviderLabel(string provider)
        {
            if (provider == "codex") return "Codex";
            if (provider == "dsh") return "DSH";
            return "Claude";
        }

        public static string ProviderShort(string provider)
        {
            if (provider == "codex") return "CDX";
            if (provider == "dsh") return "DSH";
            return "CLD";
        }

        public static Color ProviderColor(string provider)
        {
            if (provider == "codex") return Color.FromArgb(16, 163, 127);
            if (provider == "dsh") return Color.FromArgb(77, 107, 254);
            return Color.FromArgb(217, 119, 87);
        }

        public static GraphicsPath RoundPath(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (d <= 2 || r.Width <= d || r.Height <= d)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color c)
        {
            using (GraphicsPath p = RoundPath(r, radius))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, Rectangle r, int radius, Color c, float width)
        {
            using (GraphicsPath p = RoundPath(r, radius))
            using (Pen pen = new Pen(c, width))
                g.DrawPath(pen, p);
        }

        /// <summary>画一颗发光的状态灯。</summary>
        public static void DrawLed(Graphics g, float cx, float cy, float radius, Color color, float glow, float pulse)
        {
            if (glow > 0.01f)
            {
                float gr = radius * (2.6f + 0.5f * pulse);
                RectangleF grc = new RectangleF(cx - gr, cy - gr, gr * 2, gr * 2);
                using (GraphicsPath gp = new GraphicsPath())
                {
                    gp.AddEllipse(grc);
                    using (PathGradientBrush b = new PathGradientBrush(gp))
                    {
                        int a = (int)(90 * glow * (0.75f + 0.35f * pulse));
                        if (a < 0) a = 0;
                        if (a > 255) a = 255;
                        b.CenterColor = Color.FromArgb(a, color);
                        b.SurroundColors = new Color[] { Color.FromArgb(0, color) };
                        g.FillPath(b, gp);
                    }
                }
            }

            using (SolidBrush b = new SolidBrush(color))
                g.FillEllipse(b, cx - radius, cy - radius, radius * 2, radius * 2);

            // 高光
            using (SolidBrush b = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                g.FillEllipse(b, cx - radius * 0.55f, cy - radius * 0.62f, radius * 0.7f, radius * 0.55f);
        }

        /// <summary>未点亮的口（暗色玻璃）。</summary>
        public static void DrawLedOff(Graphics g, float cx, float cy, float radius, Color tint)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(38, tint)))
                g.FillEllipse(b, cx - radius, cy - radius, radius * 2, radius * 2);
            using (Pen p = new Pen(Color.FromArgb(62, tint), 1f))
                g.DrawEllipse(p, cx - radius, cy - radius, radius * 2, radius * 2);
        }

        public static readonly TextFormatFlags TfSingle =
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
            TextFormatFlags.VerticalCenter;

        public static readonly TextFormatFlags TfWrap =
            TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;

        public static void Text_(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, s ?? "", f, r, c, flags);
        }

        public static string HumanizeAgo(DateTime utc)
        {
            if (utc == DateTime.MinValue) return "";
            TimeSpan d = DateTime.UtcNow - utc;
            if (d.TotalSeconds < 0) d = TimeSpan.Zero;
            if (d.TotalSeconds < 15) return "刚刚";
            if (d.TotalSeconds < 60) return (int)d.TotalSeconds + " 秒前";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " 分钟前";
            if (d.TotalHours < 24) return (int)d.TotalHours + " 小时前";
            return (int)d.TotalDays + " 天前";
        }

        public static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "…";
        }

        public static string BaseName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                string t = path.TrimEnd('\\', '/');
                int i = t.LastIndexOfAny(new char[] { '\\', '/' });
                if (i >= 0 && i < t.Length - 1) t = t.Substring(i + 1);
                return t;
            }
            catch { return path; }
        }
    }
}
