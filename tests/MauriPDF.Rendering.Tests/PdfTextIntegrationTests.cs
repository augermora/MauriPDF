using System.Globalization;
using System.Text;
using MauriPDF.Core.Rendering;
using MauriPDF.Core.Text;
using MauriPDF.Core.Viewing;
using Xunit;

namespace MauriPDF.Rendering.Tests;

[Collection("PDFium native")]
public sealed class PdfTextIntegrationTests
{
    [Fact]
    public void NativeTextExtractionPreservesUnicodeAndLineBreaks()
    {
        WithPdf("BT /F1 18 Tf 20 140 Td 24 TL <414243> Tj T* <44> Tj ET", "", session =>
        {
            PdfTextPage page = session.ExtractText(0);
            StringBuilder text = new();
            page.AppendText(text, 0, page.Count);
            Assert.Equal("Aé😀\r\nΩ", text.ToString());
            var match = Assert.Single(new Core.Search.SearchablePageText(page).Find(0, "😀\r\nω", 10).Matches);
            StringBuilder found = new();
            page.AppendText(found, match.Start, match.End);
            Assert.Equal("😀\r\nΩ", found.ToString());
            Assert.True(page[0].Bounds.HasArea);
            Assert.InRange(page[0].Bounds.Left, 0, 1);
            Assert.InRange(page[0].Bounds.Top, 0, 1);
            using RenderedPage rendered = session.RenderPage(0, 200, 200);
            Assert.Equal(200, rendered.Width);
        });
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 90)]
    [InlineData(0, 180)]
    [InlineData(0, 270)]
    [InlineData(90, 0)]
    [InlineData(90, 90)]
    [InlineData(90, 180)]
    [InlineData(90, 270)]
    [InlineData(180, 0)]
    [InlineData(180, 90)]
    [InlineData(180, 180)]
    [InlineData(180, 270)]
    [InlineData(270, 0)]
    [InlineData(270, 90)]
    [InlineData(270, 180)]
    [InlineData(270, 270)]
    public void TextBoundsAlignWithRenderedGlyphForCropAndRotation(int rotation, int visualDegrees)
    {
        WithPdf("BT /F1 24 Tf 65 110 Td <41> Tj ET", $"/CropBox [40 40 180 160] /Rotate {rotation}", session =>
        {
            VisualRotation visual = new(visualDegrees);
            PdfPageSize intrinsicSize = session.GetPageSize(0);
            Assert.Equal(rotation % 180 == 0 ? new PdfPageSize(140, 120) : new(120, 140), intrinsicSize);
            PdfPageSize effective = visual.EffectiveSize(intrinsicSize);
            int width = (int)effective.WidthPoints * 4, height = (int)effective.HeightPoints * 4;
            PdfTextPage text = session.ExtractText(0);
            TextBounds bounds = text[0].Bounds;
            Assert.True(bounds.HasArea);
            using RenderedPage render = session.RenderPage(0, width, height, visual);
            int minX = width, minY = height, maxX = 0, maxY = 0;
            ReadOnlySpan<byte> pixels = render.Pixels.Span;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (pixels[y * render.Stride + x * 4] < 128)
                    {
                        minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                    }
            Assert.True(minX < maxX && minY < maxY);
            TextBounds displayed = TextCoordinateTransform.ToDisplay(bounds, 0, 0, width, height, visual);
            Assert.InRange(Math.Abs(displayed.Left - minX), 0, 3);
            Assert.InRange(Math.Abs(displayed.Top - minY), 0, 3);
            Assert.InRange(Math.Abs(displayed.Right - maxX), 0, 3);
            Assert.InRange(Math.Abs(displayed.Bottom - maxY), 0, 3);
        });
    }

    [Fact]
    public void ImageOnlyPageHasNoTextButRendersNormally()
    {
        WithPdf("q 100 0 0 100 20 20 cm BI /W 1 /H 1 /BPC 8 /CS /RGB /F /AHx ID FF0000> EI Q", "", session =>
        {
            Assert.Equal(0, session.ExtractText(0).Count);
            using RenderedPage rendered = session.RenderPage(0, 200, 200);
            Assert.Equal(200, rendered.Width);
        });
    }

    [Fact]
    public void TextLimitFailureDoesNotPreventRenderingOrNativeCleanup()
    {
        string lines = string.Join(" T* ", Enumerable.Repeat($"({new string('A', 200)}) Tj", 200));
        WithPdf($"BT /F1 0.75 Tf 10 190 Td 0.9 TL {lines} ET", "", session =>
        {
            Assert.Throws<InvalidDataException>(() => session.ExtractText(0));
            using RenderedPage render = session.RenderPage(0, 96, 96);
            Assert.Equal(96, render.Width);
        });
    }

    private static void WithPdf(string content, string pageOptions, Action<IPdfRenderSession> test)
    {
        string path = Path.Combine(Path.GetTempPath(), $"mauripdf-text-{Guid.NewGuid():N}.pdf");
        try
        {
            const string cmap = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def /CMapName /Test def /CMapType 2 def 1 begincodespacerange <00> <FF> endcodespacerange 4 beginbfchar <41> <0041> <42> <00E9> <43> <D83DDE00> <44> <03A9> endbfchar endcmap CMapName currentdict /CMap defineresource pop end end";
            string[] objects =
            [
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] {pageOptions} /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
                $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding /ToUnicode 6 0 R >>",
                $"<< /Length {cmap.Length} >>\nstream\n{cmap}\nendstream"
            ];
            using MemoryStream stream = new();
            void Write(string value) => stream.Write(Encoding.ASCII.GetBytes(value));
            Write("%PDF-1.4\n");
            List<long> offsets = [];
            for (int index = 0; index < objects.Length; index++)
            {
                offsets.Add(stream.Position);
                Write($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
            }
            long xref = stream.Position;
            Write($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
            foreach (long offset in offsets) Write($"{offset.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
            Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            byte[] original = stream.ToArray();
            File.WriteAllBytes(path, original);
            using (PdfiumRenderer renderer = new())
            {
                using IPdfRenderSession session = renderer.Open(path);
                test(session);
            }
            Assert.Equal(original, File.ReadAllBytes(path)); // Presentation never changes source bytes.
        }
        finally { File.Delete(path); }
    }
}
