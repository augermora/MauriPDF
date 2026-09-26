using System.Runtime.ExceptionServices;
using MauriPDF.App.Presentation;
using Xunit;

namespace MauriPDF.App.Tests;

public sealed class ShellPresentationTests
{
    private static readonly string[] TabNames = ["File", "Home", "Pages", "View", "Tools"];
    [Fact]
    public void MultiplePresentationsInvokeOneCommandAndRespectAvailability() => InSta(() =>
    {
        using MauriPdfTheme theme = new();
        using CommandIcons icons = new();
        using ToolTip tooltip = new();
        using ToolStripButton command = new("Save");
        int calls = 0;
        command.Click += (_, _) => calls++;
        using RibbonCommandButton first = new(command, "Save", CommandIcon.Save, true, theme, icons, tooltip);
        using RibbonCommandButton second = new(command, "Save", CommandIcon.Save, false, theme, icons, tooltip);
        first.PerformClick();
        second.PerformClick();
        Assert.Equal(2, calls);
        command.Enabled = false; // No document, or persistence in progress.
        first.RefreshCommand(); second.RefreshCommand();
        first.PerformClick(); second.PerformClick();
        Assert.False(first.Enabled);
        Assert.False(second.Enabled);
        Assert.Equal(2, calls);
    });

    [Fact]
    public void ParentAvailabilityAndSelectionAreReflected() => InSta(() =>
    {
        using MauriPdfTheme theme = new();
        using CommandIcons icons = new();
        using ToolTip tooltip = new();
        using ToolStripDropDownButton parent = new("Pages");
        ToolStripMenuItem source = new("Rotate") { Checked = true };
        parent.DropDownItems.Add(source);
        using RibbonCommandButton button = new(source, "Rotate", CommandIcon.RotateRight, false, theme, icons, tooltip);
        Assert.Equal("Selected", button.AccessibleDescription);
        parent.Enabled = false;
        button.RefreshCommand();
        Assert.False(button.Enabled);
        parent.Enabled = true;
        source.Checked = false;
        button.RefreshCommand();
        Assert.True(button.Enabled);
        Assert.Null(button.AccessibleDescription);
    });

    [Fact]
    public void TabSwitchingNeverExecutesCommands() => InSta(() =>
    {
        using MauriPdfTheme theme = new();
        using CommandIcons icons = new();
        using ToolStripButton command = new("Open");
        int calls = 0;
        command.Click += (_, _) => calls++;
        using RibbonHost ribbon = new(theme, icons);
        foreach (string name in TabNames)
            ribbon.Add(ribbon.AddGroup(ribbon.AddTab(name), "Commands"), command, "Open", CommandIcon.Open, true);
        for (int index = 0; index < 5; index++) { ribbon.SelectTab(index); Assert.Equal(index, ribbon.SelectedTab); }
        Assert.Equal(0, calls);
    });

    [Fact]
    public void IconsAreCachedPerDpiSizeAndBorrowedByButtons() => InSta(() =>
    {
        using MauriPdfTheme theme = new();
        using CommandIcons icons = new();
        using ToolTip tooltip = new();
        using ToolStripButton source = new("Open");
        Bitmap borrowed = icons.Get(CommandIcon.Open, MauriPdfTheme.SmallIcon);
        using (RibbonCommandButton button = new(source, "Open", CommandIcon.Open, false, theme, icons, tooltip))
            Assert.Same(borrowed, button.Image);
        Assert.Equal(MauriPdfTheme.SmallIcon, borrowed.Width); // Button disposal must not dispose shared images.
        Assert.Same(borrowed, icons.Get(CommandIcon.Open, MauriPdfTheme.SmallIcon));
        Assert.NotSame(borrowed, icons.Get(CommandIcon.Open, 30));
        foreach (CommandIcon icon in Enum.GetValues<CommandIcon>())
            Assert.Equal(42, icons.Get(icon, 42).Height);
    });

    private static void InSta(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
