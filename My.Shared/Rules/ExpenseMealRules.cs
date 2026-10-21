using System.Globalization;

namespace My.Shared.Rules;

/// <summary>
/// A meal line has one meal type and a head count. The printed form places that
/// count in the Breakfast, Lunch, or Dinner column. The price is the line amount.
/// </summary>
public static class ExpenseMealRules
{
    public const string Breakfast = "Breakfast";
    public const string Lunch = "Lunch";
    public const string Dinner = "Dinner";
    public const int MinHeadcount = 1;
    public const int MaxHeadcount = 99;

    public static string? CleanType(string? mealType)
    {
        if (string.IsNullOrWhiteSpace(mealType))
            return null;
        var value = mealType.Trim();
        if (value.Equals(Breakfast, StringComparison.OrdinalIgnoreCase)
            || value.Equals("B", StringComparison.OrdinalIgnoreCase))
            return Breakfast;
        if (value.Equals(Lunch, StringComparison.OrdinalIgnoreCase)
            || value.Equals("L", StringComparison.OrdinalIgnoreCase))
            return Lunch;
        if (value.Equals(Dinner, StringComparison.OrdinalIgnoreCase)
            || value.Equals("D", StringComparison.OrdinalIgnoreCase))
            return Dinner;
        return null;
    }

    public static int? Clean(int? count)
    {
        if (count is null or < MinHeadcount)
            return null;
        return count > MaxHeadcount ? MaxHeadcount : count;
    }

    /// <summary>Both are stored, or both are cleared.</summary>
    public static (string? MealType, int? HeadCount) Normalize(string? mealType, int? headCount)
    {
        var type = CleanType(mealType);
        var count = Clean(headCount);
        if (type == null || count == null)
            return (null, null);
        return (type, count);
    }

    public static bool HasMeal(string? mealType, int? headCount) =>
        Normalize(mealType, headCount).MealType != null;

    /// <summary>
    /// A meal can be saved and submitted without both parts. The message names what is missing.
    /// </summary>
    public static string? IncompleteMessage(string? category, string? mealType, int? headCount, string? description)
    {
        if (!string.Equals(category, ExpenseCategoryRules.Meals, StringComparison.Ordinal))
            return null;

        var typeMissing = CleanType(mealType) == null;
        var countMissing = Clean(headCount) == null;
        if (!typeMissing && !countMissing)
            return null;

        var what = string.IsNullOrWhiteSpace(description) ? "A meal line" : $"Meal \"{description.Trim()}\"";
        if (typeMissing && countMissing)
            return $"{what} needs a meal type and a head count.";
        if (typeMissing)
            return $"{what} needs a meal type.";
        return $"{what} needs a head count.";
    }

    /// <summary>Print columns. The number lands in one of B, L, or D.</summary>
    public static (int? Breakfast, int? Lunch, int? Dinner) Columns(string? mealType, int? headCount)
    {
        var meal = Normalize(mealType, headCount);
        return meal.MealType switch
        {
            Breakfast => (meal.HeadCount, null, null),
            Lunch => (null, meal.HeadCount, null),
            Dinner => (null, null, meal.HeadCount),
            _ => (null, null, null)
        };
    }

    public static string Cell(int? count)
    {
        var cleaned = Clean(count);
        return cleaned.HasValue
            ? cleaned.Value.ToString(CultureInfo.InvariantCulture)
            : "";
    }
}
