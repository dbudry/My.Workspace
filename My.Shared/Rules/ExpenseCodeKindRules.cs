namespace My.Shared.Rules;

public static class ExpenseCodeKindRules
{
    public const string Transportation = "Transportation";
    public const string Miscellaneous = "Miscellaneous";

    public static bool IsKnown(string? kind) =>
        string.Equals(kind, Transportation, StringComparison.Ordinal)
        || string.Equals(kind, Miscellaneous, StringComparison.Ordinal);
}
