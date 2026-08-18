namespace My.Shared.Rules;

/// <summary>
/// Maps a stop/delete race (row vanished between load and SaveChanges) to an HTTP outcome.
/// Friday 2026-08-14 App Insights: concurrent DELETE + POST /stop on the same item
/// threw <c>DbUpdateConcurrencyException</c> and the host returned 500.
/// </summary>
public static class StopwatchMutationRules
{
    public enum StopConflict
    {
        ItemGone,
        SessionAlreadyStopped
    }

    public static StopConflict ClassifyStopConflict(bool itemStillExists) =>
        itemStillExists ? StopConflict.SessionAlreadyStopped : StopConflict.ItemGone;
}
