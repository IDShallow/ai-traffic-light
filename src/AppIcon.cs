using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ClaudeTrafficLight
{
    internal static class AppIcon
    {
        private static readonly Icon[] Cache = new Icon[5];

        public static Icon ForState(SessState s)
        {
            int idx = (int)s;
            if (idx < 0 || idx >= Cache.Length) idx = 0;
            if (Cache[idx] == null) Cache[idx] = Build(s);
            return Cache[idx];
        }

        private static Icon Build(SessState s)
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                Rectangle housing = new Rectangle(8, 1, 16, 30);
                using (GraphicsPath p = Ui.RoundPath(housing, 8))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 28, 31, 38)))
                    g.FillPath(b, p);
                using (GraphicsPath p = Ui.RoundPath(housing, 8))
                using (Pen pen = new Pen(Color.FromArgb(255, 70, 76, 90), 1.2f))
                    g.DrawPath(pen, p);

                float cx = 16;
                float r = 4.2f;
                float y1 = 8, y2 = 16, y3 = 24;

                Color lit;
                int litIndex;
                switch (s)
                {
                    case SessState.Working: lit = Ui.Amber; litIndex = 1; break;
                    case SessState.NeedsInput:
                    case SessState.Error: lit = Ui.Red; litIndex = 0; break;
                    case SessState.Offline: lit = Ui.Gray; litIndex = -1; break;
                    default: lit = Ui.Green; litIndex = 2; break;
                }

                Color[] baseColors = new Color[] { Ui.Red, Ui.Amber, Ui.Green };
                float[] ys = new float[] { y1, y2, y3 };
                for (int i = 0; i < 3; i++)
                {
                    if (i == litIndex) Ui.DrawLed(g, cx, ys[i], r, lit, 0.7f, 0.3f);
                    else Ui.DrawLedOff(g, cx, ys[i], r, baseColors[i]);
                }

                IntPtr h = bmp.GetHicon();
                return Icon.FromHandle(h);
            }
        }
    }
}
