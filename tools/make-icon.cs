using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

internal static class MakeIcon
{
    private static void Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "app.ico";
        int size = 64;

        using (Bitmap bmp = new Bitmap(size, size))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            Rectangle housing = new Rectangle(16, 3, 32, 58);
            using (GraphicsPath p = Round(housing, 13))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 22, 25, 32)))
                g.FillPath(b, p);
            using (GraphicsPath p = Round(housing, 13))
            using (Pen pen = new Pen(Color.FromArgb(255, 72, 78, 92), 2f))
                g.DrawPath(pen, p);

            Lamp(g, 32, 17, 8f, Color.FromArgb(240, 68, 56), true);
            Lamp(g, 32, 32, 8f, Color.FromArgb(245, 165, 36), false);
            Lamp(g, 32, 47, 8f, Color.FromArgb(34, 197, 94), false);

            IntPtr h = bmp.GetHicon();
            using (Icon icon = Icon.FromHandle(h))
            using (FileStream fs = File.Create(outPath))
                icon.Save(fs);
        }
    }

    private static void Lamp(Graphics g, float cx, float cy, float r, Color c, bool lit)
    {
        if (lit)
        {
            RectangleF gr = new RectangleF(cx - r * 2.4f, cy - r * 2.4f, r * 4.8f, r * 4.8f);
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(gr);
                using (PathGradientBrush b = new PathGradientBrush(gp))
                {
                    b.CenterColor = Color.FromArgb(110, c);
                    b.SurroundColors = new Color[] { Color.FromArgb(0, c) };
                    g.FillPath(b, gp);
                }
            }
        }

        int a = lit ? 255 : 55;
        using (SolidBrush b = new SolidBrush(Color.FromArgb(a, c)))
            g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
        using (SolidBrush b = new SolidBrush(Color.FromArgb(lit ? 80 : 30, 255, 255, 255)))
            g.FillEllipse(b, cx - r * 0.5f, cy - r * 0.6f, r * 0.7f, r * 0.5f);
    }

    private static GraphicsPath Round(Rectangle r, int radius)
    {
        GraphicsPath p = new GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
