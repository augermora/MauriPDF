namespace MauriPDF.App.Presentation;

/// <summary>Logical (96 DPI) shell tokens. Fonts belong to the shell, never individual buttons.</summary>
internal sealed class MauriPdfTheme : IDisposable
{
    public static readonly Color Ink = Color.FromArgb(32, 43, 60);
    public static readonly Color Accent = Color.FromArgb(52, 120, 201);
    public static readonly Color Background = Color.FromArgb(244, 246, 249);
    public static readonly Color Panel = Color.White;
    public static readonly Color Group = Color.FromArgb(247, 249, 252);
    public static readonly Color GroupCaption = Color.FromArgb(236, 241, 247);
    public static readonly Color Pressed = Color.FromArgb(210, 226, 246);
    public static readonly Color Status = Color.FromArgb(237, 242, 248);
    public static readonly Color Border = Color.FromArgb(216, 224, 234);
    public static readonly Color Muted = Color.FromArgb(100, 116, 139);
    public static readonly Color Selected = Color.FromArgb(225, 237, 252);
    public static readonly Color Hover = Color.FromArgb(237, 243, 251);
    public static readonly Color Disabled = Color.FromArgb(151, 162, 178);
    public static readonly Color Workspace = Color.FromArgb(226, 231, 238);
    public const int Space = 8;
    public const int SmallIcon = 22;
    public const int LargeIcon = 32;
    public const int CommandHeight = 29;
    public const int LargeCommandWidth = 78;
    public const int CompactCommandWidth = 144;
    public const int RibbonHeight = 132;
    public const int RibbonGroupHeight = 82;
    public const int TabHeight = 30;
    public const int HeaderHeight = 30;
    public const int SidebarWidth = 212;
    public const int StatusHeight = 32;

    public Font Body { get; } = new("Segoe UI", 9F);
    public Font Caption { get; } = new("Segoe UI", 8.25F);
    public Font Heading { get; } = new("Segoe UI", 9F, FontStyle.Bold);
    public Font Title { get; } = new("Segoe UI", 12F, FontStyle.Bold);
    public Font WelcomeTitle { get; } = new("Segoe UI", 20F, FontStyle.Bold);

    public void Dispose()
    {
        Body.Dispose();
        Caption.Dispose();
        Heading.Dispose();
        Title.Dispose();
        WelcomeTitle.Dispose();
    }
}

internal sealed class ShellStripRenderer : ToolStripProfessionalRenderer
{
    public ShellStripRenderer() : base(new ShellColorTable()) { RoundedEdges = false; }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using Pen pen = new(MauriPdfTheme.Border);
        e.Graphics.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
    }

    private sealed class ShellColorTable : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => MauriPdfTheme.Panel;
        public override Color ToolStripGradientMiddle => MauriPdfTheme.Panel;
        public override Color ToolStripGradientEnd => MauriPdfTheme.Panel;
        public override Color ButtonSelectedHighlight => MauriPdfTheme.Hover;
        public override Color ButtonSelectedBorder => MauriPdfTheme.Accent;
        public override Color ButtonPressedHighlight => MauriPdfTheme.Selected;
        public override Color SeparatorDark => MauriPdfTheme.Border;
        public override Color SeparatorLight => MauriPdfTheme.Panel;
    }
}
