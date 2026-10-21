using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClaudeTrafficLight
{
    /// <summary>总览：一小块，每会话一个格子，一眼看全。</summary>
    internal sealed class OverviewForm : Form
    {
        public event Action<string> CellClicked;
        public event Action<SessionInfo> CellActivated;
        public event Action OpenMainRequested;
        public event Action ClosedByUser;

        private readonly AppSettings _settings;
        private readonly Rectangle _anchor;
        public Func<SessionInfo, string> NameProvider;
        public bool PrivacyMode;
        private List<SessionInfo> _items = new List<SessionInfo>();
        private string _selected = "";
        private int _hot = -1;
        private bool _positioned;

        private readonly int _pad = 8;
        private readonly int _gap = 6;
        private int _cellW, _cellH, _headerH, _cols;
        private Rectangle _btnClose;

        public OverviewForm(AppSettings settings, Rectangle anchor)
        {
            _settings = settings;
            _anchor = anchor;
            _cellW = Ui.Px(112);
            _cellH = Ui.Px(56);
            _headerH = Ui.Px(26);

            FormBorderStyle = FormBorderStyle.None;
            BackColor = Ui.Bg;
            ShowInTaskbar = false;
            TopMost = settings.TopMost;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            SetStyle(ControlStyles.StandardDoubleClick, true);
            Text = "AI 总览";

            Relayout();
            if (settings.OverviewPos != null && settings.OverviewPos.Length == 2)
                Location = new Point(settings.OverviewPos[0], settings.OverviewPos[1]);
            else
                Location = new Point(anchor.Left - Width - Ui.Px(10), anchor.Top);
            EnsureOnScreen();

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem open = new ToolStripMenuItem("打开主窗口");
            open.Click += delegate { if (OpenMainRequested != null) OpenMainRequested(); };
            ToolStripMenuItem close = new ToolStripMenuItem("关闭总览");
            close.Click += delegate { Hide(); if (ClosedByUser != null) ClosedByUser(); };
            menu.Items.Add(open);
            menu.Items.Add(close);
            ContextMenuStrip = menu;
        }

        private void EnsureOnScreen()
        {
            // 多显示器：窗口在哪块屏就留在哪块屏；每次刷新都调这里，不能按主屏夹
            _positioned = true;
            Location = ScreenUtil.KeepVisible(new Rectangle(Location, Size));
        }

        public void SetItems(List<SessionInfo> items)
        {
            _items = items ?? new List<SessionInfo>();
            Relayout();
            EnsureOnScreen();
            Invalidate();
        }

        public void SetSelected(string id)
        {
            _selected = id ?? "";
            Invalidate();
        }

        private int ShownCount()
        {
            return Math.Min(_items.Count, 16);
        }

        private void Relayout()
        {
            int n = ShownCount();
            if (n <= 0)
            {
                _cols = 1;
                Size = new Size(Ui.Px(210), _headerH + Ui.Px(58));
                return;
            }
            _cols = n <= 4 ? n : 4;
            int rows = (int)Math.Ceiling(n / (double)_cols);
            Size = new Size(_pad * 2 + _cols * _cellW + (_cols - 1) * _gap,
                            _headerH + rows * _cellH + (rows - 1) * _gap + _pad);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _btnClose = new Rectangle(Width - Ui.Px(24), Ui.Px(4), Ui.Px(20), Ui.Px(20));
            try
            {
                using (GraphicsPath p = Ui.RoundPath(new Rectangle(0, 0, Width, Height), Ui.Px(10)))
                    Region = new Region(p);
            }
            catch { }
        }

        private Rectangle CellRect(int i)
        {
            int col = i % _cols;
            int row = i / _cols;
            return new Rectangle(_pad + col * (_cellW + _gap),
                                 _headerH + row * (_cellH + _gap), _cellW, _cellH);
        }

        private int CellAt(Point p)
        {
            int n = ShownCount();
            for (int i = 0; i < n; i++)
            {
                if (CellRect(i).Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush b = new SolidBrush(Ui.Bg))
                g.FillRectangle(b, ClientRectangle);

            int needs = 0, working = 0, errors = 0, alive = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                SessionInfo s = _items[i];
                if (!s.Alive) continue;
                alive++;
                if (s.State == SessState.NeedsInput) needs++;
                else if (s.State == SessState.Working) working++;
                else if (s.State == SessState.Error) errors++;
            }

            string title = alive + " 个会话";
            if (needs > 0) title += " · " + needs + " 需要你";
            else if (errors > 0) title += " · " + errors + " 出错";
            else if (working > 0) title += " · " + working + " 工作中";

            Color tc = needs > 0 ? Ui.Red : errors > 0 ? Ui.Magenta : working > 0 ? Ui.Amber : Ui.TextDim;
            TextRenderer.DrawText(g, title, Ui.F8, new Rectangle(Ui.Px(10), Ui.Px(4), Width - Ui.Px(36), Ui.Px(18)),
                tc, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);

            TextRenderer.DrawText(g, "×", Ui.F8, _btnClose,
                _hot == -2 ? Ui.Text : Ui.TextFaint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (ShownCount() == 0)
            {
                TextRenderer.DrawText(g, "没有正在运行的会话", Ui.F8,
                    new Rectangle(0, _headerH, Width, Ui.Px(46)), Ui.TextFaint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            float pulse = (float)(0.5 + 0.5 * Math.Sin(Environment.TickCount / 360.0));
            int n = ShownCount();
            for (int i = 0; i < n; i++)
            {
                SessionInfo s = _items[i];
                Rectangle r = CellRect(i);
                bool sel = string.Equals(s.SessionId, _selected, StringComparison.OrdinalIgnoreCase);

                Ui.FillRound(g, r, Ui.Px(8), _hot == i ? Color.FromArgb(255, 38, 42, 51) : Ui.Bg2);
                Rectangle accent = new Rectangle(r.X + Ui.Px(2), r.Y + Ui.Px(8),
                    Ui.Px(3), r.Height - Ui.Px(16));
                Ui.FillRound(g, accent, Ui.Px(1), Ui.ProviderColor(s.Provider));
                if (sel)
                {
                    using (GraphicsPath p = Ui.RoundPath(r, Ui.Px(8)))
                    using (Pen pen = new Pen(Color.FromArgb(200, Ui.StateColor(s.State)), 1.4f))
                        g.DrawPath(pen, p);
                }

                Color c = Ui.StateColor(s.State);
                float cx = r.X + r.Width / 2f;
                float cy = r.Y + Ui.Px(15);
                bool urgent = s.State == SessState.NeedsInput || s.State == SessState.Error ||
                              (s.Unread && s.State == SessState.Done);
                Ui.DrawLed(g, cx, cy, Ui.Px(6), c, urgent ? 1f : 0.3f, urgent ? pulse : 0.2f);

                string name = NameProvider != null ? NameProvider(s) : s.DisplayTitle();
                TextRenderer.DrawText(g, Ui.Shorten(name, PrivacyMode ? 8 : 20), Ui.F75,
                    new Rectangle(r.X + Ui.Px(3), r.Y + Ui.Px(25), r.Width - Ui.Px(6), Ui.Px(15)),
                    Ui.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

                TextRenderer.DrawText(g, s.StateText(), Ui.F75,
                    new Rectangle(r.X + Ui.Px(3), r.Y + Ui.Px(40), r.Width - Ui.Px(6), Ui.Px(13)),
                    c, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }

            using (GraphicsPath path = Ui.RoundPath(new Rectangle(0, 0, Width - 1, Height - 1), Ui.Px(10)))
            using (Pen p = new Pen(Color.FromArgb(255, 46, 51, 62), 1f))
                g.DrawPath(p, path);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hot = CellAt(e.Location);
            if (_btnClose.Contains(e.Location)) hot = -2;
            if (hot != _hot)
            {
                _hot = hot;
                Cursor = hot >= 0 ? Cursors.Hand : (hot == -2 ? Cursors.Hand : Cursors.Default);
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hot != -1) { _hot = -1; Cursor = Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left)
            {
                if (_btnClose.Contains(e.Location))
                {
                    Hide();
                    if (ClosedByUser != null) ClosedByUser();
                    return;
                }

                int idx = CellAt(e.Location);
                if (idx >= 0)
                {
                    _selected = _items[idx].SessionId;
                    Invalidate();
                    if (CellClicked != null) CellClicked(_items[idx].SessionId);
                    return;
                }

                if (e.Y < _headerH)
                {
                    try
                    {
                        Win32.ReleaseCapture();
                        Win32.SendMessage(Handle, Win32.WM_NCLBUTTONDOWN, new IntPtr(Win32.HTCAPTION), IntPtr.Zero);
                    }
                    catch { }
                }
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left) return;

            int idx = CellAt(e.Location);
            if (idx < 0) return;

            _selected = _items[idx].SessionId;
            Invalidate();
            if (CellActivated != null) CellActivated(_items[idx]);
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (_positioned)
            {
                _settings.OverviewPos = new int[] { Left, Top };
            }
        }
    }
}
