namespace My.Shared.Rules;

/// <summary>
/// Permission checks for project mutations that need more nuance than a flat
/// Editor-vs-Manager auth gate. Extracted so <c>ProjectFunction</c>'s
/// Create/UpdateProjectAsync stay unit-testable without spinning up the Functions
/// host or a DbContext (same rationale as the other classes in this namespace).
/// </summary>
public static class ProjectPermissionRules
{
    /// <summary>
    /// Shared-availability publishing (writes a sanitized event to the workspace Team
    /// Availability calendar whenever time is logged) is a team-visibility decision,
    /// reserved for Manager+ even though Editor:Tyme can otherwise create and edit
    /// projects freely.
    ///
    /// On <b>create</b> there is no prior state — pass <paramref name="currentIsSharedAvailability"/>
    /// as <c>false</c> so any attempt to create with the flag on requires Manager+.
    ///
    /// On <b>update</b>, an Editor is allowed to save the project unchanged (the value
    /// simply round-trips through the edit form) — only an actual *change* to the flag
    /// is blocked for non-Manager callers.
    /// </summary>
    public static bool CanSetSharedAvailability(
        bool requestedIsSharedAvailability,
        bool currentIsSharedAvailability,
        bool callerHasManagerAccess)
    {
        if (callerHasManagerAccess) return true;
        return requestedIsSharedAvailability == currentIsSharedAvailability;
    }
}
