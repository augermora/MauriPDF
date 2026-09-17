using MauriPDF.Pdfium;
using PDFiumCore;

namespace MauriPDF.Editing;

/// <summary>Imports source PDF objects without rasterization, then transactionally publishes a validated file.</summary>
public sealed class PdfiumDocumentMaterializer : IPdfDocumentMaterializer
{
    private const ulong NoIncrementalSave = 2;

    public Task<PdfMaterializationResult> MaterializeAsync(string sourcePath, string destinationPath,
        PdfMaterializationPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Pages.Count == 0) throw new ArgumentException("A PDF must contain at least one page.", nameof(plan));

        string source = Path.GetFullPath(sourcePath);
        string destination = Path.GetFullPath(destinationPath);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Save As cannot overwrite the currently open source PDF. Choose a different file.");
        if (!File.Exists(source)) throw new FileNotFoundException("The source PDF was not found.", source);
        string directory = Path.GetDirectoryName(destination) ?? throw new IOException("The destination directory is invalid.");
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The destination directory does not exist.");

        return Task.Run(() => Materialize(source, destination, directory, plan, cancellationToken), cancellationToken);
    }

    private static PdfMaterializationResult Materialize(string sourcePath, string destinationPath, string directory,
        PdfMaterializationPlan plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string temporaryPath = CreateTemporaryPath(directory, Path.GetFileName(destinationPath));
        try
        {
            using IDisposable library = PdfiumRuntime.Acquire();
            using IDisposable nativeCall = PdfiumRuntime.Enter();
            WriteTemporary(sourcePath, temporaryPath, plan, cancellationToken);
            PdfMaterializationResult result = ValidateTemporary(temporaryPath, plan.Pages.Count);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destinationPath, overwrite: true);
            return result;
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch { /* Preserve the original save error; best-effort cleanup is retried by normal temp maintenance. */ }
        }
    }

    private static string CreateTemporaryPath(string directory, string destinationName)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            string path = Path.Combine(directory, $".{destinationName}.{Guid.NewGuid():N}.tmp");
            if (!File.Exists(path)) return path;
        }
        throw new IOException("Could not allocate a temporary output file.");
    }

    private static void WriteTemporary(string sourcePath, string temporaryPath, PdfMaterializationPlan plan,
        CancellationToken cancellationToken)
    {
        FpdfDocumentT? source = null;
        FpdfDocumentT? destination = null;
        try
        {
            source = fpdfview.FPDF_LoadDocument(sourcePath, null!);
            if (source is null) throw LoadFailure("source", fpdfview.FPDF_GetLastError());
            int sourceCount = fpdfview.FPDF_GetPageCount(source);
            int[] indices = plan.Pages.Select(page => page.SourcePageIndex).ToArray();
            if (indices.Any(index => index < 0 || index >= sourceCount))
                throw new InvalidDataException("The edit snapshot contains an invalid source page.");

            destination = fpdf_edit.FPDF_CreateNewDocument();
            if (destination is null) throw new InvalidOperationException("PDFium could not create the output document.");
            if (fpdf_ppo.FPDF_ImportPagesByIndex(destination, source, ref indices[0], (ulong)indices.Length, 0) == 0)
                throw new InvalidDataException("PDFium could not import the edited page sequence.");

            for (int index = 0; index < plan.Pages.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FpdfPageT? page = fpdfview.FPDF_LoadPage(destination, index);
                if (page is null) throw new InvalidDataException($"PDFium could not load imported page {index + 1}.");
                try
                {
                    int intrinsic = fpdf_edit.FPDFPageGetRotation(page);
                    if (intrinsic is < 0 or > 3) throw new InvalidDataException("PDFium returned an invalid page rotation.");
                    int structural = plan.Pages[index].StructuralRotationDegrees / 90;
                    fpdf_edit.FPDFPageSetRotation(page, (intrinsic + structural) % 4);
                    RemoveLinkAnnotations(page);
                }
                finally { fpdfview.FPDF_ClosePage(page); }
            }

            using FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 64 * 1024, FileOptions.SequentialScan);
            Exception? callbackError = null;
            using FPDF_FILEWRITE_ writer = new() { Version = 1 };
            writer.WriteBlock = (_, data, size) =>
            {
                try
                {
                    if (size > int.MaxValue) throw new IOException("PDFium produced an unsupported output block size.");
                    unsafe { stream.Write(new ReadOnlySpan<byte>(data.ToPointer(), checked((int)size))); }
                    return 1;
                }
                catch (Exception exception)
                {
                    callbackError = exception;
                    return 0;
                }
            };
            if (fpdf_save.FPDF_SaveAsCopy(destination, writer, NoIncrementalSave) == 0)
                throw new IOException("PDFium could not serialize the edited PDF.", callbackError);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            if (destination is not null) fpdfview.FPDF_CloseDocument(destination);
            if (source is not null) fpdfview.FPDF_CloseDocument(source);
        }
    }

    private static void RemoveLinkAnnotations(FpdfPageT page)
    {
        // Imported intra-document targets cannot be guaranteed after omit/reorder. Drop link annotations rather than emit known-wrong links.
        for (int index = fpdf_annot.FPDFPageGetAnnotCount(page) - 1; index >= 0; index--)
        {
            FpdfAnnotationT? annotation = fpdf_annot.FPDFPageGetAnnot(page, index);
            if (annotation is null) continue;
            int subtype;
            try { subtype = fpdf_annot.FPDFAnnotGetSubtype(annotation); }
            finally { fpdf_annot.FPDFPageCloseAnnot(annotation); }
            if (subtype == 2 && fpdf_annot.FPDFPageRemoveAnnot(page, index) == 0)
                throw new InvalidDataException("PDFium could not remove an unsafe imported link annotation.");
        }
    }

    private static PdfMaterializationResult ValidateTemporary(string path, int expectedPageCount)
    {
        FileInfo file = new(path);
        if (!file.Exists || file.Length < 8) throw new InvalidDataException("The generated PDF is empty or incomplete.");
        FpdfDocumentT? document = fpdfview.FPDF_LoadDocument(path, null!);
        if (document is null) throw LoadFailure("generated output", fpdfview.FPDF_GetLastError());
        try
        {
            int count = fpdfview.FPDF_GetPageCount(document);
            if (count != expectedPageCount) throw new InvalidDataException("The generated PDF has an unexpected page count.");
            for (int index = 0; index < count; index++)
            {
                double width = 0, height = 0;
                if (fpdfview.FPDF_GetPageSizeByIndex(document, index, ref width, ref height) == 0
                    || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
                    throw new InvalidDataException($"The generated PDF has invalid dimensions on page {index + 1}.");
            }
            ValidateFirstPageRender(document);
            return new(count, file.Length);
        }
        finally { fpdfview.FPDF_CloseDocument(document); }
    }

    private static unsafe void ValidateFirstPageRender(FpdfDocumentT document)
    {
        FpdfPageT? page = fpdfview.FPDF_LoadPage(document, 0);
        if (page is null) throw new InvalidDataException("The generated PDF's first page cannot be loaded.");
        try
        {
            byte[] pixels = new byte[8 * 8 * 4];
            fixed (byte* pointer = pixels)
            {
                FpdfBitmapT? bitmap = fpdfview.FPDFBitmapCreateEx(8, 8, (int)FPDFBitmapFormat.BGRA, new IntPtr(pointer), 32);
                if (bitmap is null) throw new InvalidDataException("The generated PDF could not be validated by rendering.");
                try { fpdfview.FPDF_RenderPageBitmap(bitmap, page, 0, 0, 8, 8, 0, 0); }
                finally { fpdfview.FPDFBitmapDestroy(bitmap); }
            }
        }
        finally { fpdfview.FPDF_ClosePage(page); }
    }

    private static InvalidDataException LoadFailure(string subject, ulong error) =>
        new($"PDFium could not open the {subject} (error {error}).");
}
