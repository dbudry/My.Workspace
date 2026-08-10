using System.ComponentModel.DataAnnotations;
using My.Client.Helpers;
using My.Shared.Dtos.TrackedTask;

namespace My.Client.Models
{
    public class TrackedTask
    {
        public string TaskId { get; set; } = null!;

        [Required]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "Name can not have less then 3 characters and more then 50.")]
        public string Name { get; set; } = null!;

        [Required]
        public TimeSpan Duration { get; set; }

        public int DurationHours
        {
            get => (int)Duration.TotalHours;
            set => Duration = new TimeSpan(value, DurationMinutes, 0);
        }

        public int DurationMinutes
        {
            get => Duration.Minutes;
            set => Duration = new TimeSpan(DurationHours, value, 0);
        }

        [Required]
        public DateTime StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public bool IsAllDay { get; set; }

        public DateTime? StartDateNullable
        {
            get => StartDate;
            set { if (value.HasValue) StartDate = value.Value; }
        }

        public string? ProjectId { get; set; }

        public Project? Project { get; set; }

        public bool IsMonthSubmitted { get; set; }

        public bool IsLocked => IsMonthSubmitted;

        /// <summary>Active stopwatch session — stop before editing start/stop times.</summary>
        public bool IsRunning => !EndDate.HasValue && !IsAllDay;

        public string UserId { get; set; } = null!;

        /// <summary>When set, this row is a stopwatch session linked to a work item.</summary>
        public string? StopwatchItemId { get; set; }

        public bool IsManagerAdjusted { get; set; }

        public string? AdjustmentKind { get; set; }

        public ManagerAdjustmentDto? ManagerAdjustment { get; set; }

        public TrackedTask()
        {
        }

        /// <summary>
        /// Maps an API DTO into UI model times. Timed values are UTC in the database and
        /// are converted to <paramref name="userTimeZone"/> (from UserSettings.TimeZone).
        /// All-day entries stay date-only (no zone shift).
        /// </summary>
        public TrackedTask(TrackedTaskDto trackedTask, TimeZoneInfo? userTimeZone = null)
        {
            TaskId = trackedTask.TaskId;
            Name = trackedTask.Name;
            StopwatchItemId = trackedTask.StopwatchItemId;
            Duration = trackedTask.Duration;
            IsAllDay = trackedTask.IsAllDay;

            if (IsAllDay)
            {
                // Date-only — converting would shift the calendar day west of UTC.
                StartDate = DateTime.SpecifyKind(trackedTask.StartDate.Date, DateTimeKind.Unspecified);
                EndDate = trackedTask.EndDate.HasValue
                    ? DateTime.SpecifyKind(trackedTask.EndDate.Value.Date, DateTimeKind.Unspecified)
                    : null;
            }
            else
            {
                var tz = userTimeZone ?? TimeZoneInfo.Utc;
                StartDate = DateTimeWire.ToUserTime(trackedTask.StartDate, tz);
                EndDate = DateTimeWire.ToUserTime(trackedTask.EndDate, tz);
            }

            ProjectId = trackedTask.ProjectId;
            IsMonthSubmitted = trackedTask.IsMonthSubmitted;
            UserId = trackedTask.UserId;

            if (trackedTask.Project != null)
            {
                Project = new Project(trackedTask.Project);
            }

            IsManagerAdjusted = trackedTask.IsManagerAdjusted;
            AdjustmentKind = trackedTask.AdjustmentKind;
            ManagerAdjustment = trackedTask.ManagerAdjustment;
        }
    }
}
