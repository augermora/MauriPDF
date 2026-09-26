namespace MauriPDF.App.Presentation;

/// <summary>Native button input/accessibility with DPI-scaled command composition.</summary>
internal sealed class RibbonCommandButton : Button
{
    private readonly ToolStripItem _command;
    private readonly CommandIcons _icons;
    private readonly CommandIcon _icon;
    private readonly bool _large;
    private bool _selected, _hover, _pressed;
    public bool IsLarge => _large;

    public RibbonCommandButton(ToolStripItem command, string label, CommandIcon icon, bool large,
        MauriPdfTheme theme, CommandIcons icons, ToolTip tooltip)
    {
        _command = command; _icons = icons; _icon = icon; _large = large;
        Text = label;
        AccessibleName = command.AccessibleName ?? command.Text ?? label;
        Font = theme.Body;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Size = new(large ? MauriPdfTheme.LargeCommandWidth : MauriPdfTheme.CompactCommandWidth,
            large ? MauriPdfTheme.CommandHeight * 2 + 2 : MauriPdfTheme.CommandHeight);
        Margin = new Padding(1);
        string hint = command is ToolStripMenuItem menu && !string.IsNullOrEmpty(menu.ShortcutKeyDisplayString)
            ? $"{AccessibleName} ({menu.ShortcutKeyDisplayString})" : AccessibleName;
        tooltip.SetToolTip(this, string.IsNullOrEmpty(command.ToolTipText) ? hint : command.ToolTipText);
        Click += (_, _) => { if (Enabled) _command.PerformClick(); };
        SetIcon();
        RefreshCommand();
    }

    private void SetIcon()
    {
        float scale = DeviceDpi / 96F;
        Image = _icons.Get(_icon, Math.Max(1, (int)((_large ? MauriPdfTheme.LargeIcon : MauriPdfTheme.SmallIcon) * scale)));
        Size = new((int)((_large ? MauriPdfTheme.LargeCommandWidth : MauriPdfTheme.CompactCommandWidth) * scale),
            (int)((_large ? MauriPdfTheme.CommandHeight * 2 + 2 : MauriPdfTheme.CommandHeight) * scale));
        Margin = new Padding(Math.Max(1, (int)scale));
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); SetIcon(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); SetIcon(); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = _pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _pressed = e.Button == MouseButtons.Left; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) { _pressed = true; Invalidate(); } }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); _pressed = false; Invalidate(); }

    public void RefreshCommand()
    {
        bool enabled = _command.Enabled;
        for (ToolStripItem? parent = _command.OwnerItem; parent is not null; parent = parent.OwnerItem) enabled &= parent.Enabled;
        bool selected = _command is ToolStripButton { Checked: true } or ToolStripMenuItem { Checked: true };
        bool changed = Enabled != enabled || _selected != selected;
        Enabled = enabled; _selected = selected;
        AccessibleDescription = selected ? "Selected" : null;
        if (changed) Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float scale = DeviceDpi / 96F;
        bool primary = _large && _icon is CommandIcon.Open or CommandIcon.Print;
        Color background = !Enabled ? MauriPdfTheme.Group : _pressed ? MauriPdfTheme.Pressed
            : _selected ? MauriPdfTheme.Selected : _hover ? MauriPdfTheme.Hover : primary ? MauriPdfTheme.Selected : MauriPdfTheme.Group;
        e.Graphics.Clear(background);
        if (_selected || _hover || primary && Enabled)
        {
            using Pen edge = new(_selected ? MauriPdfTheme.Accent : MauriPdfTheme.Border);
            e.Graphics.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
        }
        if (_selected)
        {
            using SolidBrush marker = new(MauriPdfTheme.Accent);
            e.Graphics.FillRectangle(marker, 0, 0, 3 * scale, Height);
        }
        int gap = (int)(7 * scale);
        Size icon = Image?.Size ?? Size.Empty;
        Point origin = _large ? new((Width - icon.Width) / 2, (int)(4 * scale)) : new(gap, (Height - icon.Height) / 2);
        if (Image is not null)
        {
            if (Enabled) e.Graphics.DrawImageUnscaled(Image, origin);
            else ControlPaint.DrawImageDisabled(e.Graphics, Image, origin.X, origin.Y, background);
        }
        Rectangle text = _large ? new(2, origin.Y + icon.Height + 1, Width - 4, Height - origin.Y - icon.Height - 2)
            : new(origin.X + icon.Width + gap, 0, Math.Max(1, Width - origin.X - icon.Width - gap - 3), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, text, Enabled ? MauriPdfTheme.Ink : MauriPdfTheme.Disabled,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
            | (_large ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left));
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3));
    }
}

