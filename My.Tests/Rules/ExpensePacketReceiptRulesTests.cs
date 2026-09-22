using System.Text;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpensePacketReceiptRulesTests
{
    [Theory]
    [InlineData("2026_08_Derek_Budry_Expenses.pdf")]
    [InlineData("2026_08_Derek_Budry_Expenses.xlsx")]
    [InlineData("Form_87-43.xlsx")]
    [InlineData("form_87-43.xlsx")]
    public void Nested_packet_names_are_rejected(string name)
    {
        Assert.True(ExpensePacketReceiptRules.IsNestedExpensePacket(name));
    }

    [Fact]
    public void Vendor_receipt_name_is_allowed()
    {
        Assert.False(ExpensePacketReceiptRules.IsNestedExpensePacket("invoice.pdf"));
    }

    [Theory]
    [InlineData("Invoice_87-43-0192.pdf")]
    [InlineData("TravelExpense_Agency_Receipt.pdf")]
    public void Vendor_receipt_names_that_merely_contain_packet_substrings_are_allowed(string name)
    {
        Assert.False(ExpensePacketReceiptRules.IsNestedExpensePacket(name));
    }

    [Fact]
    public void Pdf_bytes_with_form_title_are_rejected()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.4 FORM 87-43 TRAVEL EXPENSE STATEMENT");
        Assert.True(ExpensePacketReceiptRules.IsNestedExpensePacket("scan.pdf", bytes));
    }

    [Fact]
    public void Reject_message_is_set()
    {
        Assert.False(string.IsNullOrWhiteSpace(ExpensePacketReceiptRules.RejectMessage));
    }
}
