using System.Runtime.InteropServices;
using System.Text;

namespace GrabTester;

public sealed class MainForm : Form
{
    // --- controls ---
    readonly Label      _lblSc      = new() { Text = "SC 文件：", AutoSize = true };
    readonly TextBox    _scPath     = new() { ReadOnly = true, Width = 480 };
    readonly Button     _browse     = new() { Text = "Browse…", Width = 80 };
    readonly Label      _lblUid     = new() { Text = "username（必填）：", AutoSize = true };
    readonly TextBox    _userUid    = new() { Width = 580 };
    readonly Label      _lblApp     = new() { Text = "app_type（可选）：", AutoSize = true };
    readonly TextBox    _appType    = new() { Width = 180, Text = "kt" };
    readonly Label      _lblUser    = new() { Text = "user（可选）：", AutoSize = true };
    readonly TextBox    _user       = new() { Width = 180 };
    readonly Button     _start      = new() { Text = "Start",  Width = 90, Height = 30, BackColor = Color.FromArgb(40, 167, 69),  ForeColor = Color.White };
    readonly Button     _cancel     = new() { Text = "Cancel", Width = 90, Height = 30, BackColor = Color.FromArgb(220, 53, 69), ForeColor = Color.White, Enabled = false };
    readonly RichTextBox _log       = new() { ReadOnly = true, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.LightGray, Font = new Font("Consolas", 9f), ScrollBars = RichTextBoxScrollBars.Vertical };

    GrabModule? _module;

    public MainForm()
    {
        Text        = "GrabTester";
        ClientSize  = new Size(680, 520);
        MinimumSize = new Size(500, 400);

        // --- layout using absolute positioning + resize handler ---
        SuspendLayout();

        int y = 12;

        _lblSc.Location = new Point(12, y + 3);
        y += _lblSc.Height + 2;

        _scPath.Location = new Point(12, y);
        _browse.Location = new Point(_scPath.Right + 6, y - 1);
        y += _scPath.Height + 10;

        _lblUid.Location = new Point(12, y);
        y += _lblUid.Height + 2;
        _userUid.Location = new Point(12, y);
        y += _userUid.Height + 10;

        _lblApp.Location  = new Point(12, y);
        _lblUser.Location = new Point(240, y);
        y += _lblApp.Height + 2;
        _appType.Location = new Point(12, y);
        _user.Location    = new Point(240, y);
        y += _appType.Height + 12;

        _start.Location  = new Point(12, y);
        _cancel.Location = new Point(_start.Right + 10, y);
        y += _start.Height + 10;

        _log.Location = new Point(12, y);
        _log.Size     = new Size(ClientSize.Width - 24, ClientSize.Height - y - 12);
        _log.Anchor   = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        Controls.AddRange(new Control[]
        {
            _lblSc, _scPath, _browse,
            _lblUid, _userUid,
            _lblApp, _appType, _lblUser, _user,
            _start, _cancel,
            _log,
        });

        ResumeLayout(false);

        _browse.Click += Browse_Click;
        _start.Click  += Start_Click;
        _cancel.Click += Cancel_Click;
        FormClosing   += (_, _) => _module?.Dispose();
    }

    // ---- events ----

    void Browse_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "SC files (*.sc)|*.sc|All files (*.*)|*.*",
            Title  = "选择 module_grab.sc",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _scPath.Text = dlg.FileName;
    }

    void Start_Click(object? sender, EventArgs e)
    {
        var path = _scPath.Text.Trim();
        var uid  = _userUid.Text.Trim();
        if (string.IsNullOrEmpty(path)) { Log("请先选择 .sc 文件",  Color.Yellow); return; }
        if (string.IsNullOrEmpty(uid))  { Log("username 不能为空", Color.Yellow); return; }

        _module?.Dispose();
        _module = null;

        try
        {
            _module = new GrabModule(path);
        }
        catch (Exception ex)
        {
            Log($"加载 DLL 失败: {ex.Message}", Color.OrangeRed);
            return;
        }

        _log.Clear();
        SetRunning(true);
        Log("→ 启动 grab…", Color.Cyan);

        SendFn cb = OnFrame;
        GC.KeepAlive(cb);
        _module.Start(uid, _appType.Text.Trim(), _user.Text.Trim(), cb);
    }

    void Cancel_Click(object? sender, EventArgs e)
    {
        _module?.Cancel();
        Log("→ 取消信号已发", Color.Yellow);
    }

    // ---- frame callback (called from native thread) ----

    void OnFrame(byte ch, IntPtr data, nuint len, IntPtr ctx)
    {
        if (len == 0) return;
        var raw  = new byte[(int)len];
        Marshal.Copy(data, raw, 0, (int)len);
        var tok  = raw[0];
        var body = Encoding.UTF8.GetString(raw, 1, raw.Length - 1);
        var col  = tok switch { 0x82 => Color.LightGreen, 0x65 => Color.OrangeRed, _ => Color.LightGray };

        AppendLogThreadSafe($"[{DateTime.Now:HH:mm:ss}] [{tok:X2}] {body}", col);

        if (tok == 0x82 || tok == 0x65)
            BeginInvokeIfRequired(() => SetRunning(false));
    }

    // ---- helpers ----

    void SetRunning(bool running)
    {
        _start.Enabled  = !running;
        _cancel.Enabled = running;
    }

    void AppendLogThreadSafe(string line, Color color)
    {
        if (_log.IsDisposed) return;
        if (_log.InvokeRequired)
        {
            _log.BeginInvoke(() => Append(line, color));
            return;
        }
        Append(line, color);

        void Append(string l, Color c)
        {
            _log.SelectionStart  = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor  = c;
            _log.AppendText(l + "\n");
            _log.ScrollToCaret();
        }
    }

    void Log(string msg, Color color) =>
        AppendLogThreadSafe($"[{DateTime.Now:HH:mm:ss}] {msg}", color);

    void BeginInvokeIfRequired(Action action)
    {
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }
}
