namespace My.Client.Services;

/// <summary>
/// Cross-component signal fired whenever an intranet navigation item is created,
/// updated, deleted, or reordered by an admin.
/// 
/// The main NavMenu subscribes so the curated links under the "Intranet" group
/// can refresh live without a full page reload or re-login.
/// 
/// The admin page at /intranet/navigation fires NotifyChanged() after successful
/// mutations (same pattern as TimeSubmissionEvents for the overdue badge).
/// </summary>
public class IntranetNavigationEvents
{
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();
}
