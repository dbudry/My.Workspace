namespace My.Shared.Rules;

/// <summary>
/// Removing a miscellaneous code. Z stays. A letter also covers its subcategories.
/// The caller soft-deletes a code that is already on a line and deletes the row otherwise.
/// </summary>
public static class ExpenseCodeRemovalRules
{
    public const string SoftwareKeptMessage = "Z stays because software expenses print as Z.";

    /// <summary>
    /// Z cannot be renamed or retired. Checked against the stored code, before any edit is applied.
    /// </summary>
    public static string? RejectSoftwareEdit(string? storedCode, string? newCode, bool isActive)
    {
        if (!IsSoftwareCode(storedCode))
            return null;
        if (!string.Equals(
                ExpenseCategoryRules.NormalizeCode(newCode),
                ExpenseForm8743Rules.MiscSoftwareCode,
                StringComparison.Ordinal))
            return SoftwareKeptMessage;
        if (!isActive)
            return "Z stays active because software expenses print as Z.";
        return null;
    }

    public static bool IsSoftwareCode(string? code) =>
        string.Equals(
            ExpenseCategoryRules.NormalizeCode(code),
            ExpenseForm8743Rules.MiscSoftwareCode,
            StringComparison.Ordinal);

    /// <summary>
    /// Codes to remove, children before the letter, so a letter takes its subcategories with it.
    /// </summary>
    public static IReadOnlyList<string> CodesToRemove(string? code, IEnumerable<string> miscellaneousCodes)
    {
        var normalized = ExpenseCategoryRules.NormalizeCode(code);
        if (string.IsNullOrEmpty(normalized) || IsSoftwareCode(normalized))
            return [];

        if (normalized.Length != 1)
            return [normalized];

        var prefix = normalized + ".";
        return miscellaneousCodes
            .Select(ExpenseCategoryRules.NormalizeCode)
            .Where(existing => !string.IsNullOrEmpty(existing))
            .Select(existing => existing!)
            .Where(existing =>
                string.Equals(existing, normalized, StringComparison.Ordinal)
                || existing.StartsWith(prefix, StringComparison.Ordinal))
            .Where(existing => !IsSoftwareCode(existing))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(existing => existing.Length)
            .ThenBy(existing => existing, StringComparer.Ordinal)
            .ToList();
    }
}