internal sealed class RibbonGroup : Panel
{
    private readonly Label _caption;
    public FlowLayoutPanel Commands { get; } = new() { Dock = DockStyle.Fill, WrapContents = true, FlowDirection = FlowDirection.TopDown };
    public RibbonGroup(string caption, MauriPdfTheme theme)
    {
        Size = new(154, MauriPdfTheme.RibbonGroupHeight);
        BackColor = MauriPdfTheme.Group;
        Margin = new Padding(0, 0, 6, 0);
        Padding = new Padding(3, 2, 3, 1);
        Controls.Add(Commands);
        _caption = new Label { Text = caption, Dock = DockStyle.Bottom, Height = 16,
            TextAlign = ContentAlignment.MiddleCenter, BackColor = MauriPdfTheme.GroupCaption,
            ForeColor = MauriPdfTheme.Muted, Font = theme.Caption };
        Controls.Add(_caption);
    }
    public void UpdateMetrics()
    {
        float scale = DeviceDpi / 96F;
        bool large = Commands.Controls.OfType<RibbonCommandButton>().FirstOrDefault()?.IsLarge == true;
        int columns = large ? Commands.Controls.Count : (Commands.Controls.Count + 1) / 2;
        Width = (int)(((large ? MauriPdfTheme.LargeCommandWidth + 2 : MauriPdfTheme.CompactCommandWidth + 2) * columns + 6) * scale);
        Height = (int)(MauriPdfTheme.RibbonGroupHeight * scale);
        _caption.Height = (int)(16 * scale);
        Padding = new Padding((int)(3 * scale), (int)(2 * scale), (int)(3 * scale), 1);
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateMetrics(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); UpdateMetrics(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using Pen pen = new(MauriPdfTheme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

internal sealed class RibbonHost : UserControl
{
    private readonly FlowLayoutPanel _headers = new() { Dock = DockStyle.Top, Height = MauriPdfTheme.TabHeight,
        WrapContents = false, BackColor = MauriPdfTheme.Panel, Padding = new Padding(8, 0, 0, 0) };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, Padding = new Padding(8, 3, 8, 0) };
    private readonly List<FlowLayoutPanel> _pages = [];
    private readonly List<Button> _tabs = [];
    private readonly List<RibbonCommandButton> _buttons = [];
    private readonly ToolTip _tooltip = new();
    private readonly MauriPdfTheme _theme;
    private readonly CommandIcons _icons;
    public int SelectedTab { get; private set; } = -1;

    public RibbonHost(MauriPdfTheme theme, CommandIcons icons)
    {
        _theme = theme; _icons = icons;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        Dock = DockStyle.Top; Height = MauriPdfTheme.RibbonHeight;
        Font = theme.Body; BackColor = MauriPdfTheme.Panel;
        Controls.Add(_content); Controls.Add(_headers);
    }

    public void SelectTab(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _pages.Count);
        if (SelectedTab == index) return;
        SelectedTab = index;
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].Visible = i == index;
            _tabs[i].BackColor = i == index ? MauriPdfTheme.Selected : MauriPdfTheme.Panel;
            _tabs[i].ForeColor = i == index ? MauriPdfTheme.Accent : MauriPdfTheme.Muted;
            _tabs[i].Font = i == index ? _theme.Heading : _theme.Body;
            _tabs[i].AccessibleDescription = i == index ? "Selected tab" : null;
            _tabs[i].Invalidate();
        }
    }

