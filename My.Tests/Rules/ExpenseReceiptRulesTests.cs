using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpenseReceiptRulesTests
{
    [Fact]
    public void TryValidate_accepts_pdf_and_images()
    {
        Assert.True(ExpenseReceiptRules.TryValidate("invoice.pdf", 100, out _));
        Assert.True(ExpenseReceiptRules.TryValidate("photo.PNG", 100, out _));
        Assert.True(ExpenseReceiptRules.TryValidate("scan.heic", 100, out _));
    }

    [Fact]
    public void TryValidate_rejects_empty_or_unknown()
    {
        Assert.False(ExpenseReceiptRules.TryValidate("", 100, out _));
        Assert.False(ExpenseReceiptRules.TryValidate("notes.txt", 100, out _));
        Assert.False(ExpenseReceiptRules.TryValidate("a.pdf", 0, out _));
    }

    [Fact]
    public void TryValidateCount_caps_files_per_line()
    {
        Assert.True(ExpenseReceiptRules.TryValidateCount(0, 1, out _));
        Assert.False(ExpenseReceiptRules.TryValidateCount(ExpenseReceiptRules.MaxFilesPerLine, 1, out _));
    }

    [Fact]
    public void IsHeic_detects_extension_and_mime()
    {
        Assert.True(ExpenseReceiptRules.IsHeic("photo.heic"));
        Assert.True(ExpenseReceiptRules.IsHeic("img.jpg", "image/heic"));
        Assert.False(ExpenseReceiptRules.IsHeic("scan.pdf"));
    }
}
