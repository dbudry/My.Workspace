namespace My.Shared.Rules;

/// <summary>
/// Organization display-name uniqueness: one name workspace-wide, including archived.
/// </summary>
public static class OrganizationNameRules
{
    public static string Normalize(string? name) =>
        (name ?? string.Empty).Trim().ToLowerInvariant();

    public static bool NamesMatch(string? left, string? right)
    {
        var a = Normalize(left);
        if (a.Length == 0) return false;
        return a == Normalize(right);
    }

    public static string CollisionMessage(string? requestedName, bool existingIsArchived)
    {
        var shown = (requestedName ?? string.Empty).Trim();
        if (shown.Length == 0)
            shown = "that name";

        return existingIsArchived
            ? $"An organization named \"{shown}\" already exists (archived). Rename or unarchive that organization instead of creating a duplicate."
            : $"An organization named \"{shown}\" already exists. Choose a different name.";
    }
}
