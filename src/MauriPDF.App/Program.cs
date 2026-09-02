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
            using PdfiumRenderer renderer = new();
            Application.Run(new MainForm(renderer));
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
