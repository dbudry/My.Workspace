using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using My.Shared.Rules;

namespace My.Functions.Services;

public sealed class ExpenseStatementPdfService
{
    public byte[] BuildPacket(ExpenseForm8743Model model, IReadOnlyList<ExpenseReceiptPdfPage> receipts) =>
        BuildPacket(ExpenseFormDocument.Generate(model), receipts);

    public byte[] BuildPacket(byte[] formPdf, IReadOnlyList<ExpenseReceiptPdfPage> receipts)
    {
        var parts = new List<byte[]> { formPdf };
        foreach (var receipt in receipts)
        {
            if (receipt.Bytes.Length == 0)
                continue;
            if (ExpensePacketReceiptRules.IsNestedExpensePacket(receipt.FileName, receipt.Bytes))
                continue;
            if (IsPdf(receipt.MimeType, receipt.FileName))
                parts.Add(receipt.Bytes);
            else if (IsHeic(receipt.MimeType, receipt.FileName))
            {
                if (HeicJpegConverter.TryConvert(receipt.Bytes, out var jpeg))
                {
                    parts.Add(ReceiptImagePage(new ExpenseReceiptPdfPage
                    {
                        FileName = Path.ChangeExtension(receipt.FileName, ".jpg"),
                        MimeType = "image/jpeg",
                        Bytes = jpeg
                    }));
                }
            }
            else if (IsImage(receipt.MimeType, receipt.FileName))
                parts.Add(ReceiptImagePage(receipt));
        }

        return Merge(parts);
    }

    private static byte[] ReceiptImagePage(ExpenseReceiptPdfPage receipt)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(28);
                page.Header().Text(receipt.FileName).FontSize(9).FontColor(Colors.Grey.Darken1);
                page.Content().PaddingTop(8).Image(receipt.Bytes).FitArea();
            });
        }).GeneratePdf();
    }

    private static byte[] Merge(IReadOnlyList<byte[]> parts)
    {
        using var output = new PdfDocument();
        foreach (var part in parts)
        {
            using var inputStream = new MemoryStream(part);
            using var input = PdfReader.Open(inputStream, PdfDocumentOpenMode.Import);
            for (var i = 0; i < input.PageCount; i++)
                output.AddPage(input.Pages[i]);
        }

        using var ms = new MemoryStream();
        output.Save(ms, false);
        return ms.ToArray();
    }

    private static bool IsPdf(string? mime, string fileName) =>
        string.Equals(mime, "application/pdf", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    private static bool IsImage(string? mime, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(mime) && mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return true;
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif";
    }

    private static bool IsHeic(string? mime, string fileName)
    {
        if (string.Equals(mime, "image/heic", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mime, "image/heif", StringComparison.OrdinalIgnoreCase))
            return true;
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext is ".heic" or ".heif";
    }
}

public sealed class ExpenseReceiptPdfPage
{
    public string FileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public byte[] Bytes { get; set; } = [];
}
