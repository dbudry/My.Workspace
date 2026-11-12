namespace My.Shared.Rules;

/// <summary>
/// Selection logic for the checkbox multi-select. A <c>null</c> selection means "all" (no filter,
/// future options included). An empty selection means "none". Anything else is the picked keys.
/// Extracted so it is unit tested without rendering.
/// </summary>
public static class MultiSelectRules
{
    public const int MaxNamesInSummary = 3;

    /// <summary>True when the selection is null (all) or every option is picked.</summary>
    public static bool IsAll(IReadOnlyCollection<string>? selected, IReadOnlyCollection<string> allKeys) =>
        selected == null || (allKeys.Count > 0 && allKeys.All(selected.Contains));

    /// <summary>True when the checkbox for <paramref name="key"/> shows checked.</summary>
    public static bool IsChecked(IReadOnlyCollection<string>? selected, IReadOnlyCollection<string> allKeys, string key) =>
        IsAll(selected, allKeys) || (selected != null && selected.Contains(key));

    /// <summary>
    /// Result of ticking or clearing one box. Picking every option collapses back to null (all).
    /// Clearing the last box gives an empty list (none).
    /// </summary>
    public static List<string>? Toggle(
        IReadOnlyCollection<string>? selected,
        IReadOnlyCollection<string> allKeys,
        string key,
        bool isChecked)
    {
        var set = IsAll(selected, allKeys)
            ? allKeys.ToHashSet(StringComparer.Ordinal)
            : selected!.ToHashSet(StringComparer.Ordinal);

        if (isChecked) set.Add(key); else set.Remove(key);

        if (allKeys.Count > 0 && allKeys.All(set.Contains))
            return null;
        return allKeys.Where(set.Contains).ToList();
    }

    /// <summary>Text for the closed field. Shows names, never ids.</summary>
    public static string Summary(
        IReadOnlyCollection<string>? selected,
        IReadOnlyList<(string Key, string Text)> options,
        string allLabel)
    {
        var allKeys = options.Select(o => o.Key).ToList();
        if (IsAll(selected, allKeys))
            return allLabel;

        var names = options.Where(o => selected!.Contains(o.Key)).Select(o => o.Text).ToList();
        if (names.Count == 0)
            return "None selected";
        return names.Count <= MaxNamesInSummary
            ? string.Join(", ", names)
            : $"{names.Count} selected";
    }
}
