using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PersistentBridge;

internal sealed class ApprovalForm : Form
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, out int value, uint flags);
    readonly System.Windows.Forms.Timer pulse = new() { Interval = 16 };
    readonly Label countdown = Style.Label("");
    readonly TextBox reason = Style.Box();
    readonly CheckBox capture = new() { Text = "Include a local text diff of the new file (8 KiB maximum, live session only)", AutoSize = true };
    readonly Action<Decision> finish;
    readonly long deadlineStart;
    readonly Screen screen;
    readonly int limit;
    readonly TableLayoutPanel layout = Style.Stack();
    readonly TextBox command;
    bool expanded, settled;
    readonly Button stopButton = Style.Button("Stop task", Style.Red);
    long entrance;

    protected override bool ShowWithoutActivation => true;

    public ApprovalForm(Operation op, long start, int seconds, Action<Decision> finish, Action<string> draft, Action stop)
    {
        this.finish = finish; deadlineStart = start; limit = seconds;
        IntPtr foreground = GetForegroundWindow();
        screen = foreground == IntPtr.Zero ? Screen.PrimaryScreen! : Screen.FromHandle(foreground);
        Style.Theme(this); Text = "Persistent PowerShell Bridge  ·  Approval required";
        StartPosition = FormStartPosition.Manual; ShowInTaskbar = true; TopMost = true; KeyPreview = true;
        var title = Style.Label("Approval required"); title.Font = new Font(Font.FontFamily, 17, FontStyle.Bold);
        var header = Style.Bar(); header.Controls.AddRange([title, countdown]); layout.Row(header);
        var banner = Style.Bar();
        var classification = Style.Badge(op.ReadOnly ? "READ — GREEN" : "WRITE — RED", op.ReadOnly ? Style.Green : Style.Red);
        var privilege = Style.Badge(op.Privilege, op.Privilege == "STANDARD USER" ? Style.Panel : Color.FromArgb(91,66,39));
        banner.Controls.AddRange([classification,privilege]); layout.Row(banner);
        var ids = Style.Label($"Request  {op.Id}     ·     Task  {(op.TaskId == Guid.Empty ? "unassigned" : op.TaskId)}");
        ids.ForeColor = Style.Muted; ids.Dock = DockStyle.Fill; layout.Row(ids);
        var targetLine = Style.Label($"{op.Connection}    ·    {op.Target}    ·    {op.Identity}    ·    Execution limit {op.Seconds}s");
        targetLine.Dock = DockStyle.Fill; layout.Row(targetLine);
        var context = Style.Box($"Purpose (model-supplied)\r\n{op.Description}\r\n\r\nExpected effects (model-supplied)\r\n{op.Effects}\r\n\r\nWorking directory    {op.Directory}\r\nKnown resources    {op.Resources}\r\n{op.Risks}");
        context.MinimumSize = new(0,48);
        layout.Row(context, SizeType.Percent, 48);
        var tools = Style.Bar(); var copy = Style.Button("Copy"); var expand = Style.Button("Expand");
        tools.Controls.AddRange([Style.Label("Exact command or operation"), copy, expand]); layout.Row(tools);
        command = Style.Box(op.Command, true); layout.Row(command, SizeType.Percent, 52);
        copy.Enabled = op.Command.Length > 0;
        copy.Click += (_, _) => { try { Clipboard.SetText(op.Command); } catch (ExternalException) { copy.Text = "Copy unavailable"; } };
        expand.Click += (_, _) => {
            expanded = !expanded; context.Visible = !expanded; layout.RowStyles[layout.GetRow(context)].Height = expanded ? 0 : 48;
            WindowState = expanded ? FormWindowState.Maximized : FormWindowState.Normal;
            expand.Text = expanded ? "Restore view" : "Expand"; if (!expanded) Center(0);
        };
        reason.ReadOnly = false; reason.AcceptsReturn = true; reason.MaxLength = 2000; reason.PlaceholderText = "Optional rejection feedback to ChatGPT";
        reason.AccessibleName = "Rejection feedback"; reason.TextChanged += (_, _) => draft(reason.Text);
        if (op.CanCaptureDiff) layout.Row(capture, SizeType.AutoSize);
        layout.Row(reason, SizeType.Absolute, 56);
        var actions = Style.Bar();
        var reject = Style.Button("Reject"); var approve = Style.Button("Approve once", Style.Green);
        actions.Controls.AddRange([stopButton, reject, approve]); layout.Row(actions);
        Controls.Add(layout); AcceptButton = reject;
        reject.Click += (_, _) => Decide(false); approve.Click += (_, _) => Decide(true);
        stopButton.Click += (_, _) => { try { stop(); } finally { finish(new(false, reason.Text, true, "cancelled")); Close(); } };
        FormClosed += (_, _) => { pulse.Stop(); finish(new(false, reason.Text)); };
        Location = screen.WorkingArea.Location;
        Load += (_, _) => {
            var area = screen.WorkingArea;
            Size = new(Math.Min(area.Width, LogicalToDeviceUnits(840)), Math.Min(area.Height, LogicalToDeviceUnits(680)));
            settled = !SystemParametersInfo(0x1042, 0, out int motion, 0) || motion == 0;
            Center(settled ? 0 : 10); UpdateTime();
        };
        Shown += (_, _) => { Center(settled ? 0 : 10); ActiveControl = reject; entrance = Stopwatch.GetTimestamp(); pulse.Start(); };
        pulse.Tick += (_, _) => {
            if (!settled) {
                double t = Math.Min(1, Stopwatch.GetElapsedTime(entrance).TotalMilliseconds / 210);
                // Settle before pointer interaction so the decision buttons stay still.
                if (Bounds.Contains(Cursor.Position) || t >= 1) { Center(0); settled = true; }
                else Center((int)Math.Round(10 * (1-t) * (1-t) * Math.Cos(Math.PI * 1.7 * t)));
            }
            UpdateTime();
        };
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keys)
    {
        switch (keys) {
            case Keys.Control | Keys.Enter: Decide(true); return true;
            case Keys.Escape:
            case Keys.Control | Keys.R: Decide(false); return true;
            case Keys.Control | Keys.Shift | Keys.F12: stopButton.PerformClick(); return true;
            default: return base.ProcessCmdKey(ref message, keys);
        }
    }
    void Center(int offset)
    {
        var a = screen.WorkingArea;
        Location = new(a.Left + (a.Width - Width) / 2, Math.Clamp(a.Top + (a.Height - Height) / 2 + offset, a.Top, Math.Max(a.Top, a.Bottom - Height)));
    }
    void UpdateTime()
    {
        var left = Math.Max(0, limit - Stopwatch.GetElapsedTime(deadlineStart).TotalSeconds);
        countdown.Text = $"Decision expires in  {(int)Math.Ceiling(left)}s";
        countdown.ForeColor = left <= 10 ? Color.LightSalmon : Style.Muted;
        if (left == 0) { finish(new(false, reason.Text, Status: "approval_expired")); Close(); }
    }
    void Decide(bool yes)
    {
        if (Stopwatch.GetElapsedTime(deadlineStart).TotalSeconds >= limit) { UpdateTime(); return; }
        try { if (yes) DesktopAccess.Check(); finish(new(yes, yes ? "" : reason.Text, CaptureDiff: yes && capture.Checked)); }
        catch { finish(new(false, "Approval desktop unavailable")); }
        Close();
    }
    protected override void Dispose(bool disposing) { if (disposing) pulse.Dispose(); base.Dispose(disposing); }
}
