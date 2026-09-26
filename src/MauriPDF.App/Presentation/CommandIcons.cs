using System.Drawing.Drawing2D;

namespace MauriPDF.App.Presentation;

internal enum CommandIcon
{
    Open, Save, SaveAs, Undo, Redo, Search, Copy, Delete, Earlier, Later,
    RotateLeft, RotateRight, ZoomIn, ZoomOut, FitWidth, FitPage, Continuous,
    SinglePage, Thumbnails, Outline, Sidebar, Print
}

/// <summary>Original MauriPDF line icons on a 24-unit vector grid. No external assets or fonts.</summary>
internal sealed class CommandIcons : IDisposable
{
    private readonly Dictionary<(CommandIcon Icon, int Size), Bitmap> _cache = [];

    public Bitmap Get(CommandIcon icon, int size)
    {
        if (_cache.TryGetValue((icon, size), out Bitmap? image)) return image;
        image = new Bitmap(size, size);
        using Graphics g = Graphics.FromImage(image);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(size / 24F, size / 24F);
        using Pen pen = new(MauriPdfTheme.Ink, 2F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        void Line(float x, float y, float x2, float y2) => g.DrawLine(pen, x, y, x2, y2);
        void Rect(float x, float y, float w, float h) => g.DrawRectangle(pen, x, y, w, h);
        void Arrow(bool right)
        {
            float x = right ? 18 : 6;
            Line(x, 5, x, 10); Line(x, 10, right ? 13 : 11, 10);
            g.DrawArc(pen, 5, 6, 14, 14, right ? 210 : 310, right ? -260 : 260);
        }
        switch (icon)
        {
            case CommandIcon.Print:
                Rect(6, 2, 12, 6); Rect(3, 8, 18, 10); Rect(6, 14, 12, 8); Line(17, 11, 18, 11); break;
            case CommandIcon.Open:
                g.DrawPolygon(pen, [new(3, 7), new(9, 7), new(11, 10), new(21, 10), new(18, 20), new(3, 20)]);
                Line(3, 7, 3, 4); Line(3, 4, 10, 4); Line(10, 4, 13, 7); break;
            case CommandIcon.Save:
            case CommandIcon.SaveAs:
                g.DrawPolygon(pen, [new(4, 3), new(17, 3), new(21, 7), new(21, 21), new(4, 21)]);
                Rect(8, 3, 8, 6); Rect(8, 14, 9, 7);
                if (icon == CommandIcon.SaveAs) { Line(17, 12, 22, 12); Line(19.5F, 9.5F, 19.5F, 14.5F); } break;
            case CommandIcon.Undo:
            case CommandIcon.Redo:
                bool redo = icon == CommandIcon.Redo;
                Line(redo ? 19 : 5, 5, redo ? 19 : 5, 11); Line(redo ? 19 : 5, 11, redo ? 13 : 11, 11);
                g.DrawArc(pen, 5, 8, 14, 12, redo ? 205 : -25, redo ? -240 : 240); break;
            case CommandIcon.Search:
            case CommandIcon.ZoomIn:
            case CommandIcon.ZoomOut:
                g.DrawEllipse(pen, 3, 3, 13, 13); Line(14, 14, 21, 21);
                if (icon != CommandIcon.Search) Line(6, 9.5F, 13, 9.5F);
                if (icon == CommandIcon.ZoomIn) Line(9.5F, 6, 9.5F, 13); break;
            case CommandIcon.Copy: Rect(8, 7, 12, 14); Line(4, 17, 4, 3); Line(4, 3, 15, 3); break;
            case CommandIcon.Delete:
                Line(4, 6, 20, 6); Rect(9, 3, 6, 3); Rect(6, 6, 12, 15); Line(10, 10, 10, 17); Line(14, 10, 14, 17); break;
            case CommandIcon.Earlier:
            case CommandIcon.Later:
                bool down = icon == CommandIcon.Later;
                Line(12, 4, 12, 20); Line(12, down ? 20 : 4, 6, down ? 14 : 10); Line(12, down ? 20 : 4, 18, down ? 14 : 10); break;
            case CommandIcon.RotateLeft: Arrow(false); break;
            case CommandIcon.RotateRight: Arrow(true); break;
            case CommandIcon.FitWidth:
                Line(3, 4, 3, 20); Line(21, 4, 21, 20); Line(6, 12, 18, 12);
                Line(6, 12, 9, 9); Line(6, 12, 9, 15); Line(18, 12, 15, 9); Line(18, 12, 15, 15); break;
            case CommandIcon.FitPage:
                Rect(7, 5, 10, 14); Line(2, 7, 2, 2); Line(2, 2, 7, 2); Line(22, 17, 22, 22); Line(22, 22, 17, 22); break;
            case CommandIcon.Continuous: Rect(6, 2, 12, 8); Rect(6, 14, 12, 8); break;
            case CommandIcon.SinglePage: Rect(5, 3, 14, 18); Line(9, 9, 15, 9); Line(9, 13, 15, 13); break;
            case CommandIcon.Thumbnails: Rect(3, 3, 7, 8); Rect(14, 3, 7, 8); Rect(3, 15, 7, 6); Rect(14, 15, 7, 6); break;
            case CommandIcon.Outline:
                Line(4, 4, 4, 18); Line(4, 6, 9, 6); Line(4, 12, 9, 12); Line(4, 18, 9, 18);
                Line(12, 6, 21, 6); Line(12, 12, 19, 12); Line(12, 18, 21, 18); break;
            case CommandIcon.Sidebar: Rect(3, 4, 18, 16); Line(9, 4, 9, 20); break;
        }
        _cache.Add((icon, size), image);
        return image;
    }

    public void Dispose()
    {
        foreach (Bitmap image in _cache.Values) image.Dispose();
        _cache.Clear();
    }
}
