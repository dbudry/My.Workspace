using My.Functions.Services;
using My.Shared.Rules;
using QuestPDF.Infrastructure;
using Xunit;

namespace My.Tests.Services;

public class ExpenseStatementPdfServiceTests
{
    [Fact]
    public void BuildPacket_skips_heic_and_still_returns_form()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var form = ExpenseFormDocument.Generate(new ExpenseForm8743Model
        {
            EmployeeName = "Test",
            CoverStart = new DateTime(2026, 8, 1),
            CoverEnd = new DateTime(2026, 8, 31),
            ReportDate = new DateTime(2026, 8, 10),
            MileageRate = 0.555m
        });
        var pdf = new ExpenseStatementPdfService().BuildPacket(form,
        [
            new ExpenseReceiptPdfPage
            {
                FileName = "phone.heic",
                MimeType = "image/heic",
                Bytes = [0x00, 0x01, 0x02]
            }
        ]);
        Assert.True(pdf.Length > 100);
    }
}
