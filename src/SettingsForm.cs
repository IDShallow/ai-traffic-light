using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ClaudeTrafficLight
{
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings _settings;
        private readonly CheckBox[] _checks = new CheckBox[15];
        private Label _hookStatus;
        private Button _hookBtn;
        private Label _updateStatus;
        private Button _updateBtn;
        private Rectangle _btnClose;
        private bool _hotClose;
        private float _scale = 1f;
        private UiFonts _fonts = Ui.Fonts(1f);
        private UpdateInfo _pending;
        private ComboBox _updateEvery;
        private ComboBox _uiScale;
        private Panel _scroll;
        public event Action UiScaleChanged;
        private readonly List<SessionInfo> _sessions;
        private readonly Func<SessionInfo, string> _nameProvider;

        /// <summary>手动检查发现了新版本：通知主窗口点红点。</summary>
        public event Action<UpdateInfo> UpdateFound;

        private int Px(int v) { return (int)Math.Round(v * _scale); }

        private int HeaderH { get { return Px(46); } }

        private void SetScale(float scale)
        {
            if (scale < 0.5f) scale = 0.5f;
            _scale = scale;
            _fonts = Ui.Fonts(scale);
        }

        /// <summary>DPI 变了：位置、字号和标准控件都要按新比例重建。</summary>
        private void Rebuild()
        {
            SuspendLayout();
            Control[] old = new Control[Controls.Count];
            Controls.CopyTo(old, 0);
            Controls.Clear();
            for (int i = 0; i < old.Length; i++)
            {
                try { old[i].Dispose(); } catch { }
            }
            BuildUi();
            ResumeLayout(true);
            Invalidate(true);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            float sc = Win32.DpiForWindow(Handle, DeviceDpi) / 96f * Ui.ManualFactor;
            if (Math.Abs(sc - _scale) > 0.01f)
            {
                SetScale(sc);
                Rebuild();
            }
            FitToScreen();
        }

        /// <summary>窗口比屏幕还高时缩小窗口（内容由滚动面板兜住）。</summary>
        private void FitToScreen()
        {
            try
            {
                Rectangle wa = Screen.FromHandle(Handle).WorkingArea;
                int maxH = wa.Height - Px(16);
                if (Height > maxH) Height = Math.Max(Px(240), maxH);
                if (Top + Height > wa.Bottom) Top = wa.Bottom - Height;
                if (Top < wa.Top) Top = wa.Top;
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Win32.WM_DPICHANGED)
            {
                int dpi = Win32.DpiOfWParam(m.WParam);
                Win32.RECT r = Win32.RectOfLParam(m.LParam);

                SetScale(dpi / 96f * Ui.ManualFactor);
                Rebuild();
                Bounds = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                FitToScreen();
                Log.Write("settings dpi -> " + dpi + " size=" + Width + "x" + Height);

                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        public SettingsForm(AppSettings settings, UpdateInfo pending,
                            List<SessionInfo> sessions, Func<SessionInfo, string> nameProvider)
        {
            _settings = settings;
            _pending = pending;
            _sessions = sessions ?? new List<SessionInfo>();
            _nameProvider = nameProvider;
            _scale = Ui.Scale * Ui.ManualFactor;
            _fonts = Ui.Fonts(_scale);

            Text = "设置";
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Ui.Bg;
            ForeColor = Ui.Text;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            TopMost = _settings.TopMost;

            BuildUi();
        }

        private void BuildUi()
        {
            MinimumSize = Size.Empty;
            MaximumSize = Size.Empty;
            ClientSize = new Size(Px(470), Px(870));

            // 内容放进可滚动面板：手动放大时 1080p 也放得下
            _scroll = new Panel();
            _scroll.Location = new Point(0, HeaderH);
            _scroll.Size = new Size(ClientSize.Width, ClientSize.Height - HeaderH);
            _scroll.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _scroll.AutoScroll = true;
            _scroll.AutoScrollMinSize = new Size(0, Px(870) - HeaderH);
            _scroll.BackColor = Ui.Bg;
            Controls.Add(_scroll);

            int x = Px(24);
            int y = Px(12);
            int step = Px(30);

            _checks[0] = AddCheck("窗口总在最前", x, y, _settings.TopMost, delegate { _settings.TopMost = _checks[0].Checked; Save(); });
            y += step;
            _checks[1] = AddCheck("需要你回答时提示", x, y, _settings.NotifyNeeds, delegate { _settings.NotifyNeeds = _checks[1].Checked; Save(); });
            y += step;
            _checks[2] = AddCheck("完成时提示", x, y, _settings.NotifyDone, delegate { _settings.NotifyDone = _checks[2].Checked; Save(); });
            y += step;
            _checks[3] = AddCheck("出错时提示", x, y, _settings.NotifyError, delegate { _settings.NotifyError = _checks[3].Checked; Save(); });
            y += step;
            _checks[4] = AddCheck("提示音", x, y, _settings.Sound, delegate { _settings.Sound = _checks[4].Checked; Save(); });
            y += step;
            _checks[5] = AddCheck("开机自启", x, y, _settings.AutoStart, delegate { _settings.AutoStart = _checks[5].Checked; Save(); });
            y += step;
            _checks[6] = AddCheck("显示已结束的会话（最近 30 分钟）", x, y, _settings.ShowOffline, delegate { _settings.ShowOffline = _checks[6].Checked; Save(); });
            y += step;
            _checks[7] = AddCheck("按状态排序（需要你的排最前）", x, y, _settings.SortByState, delegate { _settings.SortByState = _checks[7].Checked; Save(); });
            y += step;
            _checks[8] = AddCheck("启动时最小化到托盘", x, y, _settings.StartMinimized, delegate { _settings.StartMinimized = _checks[8].Checked; Save(); });
            y += step;
            _checks[9] = AddCheck("隐私模式（只显示状态，不显示对话名 / 内容）", x, y, _settings.PrivacyMode, delegate { _settings.PrivacyMode = _checks[9].Checked; Save(); });
            y += step;
            _checks[10] = AddCheck("跳转后把主窗口收进托盘", x, y, _settings.HideAfterJump, delegate { _settings.HideAfterJump = _checks[10].Checked; Save(); });
            y += Px(22);
            Label jumpHint = new Label();
            jumpHint.Text = "开启后：跳转成功后主窗口自动收进托盘，总览窗口不受影响。";
            jumpHint.AutoSize = true;
            jumpHint.Font = _fonts.F75;
            jumpHint.ForeColor = Ui.TextFaint;
            jumpHint.BackColor = Ui.Bg;
            jumpHint.Location = new Point(x + Px(22), y);
            _scroll.Controls.Add(jumpHint);
            y += Px(24);
            _checks[11] = AddCheck("自动检查更新", x, y, _settings.AutoCheckUpdate, delegate
            {
                _settings.AutoCheckUpdate = _checks[11].Checked;
                Save();
                if (_updateEvery != null) _updateEvery.Enabled = _checks[11].Checked;
            });

            _updateEvery = new ComboBox();
            _updateEvery.DropDownStyle = ComboBoxStyle.DropDownList;
            _updateEvery.FlatStyle = FlatStyle.Flat;
            _updateEvery.Font = _fonts.F75;
            _updateEvery.BackColor = Ui.Bg2;
            _updateEvery.ForeColor = Ui.Text;
            _updateEvery.Items.AddRange(new object[]
            {
                "每 1 小时", "每 3 小时", "每 6 小时", "每 12 小时", "每 24 小时"
            });
            int[] hours = new int[] { 1, 3, 6, 12, 24 };
            int sel = 2;
            for (int i = 0; i < hours.Length; i++)
                if (hours[i] == _settings.UpdateCheckHours) sel = i;
            _updateEvery.SelectedIndex = sel;
            _updateEvery.Location = new Point(x + Px(130), y - Px(2));
            _updateEvery.Width = Px(110);
            _updateEvery.Enabled = _settings.AutoCheckUpdate;
            _updateEvery.SelectedIndexChanged += delegate
            {
                int[] opts = new int[] { 1, 3, 6, 12, 24 };
                int i = _updateEvery.SelectedIndex;
                if (i < 0 || i >= opts.Length) return;
                _settings.UpdateCheckHours = opts[i];
                Save();
            };
            _scroll.Controls.Add(_updateEvery);
            y += step;

            Label scaleLabel = new Label();
            scaleLabel.Text = "界面缩放";
            scaleLabel.AutoSize = true;
            scaleLabel.Font = _fonts.F85;
            scaleLabel.ForeColor = Ui.Text;
            scaleLabel.BackColor = Ui.Bg;
            scaleLabel.Location = new Point(x, y + Px(2));
            _scroll.Controls.Add(scaleLabel);

            _uiScale = new ComboBox();
            _uiScale.DropDownStyle = ComboBoxStyle.DropDownList;
            _uiScale.FlatStyle = FlatStyle.Flat;
            _uiScale.Font = _fonts.F75;
            _uiScale.BackColor = Ui.Bg2;
            _uiScale.ForeColor = Ui.Text;
            _uiScale.Items.AddRange(new object[]
            {
                "自动（跟随系统）", "100%", "125%", "150%", "175%", "200%"
            });
            int[] percents = new int[] { 0, 100, 125, 150, 175, 200 };
            int psel = 0;
            for (int i = 0; i < percents.Length; i++)
                if (percents[i] == _settings.UiScalePercent) psel = i;
            _uiScale.SelectedIndex = psel;
            _uiScale.Location = new Point(x + Px(90), y - Px(2));
            _uiScale.Width = Px(130);
            _uiScale.SelectedIndexChanged += delegate
            {
                int[] opts = new int[] { 0, 100, 125, 150, 175, 200 };
                int i = _uiScale.SelectedIndex;
                if (i < 0 || i >= opts.Length) return;
                _settings.UiScalePercent = opts[i];
                Save();
                if (UiScaleChanged != null) UiScaleChanged();
            };
            _scroll.Controls.Add(_uiScale);
            y += step;

            // ---- 版本 / 检查更新 ----
            y += Px(20);
            Label ver = new Label();
            ver.Text = "当前版本 v" + AppInfo.Version;
            ver.AutoSize = true;
            ver.Font = _fonts.F85;
            ver.ForeColor = Ui.TextDim;
            ver.BackColor = Ui.Bg;
            ver.Location = new Point(x, y + Px(5));
            _scroll.Controls.Add(ver);

            _updateBtn = new Button();
            _updateBtn.Text = "检查更新";
            _updateBtn.Size = new Size(Px(96), Px(28));
            _updateBtn.Location = new Point(ClientSize.Width - Px(24) - _updateBtn.Width, y);
            _updateBtn.FlatStyle = FlatStyle.Flat;
            _updateBtn.FlatAppearance.BorderColor = Ui.Line;
            _updateBtn.BackColor = Ui.Bg3;
            _updateBtn.ForeColor = Ui.Text;
            _updateBtn.Click += delegate { CheckUpdates(); };
            _scroll.Controls.Add(_updateBtn);

            _updateStatus = new Label();
            _updateStatus.Text = "";
            _updateStatus.AutoSize = false;
            _updateStatus.Font = _fonts.F75;
            _updateStatus.ForeColor = Ui.TextFaint;
            _updateStatus.BackColor = Ui.Bg;
            _updateStatus.Location = new Point(x, y + Px(28));
            _updateStatus.Size = new Size(ClientSize.Width - Px(48), Px(18));
            _scroll.Controls.Add(_updateStatus);

            // ---- 项目主页 / 求 Star ----
            y += Px(44);
            Label repoLabel = new Label();
            repoLabel.Text = "GitHub：IDShallow/ai-traffic-light ↗";
            repoLabel.AutoSize = true;
            repoLabel.Font = _fonts.F85;
            repoLabel.ForeColor = Color.FromArgb(120, 170, 255);
            repoLabel.BackColor = Ui.Bg;
            repoLabel.Cursor = Cursors.Hand;
            repoLabel.Location = new Point(x, y);
            repoLabel.Click += delegate { OpenRepo(); };
            _scroll.Controls.Add(repoLabel);

            Label starHint = new Label();
            starHint.Text = "如果它帮到了你，点上面的链接给个 ⭐ Star 支持一下；有问题也欢迎提 Issue。";
            starHint.AutoSize = true;
            starHint.Font = _fonts.F75;
            starHint.ForeColor = Ui.TextFaint;
            starHint.BackColor = Ui.Bg;
            starHint.Location = new Point(x, y + Px(20));
            _scroll.Controls.Add(starHint);

            // ---- 钩子 ----
            y += Px(56);
            Label hookTitle = new Label();
            hookTitle.Text = "精确状态（Claude 钩子）";
            hookTitle.ForeColor = Ui.Text;
            hookTitle.BackColor = Ui.Bg;
            hookTitle.Font = _fonts.F95B;
            hookTitle.AutoSize = true;
            hookTitle.Location = new Point(x, y);
            _scroll.Controls.Add(hookTitle);
            y += Px(26);

            _hookStatus = new Label();
            _hookStatus.AutoSize = true;
            _hookStatus.Font = _fonts.F8;
            _hookStatus.Location = new Point(x, y);
            _scroll.Controls.Add(_hookStatus);

            _hookBtn = new Button();
            _hookBtn.Text = "安装钩子";
            _hookBtn.Size = new Size(Px(96), Px(28));
            _hookBtn.Location = new Point(ClientSize.Width - Px(24) - _hookBtn.Width, y - Px(6));
            _hookBtn.FlatStyle = FlatStyle.Flat;
            _hookBtn.FlatAppearance.BorderColor = Ui.Line;
            _hookBtn.BackColor = Ui.Bg3;
            _hookBtn.ForeColor = Ui.Text;
            _hookBtn.Click += delegate { ToggleHooks(); };
            _scroll.Controls.Add(_hookBtn);

            y += Px(30);
            Label hint = new Label();
            hint.Text = "钩子写进 ~/.claude/settings.json（先备份、只增不改），\n用来精确捕捉“需要你回答 / 刚做完 / 出错”的时刻。\n不装也能用：靠会话注册表判断忙/闲，只是精度稍差。\n已开着的会话要重开一次才会带上钩子。";
            hint.AutoSize = true;
            hint.Font = _fonts.F75;
            hint.ForeColor = Ui.TextFaint;
            hint.BackColor = Ui.Bg;
            hint.Location = new Point(x, y);
            _scroll.Controls.Add(hint);

            // ---- 监控来源 ----
            y += Px(84);
            Label sources = new Label();
            sources.Text = "监控来源";
            sources.AutoSize = true;
            sources.Font = _fonts.F75;
            sources.ForeColor = Ui.TextFaint;
            sources.BackColor = Ui.Bg;
            sources.Location = new Point(x, y);
            _scroll.Controls.Add(sources);

            Button pick = new Button();
            pick.Text = "选择监控的对话…";
            pick.Size = new Size(Px(150), Px(24));
            pick.Location = new Point(ClientSize.Width - Px(24) - pick.Width, y - Px(4));
            pick.FlatStyle = FlatStyle.Flat;
            pick.FlatAppearance.BorderColor = Ui.Line;
            pick.BackColor = Ui.Bg3;
            pick.ForeColor = Ui.Text;
            pick.Click += delegate { PickSessions(); };
            _scroll.Controls.Add(pick);
            y += Px(24);

            _checks[12] = AddCheck("Claude Code", x, y, _settings.MonitorClaude,
                delegate { _settings.MonitorClaude = _checks[12].Checked; Save(); });
            _checks[13] = AddCheck("Codex", x + Px(106), y, _settings.MonitorCodex,
                delegate { _settings.MonitorCodex = _checks[13].Checked; Save(); });
            _checks[14] = AddCheck("DeepSeek Harness", x + Px(186), y, _settings.MonitorDsh,
                delegate { _settings.MonitorDsh = _checks[14].Checked; Save(); });

            Button openDir = new Button();
            openDir.Text = "打开配置目录";
            openDir.Size = new Size(Px(110), Px(30));
            openDir.Location = new Point(x, Px(870) - HeaderH - Px(50));
            openDir.FlatStyle = FlatStyle.Flat;
            openDir.FlatAppearance.BorderColor = Ui.Line;
            openDir.BackColor = Ui.Bg3;
            openDir.ForeColor = Ui.Text;
            openDir.Click += delegate
            {
                try
                {
                    Directory.CreateDirectory(Paths.AppData);
                    Process.Start("explorer.exe", "\"" + Paths.AppData + "\"");
                }
                catch { }
            };
            _scroll.Controls.Add(openDir);

            Button close = new Button();
            close.Text = "关闭";
            close.Size = new Size(Px(86), Px(30));
            close.Location = new Point(ClientSize.Width - Px(24) - close.Width, Px(870) - HeaderH - Px(50));
            close.FlatStyle = FlatStyle.Flat;
            close.FlatAppearance.BorderColor = Ui.Line;
            close.BackColor = Ui.Bg3;
            close.ForeColor = Ui.Text;
            close.Click += delegate { Close(); };
            _scroll.Controls.Add(close);

            UpdateHookStatus();
            ApplyPendingUpdate();
        }

        private void ApplyPendingUpdate()
        {
            if (_pending == null || _updateStatus == null) return;
            _updateStatus.ForeColor = Ui.Amber;
            _updateStatus.Text = "发现新版本 v" + _pending.Version + "，点「检查更新」安装";
        }

        /// <summary>后台检查完成时由主窗口调用。</summary>
        public void SetPendingUpdate(UpdateInfo info)
        {
            _pending = info;
            ApplyPendingUpdate();
        }

        private void PickSessions()
        {
            using (SessionsForm f = new SessionsForm(_settings, _sessions, _nameProvider, _scale))
            {
                f.ShowDialog(this);
            }
        }

        private void OpenRepo()
        {
            try
            {
                Process.Start(new ProcessStartInfo(AppInfo.RepoUrl) { UseShellExecute = true });
            }
            catch { }
        }

        private CheckBox AddCheck(string text, int x, int y, bool value, EventHandler changed)
        {
            CheckBox cb = new CheckBox();
            cb.Text = text;
            cb.AutoSize = true;
            cb.Font = _fonts.F85;
            cb.ForeColor = Ui.Text;
            cb.BackColor = Ui.Bg;
            cb.FlatStyle = FlatStyle.Flat;
            cb.Location = new Point(x, y);
            cb.Checked = value;
            cb.CheckedChanged += changed;
            _scroll.Controls.Add(cb);
            return cb;
        }

        private void Save()
        {
            _settings.Save();
        }

        // ---------- 钩子 ----------

        private void ToggleHooks()
        {
            string error;
            bool installed = HooksSetup.IsInstalled();

            if (!installed)
            {
                if (HooksSetup.Install(out error))
                {
                    _settings.HooksEnabled = true;
                    _settings.Save();
                    MessageBox.Show(this, "钩子已安装。\n\n已开着的会话要重开一次才会生效。", "完成",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(this, error, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                if (HooksSetup.Uninstall(out error))
                {
                    _settings.HooksEnabled = false;
                    _settings.Save();
                    MessageBox.Show(this, "钩子已卸载。", "完成",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(this, error, "卸载失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            UpdateHookStatus();
        }

        private void UpdateHookStatus()
        {
            bool installed = HooksSetup.IsInstalled();
            _hookStatus.Text = installed ? "状态：已安装" : "状态：未安装";
            _hookStatus.ForeColor = installed ? Ui.Green : Ui.TextDim;
            _hookBtn.Text = installed ? "卸载钩子" : "安装钩子";
        }

        // ---------- 更新 ----------

        private void CheckUpdates()
        {
            _updateBtn.Enabled = false;
            _updateStatus.ForeColor = Ui.TextDim;
            _updateStatus.Text = "正在检查更新…";

            Thread t = new Thread(delegate()
            {
                try
                {
                    UpdateInfo info = UpdateChecker.Check();
                    if (IsDisposed) return;
                    BeginInvoke((MethodInvoker)delegate { OnUpdateChecked(info); });
                }
                catch (Exception ex)
                {
                    if (IsDisposed) return;
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            _updateBtn.Enabled = true;
                            _updateStatus.ForeColor = Ui.Red;
                            _updateStatus.Text = Ui.Shorten(ex.Message, 80);
                        });
                    }
                    catch { }
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void OnUpdateChecked(UpdateInfo info)
        {
            _updateBtn.Enabled = true;

            if (info == null)
            {
                _updateStatus.ForeColor = Ui.Green;
                _updateStatus.Text = "已是最新版本（v" + AppInfo.Version + "）";
                return;
            }

            _updateStatus.ForeColor = Ui.Amber;
            _updateStatus.Text = "发现新版本 v" + info.Version;
            if (UpdateFound != null) UpdateFound(info);

            string notes = string.IsNullOrEmpty(info.Notes) ? "" : "\n\n" + info.Notes;
            DialogResult r = MessageBox.Show(this,
                "发现新版本 v" + info.Version + "（当前 v" + AppInfo.Version + "）" + notes +
                "\n\n现在下载并安装吗？安装会覆盖当前程序，设置和监控数据都会保留。",
                "检查更新", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

            if (r != DialogResult.Yes) return;

            using (UpdateForm f = new UpdateForm(info))
            {
                f.ShowDialog(this);
            }
        }

        // ---------- 绘制 ----------

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _btnClose = new Rectangle(Width - Px(34), Px(8), Px(24), Px(24));
            try
            {
                using (GraphicsPath p = Ui.RoundPath(new Rectangle(0, 0, Width, Height), Px(10)))
                    Region = new Region(p);
            }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(Ui.Bg))
                g.FillRectangle(b, ClientRectangle);

            TextRenderer.DrawText(g, "设置", _fonts.F12B,
                new Rectangle(Px(24), Px(12), Width - Px(80), Px(24)),
                Ui.Text, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            TextRenderer.DrawText(g, "×", _fonts.F10B, _btnClose,
                _hotClose ? Ui.Text : Ui.TextFaint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            using (Pen p = new Pen(Color.FromArgb(255, 40, 45, 55), 1f))
                g.DrawLine(p, 0, HeaderH - 1, Width, HeaderH - 1);

            using (GraphicsPath path = Ui.RoundPath(new Rectangle(0, 0, Width - 1, Height - 1), Px(10)))
            using (Pen p = new Pen(Color.FromArgb(255, 46, 51, 62), 1f))
                g.DrawPath(p, path);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hot = _btnClose.Contains(e.Location);
            if (hot != _hotClose)
            {
                _hotClose = hot;
                Cursor = hot ? Cursors.Hand : Cursors.Default;
                Invalidate(new Rectangle(0, 0, Width, HeaderH));
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            if (_btnClose.Contains(e.Location)) { Close(); return; }
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
    }
}
