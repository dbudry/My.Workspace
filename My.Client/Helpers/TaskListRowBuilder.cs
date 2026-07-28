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
                OrganizationName = item.Project?.OrganizationName,
                OrganizationColor = item.Project?.OrganizationColor,
                ProjectGroupName = item.Project?.ProjectGroupName,
                ProjectGroupColor = item.Project?.ProjectGroupColor,
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
                OrganizationName = task.Project?.OrganizationName,
                OrganizationColor = task.Project?.OrganizationColor,
                ProjectGroupName = task.Project?.ProjectGroupName,
                ProjectGroupColor = task.Project?.ProjectGroupColor,
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
            var startLocal = adjustment.StartDate.Kind == DateTimeKind.Utc
                ? adjustment.StartDate.ToLocalTime()
                : adjustment.StartDate;

            return new TaskListRow
            {
                Kind = TaskListRowKind.Manual,
                Name = adjustment.Name,
                ProjectDisplayName = adjustment.ProjectName,
                OrganizationName = adjustment.OrganizationName,
                OrganizationColor = adjustment.OrganizationColor,
                ProjectGroupName = adjustment.ProjectGroupName,
                ProjectGroupColor = adjustment.ProjectGroupColor,
                Duration = adjustment.Duration,
                SortDate = startLocal,
                DisplayDate = startLocal,
                IsLocked = task.IsLocked,
                IsManagerAdjustmentOverlay = isAlias,
                IsManagerAdjusted = !isAlias,
                ManualTask = task,
                // Snapshot of adjusted values for dialog open (ManualTask holds employee original).
                OverlayProjectId = adjustment.ProjectId,
                OverlayStartDate = startLocal,
                OverlayEndDate = adjustment.Duration > TimeSpan.Zero
                    ? startLocal + adjustment.Duration
                    : null,
                OverlayDuration = adjustment.Duration,
                OverlayName = adjustment.Name
            };
        }

        public static IEnumerable<TaskListRow> ExpandManualRows(
            TrackedTask task,
            EmployeeTimeDisplayMode displayMode = EmployeeTimeDisplayMode.Both)
        {
            var hasAdjustment = task.ManagerAdjustment != null
                && task.AdjustmentKind is "Alias" or "Direct";

            if (EmployeeTimeDisplayModeRules.IncludeOriginal(displayMode, hasAdjustment))
                yield return FromManual(task);

            if (EmployeeTimeDisplayModeRules.IncludeAdjustmentOverlay(displayMode, hasAdjustment))
            {
                var overlay = FromManagerAdjustmentOverlay(task);
                if (overlay != null)
                    yield return overlay;
            }
        }
    }
}
