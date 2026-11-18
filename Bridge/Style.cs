namespace PersistentBridge;

internal static class Style
{
    internal static readonly Color Canvas = Color.FromArgb(16,23,35), Panel = Color.FromArgb(28,40,57),
        Line = Color.FromArgb(68,84,105), Ink = Color.FromArgb(234,241,249), Muted = Color.FromArgb(173,189,208),
        Green = Color.FromArgb(14,91,67), Red = Color.FromArgb(112,39,52);
    internal static void Theme(Form form)
    {
        form.Font = new Font("Segoe UI", 10); form.AutoScaleMode = AutoScaleMode.Dpi;
        form.BackColor = Canvas; form.ForeColor = Ink;
    }
    internal static Label Label(string text) => new() { Text = text, AutoSize = true, ForeColor = Ink, UseMnemonic = false, Margin = new Padding(4) };
    sealed class BadgeLabel : Label
    {
        public Color Fill { get; init; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var r = new Rectangle(0,0,Width-1,Height-1);
            if (r.Width < 12 || r.Height < 12) return;
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(0,0,12,12,180,90); path.AddArc(r.Right-12,0,12,12,270,90);
            path.AddArc(r.Right-12,r.Bottom-12,12,12,0,90); path.AddArc(0,r.Bottom-12,12,12,90,90); path.CloseFigure();
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Fill); using var edge = new Pen(ControlPaint.Light(Fill));
            e.Graphics.FillPath(brush,path); e.Graphics.DrawPath(edge,path);
            TextRenderer.DrawText(e.Graphics,Text,Font,r,Ink,TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }
    }
    internal static Label Badge(string text,Color fill) => new BadgeLabel { Text = text, Fill = fill, AutoSize = true,
        Font = new Font("Segoe UI",10,FontStyle.Bold), Padding = new Padding(12,7,12,7), UseMnemonic = false };
    internal static Button Button(string text, Color? fill = null)
    {
        var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = fill ?? Panel,
            ForeColor = Ink, Padding = new Padding(7, 3, 7, 3), UseVisualStyleBackColor = false };
        b.FlatAppearance.BorderColor = Line; return b;
    }
    internal static TextBox Box(string text = "", bool mono = false) => new() {
        Text = text, ReadOnly = true, Multiline = true, WordWrap = !mono, ScrollBars = mono ? ScrollBars.Both : ScrollBars.Vertical,
        BackColor = Panel, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill,
        Font = new Font(mono ? "Consolas" : "Segoe UI", 10), AccessibleName = mono ? "Exact text" : "Operation information" };
    internal static FlowLayoutPanel Bar() => new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
    internal static TableLayoutPanel Stack() => new() { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, Padding = new Padding(12) };
    internal static void Row(this TableLayoutPanel panel, Control item, SizeType type = SizeType.AutoSize, float size = 0)
    {
        int row = panel.RowStyles.Count; panel.RowCount = row + 1; panel.RowStyles.Add(new RowStyle(type, size)); panel.Controls.Add(item, 0, row);
    }
}
