using System.Globalization;
using System.Text;
using MauriPDF.Core.Outline;
using MauriPDF.Core.Rendering;
using Xunit;

namespace MauriPDF.Rendering.Tests;

public sealed class PdfOutlineIntegrationTests
{
    [Fact]
    public void NativeHierarchyUnicodeAndLocalDestinationsSurviveSessionDisposal()
    {
        PdfOutline? snapshot = null;
        WithPdf([
            $"<< /Title {Title("Introducción 日本語 😀")} /Parent 5 0 R /Next 9 0 R /First 7 0 R /Last 8 0 R /Count 2 /Dest [3 0 R /Fit] >>",
            "<< /Title (Section 1) /Parent 6 0 R /Next 8 0 R /A << /S /GoTo /D [4 0 R /XYZ 10 20 2] >> >>",
            "<< /Title (Section 2) /Parent 6 0 R /Prev 7 0 R /Dest (named) >>",
            "<< /Title (Appendix) /Parent 5 0 R /Prev 6 0 R >>"
        ], session => snapshot = session.ExtractOutline());
        Assert.NotNull(snapshot);
        Assert.False(snapshot.WasLimited);
        Assert.Equal(["Introducción 日本語 😀", "Appendix"], snapshot.Roots.Select(node => node.Title));
        Assert.Equal(0, snapshot.Roots[0].PageIndex);
        Assert.Equal(["Section 1", "Section 2"], snapshot.Roots[0].Children.Select(node => node.Title));
        Assert.All(snapshot.Roots[0].Children, child => Assert.Equal(1, child.PageIndex));
        Assert.Null(snapshot.Roots[1].PageIndex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/Dest (missing)")]
    [InlineData("/Dest [99 /Fit]")]
    [InlineData("/A << /S /URI /URI (https://example.invalid) >>")]
    [InlineData("/A << /S /GoToR /F (other.pdf) /D [0 /Fit] >>")]
    [InlineData("/A << /S /Launch /F (program.exe) >>")]
    [InlineData("/A << /S /JavaScript /JS (app.alert) >>")]
    [InlineData("/A << /S /GoToE /D [0 /Fit] >>")]
    [InlineData("/A << /S /GoTo /D (missing) >>")]
    [InlineData("/A << /S /URI /URI (https://example.invalid) >> /Dest [3 0 R /Fit]")]
    public void UnsupportedOrMissingDestinationsRemainVisibleAndInert(string destination)
    {
        WithPdf([$"<< /Title (Visible) /Parent 5 0 R {destination} >>"], session =>
        {
            PdfOutlineNode node = Assert.Single(session.ExtractOutline().Roots);
            Assert.Equal("Visible", node.Title);
            Assert.Null(node.PageIndex);
            using RenderedPage page = session.RenderPage(0, 32, 32);
            Assert.Equal(32, page.Width);
        });
    }

    [Fact]
    public void EmptyOutlineIsNotAnError()
    {
        WithPdf([], session =>
        {
            PdfOutline outline = session.ExtractOutline();
            Assert.Empty(outline.Roots);
            Assert.False(outline.WasLimited);
        });
    }

    [Theory]
    [InlineData(0, "(Untitled bookmark)", false)]
    [InlineData(512, null, false)]
    [InlineData(513, "(Bookmark title too long)", true)]
    public void TitlesHaveBoundedAllocation(int length, string? expected, bool limited)
    {
        string title = new('é', length);
        WithPdf([$"<< /Title {Title(title)} /Parent 5 0 R /Dest [3 0 R /Fit] >>"], session =>
        {
            PdfOutline outline = session.ExtractOutline();
            Assert.Equal(expected ?? title, Assert.Single(outline.Roots).Title);
            Assert.Equal(limited, outline.WasLimited);
            Assert.Equal(0, outline.Roots[0].PageIndex);
        });
    }

    [Theory]
    [InlineData("<FEFFD8000041>", "\uFFFDA")]
    [InlineData("<FEFF004100000042>", "A B")] // Installed PDFium replaces embedded NUL before returning the title.
    public void MalformedUnicodeAndEmbeddedNullTitlesDegradeWithoutHidingSuffix(string encoded, string expected)
    {
        WithPdf([$"<< /Title {encoded} /Parent 5 0 R >>"], session =>
            Assert.Equal(expected, Assert.Single(session.ExtractOutline().Roots).Title));
    }

    [Fact]
    public void DepthLimitSkipsDescendantsButPreservesLaterRoots()
    {
        List<string> bookmarks = [];
        for (int index = 0; index < PdfOutline.MaximumDepth + 2; index++)
            bookmarks.Add($"<< /Title (Level {index}) /Parent {(index == 0 ? 5 : index + 5)} 0 R /First {index + 7} 0 R {(index == 0 ? "/Next 40 0 R" : "")} >>");
        bookmarks.Add("<< /Title (Later root) /Parent 5 0 R >>"); // Object 40.
        WithPdf(bookmarks, session =>
        {
            PdfOutline outline = session.ExtractOutline();
            Assert.True(outline.WasLimited);
            Assert.Equal(2, outline.Roots.Count);
            Assert.Equal("Later root", outline.Roots[1].Title);
            PdfOutlineNode node = outline.Roots[0];
            int depth = 1;
            while (node.Children.Count != 0) { node = Assert.Single(node.Children); depth++; }
            Assert.Equal(PdfOutline.MaximumDepth, depth);
        });
    }

    [Theory]
    [InlineData(2048, false)]
    [InlineData(2049, true)]
    public void TotalNodeLimitRetainsOrderedPrefix(int count, bool limited)
    {
        List<string> bookmarks = [];
        for (int index = 0; index < count; index++)
            bookmarks.Add($"<< /Title (Item {index}) /Parent 5 0 R {(index + 1 < count ? $"/Next {index + 7} 0 R" : "")} >>");
        WithPdf(bookmarks, session =>
        {
            PdfOutline outline = session.ExtractOutline();
            Assert.Equal(PdfOutline.MaximumNodes, outline.Roots.Count);
            Assert.Equal("Item 2047", outline.Roots[^1].Title);
            Assert.Equal(limited, outline.WasLimited);
            using RenderedPage page = session.RenderPage(1, 32, 32);
        });
    }

    [Theory]
    [InlineData("/Next 6 0 R")]
    [InlineData("/First 6 0 R")]
    public void CyclicStructureStopsAtVisitedNativePointer(string cycle)
    {
        WithPdf([
            "<< /Title (First) /Parent 5 0 R /Next 7 0 R >>",
            $"<< /Title (Second) /Parent 5 0 R {cycle} >>"
        ], session =>
        {
            PdfOutline outline = session.ExtractOutline();
            Assert.True(outline.WasLimited);
            Assert.Equal(2, outline.Roots.Count);
            Assert.All(outline.Roots, node => Assert.Empty(node.Children));
        });
    }

    private static string Title(string title) => $"<FEFF{Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(title))}>";

    private static void WithPdf(List<string> bookmarks, Action<IPdfRenderSession> test)
    {
        string path = Path.Combine(Path.GetTempPath(), $"mauripdf-outline-{Guid.NewGuid():N}.pdf");
        try
        {
            List<string> objects =
            [
                "<< /Type /Catalog /Pages 2 0 R /Outlines 5 0 R /Names << /Dests << /Names [(named) [4 0 R /Fit]] >> >> >>",
                "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << >> >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << >> >>",
                bookmarks.Count == 0 ? "<< /Type /Outlines >>" : "<< /Type /Outlines /First 6 0 R >>"
            ];
            objects.AddRange(bookmarks);
            using MemoryStream stream = new();
            void Write(string value) => stream.Write(Encoding.ASCII.GetBytes(value));
            Write("%PDF-1.7\n");
            List<long> offsets = [];
            for (int index = 0; index < objects.Count; index++)
            {
                offsets.Add(stream.Position);
                Write($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
            }
            long xref = stream.Position;
            Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
            foreach (long offset in offsets) Write($"{offset.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
            Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            File.WriteAllBytes(path, stream.ToArray());
            using PdfiumRenderer renderer = new();
            using IPdfRenderSession session = renderer.Open(path);
            test(session);
        }
        finally { File.Delete(path); }
    }
}
