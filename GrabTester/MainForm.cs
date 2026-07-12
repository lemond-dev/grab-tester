using System.Runtime.InteropServices;
using System.Text;

namespace GrabTester;

public sealed class MainForm : Form
{
    readonly TextBox _scPath   = new() { ReadOnly = true, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
    readonly Button  _browse   = new() { Text = "Browse…", AutoSize = true };
    readonly TextBox _userUid  = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, PlaceholderText = "Required — UUID" };
    readonly TextBox _appType  = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, PlaceholderText = "Optional, e.g. kt" };
    readonly TextBox _user     = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, PlaceholderText = "Optional, Windows username" };
    readonly Button  _start    = new() { Text = "Start", BackColor = Color.FromArgb(40, 167, 69),  ForeColor = Color.White, AutoSize = true };
    readonly Button  _cancel   = new() { Text = "Cancel", BackColor = Color.FromArgb(220, 53, 69), ForeColor = Color.White, AutoSize = true, Enabled = false };
    readonly RichTextBox _log  = new() { ReadOnly = true, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.LightGray, Font = new Font("Consolas", 9f), Dock = DockStyle.Fill, ScrollBars = RichTextBoxScrollBars.Vertical };

    GrabModule? _module;

    public MainForm()
    {
        Text = "GrabTester";
        Size = new Size(760, 560);
        MinimumSize = new Size(560, 400);

        var top = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Padding = new Padding(8) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        top.Controls.Add(new Label { Text = "SC 文件", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        top.SetColumnSpan(top.Controls[^1], 2);

        _scPath.Width = 500;
        top.Controls.Add(_scPath, 0, 1);
        top.Controls.Add(_browse, 1, 1);

        top.Controls.Add(new Label { Text = "user_uid", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        top.SetColumnSpan(top.Controls[^1], 2);
        top.Controls.Add(_userUid, 0, 3);
        top.SetColumnSpan(_userUid, 2);

        top.Controls.Add(new Label { Text = "app_type（可选）", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        top.Controls.Add(new Label { Text = "user（可选）", AutoSize = true, Anchor = AnchorStyles.Left }, 1, 4);
        top.Controls.Add(_appType, 0, 5);
        top.Controls.Add(_user, 1, 5);

        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        btnRow.Controls.AddRange(new Control[] { _start, _cancel });

        Controls.Add(_log);
        Controls.Add(btnRow);
        Controls.Add(top);

        _browse.Click += Browse_Click;
        _start.Click  += Start_Click;
        _cancel.Click += Cancel_Click;

        FormClosing += (_, _) => { _module?.Dispose(); };
    }

    void Browse_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Filter = "SC files (*.sc)|*.sc|All files (*.*)|*.*", Title = "选择 module_grab.sc" };
        if (dlg.ShowDialog() == DialogResult.OK)
            _scPath.Text = dlg.FileName;
    }

    void Start_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_scPath.Text))  { Log("请先选择 .sc 文件", Color.Yellow); return; }
        if (string.IsNullOrWhiteSpace(_userUid.Text)) { Log("user_uid 不能为空", Color.Yellow); return; }

        _module?.Dispose();
        try
        {
            _module = new GrabModule(_scPath.Text);
        }
        catch (Exception ex)
        {
            Log($"加载 DLL 失败: {ex.Message}", Color.OrangeRed);
            return;
        }

        _log.Clear();
        _start.Enabled  = false;
        _cancel.Enabled = true;
        Log("→ 启动 grab…", Color.Cyan);

        SendFn cb = OnFrame;
        _module.Start(_userUid.Text.Trim(), _appType.Text.Trim(), _user.Text.Trim(), cb);
        GC.KeepAlive(cb);
    }

    void Cancel_Click(object? sender, EventArgs e)
    {
        _module?.Cancel();
        Log("→ 取消信号已发送", Color.Yellow);
    }

    void OnFrame(byte ch, IntPtr data, nuint len, IntPtr ctx)
    {
        if (len == 0) return;
        var raw = new byte[(int)len];
        Marshal.Copy(data, raw, 0, (int)len);
        var tok  = raw[0];
        var body = Encoding.UTF8.GetString(raw, 1, raw.Length - 1);
        var color = tok switch
        {
            0x82 => Color.LightGreen,
            0x65 => Color.OrangeRed,
            _    => Color.LightGray,
        };
        var line = $"[{DateTime.Now:HH:mm:ss}] [{tok:X2}] {body}";

        if (InvokeRequired)
            BeginInvoke(() => AppendLog(line, color));
        else
            AppendLog(line, color);

        if (tok == 0x82 || tok == 0x65)
        {
            if (InvokeRequired)
                BeginInvoke(OnDone);
            else
                OnDone();
        }
    }

    void OnDone()
    {
        _start.Enabled  = true;
        _cancel.Enabled = false;
    }

    void Log(string msg, Color color)
    {
        AppendLog($"[{DateTime.Now:HH:mm:ss}] {msg}", color);
    }

    void AppendLog(string line, Color color)
    {
        _log.SelectionStart  = _log.TextLength;
        _log.SelectionLength = 0;
        _log.SelectionColor  = color;
        _log.AppendText(line + "\n");
        _log.ScrollToCaret();
    }
}
