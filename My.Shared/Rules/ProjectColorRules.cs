namespace My.Shared.Rules;

/// <summary>
/// Single source of truth for "what color does this project show as in the UI."
/// Before this rule existed each page rolled its own logic — ProjectManager fell back
/// from group to org, the calendar used only the group color, every other surface
/// (Stopwatch, Tasks, Reports, dashboard) showed no color at all. Now every page calls
/// <see cref="Resolve"/> with the user's <see cref="ProjectColorSource"/> preference
/// and gets a consistent answer.
///
/// Inputs are the two raw color values (group and org) — easiest to call from anywhere
/// regardless of whether the caller has a <c>ProjectDto</c>, a <c>ProjectDataItemDto</c>,
/// or the client-side <c>Project</c> wrapper.
/// </summary>
public static class ProjectColorRules
{
    /// <summary>Neutral gray used as a last-resort visible fallback. UI code that needs
    /// a non-null color (e.g. a 4px row-indicator bar) should treat <c>null</c> from
    /// <see cref="Resolve"/> as "no color" and either hide the indicator or use this.</summary>
    public const string FallbackGray = "#9e9e9e";

    /// <summary>
    /// Returns the color to use for a project, or <c>null</c> if no color should be shown.
    /// Empty/whitespace inputs are treated as null. <see cref="ProjectColorSource.None"/>
    /// always returns null — the UI surface decides whether to render anything in that case.
    /// </summary>
    public static string? Resolve(string? organizationColor, string? projectGroupColor, ProjectColorSource source)
    {
        var org = string.IsNullOrWhiteSpace(organizationColor) ? null : organizationColor;
        var group = string.IsNullOrWhiteSpace(projectGroupColor) ? null : projectGroupColor;

        return source switch
        {
            ProjectColorSource.None => null,
            ProjectColorSource.Organization => org,
            ProjectColorSource.ProjectGroup => group,
            // Default: prefer group; fall back to org if no group color is set so an
            // ungrouped project under a colored org still gets a marker.
            ProjectColorSource.GroupThenOrganization => group ?? org,
            _ => group ?? org,
        };
    }

    /// <summary>
    /// Same as <see cref="Resolve"/> but always returns a non-null hex — substitutes
    /// <paramref name="fallback"/> (defaulting to <see cref="FallbackGray"/>) when the
    /// resolved value would be null. For UI that draws a fixed-width color bar and
    /// needs *something* visible.
    /// </summary>
    public static string ResolveOrFallback(
        string? organizationColor,
        string? projectGroupColor,
        ProjectColorSource source,
        string fallback = FallbackGray)
        => Resolve(organizationColor, projectGroupColor, source) ?? fallback;

    /// <summary>
    /// Returns the *name* of the entity whose color was used by <see cref="Resolve"/>.
    /// Used by UI tooltips so a user hovering a color bar can see "Ball" or
    /// "Profit Network" instead of just a swatch with no context.
    ///
    /// Returns null when no color would be drawn (None source, or both colors empty).
    /// For <see cref="ProjectColorSource.GroupThenOrganization"/> the label matches
    /// the same fallback chain — group name when the group color won, org name when
    /// it fell back.
    /// </summary>
    public static string? ResolveLabel(
        string? organizationName,
        string? projectGroupName,
        string? organizationColor,
        string? projectGroupColor,
        ProjectColorSource source)
    {
        var color = Resolve(organizationColor, projectGroupColor, source);
        if (color == null) return null;

        var org = string.IsNullOrWhiteSpace(organizationName) ? null : organizationName;
        var group = string.IsNullOrWhiteSpace(projectGroupName) ? null : projectGroupName;
        var groupColor = string.IsNullOrWhiteSpace(projectGroupColor) ? null : projectGroupColor;

        return source switch
        {
            ProjectColorSource.Organization => org,
            ProjectColorSource.ProjectGroup => group,
            // Mirror Resolve's fallback chain — if the group provided the color, the
            // group name is the right label; otherwise we fell back to org.
            ProjectColorSource.GroupThenOrganization => groupColor != null ? group : org,
            _ => null,
        };
    }
}
