using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpenseDriveNamingRulesTests
{
    [Fact]
    public void UserFolderName_is_last_first() =>
        Assert.Equal("Budry_Derek", ExpenseDriveNamingRules.UserFolderName("Budry", "Derek"));

    [Fact]
    public void UserFolderName_with_user_id_is_unique_for_homonyms()
    {
        Assert.Equal(
            "Smith_John_682a8f",
            ExpenseDriveNamingRules.UserFolderName("Smith", "John", "682a8fdc-5e9b-434d-9a12-15e1b95258de"));
        Assert.NotEqual(
            ExpenseDriveNamingRules.UserFolderName("Smith", "John", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ExpenseDriveNamingRules.UserFolderName("Smith", "John", "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
    }

    [Theory]
    [InlineData(null, null, "Unknown")]
    [InlineData("", "Derek", "Derek")]
    [InlineData("Budry", "", "Budry")]
    [InlineData("O'Brien", "Ann-Marie", "O_Brien_Ann_Marie")]
    public void UserFolderName_sanitizes_and_handles_blanks(string? last, string? first, string expected) =>
        Assert.Equal(expected, ExpenseDriveNamingRules.UserFolderName(last!, first!));

    [Fact]
    public void PeriodFolderName_is_yyyy_MM() =>
        Assert.Equal("2026_09", ExpenseDriveNamingRules.PeriodFolderName(2026, 9));

    [Fact]
    public void FiledPacketFileName_is_unique_per_report_and_not_under_the_user_folder()
    {
        Assert.Equal("Filed", ExpenseDriveNamingRules.FiledFolderName);
        Assert.Equal(
            "2026_08_Derek_Budry_a1b2c3d4_Expenses.pdf",
            ExpenseDriveNamingRules.FiledPacketFileName(
                2026, 8, "Derek Budry", "a1b2c3d4e5f67890"));
        Assert.Equal(
            "2026_01_Employee_report_Expenses.pdf",
            ExpenseDriveNamingRules.FiledPacketFileName(2026, 1, "  ", ""));
    }

    [Fact]
    public void ReceiptFileName_uses_date_and_slug()
    {
        var date = new DateTime(2026, 8, 3);
        Assert.Equal("2026_08_03_ATT_bill.pdf",
            ExpenseDriveNamingRules.ReceiptFileName(date, "ATT bill", ".pdf"));
    }

    [Fact]
    public void ReceiptFileName_truncates_long_slug_and_defaults_blank()
    {
        var date = new DateTime(2026, 1, 1);
        var longDesc = new string('a', 80);
        var name = ExpenseDriveNamingRules.ReceiptFileName(date, longDesc, "png");
        Assert.StartsWith("2026_01_01_", name, StringComparison.Ordinal);
        Assert.EndsWith(".png", name, StringComparison.Ordinal);
        Assert.True(name.Length <= "2026_01_01_".Length + ExpenseDriveNamingRules.SlugMaxLength + ".png".Length);

        Assert.Equal("2026_01_01_Receipt.pdf",
            ExpenseDriveNamingRules.ReceiptFileName(date, "   ", "pdf"));
    }

    [Theory]
    [InlineData("2026_09-08_Expenses.pdf", "2026_08_10_Expenses.pdf")]
    [InlineData("2026_09_08_Expenses (1).pdf", "2026_08_10_Expenses (1).pdf")]
    [InlineData("2026-09-09_GitHub.pdf", "2026_08_10_GitHub.pdf")]
    [InlineData("invoice.pdf", "invoice.pdf")]
    public void ReplaceLeadingDate_rewrites_date_prefix_only(string original, string expected)
    {
        var date = new DateTime(2026, 8, 10);
        Assert.Equal(expected, ExpenseDriveNamingRules.ReplaceLeadingDate(original, date));
    }
}
