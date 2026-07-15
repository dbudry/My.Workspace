using My.Client.Models;
using My.Shared.Dtos.StopwatchItem;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Rules;

namespace My.Client.Helpers
{
    public static class TaskListRowBuilder
    {
        public static TaskListRow FromStopwatch(StopwatchItemDto item)
        {
            var duration = item.TotalDuration;
            if (item.IsRunning && item.ActiveSessionStartDate.HasValue)
                duration += StopwatchRules.ElapsedForActiveSession(item.ActiveSessionStartDate.Value, null);

            return new TaskListRow
            {
                Kind = TaskListRowKind.Stopwatch,
                Name = item.Name,
                ProjectDisplayName = ProjectDisplayHelper.FromDto(item.Project),
                Duration = duration,
                SortDate = item.LastWorkedAt,
                DisplayDate = item.LastWorkedAt.ToLocalTime(),
                IsRunning = item.IsRunning,
                StopwatchItem = item
            };
        }

        public static TaskListRow FromManual(TrackedTask task) =>
            new()
            {
                Kind = TaskListRowKind.Manual,
                Name = task.Name,
                ProjectDisplayName = task.Project?.DisplayName,
                Duration = task.Duration,
                SortDate = task.StartDate,
                DisplayDate = task.StartDate,
                IsAllDay = task.IsAllDay,
                IsLocked = task.IsLocked,
                ManualTask = task
            };

        public static TaskListRow? FromManagerAdjustmentOverlay(TrackedTask task)
        {
            var adjustment = task.ManagerAdjustment;
            if (adjustment == null || task.AdjustmentKind is not ("Alias" or "Direct"))
                return null;

            var isAlias = task.AdjustmentKind == "Alias";

            return new TaskListRow
            {
                Kind = TaskListRowKind.Manual,
                Name = adjustment.Name,
                ProjectDisplayName = adjustment.ProjectName,
                Duration = adjustment.Duration,
                SortDate = adjustment.StartDate.ToLocalTime(),
                DisplayDate = adjustment.StartDate.ToLocalTime(),
                IsLocked = task.IsLocked,
                IsManagerAdjustmentOverlay = isAlias,
                IsManagerAdjusted = !isAlias,
                ManualTask = task
            };
        }

        public static IEnumerable<TaskListRow> ExpandManualRows(TrackedTask task)
        {
            yield return FromManual(task);
            var overlay = FromManagerAdjustmentOverlay(task);
            if (overlay != null)
                yield return overlay;
        }

    }
}