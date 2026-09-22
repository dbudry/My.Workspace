using System.Text;

namespace My.Shared.Rules;

/// <summary>
/// A generated expense packet (Form 87-43 + receipts) is not a vendor receipt.
/// Attaching one would nest a second statement and every prior receipt in the next PDF.
/// </summary>
public static class ExpensePacketReceiptRules
{
    public static bool IsNestedExpensePacket(string? fileName, byte[]? bytes = null)
    {
        var name = Path.GetFileName(fileName ?? "");
        if (name.EndsWith("_Expenses.pdf", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("_Expenses.xlsx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Form_87-43.xlsx", StringComparison.OrdinalIgnoreCase))
            return true;

        if (bytes is not { Length: > 0 })
            return false;

        var take = Math.Min(bytes.Length, 512_000);
        var sample = Encoding.ASCII.GetString(bytes, 0, take);
        return sample.Contains("TRAVEL EXPENSE STATEMENT", StringComparison.OrdinalIgnoreCase)
            || sample.Contains("FORM 87-43", StringComparison.OrdinalIgnoreCase);
    }

    public const string RejectMessage =
        "That file is an expense statement PDF, not a vendor receipt. Attach the invoice or photo for this line instead.";
}
