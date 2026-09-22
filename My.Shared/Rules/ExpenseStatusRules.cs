namespace My.Shared.Rules;

public static class ExpenseStatusRules
{
    public const string Draft = "Draft";
    public const string Submitted = "Submitted";
    public const string Reimbursed = "Reimbursed";

    public static bool IsDraft(string? status) =>
        string.Equals(status, Draft, StringComparison.Ordinal);

    public static bool IsSubmitted(string? status) =>
        string.Equals(status, Submitted, StringComparison.Ordinal);

    public static bool IsReimbursed(string? status) =>
        string.Equals(status, Reimbursed, StringComparison.Ordinal);

    public static bool IsLocked(string? status) =>
        IsSubmitted(status) || IsReimbursed(status);
}
