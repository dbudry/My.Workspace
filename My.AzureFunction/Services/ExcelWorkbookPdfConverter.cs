using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace My.Functions.Services;

/// <summary>
/// Prints a workbook to PDF through Excel so the legacy Form 87-43 matches
/// the file Derek used to email. Requires Excel on the Functions host (local Windows).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ExcelWorkbookPdfConverter
{
    public byte[] ConvertToPdf(byte[] xlsx)
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException(
                "Legacy Form 87-43 PDF can only be printed on a Windows host with Excel installed.");

        var excelType = Type.GetTypeFromProgID("Excel.Application")
            ?? throw new InvalidOperationException(
                "Excel is required to render the legacy Form 87-43 PDF on this machine.");

        byte[]? pdf = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                pdf = ConvertOnSta(excelType, xlsx);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(90)))
            throw new TimeoutException("Excel took too long to print the legacy form.");
        if (error != null)
            throw error;
        return pdf ?? throw new InvalidOperationException("Excel produced no PDF.");
    }

    private static byte[] ConvertOnSta(Type excelType, byte[] xlsx)
    {
        var xlsxPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        var pdfPath = Path.ChangeExtension(xlsxPath, ".pdf");
        File.WriteAllBytes(xlsxPath, xlsx);

        dynamic? app = null;
        dynamic? workbook = null;
        try
        {
            app = Activator.CreateInstance(excelType)
                ?? throw new InvalidOperationException("Couldn't start Excel.");
            app.Visible = false;
            app.DisplayAlerts = false;
            app.ScreenUpdating = false;
            workbook = app.Workbooks.Open(xlsxPath, ReadOnly: true);
            // 0 = xlTypePDF
            workbook.ExportAsFixedFormat(0, pdfPath);
            workbook.Close(false);
            workbook = null;
            return File.ReadAllBytes(pdfPath);
        }
        finally
        {
            if (workbook != null)
            {
                try { workbook.Close(false); } catch { /* ignore */ }
                TryRelease(workbook);
            }

            if (app != null)
            {
                try { app.Quit(); } catch { /* ignore */ }
                TryRelease(app);
            }

            TryDelete(xlsxPath);
            TryDelete(pdfPath);
        }
    }

    private static void TryRelease(object com)
    {
        try { Marshal.FinalReleaseComObject(com); } catch { /* ignore */ }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
