using System;
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
        private readonly CheckBox[] _checks = new CheckBox[13];
        private Label _hookStatus;
        private Button _hookBtn;
        private Label _updateStatus;
        private Button _updateBtn;
        private Rectangle _btnClose;
        private bool _hotClose;

        private int HeaderH { get { return Ui.Px(46); } }

        public SettingsForm(AppSettings settings)
        {
            _settings = settings;

            Text = "设置";
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Ui.Bg;
            ForeColor = Ui.Text;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            TopMost = settings.TopMost;
            ClientSize = new Size(Ui.Px(470), Ui.Px(700));
            MinimumSize = Size;
            MaximumSize = Size;

            int x = Ui.Px(24);
            int y = HeaderH + Ui.Px(12);
            int step = Ui.Px(30);

            _checks[0] = AddCheck("窗口总在最前", x, y, settings.TopMost, delegate { settings.TopMost = _checks[0].Checked; Save(); });
            y += step;
            _checks[1] = AddCheck("需要你回答时提示", x, y, settings.NotifyNeeds, delegate { settings.NotifyNeeds = _checks[1].Checked; Save(); });
            y += step;
            _checks[2] = AddCheck("完成时提示", x, y, settings.NotifyDone, delegate { settings.NotifyDone = _checks[2].Checked; Save(); });
            y += step;
            _checks[3] = AddCheck("出错时提示", x, y, settings.NotifyError, delegate { settings.NotifyError = _checks[3].Checked; Save(); });
            y += step;
            _checks[4] = AddCheck("提示音", x, y, settings.Sound, delegate { settings.Sound = _checks[4].Checked; Save(); });
            y += step;
            _checks[5] = AddCheck("开机自启", x, y, settings.AutoStart, delegate { settings.AutoStart = _checks[5].Checked; Save(); });
            y += step;
            _checks[6] = AddCheck("显示已结束的会话（最近 30 分钟）", x, y, settings.ShowOffline, delegate { settings.ShowOffline = _checks[6].Checked; Save(); });
            y += step;
            _checks[7] = AddCheck("按状态排序（需要你的排最前）", x, y, settings.SortByState, delegate { settings.SortByState = _checks[7].Checked; Save(); });
            y += step;
            _checks[8] = AddCheck("启动时最小化到托盘", x, y, settings.StartMinimized, delegate { settings.StartMinimized = _checks[8].Checked; Save(); });
            y += step;
            _checks[9] = AddCheck("隐私模式（只显示状态，不显示对话名 / 内容）", x, y, settings.PrivacyMode, delegate { settings.PrivacyMode = _checks[9].Checked; Save(); });

            // ---- 版本 / 检查更新 ----
            y += Ui.Px(38);
            Label ver = new Label();
            ver.Text = "当前版本 v" + AppInfo.Version;
            ver.AutoSize = true;
            ver.Font = Ui.F85;
            ver.ForeColor = Ui.TextDim;
            ver.BackColor = Ui.Bg;
            ver.Location = new Point(x, y + Ui.Px(5));
            Controls.Add(ver);

            _updateBtn = new Button();
            _updateBtn.Text = "检查更新";
            _updateBtn.Size = new Size(Ui.Px(96), Ui.Px(28));
            _updateBtn.Location = new Point(ClientSize.Width - Ui.Px(24) - _updateBtn.Width, y);
            _updateBtn.FlatStyle = FlatStyle.Flat;
            _updateBtn.FlatAppearance.BorderColor = Ui.Line;
            _updateBtn.BackColor = Ui.Bg3;
            _updateBtn.ForeColor = Ui.Text;
            _updateBtn.Click += delegate { CheckUpdates(); };
            Controls.Add(_updateBtn);

            _updateStatus = new Label();
            _updateStatus.Text = "";
            _updateStatus.AutoSize = false;
            _updateStatus.Font = Ui.F75;
            _updateStatus.ForeColor = Ui.TextFaint;
            _updateStatus.BackColor = Ui.Bg;
            _updateStatus.Location = new Point(x, y + Ui.Px(28));
            _updateStatus.Size = new Size(ClientSize.Width - Ui.Px(48), Ui.Px(18));
            Controls.Add(_updateStatus);

            // ---- 钩子 ----
            y += Ui.Px(56);
            Label hookTitle = new Label();
            hookTitle.Text = "精确状态（Claude 钩子）";
            hookTitle.ForeColor = Ui.Text;
            hookTitle.BackColor = Ui.Bg;
            hookTitle.Font = Ui.F95B;
            hookTitle.AutoSize = true;
            hookTitle.Location = new Point(x, y);
            Controls.Add(hookTitle);
            y += Ui.Px(26);

            _hookStatus = new Label();
            _hookStatus.AutoSize = true;
            _hookStatus.Font = Ui.F8;
            _hookStatus.Location = new Point(x, y);
            Controls.Add(_hookStatus);

            _hookBtn = new Button();
            _hookBtn.Text = "安装钩子";
            _hookBtn.Size = new Size(Ui.Px(96), Ui.Px(28));
            _hookBtn.Location = new Point(ClientSize.Width - Ui.Px(24) - _hookBtn.Width, y - Ui.Px(6));
            _hookBtn.FlatStyle = FlatStyle.Flat;
            _hookBtn.FlatAppearance.BorderColor = Ui.Line;
            _hookBtn.BackColor = Ui.Bg3;
            _hookBtn.ForeColor = Ui.Text;
            _hookBtn.Click += delegate { ToggleHooks(); };
            Controls.Add(_hookBtn);

            y += Ui.Px(30);
            Label hint = new Label();
            hint.Text = "钩子写进 ~/.claude/settings.json（先备份、只增不改），\n用来精确捕捉“需要你回答 / 刚做完 / 出错”的时刻。\n不装也能用：靠会话注册表判断忙/闲，只是精度稍差。\n已开着的会话要重开一次才会带上钩子。";
            hint.AutoSize = true;
            hint.Font = Ui.F75;
            hint.ForeColor = Ui.TextFaint;
            hint.BackColor = Ui.Bg;
            hint.Location = new Point(x, y);
            Controls.Add(hint);

            // ---- 监控来源 ----
            y += Ui.Px(84);
            Label sources = new Label();
            sources.Text = "监控来源";
            sources.AutoSize = true;
            sources.Font = Ui.F75;
            sources.ForeColor = Ui.TextFaint;
            sources.BackColor = Ui.Bg;
            sources.Location = new Point(x, y);
            Controls.Add(sources);
            y += Ui.Px(24);

            _checks[10] = AddCheck("Claude Code", x, y, settings.MonitorClaude,
                delegate { settings.MonitorClaude = _checks[10].Checked; Save(); });
            _checks[11] = AddCheck("Codex", x + Ui.Px(106), y, settings.MonitorCodex,
                delegate { settings.MonitorCodex = _checks[11].Checked; Save(); });
            _checks[12] = AddCheck("DeepSeek Harness", x + Ui.Px(186), y, settings.MonitorDsh,
                delegate { settings.MonitorDsh = _checks[12].Checked; Save(); });

            Button openDir = new Button();
            openDir.Text = "打开配置目录";
            openDir.Size = new Size(Ui.Px(110), Ui.Px(30));
            openDir.Location = new Point(x, ClientSize.Height - Ui.Px(50));
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
            Controls.Add(openDir);

            Button close = new Button();
            close.Text = "关闭";
            close.Size = new Size(Ui.Px(86), Ui.Px(30));
            close.Location = new Point(ClientSize.Width - Ui.Px(24) - close.Width, ClientSize.Height - Ui.Px(50));
            close.FlatStyle = FlatStyle.Flat;
            close.FlatAppearance.BorderColor = Ui.Line;
            close.BackColor = Ui.Bg3;
            close.ForeColor = Ui.Text;
            close.Click += delegate { Close(); };
            Controls.Add(close);

            UpdateHookStatus();
        }

        private CheckBox AddCheck(string text, int x, int y, bool value, EventHandler changed)
        {
            CheckBox cb = new CheckBox();
            cb.Text = text;
            cb.AutoSize = true;
            cb.Font = Ui.F85;
            cb.ForeColor = Ui.Text;
            cb.BackColor = Ui.Bg;
            cb.FlatStyle = FlatStyle.Flat;
            cb.Location = new Point(x, y);
            cb.Checked = value;
            cb.CheckedChanged += changed;
            Controls.Add(cb);
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
            _btnClose = new Rectangle(Width - Ui.Px(34), Ui.Px(8), Ui.Px(24), Ui.Px(24));
            try
            {
                using (GraphicsPath p = Ui.RoundPath(new Rectangle(0, 0, Width, Height), Ui.Px(10)))
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

            TextRenderer.DrawText(g, "设置", Ui.F12B,
                new Rectangle(Ui.Px(24), Ui.Px(12), Width - Ui.Px(80), Ui.Px(24)),
                Ui.Text, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            TextRenderer.DrawText(g, "×", Ui.F10B, _btnClose,
                _hotClose ? Ui.Text : Ui.TextFaint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            using (Pen p = new Pen(Color.FromArgb(255, 40, 45, 55), 1f))
                g.DrawLine(p, 0, HeaderH - 1, Width, HeaderH - 1);

            using (GraphicsPath path = Ui.RoundPath(new Rectangle(0, 0, Width - 1, Height - 1), Ui.Px(10)))
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
