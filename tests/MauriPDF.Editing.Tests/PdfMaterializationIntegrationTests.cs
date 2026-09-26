using System.Globalization;
using System.Text;
using MauriPDF.Core.Documents;
using MauriPDF.Core.Rendering;
using MauriPDF.Core.Text;
using MauriPDF.Rendering;
using Xunit;

namespace MauriPDF.Editing.Tests;

[CollectionDefinition("PDFium editing native", DisableParallelization = true)]
public sealed class PdfiumEditingScope;

[Collection("PDFium editing native")]
public sealed class PdfMaterializationIntegrationTests
{
    [Fact]
    public async Task RealOutputUsesLogicalOrderPreservesTextAndAddsOnlyStructuralRotation()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"mauripdf-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "source.pdf"), output = Path.Combine(directory, "output.pdf");
        try
        {
            byte[] original = CreateSourcePdf();
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            await File.WriteAllBytesAsync(source, original, cancellationToken);
            DocumentEditSession edits = new(3);
            DocumentPageId c = edits.State.Pages[2].Id, b = edits.State.Pages[1].Id, a = edits.State.Pages[0].Id;
            edits.Execute(new MovePageEdit(c, 0));
            edits.Execute(new DeletePageEdit(b));
            edits.Execute(new RotatePageEdit(c, true));
            edits.Execute(new RotatePageEdit(a, true));
            edits.Execute(new RotatePageEdit(a, true));

            PdfMaterializationResult result = await new PdfiumDocumentMaterializer()
                .MaterializeAsync(new(source, output, PdfMaterializationPlan.From(edits.State),
                    PdfDestinationPolicy.RequireMissing), cancellationToken);

            Assert.Equal(2, result.PageCount);
            Assert.True(result.FileLength > 100);
            Assert.Equal(original, await File.ReadAllBytesAsync(source, cancellationToken));
            using PdfiumRenderer renderer = new();
            using IPdfRenderSession session = renderer.Open(output);
            Assert.Equal(2, session.PageCount);
            Assert.Equal("PAGE C", Text(session.ExtractText(0)));
            Assert.Equal("PAGE A", Text(session.ExtractText(1)));
            Assert.Empty(session.ExtractOutline().Roots); // Destination catalog/outline is intentionally not imported.
            // C: intrinsic 270 + structural 90 = 0. A: intrinsic 90 + structural 180 = 270.
            Assert.Equal(new PdfPageSize(240, 140), session.GetPageSize(0));
            Assert.Equal(new PdfPageSize(100, 200), session.GetPageSize(1));
            using RenderedPage rendered = session.RenderPage(0, 240, 140);
            Assert.Equal(240, rendered.Width);
            Assert.True(Enumerable.Range(0, rendered.Height).Any(y => Enumerable.Range(0, rendered.Width).Any(x =>
            {
                int offset = y * rendered.Stride + x * 4;
                return rendered.Pixels.Span[offset + 2] > 200 && rendered.Pixels.Span[offset + 1] < 80
                    && rendered.Pixels.Span[offset] < 80;
            })), "The imported red image must remain renderable.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task FailureLeavesExistingDestinationAndSourceUntouchedAndRemovesTemporaryFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"mauripdf-save-fail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "source.pdf"), output = Path.Combine(directory, "output.pdf");
        try
        {
            byte[] original = CreateSourcePdf(), existing = [1, 2, 3, 4];
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            await File.WriteAllBytesAsync(source, original, cancellationToken);
            await File.WriteAllBytesAsync(output, existing, cancellationToken);
            PdfMaterializationPlan invalid = PdfMaterializationPlan.From(new DocumentEditSession(4).State);
            await Assert.ThrowsAsync<InvalidDataException>(() => new PdfiumDocumentMaterializer().MaterializeAsync(
                new(source, output, invalid, PdfDestinationPolicy.OverwriteApproved), cancellationToken));
            Assert.Equal(existing, await File.ReadAllBytesAsync(output, cancellationToken));
            Assert.Equal(original, await File.ReadAllBytesAsync(source, cancellationToken));
            Assert.Empty(Directory.EnumerateFiles(directory, ".*.tmp"));
            await Assert.ThrowsAsync<IOException>(() => new PdfiumDocumentMaterializer().MaterializeAsync(new(source, source,
                PdfMaterializationPlan.From(new DocumentEditSession(3).State), PdfDestinationPolicy.OverwriteApproved), cancellationToken));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task RepeatedSaveReplacesExpectedTargetDetectsExternalChangesAndKeepsSource()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"mauripdf-resave-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "source.pdf"), output = Path.Combine(directory, "output.pdf");
        try
        {
            byte[] original = CreateSourcePdf();
            await File.WriteAllBytesAsync(source, original, TestContext.Current.CancellationToken);
            PdfiumDocumentMaterializer writer = new();
            DocumentEditSession edits = new(3);
            edits.Execute(new DeletePageEdit(edits.State.Pages[1].Id));
            PdfMaterializationResult first = await writer.MaterializeAsync(new(source, output,
                PdfMaterializationPlan.From(edits.State), PdfDestinationPolicy.RequireMissing), TestContext.Current.CancellationToken);
            edits.MarkSavedBaseline(edits.State.Revision);

            Assert.True(edits.Undo()); // Restores B from the still-open/original source model.
            Assert.True(edits.IsDirty);
            PdfMaterializationResult second = await writer.MaterializeAsync(new(source, output,
                PdfMaterializationPlan.From(edits.State), PdfDestinationPolicy.RequireExpectedIdentity, first.DestinationIdentity),
                TestContext.Current.CancellationToken);
            edits.MarkSavedBaseline(edits.State.Revision);
            Assert.False(edits.IsDirty);
            using (PdfiumRenderer renderer = new())
            using (IPdfRenderSession session = renderer.Open(output))
            {
                Assert.Equal(3, session.PageCount);
                Assert.Equal("PAGE B", Text(session.ExtractText(1)));
            }
            Assert.Equal(original, await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken));

            await File.AppendAllTextAsync(output, "% external", TestContext.Current.CancellationToken);
            byte[] external = await File.ReadAllBytesAsync(output, TestContext.Current.CancellationToken);
            PdfDestinationConflictException conflict = await Assert.ThrowsAsync<PdfDestinationConflictException>(() =>
                writer.MaterializeAsync(new(source, output, PdfMaterializationPlan.From(edits.State),
                    PdfDestinationPolicy.RequireExpectedIdentity, second.DestinationIdentity), TestContext.Current.CancellationToken));
            Assert.Equal(PdfDestinationConflictKind.Changed, conflict.Kind);
            Assert.Equal(external, await File.ReadAllBytesAsync(output, TestContext.Current.CancellationToken));
            Assert.Empty(Directory.EnumerateFiles(directory, ".*.tmp"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void SavedFileIdentityDetectsSameLengthContentChanges()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "AAAA");
            SavedFileIdentity first = SavedFileIdentity.Capture(path);
            File.WriteAllText(path, "BBBB");
            File.SetLastWriteTimeUtc(path, first.LastWriteTimeUtc);
            Assert.NotEqual(first, SavedFileIdentity.Capture(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ExpectedDestinationMustStillExistAndMissingPolicyWillNotOverwrite()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"mauripdf-policy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "source.pdf"), output = Path.Combine(directory, "output.pdf");
        try
        {
            await File.WriteAllBytesAsync(source, CreateSourcePdf(), TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(output, "existing", TestContext.Current.CancellationToken);
            SavedFileIdentity expected = SavedFileIdentity.Capture(output);
            PdfMaterializationPlan plan = PdfMaterializationPlan.From(new DocumentEditSession(3).State);
            File.Delete(output);
            PdfDestinationConflictException missing = await Assert.ThrowsAsync<PdfDestinationConflictException>(() =>
                new PdfiumDocumentMaterializer().MaterializeAsync(new(source, output, plan,
                    PdfDestinationPolicy.RequireExpectedIdentity, expected), TestContext.Current.CancellationToken));
            Assert.Equal(PdfDestinationConflictKind.Missing, missing.Kind);

            await File.WriteAllTextAsync(output, "appeared", TestContext.Current.CancellationToken);
            PdfDestinationConflictException appeared = await Assert.ThrowsAsync<PdfDestinationConflictException>(() =>
                new PdfiumDocumentMaterializer().MaterializeAsync(new(source, output, plan,
                    PdfDestinationPolicy.RequireMissing), TestContext.Current.CancellationToken));
            Assert.Equal(PdfDestinationConflictKind.UnexpectedlyExists, appeared.Kind);
            Assert.Equal("appeared", await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken));
            Assert.Empty(Directory.EnumerateFiles(directory, ".*.tmp"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string Text(PdfTextPage page)
    {
        StringBuilder value = new();
        page.AppendText(value, 0, page.Count);
        return value.ToString();
    }

    private static byte[] CreateSourcePdf()
    {
        string[] labels = ["PAGE A", "PAGE B", "PAGE C"];
        (int Width, int Height, int Rotation)[] geometry = [(200, 100, 90), (160, 160, 0), (240, 140, 270)];
        List<string> objects =
        [
            "<< /Type /Catalog /Pages 2 0 R /Outlines 10 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 5 0 R 7 0 R] /Count 3 >>"
        ];
        for (int index = 0; index < 3; index++)
        {
            int contentObject = 4 + index * 2;
            string content = $"BT /F1 18 Tf 20 40 Td ({labels[index]}) Tj ET 10 10 40 20 re S q 20 0 0 20 100 10 cm /Im1 Do Q";
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {geometry[index].Width} {geometry[index].Height}] /Rotate {geometry[index].Rotation} /Resources << /Font << /F1 9 0 R >> /XObject << /Im1 12 0 R >> >> /Contents {contentObject} 0 R >>");
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"); // 9
        objects.Add("<< /Type /Outlines /First 11 0 R /Last 11 0 R /Count 1 >>");
        objects.Add("<< /Title (Source bookmark) /Parent 10 0 R /Dest [3 0 R /Fit] >>");
        objects.Add("<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /ASCIIHexDecode /Length 7 >>\nstream\nFF0000>\nendstream");
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
        return stream.ToArray();
    }
}
