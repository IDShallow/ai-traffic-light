using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace ClaudeTrafficLight.Setup
{
    internal sealed class SetupForm : Form
    {
        private readonly string _existingDir;
        private readonly bool _upgrade;
        private readonly TextBox _path;
        private readonly Button _browse;
        private readonly CheckBox _desktop;
        private readonly CheckBox _autoStart;
        private readonly CheckBox _runAfter;
        private readonly Button _install;
        private readonly Label _status;
        private Rectangle _btnClose;
        private bool _hotClose;
        private bool _busy;
        private float _scale = 1f;
        private UiFonts _fonts = Ui.Fonts(1f);

        private int Px(int v) { return (int)Math.Round(v * _scale); }

        private int HeaderH { get { return Px(48); } }

        public SetupForm()
        {
            _scale = Ui.Scale;
            _fonts = Ui.Fonts(_scale);

            _existingDir = Installer.ExistingDir();
            _upgrade = !string.IsNullOrEmpty(_existingDir);

            Text = AppInfo.ProductName + " 安装程序";
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Ui.Bg;
            ForeColor = Ui.Text;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            ClientSize = new Size(Px(500), Px(470));
            MinimumSize = Size;
            MaximumSize = Size;
            Icon = SetupIcon.Load();

            int x = Px(28);
            int y = HeaderH + Px(14);

            Label intro = new Label();
            intro.Text = _upgrade
                ? "检测到已安装 v" + (Installer.ExistingVersion().Length > 0 ? Installer.ExistingVersion() : "?") +
                  "。\n本次是覆盖更新：安装位置保持不变，设置与监控数据都会保留。"
                : "选择安装位置。安装到当前用户目录不需要管理员权限。";
            intro.AutoSize = false;
            intro.Font = _fonts.F85;
            intro.ForeColor = Ui.TextDim;
            intro.BackColor = Ui.Bg;
            intro.Location = new Point(x, y);
            intro.Size = new Size(ClientSize.Width - x * 2, Px(44));
            Controls.Add(intro);
            y += Px(50);

            Label pathLabel = new Label();
            pathLabel.Text = "安装位置";
            pathLabel.AutoSize = true;
            pathLabel.Font = _fonts.F85;
            pathLabel.ForeColor = Ui.Text;
            pathLabel.BackColor = Ui.Bg;
            pathLabel.Location = new Point(x, y);
            Controls.Add(pathLabel);
            y += Px(24);

            _path = new TextBox();
            _path.Font = _fonts.F8;
            _path.BackColor = Ui.Bg2;
            _path.ForeColor = Ui.Text;
            _path.BorderStyle = BorderStyle.FixedSingle;
            _path.Location = new Point(x, y);
            _path.Size = new Size(ClientSize.Width - x * 2 - Px(96), Px(26));
            _path.Text = _upgrade ? _existingDir : Installer.DefaultDir();
            _path.ReadOnly = _upgrade;
            _path.SelectionStart = 0;
            _path.SelectionLength = 0;
            Controls.Add(_path);

            _browse = new Button();
            _browse.Text = "浏览…";
            _browse.Size = new Size(Px(86), Px(26));
            _browse.Location = new Point(ClientSize.Width - x - _browse.Width, y - Px(1));
            _browse.FlatStyle = FlatStyle.Flat;
            _browse.FlatAppearance.BorderColor = Ui.Line;
            _browse.BackColor = Ui.Bg3;
            _browse.ForeColor = Ui.Text;
            _browse.Enabled = !_upgrade;
            _browse.Click += delegate { Browse(); };
            Controls.Add(_browse);
            y += Px(34);

            Label hint = new Label();
            hint.Text = "建议装在当前用户目录（不需要管理员权限）；装到 Program Files 需要管理员权限。";
            hint.AutoSize = false;
            hint.Font = _fonts.F75;
            hint.ForeColor = Ui.TextFaint;
            hint.BackColor = Ui.Bg;
            hint.Location = new Point(x, y);
            hint.Size = new Size(ClientSize.Width - x * 2, Px(30));
            Controls.Add(hint);
            y += Px(40);

            _desktop = AddCheck("创建桌面快捷方式", x, y,
                _upgrade ? Installer.DesktopShortcutExists() : true);
            y += Px(30);
            _autoStart = AddCheck("开机自启（最小化到托盘）", x, y,
                _upgrade ? Installer.AutoStartEnabled() : true);
            y += Px(30);
            _runAfter = AddCheck("安装完成后运行", x, y, true);

            y += Px(40);
            _status = new Label();
            _status.AutoSize = false;
            _status.Font = _fonts.F8;
            _status.ForeColor = Ui.TextDim;
            _status.BackColor = Ui.Bg;
            _status.Location = new Point(x, y);
            _status.Size = new Size(ClientSize.Width - x * 2, Px(22));
            Controls.Add(_status);

            _install = new Button();
            _install.Text = _upgrade ? "覆盖更新" : "安装";
            _install.Size = new Size(Px(110), Px(32));
            _install.Location = new Point(ClientSize.Width - x - _install.Width,
                                          ClientSize.Height - Px(54));
            _install.FlatStyle = FlatStyle.Flat;
            _install.FlatAppearance.BorderColor = Ui.Line;
            _install.BackColor = Color.FromArgb(255, 45, 90, 160);
            _install.ForeColor = Ui.Text;
            _install.Click += delegate { DoInstall(); };
            Controls.Add(_install);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Size = new Size(Px(86), Px(32));
            cancel.Location = new Point(ClientSize.Width - x - _install.Width - Px(12) - cancel.Width,
                                        ClientSize.Height - Px(54));
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.FlatAppearance.BorderColor = Ui.Line;
            cancel.BackColor = Ui.Bg3;
            cancel.ForeColor = Ui.Text;
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);
        }

        private CheckBox AddCheck(string text, int x, int y, bool value)
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
            Controls.Add(cb);
            return cb;
        }

        private void Browse()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择安装位置";
                dlg.ShowNewFolderButton = true;
                try { dlg.SelectedPath = _path.Text; } catch { }
                if (dlg.ShowDialog(this) == DialogResult.OK) _path.Text = dlg.SelectedPath;
            }
        }

        private void DoInstall()
        {
            string dir = (_path.Text ?? "").Trim();
            if (dir.Length == 0)
            {
                MessageBox.Show(this, "请先选择安装位置。", "安装", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try { dir = Path.GetFullPath(dir); }
            catch
            {
                MessageBox.Show(this, "安装位置不合法。", "安装", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _busy = true;
            SetEnabled(false);
            _status.ForeColor = Ui.TextDim;

            try
            {
                InstallOptions o = new InstallOptions();
                o.Dir = dir;
                o.DesktopShortcut = _desktop.Checked;
                o.AutoStart = _autoStart.Checked;
                o.RunAfter = _runAfter.Checked;

                Installer.Install(o, delegate(string s)
                {
                    _status.Text = s;
                    try { Application.DoEvents(); } catch { }
                });

                _busy = false;
                _status.ForeColor = Ui.Green;
                _status.Text = "安装完成。";
                MessageBox.Show(this, "安装完成。\n\n安装位置：" + dir, "完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                _busy = false;
                SetEnabled(true);
                _status.ForeColor = Ui.Red;
                _status.Text = "安装失败：" + ex.Message;
                MessageBox.Show(this, ex.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SetEnabled(bool on)
        {
            _install.Enabled = on;
            _browse.Enabled = on && !_upgrade;
            _path.Enabled = on;
            _desktop.Enabled = on;
            _autoStart.Enabled = on;
            _runAfter.Enabled = on;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _btnClose = new Rectangle(Width - Px(34), Px(8), Px(24), Px(24));
            try
            {
                using (GraphicsPath p = Ui.RoundPath(new Rectangle(0, 0, Width, Height), Px(12)))
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

            TextRenderer.DrawText(g, AppInfo.ProductName, _fonts.F12B,
                new Rectangle(Px(28), Px(12), Width - Px(90), Px(24)),
                Ui.Text, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            TextRenderer.DrawText(g, "v" + AppInfo.Version, _fonts.F8,
                new Rectangle(Px(28) + TextRenderer.MeasureText(AppInfo.ProductName, _fonts.F12B).Width + Px(10),
                              Px(17), Px(80), Px(18)),
                Ui.TextFaint, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            TextRenderer.DrawText(g, "×", _fonts.F10B, _btnClose,
                _hotClose ? Ui.Text : Ui.TextFaint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            using (Pen p = new Pen(Color.FromArgb(255, 40, 45, 55), 1f))
                g.DrawLine(p, 0, HeaderH - 1, Width, HeaderH - 1);

            using (GraphicsPath path = Ui.RoundPath(new Rectangle(0, 0, Width - 1, Height - 1), Px(12)))
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

            if (_btnClose.Contains(e.Location)) { if (!_busy) Close(); return; }
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

    /// <summary>安装程序自己的图标：从 exe 的资源里取（没有就用系统默认）。</summary>
    internal static class SetupIcon
    {
        public static System.Drawing.Icon Load()
        {
            try
            {
                return System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { return null; }
        }
    }
}
