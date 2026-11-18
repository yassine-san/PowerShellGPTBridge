using System.Diagnostics;
using System.Text.Json;

namespace PersistentBridge;

internal sealed class Window : Form, IOperatorView
{
    public readonly Label State = Style.Label(""), Transport = Style.Label("Checking tunnel client…");
    public readonly Button Reset = Style.Button("Reset session"), StopTask = Style.Button("Stop task", Style.Red), NewTask = Style.Button("Start new task");
    public Task<Credential?> RequestCredential(string target,bool sudo,CancellationToken token) =>
        CredentialStore.Prompt(this,target,token,sudo);
    public event Action? TaskStopped;
    public event Action? NewTaskRequested;
    public void Notify(string text) => MessageBox.Show(this,text);
    public void SetState(string text) => State.Text = text;
    readonly Dictionary<Guid, HistoryEntry> entries = new();
    readonly Queue<Guid> liveTranscripts = new();
    readonly ListBox history = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, BorderStyle = BorderStyle.None,
        BackColor = Style.Panel, ForeColor = Style.Ink, AccessibleName = "Operation history" };
    readonly TextBox filter = new() { Dock = DockStyle.Fill, PlaceholderText = "Search operations or host", BackColor = Style.Panel, ForeColor = Style.Ink };
    readonly TextBox[] pages = Enumerable.Range(0,4).Select(i => Style.Box("", i >= 2)).ToArray();
    readonly Label activity = Style.Label("Ready for a request");
    readonly System.Windows.Forms.Timer clock = new() { Interval = 500 };
    readonly History? store;
    readonly string? placement;
    bool refreshing;

    public Window(string? root = null)
    {
        NewTask.Click += (_, _) => NewTaskRequested?.Invoke();
        Style.Theme(this); Text = "Persistent PowerShell Bridge  ·  Operations"; Size = new(1120, 720);
        if (root != null) {
            store = new(root); placement = Path.Combine(root, "manager-bounds.json");
            foreach (var entry in store.Load()) entries[entry.Op.Id] = entry;
        }
        history.DrawMode = DrawMode.OwnerDrawFixed;
        history.ItemHeight = LogicalToDeviceUnits(77);
        history.DrawItem += DrawEntry;
        DpiChanged += (_, _) => history.ItemHeight = LogicalToDeviceUnits(77);
        var layout = Style.Stack();
        var heading = Style.Bar(); Transport.ForeColor = Color.Khaki; heading.Controls.AddRange([Transport, Style.Label($"{Environment.MachineName}    |    {IdentityInfo.Name}    |    {IdentityInfo.Privilege}")]); layout.Row(heading);
        layout.Row(State); layout.Row(activity);
        var split = new SplitContainer { Dock = DockStyle.Fill, BackColor = Style.Line, Panel1MinSize = 0, Panel2MinSize = 0 };
        var left = Style.Stack(); left.BackColor = Style.Canvas; left.Row(Style.Label("Operation history")); left.Row(filter); left.Row(history, SizeType.Percent, 100); split.Panel1.Controls.Add(left);
        var tabs = new TabControl { Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed, SizeMode = TabSizeMode.Fixed, ItemSize = new(81,29) };
        foreach (var name in new[]{"Summary","Changes","Output","Details"}) {
            var page = new TabPage(name) { BackColor = Style.Canvas, Padding = new Padding(6) };
            page.Controls.Add(pages[tabs.TabCount]); tabs.TabPages.Add(page);
        }
        tabs.DrawItem += (_, e) => {
            var r = tabs.GetTabRect(e.Index); using var brush = new SolidBrush(e.Index == tabs.SelectedIndex ? Color.FromArgb(40,64,92) : Style.Panel);
            e.Graphics.FillRectangle(brush,r); TextRenderer.DrawText(e.Graphics,tabs.TabPages[e.Index].Text,Font,r,Style.Ink,TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        split.Panel2.Controls.Add(tabs); layout.Row(split, SizeType.Percent, 100);
        var bar = Style.Bar(); var disconnect = Style.Button("Disconnect",Style.Red);
        var retain = Style.Button("Keep reviewed details…");
        retain.Click += (_, _) => ReviewRetention();
        bar.Controls.AddRange([StopTask, NewTask, Reset, retain, disconnect]); layout.Row(bar); Controls.Add(layout);
        disconnect.Click += (_, _) => Close(); StopTask.Click += (_, _) => TaskStopped?.Invoke();
        filter.TextChanged += (_, _) => RefreshList(); history.SelectedIndexChanged += (_, _) => { if (!refreshing) ShowSelection(); };
        Shown += (_, _) => { RestoreBoundsFile(); RefreshList(); clock.Start(); };
        split.SizeChanged += (_, _) => {
            var direction = ClientSize.Width < LogicalToDeviceUnits(760) ? Orientation.Horizontal : Orientation.Vertical;
            split.Orientation = direction; int extent = direction == Orientation.Vertical ? split.Width : split.Height;
            if (extent > 40) split.SplitterDistance = extent * (direction == Orientation.Vertical ? 39 : 36) / 100;
        };
        clock.Tick += (_, _) => UpdateActivity();
        FormClosing += (_, _) => {
            if (placement == null) return;
            var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            try { File.WriteAllText(placement, JsonSerializer.Serialize(new[] {r.X,r.Y,r.Width,r.Height})); } catch (IOException) { }
        };
    }

    void ReviewRetention()
    {
        if (history.SelectedItem is not Item item || item.Entry.Ended == null || store == null) return;
        var entry = item.Entry;
        using var form = new Form { Text = "Retain reviewed operation details", Size = new(720,530), StartPosition = FormStartPosition.CenterParent };
        Style.Theme(form);
        var body = Style.Stack(); var fields = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BackColor = Style.Panel, ForeColor = Style.Ink };
        var preview = Style.Box("",true);
        string[] names = ["Exact operation","Purpose and expected effects","Paths and resources","Rejection or draft feedback","Success stream","Error stream"];
        string[] values = [entry.Op.Command,entry.Op.Description+"\r\n"+entry.Op.Effects,entry.Op.Directory+"\r\n"+entry.Op.Resources+"\r\n"+entry.Result?.Directory,
            entry.Reason,entry.Result?.Output ?? "",entry.Result?.Error ?? ""];
        fields.Items.AddRange(names);
        fields.SelectedIndexChanged += (_, _) => preview.Text = fields.SelectedIndex >= 0 ? values[fields.SelectedIndex] : "";
        body.Row(Style.Label("Select only fields you have reviewed for passwords, keys and tokens."));
        body.Row(fields,SizeType.Percent,35); body.Row(preview,SizeType.Percent,65);
        var actions = Style.Bar(); var cancel = Style.Button("Cancel"); var save = Style.Button("Keep selected fields");
        cancel.DialogResult = DialogResult.Cancel; save.DialogResult = DialogResult.OK;
        actions.Controls.AddRange([cancel,save]); body.Row(actions); form.Controls.Add(body); form.AcceptButton = cancel; form.CancelButton = cancel;
        if (form.ShowDialog(this) != DialogResult.OK) return;
        try { store.KeepReviewed(entry, fields.CheckedIndices.Cast<int>().ToHashSet()); }
        catch (Exception ex) { MessageBox.Show(this,"Could not save reviewed details: " + ex.GetType().Name); }
    }

    void RestoreBoundsFile()
    {
        if (placement == null || !File.Exists(placement)) return;
        try {
            if (JsonSerializer.Deserialize<int[]>(File.ReadAllText(placement)) is not [var x,var y,var w,var h] || w < 300 || h < 250) return;
            var area = Screen.FromRectangle(new(x,y,w,h)).WorkingArea; w = Math.Min(w,area.Width); h = Math.Min(h,area.Height);
            Bounds = new(Math.Clamp(x,area.Left,area.Right-w), Math.Clamp(y,area.Top,area.Bottom-h),w,h);
        } catch (Exception e) when (e is IOException or JsonException) { }
    }

    public void Record(HistoryEntry entry)
    {
        store?.Save(entry);
        if (!IsHandleCreated || IsDisposed) return;
        BeginInvoke((Action)(() => {
            entries[entry.Op.Id] = entry;
            if (entry.Ended != null) {
                liveTranscripts.Enqueue(entry.Op.Id);
                if (liveTranscripts.Count > 128 && entries.TryGetValue(liveTranscripts.Dequeue(),out var older))
                    entries[older.Op.Id] = store?.RestoreReviewed(older) ?? History.ForDisk(older);
            }
            RefreshList(); UpdateActivity();
        }));
    }

    sealed record Item(HistoryEntry Entry)
    {
        public override string ToString() => $"{Entry.Requested.ToLocalTime():MM-dd HH:mm:ss}  {(Entry.Op.ReadOnly ? "READ" : "WRITE")}  {Entry.Op.Description.ReplaceLineEndings(" ")}  ·  {Entry.Op.Target}  ·  {Entry.Approval} / {Entry.Status}  {Entry.DurationMs} ms";
    }
    void DrawEntry(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= history.Items.Count || history.Items[e.Index] is not Item item) return;
        var row = item.Entry;
        using var bg = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(42,61,85) : Style.Panel);
        e.Graphics.FillRectangle(bg,e.Bounds);
        using var stripe = new SolidBrush(row.Op.ReadOnly ? Color.MediumSeaGreen : Color.IndianRed);
        e.Graphics.FillRectangle(stripe,e.Bounds.Left,e.Bounds.Top,LogicalToDeviceUnits(3),e.Bounds.Height-1);
        var r = Rectangle.Inflate(e.Bounds,-LogicalToDeviceUnits(9),-LogicalToDeviceUnits(3));
        r.Height /= 3;
        const TextFormatFlags flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        TextRenderer.DrawText(e.Graphics,row.Op.Description.ReplaceLineEndings(" "),Font,r,Style.Ink,flags);
        r.Y += r.Height;
        TextRenderer.DrawText(e.Graphics,$"{row.Requested.ToLocalTime():MM-dd HH:mm:ss}   ·   {row.Op.Target}",Font,r,Style.Muted,flags);
        r.Y += r.Height;
        TextRenderer.DrawText(e.Graphics,$"{(row.Op.ReadOnly ? "READ" : "WRITE")}    {row.Approval} / {row.Status}    {row.DurationMs} ms",Font,r,Style.Ink,flags);
        e.DrawFocusRectangle();
    }

    void RefreshList()
    {
        Guid? selected = (history.SelectedItem as Item)?.Entry.Op.Id;
        var rows = entries.Values.OrderByDescending(e => e.Requested).Select(e => new Item(e))
            .Where(i => ($"{i} {i.Entry.Op.Id} {i.Entry.TaskId}").Contains(filter.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        refreshing = true; history.BeginUpdate();
        try {
            history.Items.Clear(); history.Items.AddRange(rows);
            if (rows.Length != 0) history.SelectedIndex = Math.Max(0, Array.FindIndex(rows, i => i.Entry.Op.Id == selected));
        } finally { history.EndUpdate(); refreshing = false; }
        ShowSelection();
    }

    void UpdateActivity()
    {
        var e = entries.Values.FirstOrDefault(e => e.Ended == null && e.Status != "result_unknown");
        activity.Text = e == null ? "No active operation" : $"{e.Status.Replace('_',' ')}   ·   {e.Op.Description.ReplaceLineEndings(" ")}   ·   {e.Op.Target}   ·   {(DateTimeOffset.UtcNow - (e.Started ?? e.Requested)):mm\\:ss} elapsed   ·   execution limit {e.Op.Seconds}s";
    }

    void ShowSelection()
    {
        if (history.SelectedItem is not Item item) { foreach (var page in pages) page.Clear(); pages[0].Text = filter.Text.Length == 0 ? "Waiting for the first operation." : "No matching operations."; return; }
        var e = item.Entry; var o = e.Op; var r = e.Result;
        pages[0].Text = $"{o.Description}  (model-supplied purpose)\r\n\r\n{o.Connection}   ·   {o.Target}\r\n{o.Identity}   ·   {o.Privilege}\r\n{(o.ReadOnly ? "READ" : "WRITE")}   ·   {e.Status}\r\n\r\nExpected effects (model-supplied)\r\n{o.Effects}\r\n\r\nEvidence\r\n{e.Evidence}\r\n\r\nScope\r\n{o.Risks}\r\n\r\nApproval   {e.Approval}\r\n{(e.Approval is "approval_expired" or "cancelled" ? "Incomplete feedback" : "Decision feedback")}   {e.Reason}";
        pages[1].Text = $"Target   {o.Target}\r\n{o.Resources}\r\n\r\nObserved changes\r\n{e.Evidence}\r\n\r\n{r?.FileDiff ?? "No verified content diff available."}";
        pages[2].Text = $"Status   {e.Status}    Duration   {e.DurationMs} ms    Exit   {r?.ExitCode?.ToString() ?? "unavailable"}\r\nTruncation   {r?.Truncated.ToString() ?? "pending"}\r\n\r\nSuccess stream\r\n{r?.Output ?? "Results appear when execution returns."}\r\n\r\nError stream / diagnostics\r\n{r?.Error}";
        pages[3].Text = $"Request   {o.Id}\r\nTask   {e.TaskId}\r\nSession   {e.SessionId}\r\nTarget   {o.Target}\r\nDirectory before   {o.Directory}\r\nDirectory after   {r?.Directory}\r\nRequested   {e.Requested:O}\r\nDecision   {e.Decided:O}\r\nStarted   {e.Started:O}\r\nEnded   {e.Ended:O}\r\nExecution limit   {o.Seconds}s\r\n\r\nExact operation\r\n{o.Command}";
    }

    public Task<T> OnUi<T>(Func<T> action, T rejected, CancellationToken token)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try { BeginInvoke((Action)(() => {
            try { if (token.IsCancellationRequested || IsDisposed) { result.TrySetResult(rejected); return; }
                DesktopAccess.Check(); result.TrySetResult(action()); }
            catch (Exception e) { Console.Error.WriteLine("UI failed closed: " + e.GetType().Name); result.TrySetResult(rejected); }
        })); } catch { result.TrySetResult(rejected); }
        return result.Task.WaitAsync(token);
    }
    public async Task<Decision> Approve(Operation op, CancellationToken token, long start = 0)
    {
        if (start == 0) start = Stopwatch.GetTimestamp();
        var done = new TaskCompletionSource<Decision>(TaskCreationOptions.RunContinuationsAsynchronously);
        ApprovalForm? dialog = null; string draft = "";
        void Post(Action action) { try { BeginInvoke(action); } catch (InvalidOperationException) { done.TrySetResult(new(false,"Approval UI unavailable")); } }
        using var cancel = token.Register(() => {
            done.TrySetResult(new(false, Volatile.Read(ref draft), Status: "approval_expired"));
            Post(() => dialog?.Close());
        });
        Post(() => {
            if (done.Task.IsCompleted) return;
            try {
                DesktopAccess.Check();
                dialog = new(op, start, 60,
                    d => done.TrySetResult(d), text => Volatile.Write(ref draft,text), () => TaskStopped?.Invoke());
                if (!done.Task.IsCompleted) dialog.Show(); else dialog.Dispose();
            } catch { dialog?.Dispose(); done.TrySetResult(new(false,"Approval UI unavailable")); }
        });
        return await done.Task.ConfigureAwait(false);
    }
    protected override void Dispose(bool disposing) { if (disposing) clock.Dispose(); base.Dispose(disposing); }
}
