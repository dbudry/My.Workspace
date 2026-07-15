namespace My.Shared.Rules;

/// <summary>
/// Builds the sanitized "team availability" event that gets dual-published to the
/// workspace-shared Google calendar whenever a user logs time against a project
/// flagged <c>IsSharedAvailability = true</c>.
///
/// The team calendar exposes <em>availability</em> only — name + project title. No task
/// name, no description, no slug — Tyme entries are private "except to the manager or
/// admin"; the team calendar gets a sanitized echo.
/// </summary>
public static class TeamAvailabilityEventRules
{
    /// <summary>Google Calendar color id used for every team-availability event.</summary>
    public const string FixedColorId = "2"; // Sage

    /// <summary>
    /// Used as the public-facing project label on the team calendar when a project
    /// has no explicit <c>DisplayName</c> override. Generic by design — the team
    /// calendar is an "availability echo", not a leak of internal project names
    /// like "Vacation Q2" or "Sick — flu". A manager who wants something more
    /// specific (Wedding, Jury Duty) overrides via the DisplayName field.
    /// </summary>
    public const string DefaultDisplayName = "Out of Office";

    /// <summary>
    /// Resolves the effective name to show on the team calendar for a project.
    /// Prefers an explicit DisplayName; falls back to <see cref="DefaultDisplayName"/>
    /// instead of leaking the internal Name (which historically was used as a
    /// fallback but defeated the privacy intent).
    /// </summary>
    public static string ResolveDisplayName(string? displayName)
        => !string.IsNullOrWhiteSpace(displayName)
            ? displayName.Trim()
            : DefaultDisplayName;

    /// <summary>
    /// Builds the event title: "<c>[&lt;DisplayName&gt;] &lt;ProjectName&gt;</c>" —
    /// matches the existing legacy Team Availability calendar format (the script the
    /// team used before Tyme wrote here). <paramref name="displayName"/> should already
    /// be the user's resolved name (use <see cref="UserDisplayNameRules.Resolve"/> at
    /// the call site) — passing a raw email here would leak the email onto the public
    /// team calendar. Whitespace is trimmed; missing parts collapse cleanly.
    /// </summary>
    public static string BuildTitle(string? displayName, string? projectName)
    {
        var name = (displayName ?? string.Empty).Trim();
        var project = (projectName ?? string.Empty).Trim();

        if (name.Length == 0 && project.Length == 0) return string.Empty;
        if (name.Length == 0) return project;
        if (project.Length == 0) return name;
        return $"[{name}] {project}";
    }
}
