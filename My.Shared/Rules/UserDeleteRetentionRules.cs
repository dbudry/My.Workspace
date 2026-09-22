namespace My.Shared.Rules;

/// <summary>
/// User delete must look at expense activity, not only Tyme time entries.
/// </summary>
public static class UserDeleteRetentionRules
{
    public static bool HasRecentExpenseActivity(
        DateTime cutoffUtc,
        DateTime createdAt,
        DateTime updatedAt,
        DateTime? submittedAt,
        DateTime? reimbursedAt) =>
        createdAt > cutoffUtc
        || updatedAt > cutoffUtc
        || (submittedAt is { } submitted && submitted > cutoffUtc)
        || (reimbursedAt is { } reimbursed && reimbursed > cutoffUtc);
}
