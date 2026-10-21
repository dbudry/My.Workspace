namespace My.Shared.Rules;

/// <summary>
/// Problems that still let a report be submitted after the person confirms.
/// </summary>
public static class ExpenseSubmitWarningRules
{
    public readonly record struct Line(
        DateTime Date,
        string? Description,
        decimal Amount,
        string? Category,
        string? MealType,
        int? HeadCount,
        int ReceiptCount);

    /// <summary>One problem. Lines are the rows under it, each shown on its own line.</summary>
    public readonly record struct Issue(string Text, IReadOnlyList<string> Lines);

    public static List<Issue> Issues(IEnumerable<Line> lines)
    {
        var source = lines as IReadOnlyList<Line> ?? lines.ToList();
        var issues = new List<Issue>();
        foreach (var line in source)
        {
            var meal = ExpenseMealRules.IncompleteMessage(line.Category, line.MealType, line.HeadCount, line.Description);
            if (meal != null)
                issues.Add(new Issue(meal, []));
        }

        var missingReceipts = ExpenseChargeToRules.MissingReceiptLabels(
            source.Select(line => (line.Date, line.Description, line.Amount, line.ReceiptCount)));
        if (missingReceipts.Count > 0)
            issues.Add(new Issue("No receipt", missingReceipts));

        return issues;
    }

    public static List<string> Messages(IEnumerable<Line> lines) =>
        Issues(lines).Select(issue =>
            issue.Lines.Count == 0
                ? issue.Text
                : issue.Text + ":\n" + string.Join("\n", issue.Lines)).ToList();
}