    public FlowLayoutPanel AddTab(string name)
    {
        int index = _pages.Count;
        Button header = new() { Text = name, AccessibleName = $"{name} commands", AccessibleRole = AccessibleRole.PageTab,
            FlatStyle = FlatStyle.Flat, Size = new(66, MauriPdfTheme.TabHeight - 2), Margin = new Padding(0, 0, 2, 0) };
        header.FlatAppearance.BorderSize = 0;
        header.FlatAppearance.MouseOverBackColor = MauriPdfTheme.Hover;
        header.PreviewKeyDown += (_, e) => e.IsInputKey = e.KeyCode is Keys.Left or Keys.Right or Keys.Home or Keys.End;
        header.Click += (_, _) => SelectTab(index);
        header.Paint += (_, e) =>
        {
            if (SelectedTab != index) return;
            using Pen pen = new(MauriPdfTheme.Accent, Math.Max(2, DeviceDpi / 48F));
            e.Graphics.DrawLine(pen, 8, header.Height - 2, header.Width - 8, header.Height - 2);
        };
        header.KeyDown += (_, e) =>
        {
            if (e.KeyCode is not (Keys.Left or Keys.Right or Keys.Home or Keys.End)) return;
            e.SuppressKeyPress = true;
            int next = e.KeyCode == Keys.Home ? 0 : e.KeyCode == Keys.End ? _pages.Count - 1
                : (index + (e.KeyCode == Keys.Left ? -1 : 1) + _pages.Count) % _pages.Count;
            SelectTab(next); _tabs[next].Focus();
        };
        _tabs.Add(header); _headers.Controls.Add(header);
        FlowLayoutPanel row = new() { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true,
            Visible = false, BackColor = MauriPdfTheme.Panel };
        _pages.Add(row); _content.Controls.Add(row);
        if (SelectedTab < 0) SelectTab(0);
        return row;
    }

    public RibbonGroup AddGroup(FlowLayoutPanel tab, string caption)
    {
        RibbonGroup group = new(caption, _theme);
        tab.Controls.Add(group); return group;
    }
    public void Add(RibbonGroup group, ToolStripItem command, string label, CommandIcon icon, bool large = false)
    {
        RibbonCommandButton button = new(command, label, icon, large, _theme, _icons, _tooltip);
        group.Commands.Controls.Add(button);
        group.UpdateMetrics();
        _buttons.Add(button);
    }
    public void RefreshCommands() { foreach (RibbonCommandButton button in _buttons) button.RefreshCommand(); }
    private void UpdateMetrics()
    {
        float scale = DeviceDpi / 96F;
        Height = (int)(MauriPdfTheme.RibbonHeight * scale);
        _headers.Height = (int)(MauriPdfTheme.TabHeight * scale);
        foreach (Button header in _tabs) header.Size = new((int)(66 * scale), (int)((MauriPdfTheme.TabHeight - 2) * scale));
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateMetrics(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); UpdateMetrics(); }
    protected override void Dispose(bool disposing) { if (disposing) _tooltip.Dispose(); base.Dispose(disposing); }
}

internal sealed class ShellTabs : TabControl
{
    public ShellTabs() { DrawMode = TabDrawMode.OwnerDrawFixed; Padding = new Point(14, 7); }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        bool selected = e.Index == SelectedIndex;
        using SolidBrush background = new(selected ? MauriPdfTheme.Selected : MauriPdfTheme.Group);
        e.Graphics.FillRectangle(background, e.Bounds);
        TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text, Font, e.Bounds,
            selected ? MauriPdfTheme.Accent : MauriPdfTheme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (selected)
        {
            using Pen pen = new(MauriPdfTheme.Accent, Math.Max(2, DeviceDpi / 48F));
            e.Graphics.DrawLine(pen, e.Bounds.Left + 3, e.Bounds.Bottom - 2, e.Bounds.Right - 3, e.Bounds.Bottom - 2);
        }
        if (Focused && selected) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -3, -3));
    }
}
