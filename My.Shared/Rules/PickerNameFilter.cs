namespace My.Shared.Rules;

/// <summary>
/// Client-side name filter for dialog pickers (group, department).
/// Organization search uses the server lookup instead.
/// </summary>
public static class PickerNameFilter
{
    public static IReadOnlyList<T> Match<T>(
        IEnumerable<T> items,
        string? query,
        Func<T, string?> nameSelector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(nameSelector);

        if (string.IsNullOrWhiteSpace(query))
            return items as IReadOnlyList<T> ?? items.ToList();

        var needle = query.Trim();
        return items
            .Where(item =>
            {
                var name = nameSelector(item);
                return !string.IsNullOrEmpty(name)
                    && name.Contains(needle, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
    }
}
