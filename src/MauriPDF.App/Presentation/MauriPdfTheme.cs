namespace MauriPDF.App.Presentation;

/// <summary>Logical (96 DPI) shell tokens. Fonts belong to the shell, never individual buttons.</summary>
internal sealed class MauriPdfTheme : IDisposable
{
    public static Color Ink => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(32, 43, 60);
    public static Color Accent => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(52, 120, 201);
    public static Color Background => SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(244, 246, 249);
    public static Color Panel => SystemInformation.HighContrast ? SystemColors.Window : Color.White;
    public static Color Group => SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(247, 249, 252);
    public static Color GroupCaption => SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(236, 241, 247);
    public static Color Pressed => SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(210, 226, 246);
    public static Color Status => SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(237, 242, 248);
    public static Color Border => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(216, 224, 234);
    public static Color Muted => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(100, 116, 139);
    public static Color Selected => SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(225, 237, 252);
    public static Color Hover => SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(237, 243, 251);
    public static Color Disabled => SystemInformation.HighContrast ? SystemColors.GrayText : Color.FromArgb(151, 162, 178);
    public static Color Workspace => SystemInformation.HighContrast ? SystemColors.AppWorkspace : Color.FromArgb(226, 231, 238);
    public const int Space = 8;
    public static Color SelectedText => SelectionColors(SystemInformation.HighContrast).Foreground;
    internal static (Color Background, Color Foreground) SelectionColors(bool highContrast) => highContrast
        ? (SystemColors.Highlight, SystemColors.HighlightText) : (Color.FromArgb(225, 237, 252), Color.FromArgb(32, 43, 60));
    public static Color ActionText => SystemInformation.HighContrast ? SystemColors.HighlightText : Color.White;
    public static Color ActionBackground => SystemInformation.HighContrast ? SystemColors.Highlight : Accent;
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
