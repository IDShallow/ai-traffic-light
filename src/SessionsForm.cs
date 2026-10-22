using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ClaudeTrafficLight
{
    /// <summary>勾选要监控的会话；默认全选，取消勾选的会话从列表/总览/提醒里隐藏。</summary>
    internal sealed class SessionsForm : Form
    {
        private readonly AppSettings _settings;
        private readonly List<SessionInfo> _sessions;
        private readonly Func<SessionInfo, string> _nameProvider;
        private readonly float _scale;
        private readonly UiFonts _fonts;
        private readonly CheckedListBox _list;

        private int Px(int v) { return (int)Math.Round(v * _scale); }

        public SessionsForm(AppSettings settings, List<SessionInfo> sessions,
                            Func<SessionInfo, string> nameProvider, float scale)
        {
            _settings = settings;
            _sessions = sessions ?? new List<SessionInfo>();
            _nameProvider = nameProvider;
            _scale = scale < 0.5f ? 0.5f : scale;
            _fonts = Ui.Fonts(_scale);

            Text = "选择要监控的对话";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Ui.Bg;
            ForeColor = Ui.Text;
            ClientSize = new Size(Px(520), Px(460));
            Font = _fonts.F85;

            Label hint = new Label();
            hint.Text = "勾选的会话会显示在列表和总览里并参与提醒；取消勾选的会被隐藏。新会话默认勾选。";
            hint.AutoSize = false;
            hint.Font = _fonts.F75;
            hint.ForeColor = Ui.TextDim;
            hint.BackColor = Ui.Bg;
            hint.Location = new Point(Px(20), Px(14));
            hint.Size = new Size(ClientSize.Width - Px(40), Px(36));
            Controls.Add(hint);

            _list = new CheckedListBox();
            _list.CheckOnClick = true;
            _list.Font = _fonts.F85;
            _list.BackColor = Ui.Bg2;
            _list.ForeColor = Ui.Text;
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.IntegralHeight = false;
            _list.Location = new Point(Px(20), Px(54));
            _list.Size = new Size(ClientSize.Width - Px(40), ClientSize.Height - Px(120));
            Controls.Add(_list);

            for (int i = 0; i < _sessions.Count; i++)
            {
                SessionInfo s = _sessions[i];
                string name = _nameProvider != null ? _nameProvider(s) : s.DisplayTitle();
                string text = Ui.ProviderLabel(s.Provider) + " · " + name + " · " + s.StateText();
                _list.Items.Add(text, !_settings.IsMuted(s));
            }

            int by = ClientSize.Height - Px(48);
            Button all = MakeButton("全选", Px(20), by, Px(76));
            all.Click += delegate { SetAll(true); };

            Button none = MakeButton("全不选", Px(104), by, Px(76));
            none.Click += delegate { SetAll(false); };

            Button ok = MakeButton("确定", ClientSize.Width - Px(20) - Px(86) - Px(96), by, Px(86));
            ok.BackColor = Color.FromArgb(255, 45, 90, 160);
            ok.Click += delegate { Apply(); };

            Button cancel = MakeButton("取消", ClientSize.Width - Px(20) - Px(86), by, Px(86));
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private Button MakeButton(string text, int x, int y, int w)
        {
            Button b = new Button();
            b.Text = text;
            b.Size = new Size(w, Px(30));
            b.Location = new Point(x, y);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Ui.Line;
            b.BackColor = Ui.Bg3;
            b.ForeColor = Ui.Text;
            b.Font = _fonts.F85;
            Controls.Add(b);
            return b;
        }

        private void SetAll(bool on)
        {
            for (int i = 0; i < _list.Items.Count; i++) _list.SetItemChecked(i, on);
        }

        private void Apply()
        {
            for (int i = 0; i < _sessions.Count && i < _list.Items.Count; i++)
                _settings.SetMuted(_sessions[i], !_list.GetItemChecked(i));
            _settings.Save();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
