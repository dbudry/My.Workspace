namespace My.Shared.Rules;

/// <summary>
/// The stored status stays <see cref="ExpenseStatusRules.Reimbursed"/>.
/// Every screen says Paid.
/// </summary>
public static class ExpensePayLabelRules
{
    public static string StatusWord(string? status)
    {
        if (!ExpenseStatusRules.IsReimbursed(status))
            return status ?? "";
        return "Paid";
    }

    public const string Mark = "Mark paid";
    public const string Undo = "Undo payment";
    public const string Done = "Paid.";
    public const string AskMark = "Mark paid?";
    public const string AskUndo = "Undo payment?";
    public const string Noun = "Payment";
    public const string DateLabel = "Payment date";
}
