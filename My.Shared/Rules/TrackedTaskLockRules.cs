namespace My.Shared.Rules;

/// <summary>
/// Copy for a read-only task dialog when the month is submitted.
/// </summary>
public static class TrackedTaskLockRules
{
    public const string SubmittedMonthMessage =
        "This time is in a submitted month, so it can't be edited here. Unsubmit that month from Tyme → Submit, then come back to Tasks.";

    public const string OpenSubmitLabel = "Open Submit";

    public const string SubmitPagePath = "tyme/submit";
}
