using My.Shared.Dtos.GoogleCalendar;

namespace My.Shared.Rules;

/// <summary>
/// User-facing copy for POST /api/googlecalendar/pullfromgoogle. Settings and
/// admin share this so the same counts never become two different stories.
/// </summary>
public static class CalendarPullResultRules
{
    public const int MaxUnresolvedNotes = 20;

    public static string Headline(CalendarPullResultDto r)
    {
        if (!string.IsNullOrWhiteSpace(r.Error))
            return r.Error!;

        return $"Looked at {r.Scanned} Google event{(r.Scanned == 1 ? "" : "s")} in that date range.";
    }

    public static IReadOnlyList<string> DetailLines(CalendarPullResultDto r)
    {
        var lines = new List<string>();
        if (r.Created > 0)
            lines.Add($"{r.Created} new task{(r.Created == 1 ? " was" : "s were")} added to Tyme.");
        if (r.Updated > 0)
            lines.Add($"{r.Updated} existing task{(r.Updated == 1 ? " was" : "s were")} updated to match Google.");
        if (r.Cancelled > 0)
            lines.Add($"{r.Cancelled} cancelled Google event{(r.Cancelled == 1 ? " was" : "s were")} left on your timesheet — Tyme did not delete {(r.Cancelled == 1 ? "that task" : "those tasks")}.");
        if (r.SkippedNoTag > 0)
            lines.Add($"{r.SkippedNoTag} had no [project tag], so {(r.SkippedNoTag == 1 ? "it" : "they")} stayed private.");
        if (r.SkippedOurs > 0)
            lines.Add($"{r.SkippedOurs} {(r.SkippedOurs == 1 ? "was an event" : "were events")} Tyme already put on your calendar (not imported again).");
        if (r.SkippedDeclinedInvite > 0)
            lines.Add($"{r.SkippedDeclinedInvite} {(r.SkippedDeclinedInvite == 1 ? "was a meeting" : "were meetings")} you declined.");
        if (r.SkippedUnresolvedTag > 0)
            lines.Add($"{r.SkippedUnresolvedTag} had a [tag] that does not match any project.");
        if (r.SkippedMonthSubmitted > 0)
            lines.Add($"{r.SkippedMonthSubmitted} fall in a month you already submitted, so {(r.SkippedMonthSubmitted == 1 ? "it was" : "they were")} left alone.");
        if (r.SkippedNoDates > 0)
            lines.Add($"{r.SkippedNoDates} had no usable start or end time.");
        if (r.Failed > 0)
            lines.Add($"{r.Failed} could not be read from Google. Try the pull again.");
        if (lines.Count == 0 && r.Scanned > 0)
            lines.Add("Nothing needed to change in Tyme.");
        return lines;
    }

    public static string AdminSnackbar(CalendarPullResultDto r)
    {
        if (!string.IsNullOrWhiteSpace(r.Error))
            return r.Error!;
        var details = string.Join(" ", DetailLines(r));
        return $"{Headline(r)} {details}".Trim();
    }
}
