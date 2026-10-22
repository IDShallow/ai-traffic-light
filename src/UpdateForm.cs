using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace ClaudeTrafficLight
{
    /// <summary>下载新版本并交给安装包（静默覆盖安装）。</summary>
    internal sealed class UpdateForm : Form
    {
        private readonly UpdateInfo _info;
        private readonly Label _title;
        private readonly Label _status;
        private readonly ProgressBar _bar;
        private readonly Button _close;
        private Rectangle _btnClose;
        private bool _hotClose;
        private bool _busy = true;
        private float _scale = 1f;
        private UiFonts _fonts = Ui.Fonts(1f);

        private int Px(int v) { return (int)Math.Round(v * _scale); }

        private int HeaderH { get { return Px(42); } }

        public UpdateForm(UpdateInfo info)
        {
            _scale = Ui.Scale;
            _fonts = Ui.Fonts(_scale);

            _info = info;

            Text = "软件更新";
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Ui.Bg;
            ForeColor = Ui.Text;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            ClientSize = new Size(Px(430), Px(180));
            MinimumSize = ClientSize;
            MaximumSize = ClientSize;

            _title = new Label();
            _title.Text = "正在下载 v" + info.Version + " …";
            _title.AutoSize = true;
            _title.Font = _fonts.F95B;
            _title.ForeColor = Ui.Text;
            _title.BackColor = Ui.Bg;
            _title.Location = new Point(Px(24), HeaderH + Px(12));
            Controls.Add(_title);

            _bar = new ProgressBar();
            _bar.Minimum = 0;
            _bar.Maximum = 100;
            _bar.Value = 0;
            _bar.Location = new Point(Px(24), HeaderH + Px(44));
            _bar.Size = new Size(ClientSize.Width - Px(48), Px(16));
            Controls.Add(_bar);

            _status = new Label();
            _status.Text = "连接更新源…";
            _status.AutoSize = false;
            _status.Font = _fonts.F8;
            _status.ForeColor = Ui.TextDim;
            _status.BackColor = Ui.Bg;
            _status.Location = new Point(Px(24), HeaderH + Px(68));
            _status.Size = new Size(ClientSize.Width - Px(48), Px(36));
            Controls.Add(_status);

            _close = new Button();
            _close.Text = "关闭";
            _close.Size = new Size(Px(84), Px(28));
            _close.Location = new Point(ClientSize.Width - Px(24) - _close.Width,
                                        ClientSize.Height - Px(44));
            _close.FlatStyle = FlatStyle.Flat;
            _close.FlatAppearance.BorderColor = Ui.Line;
            _close.BackColor = Ui.Bg3;
            _close.ForeColor = Ui.Text;
            _close.Enabled = false;
            _close.Click += delegate { Close(); };
            Controls.Add(_close);

            Shown += delegate { StartDownload(); };
        }

        private void StartDownload()
        {
            Thread t = new Thread(delegate()
            {
                try
                {
                    Action<int> report = delegate(int pct)
                    {
                        try
                        {
                            if (!IsDisposed) BeginInvoke((MethodInvoker)delegate
                            {
                                if (pct < 0) pct = 0;
                                if (pct > 100) pct = 100;
                                _bar.Value = pct;
                                _status.Text = "已下载 " + pct + "%";
                            });
                        }
                        catch { }
                    };

                    bool portable = UpdateChecker.IsPortableInstall() && !string.IsNullOrEmpty(_info.ExeUrl);
                    string path = portable
                        ? UpdateChecker.DownloadExe(_info, report)
                        : UpdateChecker.Download(_info, report);

                    if (IsDisposed) return;
                    BeginInvoke((MethodInvoker)delegate { Launch(path, portable); });
                }
                catch (Exception ex)
                {
                    if (IsDisposed) return;
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            _busy = false;
                            _title.Text = "更新失败";
                            _status.Text = ex.Message;
                            _status.ForeColor = Ui.Red;
                            _close.Enabled = true;
                        });
                    }
                    catch { }
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Launch(string path, bool portable)
        {
            _title.Text = "正在安装…";
            _status.Text = portable
                ? "程序会退出并就地替换，然后自动重新打开。"
                : "程序会退出，安装完成后自动重新打开。";
            _bar.Value = 100;

            try
            {
                string dir = Path.GetDirectoryName(Paths.ExePath);
                ProcessStartInfo psi = portable
                    ? new ProcessStartInfo(path, "--apply-update \"" + Paths.ExePath + "\"")
                    : new ProcessStartInfo(path, "--silent --update --restart --dir=\"" + dir + "\"");
                psi.UseShellExecute = true;
                psi.WorkingDirectory = Path.GetTempPath();
                if (!portable && NeedsElevation(dir) && !IsAdmin()) psi.Verb = "runas";

                Process.Start(psi);
                Log.Write("update launched: " + path + " -> " + dir + (portable ? " (portable)" : ""));
                Application.DoEvents();
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                _busy = false;
                _title.Text = portable ? "无法启动更新程序" : "无法启动安装程序";
                _status.Text = ex.Message + "\n可以手动运行：" + path;
                _status.ForeColor = Ui.Red;
                _close.Enabled = true;
            }
        }

        private static bool NeedsElevation(string dir)
        {
            try
            {
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                return (!string.IsNullOrEmpty(pf) && dir.StartsWith(pf, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(pf86) && dir.StartsWith(pf86, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        private static bool IsAdmin()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                WindowsPrincipal p = new WindowsPrincipal(id);
                return p.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private void ApplyBounds()
        {
            MinimumSize = Size.Empty;
            MaximumSize = Size.Empty;
            ClientSize = new Size(Px(430), Px(180));
            MinimumSize = ClientSize;
            MaximumSize = ClientSize;

            _title.Font = _fonts.F95B;
            _title.Location = new Point(Px(24), HeaderH + Px(12));

            _bar.Location = new Point(Px(24), HeaderH + Px(44));
            _bar.Size = new Size(ClientSize.Width - Px(48), Px(16));

            _status.Font = _fonts.F8;
            _status.Location = new Point(Px(24), HeaderH + Px(68));
            _status.Size = new Size(ClientSize.Width - Px(48), Px(36));

            _close.Size = new Size(Px(84), Px(28));
            _close.Location = new Point(ClientSize.Width - Px(24) - _close.Width,
                                        ClientSize.Height - Px(44));
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            float sc = Win32.DpiForWindow(Handle, DeviceDpi) / 96f;
            if (Math.Abs(sc - _scale) > 0.01f)
            {
                _scale = sc;
                _fonts = Ui.Fonts(sc);
                ApplyBounds();
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Win32.WM_DPICHANGED)
            {
                int dpi = Win32.DpiOfWParam(m.WParam);
                Win32.RECT r = Win32.RectOfLParam(m.LParam);

                _scale = dpi / 96f;
                if (_scale < 0.5f) _scale = 0.5f;
                _fonts = Ui.Fonts(_scale);
                ApplyBounds();
                Location = new Point(r.Left, r.Top);

                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _btnClose = new Rectangle(Width - Px(32), Px(6), Px(22), Px(22));
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

            TextRenderer.DrawText(g, "软件更新", _fonts.F12B,
                new Rectangle(Px(24), Px(10), Width - Px(80), Px(24)),
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
            if (hot != _hotClose) { _hotClose = hot; Cursor = hot ? Cursors.Hand : Cursors.Default; Invalidate(new Rectangle(0, 0, Width, HeaderH)); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (_btnClose.Contains(e.Location))
            {
                if (_busy) return;
                Close();
                return;
            }
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
