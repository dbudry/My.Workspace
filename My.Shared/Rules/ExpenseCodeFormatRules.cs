namespace My.Shared.Rules;

/// <summary>
/// Letter codes shared by transportation and miscellaneous: A–Z, or a subcategory A.00–A.99.
/// </summary>
public static class ExpenseCodeFormatRules
{
    public static bool IsValid(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        var value = code.Trim().ToUpperInvariant();
        if (value.Length == 1)
            return value[0] is >= 'A' and <= 'Z';

        if (value.Length != 4 || value[1] != '.')
            return false;

        return value[0] is >= 'A' and <= 'Z'
            && char.IsDigit(value[2])
            && char.IsDigit(value[3]);
    }
}
