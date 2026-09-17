using MauriPDF.Core.Outline;

namespace MauriPDF.App;

/// <summary>UI-only mapping of a bounded immutable outline; no native handles or page images.</summary>
internal sealed class OutlineView : UserControl
{
    private readonly TreeView _tree = new()
    {
        Dock = DockStyle.Fill, HideSelection = false, ShowNodeToolTips = true, AccessibleName = "Document bookmarks"
    };
    private readonly Label _status = new() { Dock = DockStyle.Top, AutoSize = true, Text = "No bookmarks" };

    public OutlineView()
    {
        Dock = DockStyle.Fill;
        Controls.Add(_tree);
        Controls.Add(_status);
        _tree.NodeMouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && (_tree.HitTest(e.Location).Location & TreeViewHitTestLocations.Label) != 0)
                ActivateNode(e.Node);
        };
        _tree.KeyDown += (_, e) =>
        {
            if (e.KeyData != Keys.Enter) return;
            e.SuppressKeyPress = true;
            ActivateNode(_tree.SelectedNode);
        };
    }

    public event Action<int>? PageRequested;

    public void Clear(string message = "No bookmarks")
    {
        _tree.Nodes.Clear(); // Detached nodes can no longer activate, even through a queued event.
        _status.Text = message;
        _status.Visible = true;
    }

    public void SetOutline(PdfOutline outline)
    {
        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();
            foreach (PdfOutlineNode root in outline.Roots) _tree.Nodes.Add(CreateNode(root));
            _status.Text = outline.WasLimited ? "Some bookmarks were limited" : "No bookmarks";
            _status.Visible = outline.WasLimited || outline.Roots.Count == 0;
        }
        finally { _tree.EndUpdate(); }
    }

    private static TreeNode CreateNode(PdfOutlineNode node)
    {
        TreeNode result = new(node.Title)
        {
            Tag = node,
            ToolTipText = node.PageIndex is int page ? $"Source page {page + 1}" : "No supported local destination"
        };
        foreach (PdfOutlineNode child in node.Children) result.Nodes.Add(CreateNode(child));
        return result;
    }

    private void ActivateNode(TreeNode? node)
    {
        if (node?.TreeView != _tree || node.Tag is not PdfOutlineNode { PageIndex: int page }) return;
        _tree.SelectedNode = node;
        PageRequested?.Invoke(page);
    }
}
