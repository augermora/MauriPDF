using System.Text;
using System.Runtime.InteropServices;
using MauriPDF.Core.Documents;
using MauriPDF.Core.Rendering;
using MauriPDF.Core.Text;
using MauriPDF.Rendering;
using Xunit;

namespace MauriPDF.Editing.Tests;

[Collection("PDFium editing native")]
public sealed class MultiSourceMaterializationTests
{
    private static readonly string[] ExpectedText = ["SOURCE1 A", "SOURCE2 B", "SOURCE1 B", "SOURCE2 A", "SOURCE2 B"];
    [Fact]
    public async Task RealCompositionPreservesPhysicalOrderTextRotationSourcesAndTransactionalResave()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"mauripdf-compose-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string aPath = Path.Combine(directory, "a.pdf"), bPath = Path.Combine(directory, "b.pdf"), output = Path.Combine(directory, "out.pdf");
        try
        {
            byte[] aBytes = PdfMaterializationIntegrationTests.CreateSourcePdf("SOURCE1");
            byte[] bBytes = PdfMaterializationIntegrationTests.CreateSourcePdf("SOURCE2");
            await File.WriteAllBytesAsync(aPath, aBytes, TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(bPath, bBytes, TestContext.Current.CancellationToken);
            using PdfiumRenderer renderer = new();
            using IPdfRenderSession a = renderer.Open(aPath), b = renderer.Open(bPath);
            PdfSourceDocument aSource = new(Guid.NewGuid(), aPath, Enumerable.Range(0, 3).Select(a.GetPageSize).ToArray());
            PdfSourceDocument bSource = new(Guid.NewGuid(), bPath, Enumerable.Range(0, 3).Select(b.GetPageSize).ToArray());
            DocumentEditSession edits = new(aSource);
            edits.Execute(new DeletePageEdit(edits.State.Pages[2].Id));
            edits.Import(bSource, 1, 1, 1, edits.State.Pages[0].Id);
            DocumentPageId rotated = edits.State.Pages[1].Id;
            edits.Execute(new RotatePageEdit(rotated, true));
            edits.Import(bSource, 0, 1, 3, rotated);
            edits.Execute(new RotatePageEdit(edits.State.Pages[3].Id, true));
            edits.Import(bSource, 1, 1, 4, rotated); // Repeated physical page, separate instance.
            edits.Import(bSource, 2, 1, 5, rotated);
            edits.Execute(new DeletePageEdit(edits.State.Pages[5].Id));
            PdfMaterializationPlan snapshot = edits.CreateMaterializationPlan();
            Assert.Equal(new[] { aSource.Id, bSource.Id, aSource.Id, bSource.Id, bSource.Id }, snapshot.Pages.Select(p => p.SourceDocumentId));
            PdfiumDocumentMaterializer writer = new();
            PdfMaterializationResult first = await writer.MaterializeAsync(new(aPath, output, snapshot, PdfDestinationPolicy.RequireMissing), TestContext.Current.CancellationToken);
            edits.MarkSavedBaseline(edits.State.Revision);
            using (IPdfRenderSession saved = renderer.Open(output))
            {
                Assert.Equal(5, saved.PageCount);
                Assert.Equal(ExpectedText, Enumerable.Range(0, 5).Select(i => Text(saved.ExtractText(i))));
                Assert.Equal(new PdfPageSize(100, 200), saved.GetPageSize(0));
                Assert.Equal(new PdfPageSize(160, 160), saved.GetPageSize(1));
                Assert.Equal(new PdfPageSize(200, 100), saved.GetPageSize(3)); // B0 intrinsic 90 + structural 90.
            }
            foreach (string source in new[] { aPath, bPath })
                await Assert.ThrowsAsync<IOException>(() => writer.MaterializeAsync(new(aPath, source, snapshot, PdfDestinationPolicy.OverwriteApproved), TestContext.Current.CancellationToken));
            string alias = Path.Combine(directory, "imported-hard-link.pdf");
            Assert.True(CreateHardLink(alias, bPath, IntPtr.Zero));
            Assert.Same(bSource, edits.Sources.FindPath(alias));
            Assert.Throws<IOException>(() => edits.Sources.ProtectDestination(alias));
            await Assert.ThrowsAsync<IOException>(() => writer.MaterializeAsync(new(aPath, alias, snapshot,
                PdfDestinationPolicy.OverwriteApproved), TestContext.Current.CancellationToken));
            edits.Undo(); Assert.True(edits.IsDirty); // Restore deleted imported page.
            var second = await writer.MaterializeAsync(new(aPath, output, edits.CreateMaterializationPlan(), PdfDestinationPolicy.RequireExpectedIdentity, first.DestinationIdentity), TestContext.Current.CancellationToken);
            Assert.Equal(6, second.PageCount);
            edits.MarkSavedBaseline(edits.State.Revision);
            edits.Redo(); Assert.True(edits.IsDirty);
            edits.Undo(); Assert.False(edits.IsDirty);
            Assert.Equal(aBytes, await File.ReadAllBytesAsync(aPath, TestContext.Current.CancellationToken));
            Assert.Equal(bBytes, await File.ReadAllBytesAsync(bPath, TestContext.Current.CancellationToken));
            Assert.Empty(Directory.EnumerateFiles(directory, ".*.tmp"));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string Text(PdfTextPage page)
    {
        StringBuilder text = new(); page.AppendText(text, 0, page.Count); return text.ToString();
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string alias, string existingPath, IntPtr securityAttributes);
}
