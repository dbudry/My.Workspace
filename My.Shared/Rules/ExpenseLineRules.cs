namespace My.Shared.Rules;

public static class ExpenseLineRules
{
    public const int MaxLinesPerReport = 200;
    public const int DescriptionMaxLength = 200;

    public static decimal ComputeAmount(
        string category,
        decimal? miles,
        decimal amount,
        decimal mileageRatePerMile,
        string? transportationCode = null)
    {
        if (ExpenseCategoryRules.IsPersonalCar(category, transportationCode))
        {
            var m = miles ?? 0m;
            if (m < 0m) m = 0m;
            return decimal.Round(m * mileageRatePerMile, 2, MidpointRounding.AwayFromZero);
        }

        return decimal.Round(amount < 0m ? 0m : amount, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Calendar day with no time zone. JSON UTC midnight must not shift the date.
    /// </summary>
    public static DateTime CalendarDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);

    public static DateTime DateOnly(DateTime value) => CalendarDate(value);

    /// <summary>
    /// Line-level validation errors are tracked by index (computed once, at Save time).
    /// Removing a line shifts every later line's index down by one, so the error set must
    /// shift with it — otherwise a highlight lands on the wrong row (or the row that still
    /// has the problem loses its highlight).
    /// </summary>
    public static void ShiftIndicesAfterRemoval(ISet<int> indices, int removedIndex)
    {
        indices.Remove(removedIndex);
        var toShift = indices.Where(i => i > removedIndex).ToList();
        foreach (var i in toShift) indices.Remove(i);
        foreach (var i in toShift) indices.Add(i - 1);
    }
}
