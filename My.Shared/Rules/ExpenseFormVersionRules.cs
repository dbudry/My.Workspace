using System.Globalization;

namespace My.Shared.Rules;

/// <summary>
/// Version of the generated expense form. Increment <see cref="Current"/> only when the
/// form layout, labels, or totals change. Saving or editing a report does not change it.
/// Each filed PDF stores the value that produced it.
/// </summary>
public static class ExpenseFormVersionRules
{
    public const int Current = 2;

    public static string Label =>
        "Version " + Current.ToString(CultureInfo.InvariantCulture);
}
