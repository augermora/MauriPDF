using System.Globalization;
using System.Text;
using MauriPDF.Core.Rendering;
using MauriPDF.Rendering;
using Xunit;

namespace MauriPDF.Rendering.Tests;

public sealed class AssemblyTests
{
    [Fact]
    public void RenderingAssemblyHasExpectedName()
    {
        Assert.Equal("MauriPDF.Rendering", typeof(Rendering.AssemblyMarker).Assembly.GetName().Name);
    }

    [Fact]
    public void OpenRejectsMissingFile()
    {
        using PdfiumRenderer renderer = new();

        Assert.Throws<FileNotFoundException>(() => renderer.Open(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf")));
    }

    [Fact]
    public void OpenRejectsInvalidPdfWithoutCrashing()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mauripdf-{Guid.NewGuid():N}.pdf");

        try
        {
            File.WriteAllText(path, "This is not a PDF.");
            using PdfiumRenderer renderer = new();

            Assert.Throws<InvalidDataException>(() => renderer.Open(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OpensAndRendersOnePagePdf()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mauripdf-{Guid.NewGuid():N}.pdf");

        try
        {
            File.WriteAllBytes(path, CreateOnePagePdf());
            using PdfiumRenderer renderer = new();
            using IPdfRenderSession session = renderer.Open(path);

            Assert.Equal(1, session.PageCount);
            Assert.Equal(new PdfPageSize(72, 72), session.GetPageSize(0));

            using RenderedPage renderedPage = session.RenderPage(0, 96, 96);
            Assert.Equal(96, renderedPage.Width);
            Assert.Equal(96, renderedPage.Height);
            Assert.Equal(96 * 4, renderedPage.Stride);
            Assert.Equal(96 * 96 * 4, renderedPage.Pixels.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] CreateOnePagePdf()
    {
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 72 72] /Resources << >> /Contents 4 0 R >>",
            "<< /Length 0 >>\nstream\n\nendstream"
        ];

        using MemoryStream stream = new();
        WriteAscii(stream, "%PDF-1.4\n");
        List<long> offsets = [];

        for (int index = 0; index < objects.Length; index++)
        {
            offsets.Add(stream.Position);
            WriteAscii(stream, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        long xrefOffset = stream.Position;
        WriteAscii(stream, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");

        foreach (long offset in offsets)
        {
            WriteAscii(stream, $"{offset.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        }

        WriteAscii(
            stream,
            $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");

        return stream.ToArray();
    }

    private static void WriteAscii(Stream stream, string value)
    {
        stream.Write(Encoding.ASCII.GetBytes(value));
    }
}
