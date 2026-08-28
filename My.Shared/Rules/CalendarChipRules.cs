namespace My.Shared.Rules;

/// <summary>
/// Label for a Tyme Calendar chip. Details is optional; an empty name must not
/// paint a blank bar (availability / slug-only Google events).
/// </summary>
public static class CalendarChipRules
{
    public const string UntitledFallback = "Time";

    /// <summary>
    /// Prefer Details; if blank, the project display name; last resort
    /// <see cref="UntitledFallback"/>.
    /// </summary>
    public static string FormatText(string? details, string? projectDisplayName)
    {
        var name = (details ?? string.Empty).Trim();
        if (name.Length > 0)
            return name;
        var project = (projectDisplayName ?? string.Empty).Trim();
        if (project.Length > 0)
            return project;
        return UntitledFallback;
    }
}
