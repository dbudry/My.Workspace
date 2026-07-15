namespace My.Shared.Rules;

/// <summary>
/// Google Calendar exposes 11 fixed event color ids — events accept ids "1" through "11"
/// (string, not int — Google's enum is opaque) and the API rejects any other value, *including*
/// the empty string. This helper centralizes the list so the UI dropdown, server-side validation,
/// and tests all agree on what's valid, and so Tyme can't push an unknown id and silently fail
/// the entire create.
///
/// "Tomato" (id 11) is the default for unmatched (no-slug) Tyme events — visual "needs a project"
/// flag. The matched-event color defaults to <c>null</c> meaning "use the calendar's default"
/// so the user's existing color theme isn't overridden without their say-so.
/// </summary>
public static class GoogleEventColors
{
    public record Color(string Id, string Name);

    /// <summary>
    /// Google's documented event color palette. Order matches the Google Calendar UI's
    /// color picker for events.
    /// </summary>
    public static readonly IReadOnlyList<Color> All = new[]
    {
        new Color("1", "Lavender"),
        new Color("2", "Sage"),
        new Color("3", "Grape"),
        new Color("4", "Flamingo"),
        new Color("5", "Banana"),
        new Color("6", "Tangerine"),
        new Color("7", "Peacock"),
        new Color("8", "Graphite"),
        new Color("9", "Blueberry"),
        new Color("10", "Basil"),
        new Color("11", "Tomato"),
    };

    /// <summary>The most-red color in Google's palette — default for unmatched/no-project events.</summary>
    public const string DefaultUnmatchedColorId = "11";

    /// <summary>True if <paramref name="colorId"/> is one of the 11 valid Google color ids.</summary>
    public static bool IsValidColorId(string? colorId)
    {
        if (string.IsNullOrEmpty(colorId)) return false;
        foreach (var c in All)
            if (c.Id == colorId) return true;
        return false;
    }

    /// <summary>
    /// Returns <paramref name="colorId"/> if it's a valid Google color id, otherwise null.
    /// Use when applying a stored setting: an invalid value (corrupted row, deprecated id,
    /// empty string) is silently ignored rather than 400-ing the whole calendar push.
    /// </summary>
    public static string? NormalizeOrNull(string? colorId)
        => IsValidColorId(colorId) ? colorId : null;
}
