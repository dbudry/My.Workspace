using My.Client.Models;
using My.Shared.Dtos.StopwatchItem;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Rules;

namespace My.Client.Helpers
{
    public static class TaskListRowBuilder
    {
        public static TaskListRow FromStopwatch(StopwatchItemDto item, TimeZoneInfo? userTimeZone = null)
        {
            var duration = item.TotalDuration;
            if (item.IsRunning && item.ActiveSessionStartDate.HasValue)
                duration += StopwatchRules.ElapsedForActiveSession(item.ActiveSessionStartDate.Value, null);

            var tz = userTimeZone ?? TimeZoneInfo.Utc;
            var lastWorked = DateTimeWire.ToUserTime(item.LastWorkedAt, tz);

            return new TaskListRow
            {
                Kind = TaskListRowKind.Stopwatch,
                Name = item.Name,
                ProjectName = item.Project?.Name,
                ProjectDisplayName = ProjectDisplayHelper.FromDto(item.Project),
                OrganizationName = item.Project?.OrganizationName,
                OrganizationColor = item.Project?.OrganizationColor,
                ProjectGroupName = item.Project?.ProjectGroupName,
                ProjectGroupColor = item.Project?.ProjectGroupColor,
                Duration = duration,
                SortDate = lastWorked,
                DisplayDate = lastWorked,
                IsRunning = item.IsRunning,
                StopwatchItem = item
            };
        }

        public static TaskListRow FromManual(TrackedTask task) =>
            new()
            {
                Kind = TaskListRowKind.Manual,
                Name = task.Name,
                ProjectName = task.Project?.Name,
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

        public static TaskListRow? FromManagerAdjustmentOverlay(TrackedTask task, TimeZoneInfo? userTimeZone = null)
        {
            var adjustment = task.ManagerAdjustment;
            if (adjustment == null || task.AdjustmentKind is not ("Alias" or "Direct"))
                return null;

            var isAlias = task.AdjustmentKind == "Alias";
            // Overlay times arrive as UTC instants from the API.
            var startLocal = DateTimeWire.ToUserTime(adjustment.StartDate, userTimeZone ?? TimeZoneInfo.Utc);

            return new TaskListRow
            {
                Kind = TaskListRowKind.Manual,
                Name = adjustment.Name,
                ProjectName = adjustment.ProjectName,
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
            EmployeeTimeDisplayMode displayMode = EmployeeTimeDisplayMode.Both,
            TimeZoneInfo? userTimeZone = null)
        {
            var hasAdjustment = task.ManagerAdjustment != null
                && task.AdjustmentKind is "Alias" or "Direct";

            if (EmployeeTimeDisplayModeRules.IncludeOriginal(displayMode, hasAdjustment))
                yield return FromManual(task);

            if (EmployeeTimeDisplayModeRules.IncludeAdjustmentOverlay(displayMode, hasAdjustment))
            {
                var overlay = FromManagerAdjustmentOverlay(task, userTimeZone);
                if (overlay != null)
                    yield return overlay;
            }
        }

        /// <summary>
        /// Rows for Tasks → Weekly: manuals (with original/adjusted display mode) plus
        /// stopwatch sessions as editable manual-shaped rows, sorted by start then name.
        /// </summary>
        public static List<TaskListRow> FromWeekTasks(
            IEnumerable<TrackedTask> tasks,
            EmployeeTimeDisplayMode displayMode = EmployeeTimeDisplayMode.Both,
            TimeZoneInfo? userTimeZone = null)
        {
            var rows = new List<TaskListRow>();
            foreach (var task in tasks
                         .OrderBy(t => t.StartDate)
                         .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(task.StopwatchItemId))
                {
                    rows.Add(FromManual(task));
                    continue;
                }

                rows.AddRange(ExpandManualRows(task, displayMode, userTimeZone));
            }

            return rows;
        }
    }
}
