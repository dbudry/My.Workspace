namespace My.Client.Services;

/// <summary>
/// Cross-component signal fired whenever a TimeSubmission row is created or deleted
/// (submit or unsubmit, by anyone). The NavMenu's overdue badge subscribes so it
/// can refresh without polling. Submit-related pages fire <see cref="NotifyChanged"/>
/// after a successful API call.
/// </summary>
public class TimeSubmissionEvents
{
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();
}
