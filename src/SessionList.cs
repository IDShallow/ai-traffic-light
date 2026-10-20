using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ClaudeTrafficLight
{
    /// <summary>会话列表 + 展开详情。整块自绘，避免透明/闪烁问题。</summary>
    internal sealed class SessionList : Control
    {
        private sealed class Hit
        {
            public string Id = "";
            public Rectangle Rect;
        }

        public event Action<SessionInfo> SelectionChanged;
        public event Action<SessionInfo> JumpRequested;
        public event Action<string, SessionInfo> ActionRequested;

        /// <summary>显示名由外部提供（隐私模式下换成“会话 N”）。</summary>
        public Func<SessionInfo, string> NameProvider;
        public bool PrivacyMode;

        private List<SessionInfo> _items = new List<SessionInfo>();
        private readonly List<Hit> _hits = new List<Hit>();
        private string _selectedId = "";
        private int _scroll;
        private int _hot = -1;
        private int _hotAction = 0;
        private Rectangle _btnCopy, _btnFolder, _btnJump;
        private bool _draggingThumb;
        private int _dragStartY;
        private int _dragStartScroll;
        private bool _resizing;
        private Point _resizeStart;
        private Size _resizeFormStart;

        private int RowH { get { return Ui.Px(58); } }
        private int DetailH { get { return Ui.Px(208); } }
        private int Pad { get { return Ui.Px(8); } }

        public SessionList()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = Ui.Bg;
            ForeColor = Ui.Text;
            TabStop = true;
        }

        public List<SessionInfo> Items
        {
            get { return _items; }
            set
            {
                _items = value ?? new List<SessionInfo>();
                ClampScroll();
                Invalidate();
            }
        }

        public string SelectedId
        {
            get { return _selectedId; }
        }

        public void Select(string id, bool notify)
        {
            _selectedId = id ?? "";
            SessionInfo s = Find(_selectedId);
            if (s != null && notify && SelectionChanged != null) SelectionChanged(s);
            EnsureVisible();
            Invalidate();
        }

        private SessionInfo Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _items.Count; i++)
            {
                if (string.Equals(_items[i].SessionId, id, StringComparison.OrdinalIgnoreCase)) return _items[i];
            }
            return null;
        }

        private int ContentHeight()
        {
            int h = Pad;
            for (int i = 0; i < _items.Count; i++)
            {
                h += RowH;
                if (IsSelected(_items[i])) h += DetailH;
            }
            return h + Pad;
        }

        private bool IsSelected(SessionInfo s)
        {
            return s != null && string.Equals(s.SessionId, _selectedId, StringComparison.OrdinalIgnoreCase);
        }

        private string NameOf(SessionInfo s)
        {
            if (NameProvider != null) return NameProvider(s);
            return s.DisplayTitle();
        }

        private void ClampScroll()
        {
            int max = Math.Max(0, ContentHeight() - Height);
            if (_scroll > max) _scroll = max;
            if (_scroll < 0) _scroll = 0;
        }

        private void EnsureVisible()
        {
            int y = Pad;
            for (int i = 0; i < _items.Count; i++)
            {
                int h = RowH + (IsSelected(_items[i]) ? DetailH : 0);
                if (IsSelected(_items[i]))
                {
                    if (y - _scroll < 0) _scroll = y;
                    else if (y + h - _scroll > Height) _scroll = y + h - Height;
                    break;
                }
                y += h;
            }
            ClampScroll();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ClampScroll();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _scroll -= (e.Delta / 120) * Ui.Px(54);
            ClampScroll();
            Invalidate();
        }

        private bool InGrip(Point p)
        {
            int s = Ui.Px(16);
            return p.X >= Width - s && p.Y >= Height - s;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_resizing)
            {
                Form f = FindForm();
                if (f != null)
                {
                    int w = Math.Max(f.MinimumSize.Width, _resizeFormStart.Width + (e.X - _resizeStart.X));
                    int h = Math.Max(f.MinimumSize.Height, _resizeFormStart.Height + (e.Y - _resizeStart.Y));
                    f.Size = new Size(w, h);
                }
                return;
            }

            if (_draggingThumb)
            {
                int contentH = ContentHeight();
                int trackH = Height - Pad * 2;
                int thumbH = Math.Max(Ui.Px(26), trackH * Height / Math.Max(1, contentH));
                int span = Math.Max(1, trackH - thumbH);
                int maxScroll = Math.Max(1, contentH - Height);
                int dy = e.Y - _dragStartY;
                _scroll = _dragStartScroll + dy * maxScroll / span;
                ClampScroll();
                Invalidate();
                return;
            }

            int hot = -1;
            for (int i = 0; i < _hits.Count; i++)
            {
                if (_hits[i].Rect.Contains(e.X, e.Y)) { hot = i; break; }
            }
            int action = ActionAt(e.X, e.Y);
            if (hot != _hot || action != _hotAction)
            {
                _hot = hot;
                _hotAction = action;
                Cursor = InGrip(e.Location) ? Cursors.SizeNWSE : (action != 0 ? Cursors.Hand : Cursors.Default);
                Invalidate();
            }
            else if (InGrip(e.Location) && Cursor != Cursors.SizeNWSE)
            {
                Cursor = Cursors.SizeNWSE;
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hot != -1 || _hotAction != 0)
            {
                _hot = -1;
                _hotAction = 0;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        private int ActionAt(int x, int y)
        {
            if (_btnCopy.Contains(x, y)) return 1;
            if (_btnFolder.Contains(x, y)) return 2;
            if (_btnJump.Contains(x, y)) return 3;
            return 0;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            Form form = FindForm();
            if (e.Button == MouseButtons.Left && InGrip(e.Location) && form != null)
            {
                _resizing = true;
                _resizeStart = e.Location;
                _resizeFormStart = form.Size;
                Capture = true;
                Cursor = Cursors.SizeNWSE;
                return;
            }

            Focus();

            if (e.Button == MouseButtons.Left && IsOnScrollbar(e.X))
            {
                _draggingThumb = true;
                _dragStartY = e.Y;
                _dragStartScroll = _scroll;
                return;
            }

            int action = ActionAt(e.X, e.Y);
            if (action != 0)
            {
                SessionInfo sel = Find(_selectedId);
                if (sel != null && ActionRequested != null)
                {
                    if (action == 1) ActionRequested("copy", sel);
                    else if (action == 2) ActionRequested("folder", sel);
                    else if (action == 3) ActionRequested("jump", sel);
                }
                return;
            }

            for (int i = 0; i < _hits.Count; i++)
            {
                if (_hits[i].Rect.Contains(e.X, e.Y))
                {
                    string id = _hits[i].Id;
                    if (string.Equals(id, _selectedId, StringComparison.OrdinalIgnoreCase)) _selectedId = "";
                    else _selectedId = id;

                    SessionInfo s = Find(_selectedId);
                    if (s != null && SelectionChanged != null) SelectionChanged(s);
                    ClampScroll();
                    Invalidate();
                    return;
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _draggingThumb = false;
            if (_resizing)
            {
                _resizing = false;
                Capture = false;
                Cursor = Cursors.Default;
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            for (int i = 0; i < _hits.Count; i++)
            {
                if (_hits[i].Rect.Contains(e.X, e.Y))
                {
                    // 双击时单击已经把这一行折叠了，这里恢复选中再跳转
                    string id = _hits[i].Id;
                    if (string.Equals(id, _selectedId, StringComparison.OrdinalIgnoreCase) == false)
                    {
                        _selectedId = id;
                        SessionInfo again = Find(id);
                        if (again != null && SelectionChanged != null) SelectionChanged(again);
                        Invalidate();
                    }
                    SessionInfo s = Find(id);
                    if (s != null && JumpRequested != null) JumpRequested(s);
                    return;
                }
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Enter) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_items.Count == 0) return;

            int idx = -1;
            for (int i = 0; i < _items.Count; i++)
            {
                if (IsSelected(_items[i])) { idx = i; break; }
            }

            if (e.KeyCode == Keys.Down)
            {
                int n = Math.Min(_items.Count - 1, idx + 1);
                Select(_items[n].SessionId, true);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up)
            {
                int n = Math.Max(0, idx - 1);
                Select(_items[n].SessionId, true);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter && idx >= 0)
            {
                if (JumpRequested != null) JumpRequested(_items[idx]);
                e.Handled = true;
            }
        }

        private bool IsOnScrollbar(int x)
        {
            return ContentHeight() > Height && x >= Width - Ui.Px(8);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush bg = new SolidBrush(Ui.Bg))
                g.FillRectangle(bg, ClientRectangle);

            _hits.Clear();
            _btnCopy = Rectangle.Empty;
            _btnFolder = Rectangle.Empty;
            _btnJump = Rectangle.Empty;

            if (_items.Count == 0)
            {
                DrawEmpty(g);
                DrawScrollbar(g);
                DrawGrip(g);
                return;
            }

            float pulse = (float)(0.5 + 0.5 * Math.Sin(Environment.TickCount / 360.0));
            int y = Pad - _scroll;

            for (int i = 0; i < _items.Count; i++)
            {
                SessionInfo s = _items[i];
                bool sel = IsSelected(s);
                int h = RowH + (sel ? DetailH : 0);
                Rectangle rr = new Rectangle(Pad, y, Width - Pad * 2, h);

                // 命中表始终包含所有行（_hot 用行下标），只是不画屏幕外的
                Hit hit = new Hit();
                hit.Id = s.SessionId;
                hit.Rect = new Rectangle(rr.X, rr.Y, rr.Width, RowH);
                _hits.Add(hit);

                if (rr.Bottom + _scroll < -20 || rr.Top + _scroll > Height + 20)
                {
                    y += h;
                    continue;
                }

                bool hot = (_hot == i && !sel);
                if (sel) Ui.FillRound(g, rr, Ui.Px(10), Ui.Bg2);
                else if (hot) Ui.FillRound(g, rr, Ui.Px(10), Color.FromArgb(25, 255, 255, 255));

                DrawRow(g, rr, s, sel, pulse);
                if (sel) DrawDetail(g, rr, s, pulse);
                else if (i < _items.Count - 1)
                {
                    using (Pen p = new Pen(Color.FromArgb(28, 255, 255, 255), 1f))
                        g.DrawLine(p, rr.X + Ui.Px(14), rr.Bottom - 1, rr.Right - Ui.Px(14), rr.Bottom - 1);
                }

                y += h;
            }

            DrawScrollbar(g);
            DrawGrip(g);
        }

        private void DrawGrip(Graphics g)
        {
            int x = Width - Ui.Px(5);
            int y = Height - Ui.Px(5);
            using (Pen p = new Pen(Color.FromArgb(70, 255, 255, 255), 1f))
            {
                for (int i = 0; i < 3; i++)
                    g.DrawLine(p, x - i * Ui.Px(4), y, x, y - i * Ui.Px(4));
            }
        }

        private void DrawEmpty(Graphics g)
        {
            int cy = Height / 2 - Ui.Px(20);
            string t = "没有正在运行的 Claude 会话";
            string sub = "打开一个 Claude Code 对话就会出现在这里";
            Size ts = TextRenderer.MeasureText(t, Ui.F10B);
            Size ss = TextRenderer.MeasureText(sub, Ui.F8);
            TextRenderer.DrawText(g, t, Ui.F10B,
                new Rectangle(0, cy, Width, ts.Height), Ui.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, sub, Ui.F8,
                new Rectangle(0, cy + ts.Height + Ui.Px(8), Width, ss.Height), Ui.TextFaint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }

        private void DrawRow(Graphics g, Rectangle rr, SessionInfo s, bool sel, float pulse)
        {
            Color c = Ui.StateColor(s.State);
            float cx = rr.X + Ui.Px(19);
            float cy = rr.Y + RowH / 2f - Ui.Px(2);
            float radius = Ui.Px(6);

            bool urgent = s.State == SessState.NeedsInput || s.State == SessState.Error ||
                          (s.State == SessState.Working) || (s.Unread && s.State == SessState.Done);
            float glow = urgent ? 1f : 0.35f;

            if (s.State == SessState.NeedsInput || s.State == SessState.Error ||
                (s.Unread && s.State == SessState.Done))
                Ui.DrawLed(g, cx, cy, radius, c, glow, pulse);
            else
                Ui.DrawLed(g, cx, cy, radius, c, 0.28f, 0.2f);

            if (s.State == SessState.NeedsInput || s.State == SessState.Error)
            {
                float bw = Math.Max(1.6f, radius * 0.24f);
                float bh = radius * 0.95f;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                {
                    g.FillRectangle(b, cx - bw / 2f, cy - radius * 0.55f, bw, bh);
                    g.FillEllipse(b, cx - bw / 2f, cy + radius * 0.45f, bw, bw);
                }
            }

            int tx = rr.X + Ui.Px(36);
            int timeW = Ui.Px(66);
            Rectangle tr = new Rectangle(tx, rr.Y + Ui.Px(8), rr.Width - Ui.Px(36) - timeW - Ui.Px(10), Ui.Px(22));
            TextRenderer.DrawText(g, NameOf(s), Ui.F95B, tr, Ui.Text, Ui.TfSingle);

            string sub = s.StateText();
            if (!PrivacyMode && !string.IsNullOrEmpty(s.ProjectName) && s.ProjectName != s.DisplayTitle())
                sub += " · " + s.ProjectName;
            if (s.TodoTotal > 0) sub += " · todo " + s.TodoDone + "/" + s.TodoTotal;

            int subX = tx;
            int subMax = rr.Right - timeW - Ui.Px(10) - tx;
            int subY = rr.Y + Ui.Px(29);

            // 来源徽标：CLD / CDX / DSH
            if (subMax > Ui.Px(90))
            {
                string shortName = Ui.ProviderShort(s.Provider);
                Size ps = TextRenderer.MeasureText(shortName, Ui.F75);
                int pw = ps.Width + Ui.Px(8);
                Rectangle pill = new Rectangle(subX, subY + Ui.Px(1), pw, Ui.Px(15));
                Color pc = Ui.ProviderColor(s.Provider);
                Ui.FillRound(g, pill, Ui.Px(4), Color.FromArgb(42, pc));
                TextRenderer.DrawText(g, shortName, Ui.F75, pill, pc,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding);
                subX += pw + Ui.Px(6);
                subMax -= pw + Ui.Px(6);
            }

            Rectangle sr = new Rectangle(subX, subY, subMax, Ui.Px(18));
            Color subColor = (s.State == SessState.NeedsInput) ? Ui.Red :
                             (s.State == SessState.Error) ? Ui.Magenta :
                             (s.State == SessState.Working) ? Color.FromArgb(220, 200, 150) : Ui.TextDim;
            TextRenderer.DrawText(g, sub, Ui.F85, sr, subColor, Ui.TfSingle);

            Rectangle timeR = new Rectangle(rr.Right - timeW - Ui.Px(6), rr.Y + Ui.Px(9),
                timeW, Ui.Px(18));
            TextRenderer.DrawText(g, Ui.HumanizeAgo(s.LastActivity), Ui.F75, timeR, Ui.TextFaint,
                TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine |
                TextFormatFlags.VerticalCenter);

            if (s.Unread)
            {
                using (SolidBrush b = new SolidBrush(c))
                    g.FillEllipse(b, rr.Right - Ui.Px(13), rr.Y + Ui.Px(34), Ui.Px(6), Ui.Px(6));
            }
        }

        private void DrawDetail(Graphics g, Rectangle rr, SessionInfo s, float pulse)
        {
            Rectangle det = new Rectangle(rr.X + Ui.Px(8), rr.Y + RowH - Ui.Px(6),
                rr.Width - Ui.Px(16), DetailH - Ui.Px(10));
            Ui.FillRound(g, det, Ui.Px(10), Color.FromArgb(255, 30, 34, 42));

            Color c = Ui.StateColor(s.State);
            using (GraphicsPath p = Ui.RoundPath(new Rectangle(det.X + Ui.Px(2), det.Y + Ui.Px(12),
                       Ui.Px(3), det.Height - Ui.Px(24)), Ui.Px(2)))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(200, c)))
                g.FillPath(b, p);

            int x = det.X + Ui.Px(14);
            int w = det.Width - Ui.Px(24);

            if (PrivacyMode)
            {
                Ui.FillRound(g, new Rectangle(x, det.Y + Ui.Px(16), w, Ui.Px(84)), Ui.Px(8),
                    Color.FromArgb(255, 34, 38, 46));
                TextRenderer.DrawText(g, "隐私模式", Ui.F95B,
                    new Rectangle(x + Ui.Px(10), det.Y + Ui.Px(26), w - Ui.Px(20), Ui.Px(20)),
                    Ui.TextDim, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                TextRenderer.DrawText(g, "对话标题、提问与回复均不显示，只保留状态。", Ui.F8,
                    new Rectangle(x + Ui.Px(10), det.Y + Ui.Px(48), w - Ui.Px(20), Ui.Px(34)),
                    Ui.TextFaint, Ui.TfWrap);
            }
            else
            {
                TextRenderer.DrawText(g, "最后提问", Ui.F75, new Rectangle(x, det.Y + Ui.Px(9), w, Ui.Px(14)),
                    Ui.TextFaint, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

                string prompt = string.IsNullOrEmpty(s.LastPrompt) ? "（还没有提问）" : s.LastPrompt;
                TextRenderer.DrawText(g, prompt, Ui.F85, new Rectangle(x, det.Y + Ui.Px(24), w, Ui.Px(32)),
                    Ui.TextDim, Ui.TfWrap | TextFormatFlags.EndEllipsis);

                TextRenderer.DrawText(g, "最后回复", Ui.F75, new Rectangle(x, det.Y + Ui.Px(60), w, Ui.Px(14)),
                    Ui.TextFaint, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

                string reply = s.State == SessState.Error && !string.IsNullOrEmpty(s.ErrorText)
                    ? s.ErrorText
                    : (string.IsNullOrEmpty(s.LastAssistant)
                        ? (s.State == SessState.Working ? "（正在处理…）" : "（还没有回复）")
                        : s.LastAssistant);
                Color replyColor = s.State == SessState.Error ? Color.FromArgb(245, 170, 245) : Ui.Text;
                TextRenderer.DrawText(g, reply, Ui.F85, new Rectangle(x, det.Y + Ui.Px(74), w, Ui.Px(44)),
                    replyColor, Ui.TfWrap | TextFormatFlags.EndEllipsis);
            }

            // todo 进度
            if (s.TodoTotal > 0)
            {
                string todo = "待办 " + s.TodoDone + "/" + s.TodoTotal;
                TextRenderer.DrawText(g, todo, Ui.F75, new Rectangle(x, det.Y + Ui.Px(122), w, Ui.Px(14)),
                    Ui.TextFaint, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

                Rectangle track = new Rectangle(x + Ui.Px(60), det.Y + Ui.Px(127), w - Ui.Px(60), Ui.Px(5));
                Ui.FillRound(g, track, Ui.Px(3), Color.FromArgb(45, 255, 255, 255));
                int fillW = (int)(track.Width * (s.TodoDone / (float)s.TodoTotal));
                if (fillW > 2)
                    Ui.FillRound(g, new Rectangle(track.X, track.Y, fillW, track.Height), Ui.Px(3),
                        Color.FromArgb(220, Ui.Green));
            }

            string meta = Ui.ProviderLabel(s.Provider);
            if (!string.IsNullOrEmpty(s.Model)) meta += " · " + s.Model;
            if (s.SessionId.Length >= 8)
                meta += (meta.Length > 0 ? " · " : "") + s.SessionId.Substring(0, 8);
            if (!string.IsNullOrEmpty(s.Entrypoint))
                meta += " · " + s.Entrypoint;
            if (!PrivacyMode && !string.IsNullOrEmpty(s.Cwd))
                meta += " · " + Ui.Shorten(s.Cwd, 40);
            TextRenderer.DrawText(g, meta, Ui.F75, new Rectangle(x, det.Y + Ui.Px(142), w, Ui.Px(14)),
                Ui.TextFaint, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            // 按钮
            int by = det.Y + Ui.Px(162);
            int bh = Ui.Px(24);
            _btnCopy = MakeButton(g, "复制会话 ID", x, by, bh);
            _btnFolder = MakeButton(g, "打开目录", _btnCopy.Right + Ui.Px(6), by, bh);
            _btnJump = MakeButton(g, "跳到会话", _btnFolder.Right + Ui.Px(6), by, bh);

            DrawButton(g, _btnCopy, "复制会话 ID", _hotAction == 1);
            DrawButton(g, _btnFolder, "打开目录", _hotAction == 2);
            DrawButton(g, _btnJump, "跳到会话", _hotAction == 3);
        }

        private Rectangle MakeButton(Graphics g, string text, int x, int y, int h)
        {
            Size sz = TextRenderer.MeasureText(text, Ui.F75);
            return new Rectangle(x, y, sz.Width + Ui.Px(18), h);
        }

        private void DrawButton(Graphics g, Rectangle r, string text, bool hot)
        {
            Ui.FillRound(g, r, Ui.Px(6), hot ? Ui.Bg3 : Color.FromArgb(255, 38, 43, 52));
            Ui.DrawRound(g, r, Ui.Px(6), Ui.Line, 1f);
            TextRenderer.DrawText(g, text, Ui.F75, r, hot ? Ui.Text : Ui.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private void DrawScrollbar(Graphics g)
        {
            int contentH = ContentHeight();
            if (contentH <= Height) return;

            int trackH = Height - Pad * 2;
            int thumbH = Math.Max(Ui.Px(26), trackH * Height / Math.Max(1, contentH));
            int maxScroll = Math.Max(1, contentH - Height);
            int span = Math.Max(1, trackH - thumbH);
            int thumbY = Pad + (int)((long)_scroll * span / maxScroll);

            Rectangle track = new Rectangle(Width - Ui.Px(6), Pad, Ui.Px(3), trackH);
            Ui.FillRound(g, track, Ui.Px(2), Color.FromArgb(20, 255, 255, 255));
            Ui.FillRound(g, new Rectangle(track.X, thumbY, track.Width, thumbH), Ui.Px(2),
                Color.FromArgb(80, 255, 255, 255));
        }
    }
}
