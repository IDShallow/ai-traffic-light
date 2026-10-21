using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClaudeTrafficLight
{
    internal sealed class MainForm : Form
    {
        private readonly AppSettings _settings;
        private readonly Monitor _monitor;
        private readonly SessionList _list;
        private readonly NotifyIcon _tray;
        private readonly Timer _poll;
        private readonly Timer _pulse;
        private Timer _saveTimer;
        private OverviewForm _overview;

        private int _lastVersion = -1;
        private bool _reallyExit;
        private bool _startHidden;
        private string _lastBalloonSession = "";
        private SessState _lastOverall = SessState.Offline;
        private bool _trayHintShown;
        private int _hotHeader;
        private float _scale = 1f;
        private UiFonts _fonts = Ui.Fonts(1f);

        private Rectangle _btnOverview, _btnSettings, _btnMin, _btnClose;
        private ToolStripMenuItem _miPrivacy;
        private readonly Dictionary<string, int> _aliases =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _aliasCounters =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private int HeaderH { get { return Px(66); } }

        private int Px(int v) { return (int)Math.Round(v * _scale); }

        private void SetScale(float scale)
        {
            if (scale < 0.5f) scale = 0.5f;
            _scale = scale;
            _fonts = Ui.Fonts(scale);
            MinimumSize = new Size(Px(340), Px(300));
            if (_list != null) _list.SetScale(scale, _fonts);
        }

        public MainForm(AppSettings settings, Monitor monitor, bool startHidden)
        {
            _scale = Ui.Scale;
            _fonts = Ui.Fonts(_scale);

            _settings = settings;
            _monitor = monitor;
            _startHidden = startHidden;

            Text = AppInfo.DisplayName;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Ui.Bg;
            ForeColor = Ui.Text;
            DoubleBuffered = true;
            ShowInTaskbar = true;
            TopMost = settings.TopMost;
            MinimumSize = new Size(Px(340), Px(300));
            StartPosition = FormStartPosition.Manual;

            int w = Px(430), h = Px(640);
            if (settings.MainSize != null && settings.MainSize.Length == 2 &&
                settings.MainSize[0] > Px(300) && settings.MainSize[1] > Px(240))
            {
                w = settings.MainSize[0];
                h = settings.MainSize[1];
            }
            Size = new Size(w, h);

            if (settings.MainPos != null && settings.MainPos.Length == 2)
                Location = new Point(settings.MainPos[0], settings.MainPos[1]);
            else
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Right - w - Px(24), wa.Top + Px(24));
            }
            EnsureOnScreen();

            Icon = AppIcon.ForState(SessState.Done);

            _list = new SessionList();
            _list.NameProvider = NameFor;
            _list.PrivacyMode = settings.PrivacyMode;
            _list.SelectionChanged += OnSelectionChanged;
            _list.JumpRequested += OnJumpRequested;
            _list.ActionRequested += OnActionRequested;
            _list.QuickReplyRequested += OnQuickReplyRequested;
            Controls.Add(_list);

            _tray = new NotifyIcon();
            _tray.Icon = AppIcon.ForState(SessState.Done);
            _tray.Text = AppInfo.DisplayName;
            _tray.Visible = true;
            _tray.DoubleClick += delegate { ShowMain(); };
            _tray.BalloonTipClicked += delegate { OnBalloonClicked(); };
            BuildTrayMenu();

            _poll = new Timer();
            _poll.Interval = 300;
            _poll.Tick += OnPollTick;
            _poll.Start();

            _pulse = new Timer();
            _pulse.Interval = 160;
            _pulse.Tick += OnPulseTick;
            _pulse.Start();

            // 拖动/改大小之后延迟落盘，避免每移动一个像素就写一次文件
            _saveTimer = new Timer();
            _saveTimer.Interval = 1500;
            _saveTimer.Tick += delegate
            {
                _saveTimer.Stop();
                SaveLayout();
            };

            LayoutParts();
            ApplySettings();
        }

        private void SaveLayout()
        {
            try
            {
                _settings.MainPos = new int[] { Left, Top };
                _settings.MainSize = new int[] { Width, Height };
                _settings.MainDpi = Math.Max(1, (int)Math.Round(_scale * 96f));
                _settings.Save();
            }
            catch { }
        }

        /// <summary>隐私模式下的显示名：只给“来源 + 序号”，不暴露标题/项目名。</summary>
        private string NameFor(SessionInfo s)
        {
            if (s == null) return "";
            if (!_settings.PrivacyMode) return s.DisplayTitle();

            string key = s.Provider + ":" + s.SessionId;
            int n;
            if (!_aliases.TryGetValue(key, out n))
            {
                int c;
                _aliasCounters.TryGetValue(s.Provider, out c);
                c++;
                _aliasCounters[s.Provider] = c;
                n = c;
                _aliases[key] = n;
            }
            return Ui.ProviderLabel(s.Provider) + " " + n;
        }

        public void ExitFromOutside()
        {
            _reallyExit = true;
            Close();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (_saveTimer != null && Visible && WindowState == FormWindowState.Normal)
            {
                _saveTimer.Stop();
                _saveTimer.Start();
            }
        }

        // ---------- 窗口尺寸 / 位置 ----------

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutParts();
            try
            {
                using (GraphicsPath p = Ui.RoundPath(new Rectangle(0, 0, Width, Height), Px(12)))
                    Region = new Region(p);
            }
            catch { }
            Invalidate();

            if (_saveTimer != null && Visible && WindowState == FormWindowState.Normal)
            {
                _saveTimer.Stop();
                _saveTimer.Start();
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // 窗口建出来后才知道自己在哪块屏上：按该屏 DPI 重算缩放与（保存的）尺寸
            SetScale(DeviceDpi / 96f);
            if (_settings.MainSize != null && _settings.MainSize.Length == 2 &&
                _settings.MainDpi > 0 && _settings.MainDpi != DeviceDpi)
            {
                Size = new Size(
                    Math.Max(Px(340), (int)Math.Round(_settings.MainSize[0] * (double)DeviceDpi / _settings.MainDpi)),
                    Math.Max(Px(300), (int)Math.Round(_settings.MainSize[1] * (double)DeviceDpi / _settings.MainDpi)));
            }
            EnsureOnScreen();
            LayoutParts();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Win32.WM_DPICHANGED)
            {
                int dpi = Win32.DpiOfWParam(m.WParam);
                Win32.RECT r = Win32.RectOfLParam(m.LParam);

                SetScale(dpi / 96f);
                Bounds = new Rectangle(r.Left, r.Top,
                    Math.Max(Px(340), r.Right - r.Left),
                    Math.Max(Px(300), r.Bottom - r.Top));
                EnsureOnScreen();
                Invalidate(true);
                Log.Write("main dpi -> " + dpi + " size=" + Width + "x" + Height);

                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        private void LayoutParts()
        {
            if (_list != null)
                _list.Bounds = new Rectangle(0, HeaderH, ClientSize.Width, Math.Max(1, ClientSize.Height - HeaderH));

            int bw = Px(46), bh = Px(24);
            _btnClose = new Rectangle(Width - Px(30), Px(9), Px(24), Px(24));
            _btnMin = new Rectangle(Width - Px(56), Px(9), Px(24), Px(24));
            _btnSettings = new Rectangle(Width - Px(110), Px(9), bw, bh);
            _btnOverview = new Rectangle(Width - Px(160), Px(9), bw, bh);
        }

        private void EnsureOnScreen()
        {
            // 多显示器：主窗口在副屏（含负坐标）就留在副屏；
            // 只有整块跑出所有屏幕（显示器被拔掉/分辨率变了）才挪回最近的一块
            Location = ScreenUtil.KeepVisible(new Rectangle(Location, Size));
        }

        protected override void SetVisibleCore(bool value)
        {
            if (_startHidden && value)
            {
                _startHidden = false;
                if (!IsHandleCreated) CreateHandle();
                base.SetVisibleCore(false);
                return;
            }
            base.SetVisibleCore(value);
        }

        public void ShowMain()
        {
            _startHidden = false;
            ShowInTaskbar = true;
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            TopMost = _settings.TopMost;
            Activate();
            BringToFront();
        }

        // ---------- 绘制 ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush b = new SolidBrush(Ui.Bg))
                g.FillRectangle(b, ClientRectangle);

            int needs, working, errors, total;
            SessState overall = Aggregate(out needs, out working, out errors, out total);

            Rectangle housing = new Rectangle(Px(14), Px(6), Px(38), Px(54));
            DrawTrafficLight(g, housing, overall, Pulse());

            TextRenderer.DrawText(g, "AI 状态", _fonts.F12B,
                new Rectangle(Px(64), Px(11), Width - Px(64) - Px(168), Px(24)),
                Ui.Text, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            string sub;
            if (total == 0)
            {
                sub = "没有活跃会话";
            }
            else if (_settings.PrivacyMode)
            {
                // 隐私模式：只给最要紧的一个数，别把标题栏挤爆
                if (needs > 0) sub = "隐私 · " + needs + " 需你";
                else if (errors > 0) sub = "隐私 · " + errors + " 出错";
                else if (working > 0) sub = "隐私 · " + working + " 工作";
                else sub = "隐私 · " + total + " 会话";
            }
            else
            {
                sub = total + " 会话" +
                      (needs > 0 ? " · " + needs + " 需你" : "") +
                      (errors > 0 ? " · " + errors + " 出错" : "") +
                      (working > 0 ? " · " + working + " 工作" : "");
            }

            Color subColor = Ui.TextDim;
            if (needs > 0) subColor = Ui.Red;
            else if (errors > 0) subColor = Ui.Magenta;
            else if (working > 0) subColor = Ui.Amber;

            TextRenderer.DrawText(g, sub, _fonts.F85,
                new Rectangle(Px(64), Px(36), Width - Px(64) - Px(168), Px(20)),
                subColor, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            DrawHeaderButton(g, _btnOverview, "总览", _hotHeader == 1);
            DrawHeaderButton(g, _btnSettings, "设置", _hotHeader == 2);
            DrawHeaderButton(g, _btnMin, "—", _hotHeader == 3);
            DrawHeaderButton(g, _btnClose, "×", _hotHeader == 4);

            using (Pen p = new Pen(Color.FromArgb(255, 40, 45, 55), 1f))
                g.DrawLine(p, 0, HeaderH - 1, Width, HeaderH - 1);

            using (GraphicsPath path = Ui.RoundPath(new Rectangle(0, 0, Width - 1, Height - 1), Px(12)))
            using (Pen p = new Pen(Color.FromArgb(255, 44, 49, 60), 1f))
                g.DrawPath(p, path);
        }

        private void DrawTrafficLight(Graphics g, Rectangle housing, SessState state, float pulse)
        {
            Ui.FillRound(g, housing, Px(9), Color.FromArgb(255, 11, 13, 17));
            Ui.DrawRound(g, housing, Px(9), Color.FromArgb(255, 48, 53, 64), 1f);

            float cx = housing.X + housing.Width / 2f;
            float r = Px(8);
            float[] cy = new float[]
            {
                housing.Y + Px(13),
                housing.Y + housing.Height / 2f,
                housing.Y + housing.Height - Px(13)
            };
            Color[] cols = new Color[] { Ui.Red, Ui.Amber, Ui.Green };

            int lit = -1;
            if (state == SessState.NeedsInput || state == SessState.Error) lit = 0;
            else if (state == SessState.Working) lit = 1;
            else if (state == SessState.Done) lit = 2;

            for (int i = 0; i < 3; i++)
            {
                if (i == lit) Ui.DrawLed(g, cx, cy[i], r, cols[i], 1f, pulse);
                else Ui.DrawLedOff(g, cx, cy[i], r, cols[i]);
            }

            if (state == SessState.Error && lit == 0)
            {
                TextRenderer.DrawText(g, "!", _fonts.F8B, new Rectangle((int)(cx - r), (int)(cy[0] - r), (int)(r * 2), (int)(r * 2)),
                    Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        private void DrawHeaderButton(Graphics g, Rectangle r, string text, bool hot)
        {
            if (hot) Ui.FillRound(g, r, Px(5), Color.FromArgb(28, 255, 255, 255));
            TextRenderer.DrawText(g, text, text == "—" || text == "×" ? _fonts.F10B : _fonts.F8, r,
                hot ? Ui.Text : Ui.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private void OnPulseTick(object sender, EventArgs e)
        {
            // 只有"需要你 / 出错 / 未读完成"才需要动画；
            // 工作态和空闲态都是静态的，不重绘 —— 常驻程序别白白烧 CPU。
            bool animate = false;
            List<SessionInfo> snap = _monitor.Snapshot;
            for (int i = 0; i < snap.Count; i++)
            {
                SessionInfo s = snap[i];
                if (s.State == SessState.NeedsInput || s.State == SessState.Error ||
                    (s.Unread && s.State == SessState.Done))
                {
                    animate = true;
                    break;
                }
            }
            if (!animate) return;

            _list.Invalidate();
            Invalidate(new Rectangle(0, 0, Width, HeaderH));
            if (_overview != null && _overview.Visible) _overview.Invalidate();
        }

        private static float Pulse()
        {
            return (float)(0.5 + 0.5 * Math.Sin(Environment.TickCount / 360.0));
        }

        private SessState Aggregate(out int needs, out int working, out int errors, out int total)
        {
            needs = 0;
            working = 0;
            errors = 0;
            total = 0;

            List<SessionInfo> snap = _monitor.Snapshot;
            for (int i = 0; i < snap.Count; i++)
            {
                SessionInfo s = snap[i];
                if (!s.Alive) continue;
                total++;
                if (s.State == SessState.NeedsInput) needs++;
                else if (s.State == SessState.Working) working++;
                else if (s.State == SessState.Error) errors++;
            }

            if (needs > 0) return SessState.NeedsInput;
            if (errors > 0) return SessState.Error;
            if (working > 0) return SessState.Working;
            if (total > 0) return SessState.Done;
            return SessState.Offline;
        }

        // ---------- 鼠标 ----------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hot = 0;
            if (_btnOverview.Contains(e.Location)) hot = 1;
            else if (_btnSettings.Contains(e.Location)) hot = 2;
            else if (_btnMin.Contains(e.Location)) hot = 3;
            else if (_btnClose.Contains(e.Location)) hot = 4;

            if (hot != _hotHeader)
            {
                _hotHeader = hot;
                Cursor = hot != 0 ? Cursors.Hand : Cursors.Default;
                Invalidate(new Rectangle(0, 0, Width, HeaderH));
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hotHeader != 0)
            {
                _hotHeader = 0;
                Cursor = Cursors.Default;
                Invalidate(new Rectangle(0, 0, Width, HeaderH));
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            if (_btnClose.Contains(e.Location)) { HideToTray(); return; }
            if (_btnMin.Contains(e.Location)) { WindowState = FormWindowState.Minimized; return; }
            if (_btnSettings.Contains(e.Location)) { OpenSettings(); return; }
            if (_btnOverview.Contains(e.Location)) { ToggleOverview(); return; }

            if (e.Y < HeaderH)
            {
                try
                {
                    Win32.ReleaseCapture();
                    Win32.SendMessage(Handle, Win32.WM_NCLBUTTONDOWN, new IntPtr(Win32.HTCAPTION), IntPtr.Zero);
                }
                catch { }
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (_settings.TopMost && !TopMost) TopMost = true;
        }

        // ---------- 轮询 ----------

        private void OnPollTick(object sender, EventArgs e)
        {
            if (_monitor.Version != _lastVersion)
            {
                _lastVersion = _monitor.Version;
                List<SessionInfo> snap = _monitor.Snapshot;
                _list.Items = snap;
                if (_overview != null && _overview.Visible) _overview.SetItems(snap);
                UpdateTray(snap);
                Invalidate(new Rectangle(0, 0, Width, HeaderH));
            }

            MonitorEvent ev;
            while (_monitor.TryDequeue(out ev)) HandleEvent(ev);
        }

        private void UpdateTray(List<SessionInfo> snap)
        {
            int needs = 0, working = 0, errors = 0, total = 0;
            for (int i = 0; i < snap.Count; i++)
            {
                SessionInfo s = snap[i];
                if (!s.Alive) continue;
                total++;
                if (s.State == SessState.NeedsInput) needs++;
                else if (s.State == SessState.Working) working++;
                else if (s.State == SessState.Error) errors++;
            }

            SessState overall = needs > 0 ? SessState.NeedsInput :
                                errors > 0 ? SessState.Error :
                                working > 0 ? SessState.Working :
                                total > 0 ? SessState.Done : SessState.Offline;

            if (overall != _lastOverall)
            {
                _lastOverall = overall;
                _tray.Icon = AppIcon.ForState(overall);
            }

            string tip = AppInfo.DisplayName + " — " + total + " 个会话";
            if (needs > 0) tip += "，" + needs + " 个需要你";
            else if (errors > 0) tip += "，" + errors + " 个出错";
            else if (working > 0) tip += "，" + working + " 个工作中";
            if (tip.Length > 62) tip = tip.Substring(0, 62);
            _tray.Text = tip;
            Icon = AppIcon.ForState(overall);
        }

        private void HandleEvent(MonitorEvent ev)
        {
            if (ev == null || ev.Session == null) return;
            if (!_settings.Notify) return;
            if (ev.Kind == "needs" && !_settings.NotifyNeeds) return;
            if (ev.Kind == "done" && !_settings.NotifyDone) return;
            if (ev.Kind == "error" && !_settings.NotifyError) return;

            string title;
            string text;
            string provider = ev.Session != null ? Ui.ProviderLabel(ev.Session.Provider) : "";
            if (_settings.PrivacyMode)
            {
                title = provider + (ev.Kind == "needs" ? " 有会话需要你回答"
                      : ev.Kind == "error" ? " 有会话出错"
                      : " 有会话已完成");
                text = "打开红绿灯查看状态（隐私模式）";
            }
            else
            {
                string kindText = ev.Kind == "needs" ? "需要你回答"
                                : ev.Kind == "error" ? "出错"
                                : "已完成";
                title = provider + " · " + kindText + " · " + ev.Session.DisplayTitle();

                text = string.IsNullOrEmpty(ev.Text) ? " " : ev.Text;
                if (text.Length > 200) text = text.Substring(0, 200);
            }

            _lastBalloonSession = ev.Session.SessionId;
            try { _tray.ShowBalloonTip(6000, title, text, ToolTipIcon.None); }
            catch { }
            Log.Write("balloon " + title + " :: " + text);

            if (_settings.Sound)
            {
                try
                {
                    if (ev.Kind == "done") SystemSounds.Asterisk.Play();
                    else SystemSounds.Exclamation.Play();
                }
                catch { }
            }
        }

        private void OnBalloonClicked()
        {
            ShowMain();
            if (!string.IsNullOrEmpty(_lastBalloonSession))
            {
                _list.Select(_lastBalloonSession, false);
                _monitor.MarkRead(_lastBalloonSession);
                _list.Invalidate();
            }
        }

        // ---------- 列表事件 ----------

        private void OnSelectionChanged(SessionInfo s)
        {
            if (s != null)
            {
                _monitor.MarkRead(s.SessionId);
                if (_overview != null && _overview.Visible) _overview.SetSelected(s.SessionId);
            }
        }

        private void OnJumpRequested(SessionInfo s)
        {
            if (s == null) return;

            // Codex 桌面端：官方深链直接打开对应会话（不要先去前台聚焦 VS Code）
            if (string.Equals(s.Provider, "codex", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(s.SessionId) && OpenCodexSession(s.SessionId))
                return;

            bool isVscode = string.Equals(s.Entrypoint, "claude-vscode", StringComparison.OrdinalIgnoreCase);

            IntPtr hwnd = IntPtr.Zero;
            bool focused = false;
            try
            {
                hwnd = Win32.FindOwnerWindow(s.Pid);
                if (hwnd != IntPtr.Zero)
                {
                    Win32.ForceForeground(hwnd);
                    focused = true;
                }
            }
            catch { }

            Log.Write("jump " + s.SessionId + " ep=" + s.Entrypoint + " name=" + s.Name +
                      " host=" + s.HostSessionId + " hwnd=" + hwnd);

            if (isVscode)
            {
                // 1) 会话标签页已经开着：按标题直接选中（VSCode 的会话就是标题命名的标签页）
                if (hwnd != IntPtr.Zero && !string.IsNullOrEmpty(s.Title) &&
                    TabFocus.SelectByTitle(hwnd, s.Title))
                    return;

                // 2) 没开：用扩展登记的 deep link 打开该会话，再选中一次
                if (!string.IsNullOrEmpty(s.SessionId))
                {
                    try
                    {
                        System.Threading.Thread.Sleep(120);
                        string uri = "vscode://anthropic.claude-code/open?session=" +
                                     Uri.EscapeDataString(s.SessionId);
                        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                        System.Threading.Thread.Sleep(500);
                        if (hwnd != IntPtr.Zero && !string.IsNullOrEmpty(s.Title))
                            TabFocus.SelectByTitle(hwnd, s.Title);
                        return;
                    }
                    catch { }
                }
            }

            // Claude 桌面端：点侧边栏里对应的会话条目，直接切过去
            if (!isVscode && !string.IsNullOrEmpty(s.HostSessionId))
            {
                if (hwnd != IntPtr.Zero && !string.IsNullOrEmpty(s.Name) &&
                    DesktopFocus.SelectSession(hwnd, s.Name))
                    return;

                // 退路：官方深链。部分版本没接本地会话，留着兜底
                if (OpenDesktopSession(s.HostSessionId)) return;
            }

            if (!focused)
            {
                try
                {
                    _tray.ShowBalloonTip(3000, "没找到会话窗口",
                        "这个会话可能已经关闭，或它的宿主窗口不在前台。", ToolTipIcon.None);
                }
                catch { }
            }
        }

        private static bool OpenCodexSession(string sessionId)
        {
            try
            {
                if (!SchemeRegistered("codex")) return false;
                string uri = "codex://threads/" + Uri.EscapeDataString(sessionId);
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                Log.Write("codex jump " + sessionId);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>没注册协议时不要启动，否则 Windows 会弹"选择打开方式"。</summary>
        private static bool SchemeRegistered(string scheme)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k =
                    Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(scheme))
                {
                    return k != null;
                }
            }
            catch { return false; }
        }

        private static bool OpenDesktopSession(string hostSessionId)
        {
            try
            {
                if (!SchemeRegistered("claude")) return false;
                string uri = "claude://code/continue?session=" + Uri.EscapeDataString(hostSessionId);
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void OnQuickReplyRequested(SessionInfo s, string option)
        {
            if (s == null || string.IsNullOrEmpty(option)) return;

            // Claude Code（VSCode）：聚焦会话 + 用扩展 deep link 把选项预填进输入框。
            // 扩展的 initialPrompt 只调用 setInputText，不会自动发送 —— 由用户按 Enter。
            if (string.Equals(s.Provider, "claude", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(s.Entrypoint, "claude-vscode", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(s.SessionId))
            {
                try
                {
                    IntPtr hwnd = Win32.FindOwnerWindow(s.Pid);
                    if (hwnd != IntPtr.Zero)
                    {
                        Win32.ForceForeground(hwnd);
                        if (!string.IsNullOrEmpty(s.Title)) TabFocus.SelectByTitle(hwnd, s.Title);
                    }

                    string uri = "vscode://anthropic.claude-code/open?session=" +
                                 Uri.EscapeDataString(s.SessionId) +
                                 "&prompt=" + Uri.EscapeDataString(option);
                    Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                    Log.Write("quick reply prefill " + s.SessionId + " :: " + option);
                    ShowBalloon("已填入输入框", "到会话里按 Enter 发送：" + Ui.Shorten(option, 100));
                    return;
                }
                catch { }
            }

            // 其它来源：复制选项并聚焦会话
            try
            {
                Clipboard.SetText(option);
                Log.Write("quick reply copied " + s.SessionId + " :: " + option);
                ShowBalloon("已复制选项", "已复制到剪贴板，粘贴后发送：" + Ui.Shorten(option, 100));
            }
            catch { }
            OnJumpRequested(s);
        }

        private void ShowBalloon(string title, string text)
        {
            try { _tray.ShowBalloonTip(2500, title, text, ToolTipIcon.None); }
            catch { }
        }

        private void OnActionRequested(string action, SessionInfo s)
        {
            if (s == null) return;

            if (action == "copy")
            {
                try
                {
                    Clipboard.SetText(s.SessionId);
                    _tray.ShowBalloonTip(1500, "已复制", s.SessionId, ToolTipIcon.None);
                }
                catch { }
            }
            else if (action == "folder")
            {
                string dir = s.Cwd;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    if (!string.IsNullOrEmpty(s.TranscriptPath))
                        dir = Path.GetDirectoryName(s.TranscriptPath);
                }
                try
                {
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        Process.Start("explorer.exe", "\"" + dir + "\"");
                }
                catch { }
            }
            else if (action == "jump")
            {
                OnJumpRequested(s);
            }
        }

        // ---------- 托盘菜单 ----------

        private ToolStripMenuItem _miShow, _miOverview, _miNotify, _miAutoStart;

        private void BuildTrayMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.BackColor = Ui.Bg2;
            menu.ForeColor = Ui.Text;

            _miShow = new ToolStripMenuItem("显示主窗口");
            _miShow.Click += delegate { ShowMain(); };

            _miOverview = new ToolStripMenuItem("总览窗口");
            _miOverview.Click += delegate { ToggleOverview(); };

            _miPrivacy = new ToolStripMenuItem("隐私模式（隐藏对话名）");
            _miPrivacy.Click += delegate
            {
                _settings.PrivacyMode = !_settings.PrivacyMode;
                _settings.Save();
                ApplySettings();
            };

            _miNotify = new ToolStripMenuItem("提示通知");
            _miNotify.Click += delegate
            {
                _settings.Notify = !_settings.Notify;
                _settings.Save();
                SyncMenu();
            };

            _miAutoStart = new ToolStripMenuItem("开机自启");
            _miAutoStart.Click += delegate
            {
                _settings.AutoStart = !_settings.AutoStart;
                ApplyAutoStart(_settings.AutoStart);
                _settings.Save();
                SyncMenu();
            };

            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { ExitApp(); };

            menu.Items.Add(_miShow);
            menu.Items.Add(_miOverview);
            menu.Items.Add(_miPrivacy);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_miNotify);
            menu.Items.Add(_miAutoStart);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);

            _tray.ContextMenuStrip = menu;
            SyncMenu();
        }

        private void SyncMenu()
        {
            if (_miOverview != null) _miOverview.Checked = _settings.OverviewVisible;
            if (_miNotify != null) _miNotify.Checked = _settings.Notify;
            if (_miAutoStart != null) _miAutoStart.Checked = _settings.AutoStart;
            if (_miPrivacy != null) _miPrivacy.Checked = _settings.PrivacyMode;
        }

        // ---------- 设置 ----------

        private void OpenSettings()
        {
            using (SettingsForm f = new SettingsForm(_settings))
            {
                f.ShowDialog(this);
            }
            ApplySettings();
            _settings.Save();
        }

        public void ApplySettings()
        {
            TopMost = _settings.TopMost;
            if (_overview != null) _overview.TopMost = _settings.TopMost;
            ApplyAutoStart(_settings.AutoStart);

            if (_list != null)
            {
                _list.PrivacyMode = _settings.PrivacyMode;
                _list.NameProvider = NameFor;
                _list.Invalidate();
            }
            if (_overview != null)
            {
                _overview.PrivacyMode = _settings.PrivacyMode;
                _overview.NameProvider = NameFor;
                if (_overview.Visible) _overview.Invalidate();
            }
            Invalidate(new Rectangle(0, 0, Width, HeaderH));
            SyncMenu();

            if (_settings.OverviewVisible && (_overview == null || !_overview.Visible))
                SetOverviewVisible(true);
            else if (!_settings.OverviewVisible && _overview != null && _overview.Visible)
                SetOverviewVisible(false);
        }

        private void ToggleOverview()
        {
            SetOverviewVisible(!(_overview != null && _overview.Visible));
        }

        private void SetOverviewVisible(bool visible)
        {
            if (visible)
            {
                if (_overview == null || _overview.IsDisposed)
                {
                    _overview = new OverviewForm(_settings, new Rectangle(Location, Size));
                    _overview.NameProvider = NameFor;
                    _overview.PrivacyMode = _settings.PrivacyMode;
                    _overview.CellClicked += delegate(string id)
                    {
                        // 单击选中并打开主窗口；双击（CellActivated）直接跳到会话
                        ShowMain();
                        _list.Select(id, false);
                        _monitor.MarkRead(id);
                        _list.Invalidate();
                    };
                    _overview.CellActivated += delegate(SessionInfo s)
                    {
                        _list.Select(s.SessionId, false);
                        _monitor.MarkRead(s.SessionId);
                        _list.Invalidate();
                        OnJumpRequested(s);
                    };
                    _overview.OpenMainRequested += delegate { ShowMain(); };
                    _overview.ClosedByUser += delegate
                    {
                        _settings.OverviewVisible = false;
                        _settings.Save();
                        SyncMenu();
                    };
                }
                _overview.TopMost = _settings.TopMost;
                _overview.SetItems(_monitor.Snapshot);
                _overview.SetSelected(_list.SelectedId);
                _overview.Show();
                _overview.BringToFront();
            }
            else if (_overview != null)
            {
                _overview.Hide();
            }

            _settings.OverviewVisible = visible;
            _settings.Save();
            SyncMenu();
        }

        private static void ApplyAutoStart(bool on)
        {
            // 只删"指向自己"的自启项：如果指向别的安装副本，别替人家删
            if (on) AutoStart.Set(true);
            else if (AutoStart.IsEnabled()) AutoStart.Set(false);
        }

        // ---------- 退出 ----------

        private void HideToTray()
        {
            _settings.MainPos = new int[] { Left, Top };
            _settings.MainSize = new int[] { Width, Height };
            _settings.Save();
            Hide();

            if (!_trayHintShown)
            {
                _trayHintShown = true;
                try
                {
                    _tray.ShowBalloonTip(4000, "还在后台运行",
                        "红绿灯会继续监视；双击托盘图标可以再打开窗口。", ToolTipIcon.None);
                }
                catch { }
            }
        }

        private void ExitApp()
        {
            _reallyExit = true;
            _settings.MainPos = new int[] { Left, Top };
            _settings.MainSize = new int[] { Width, Height };
            _settings.Save();
            try { _tray.Visible = false; } catch { }
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            try
            {
                _settings.MainPos = new int[] { Left, Top };
                _settings.MainSize = new int[] { Width, Height };
                _settings.MainDpi = Math.Max(1, (int)Math.Round(_scale * 96f));
                _settings.Save();
            }
            catch { }

            try { _tray.Visible = false; _tray.Dispose(); } catch { }
            base.OnFormClosing(e);
        }
    }
}
