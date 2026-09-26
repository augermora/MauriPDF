using MauriPDF.App.Presentation;

namespace MauriPDF.App;

internal sealed partial class MainForm
{
    private bool _presentationRefreshQueued;
    private double _sidebarLogicalWidth = MauriPdfTheme.SidebarWidth;
    private bool _scalingShell;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_ribbon is null) return;
        UpdateShellMetrics();
        QueuePresentationRefresh();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        double sidebarLogicalWidth = _split.SplitterDistance * 96.0 / e.DeviceDpiOld;
        _scalingShell = true;
        try { base.OnDpiChanged(e); }
        finally { _scalingShell = false; }
        BeginInvoke(() =>
        {
            if (_resourcesDisposed) return;
            _sidebarLogicalWidth = sidebarLogicalWidth;
            UpdateShellMetrics();
            QueuePresentationRefresh();
        });
    }

    private void UpdateShellMetrics()
    {
        _scalingShell = true;
        try
        {
            Rectangle working = Screen.FromControl(this).WorkingArea;
            MinimumSize = new(Math.Min(DisplayMetrics.Scale(800, DeviceDpi), working.Width),
                Math.Min(DisplayMetrics.Scale(560, DeviceDpi), working.Height));
            _split.Panel1MinSize = DisplayMetrics.Scale(170, DeviceDpi);
            _split.Panel2MinSize = DisplayMetrics.Scale(200, DeviceDpi);
            _split.SplitterWidth = DisplayMetrics.Scale(4, DeviceDpi);
            int max = Math.Max(_split.Panel1MinSize, _split.Width - _split.Panel2MinSize - _split.SplitterWidth);
            _split.SplitterDistance = Math.Clamp(DisplayMetrics.Scale((int)Math.Round(_sidebarLogicalWidth), DeviceDpi), _split.Panel1MinSize, max);
        }
        finally { _scalingShell = false; }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        // Form-owned message hook avoids a static SystemEvents subscription retaining closed windows.
        if (m.Msg is 0x0015 or 0x001A or 0x031A) QueuePresentationRefresh();
    }

    private void QueuePresentationRefresh()
    {
        if (_presentationRefreshQueued || _resourcesDisposed || !IsHandleCreated || _ribbon is null) return;
        _presentationRefreshQueued = true;
        BeginInvoke(() =>
        {
            _presentationRefreshQueued = false;
            if (_resourcesDisposed) return;
            ApplyShellColors();
            HashSet<Image> borrowed = [];
            CollectImages(this, borrowed);
            _icons.Retain(borrowed); // Rebind ALL controls, including hidden tabs, before freeing old variants.
        });
    }

    private void ApplyShellColors()
    {
        static void Apply(Control control)
        {
            control.BackColor = MauriPdfTheme.Panel;
            control.ForeColor = MauriPdfTheme.Ink;
            foreach (Control child in control.Controls) Apply(child);
        }
        Apply(this);
        BackColor = MauriPdfTheme.Background;
        _split.BackColor = MauriPdfTheme.Border;
        _viewport.BackColor = MauriPdfTheme.Workspace;
        _brand.BackColor = MauriPdfTheme.Ink; _brand.ForeColor = MauriPdfTheme.Panel;
        _navigationHeading.BackColor = _split.Panel1.BackColor = MauriPdfTheme.Group;
        _ribbon?.ApplyTheme();
        _welcome?.ApplyTheme();
        _statusBar?.ApplyTheme();
        Invalidate(true);
    }

    private static void CollectImages(Control control, ISet<Image> images)
    {
        if (control is ButtonBase { Image: not null } button) images.Add(button.Image);
        if (control is PictureBox { Image: not null } picture) images.Add(picture.Image);
        foreach (Control child in control.Controls) CollectImages(child, images);
    }
}
