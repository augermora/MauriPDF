using MauriPDF.Rendering;

namespace MauriPDF.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        try
        {
            PdfViewerRenderer renderer = new(() => new PdfiumRenderer());
            try
            {
                Application.Run(new MainForm(renderer));
            }
            finally
            {
                // The window is closed: drain native work before releasing the engine.
                renderer.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"MauriPDF could not start.\n\n{exception.Message}",
                "MauriPDF",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
