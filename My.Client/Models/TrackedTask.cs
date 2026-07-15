using System.ComponentModel.DataAnnotations;
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

        public TrackedTask(TrackedTaskDto trackedTask)
        {
            TaskId = trackedTask.TaskId;
            Name = trackedTask.Name;
            StopwatchItemId = trackedTask.StopwatchItemId;
            Duration = trackedTask.Duration;
            IsAllDay = trackedTask.IsAllDay;

            // All-day entries are date-only — the day the user picked. Converting to local
            // time shifted them by the UTC offset and made a "May 20" entry render on
            // "May 19" for anyone west of UTC. Skip the conversion and preserve the date.
            if (IsAllDay)
            {
                StartDate = DateTime.SpecifyKind(trackedTask.StartDate.Date, DateTimeKind.Unspecified);
                EndDate = trackedTask.EndDate.HasValue
                    ? DateTime.SpecifyKind(trackedTask.EndDate.Value.Date, DateTimeKind.Unspecified)
                    : null;
            }
            else
            {
                StartDate = trackedTask.StartDate.ToLocalTime();
                EndDate = trackedTask.EndDate?.ToLocalTime();
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
