using System.ComponentModel.DataAnnotations;
using My.Shared.Dtos.Project;

namespace My.Shared.Dtos.TrackedTask
{
    public class CreateTrackedTaskDto
    {
        [Required]
        [StringLength(50, ErrorMessage = "Name of project is too long.", MinimumLength = 2)]
        public string Name { get; set; } = null!;

        [Required]
        public DateTime StartDate { get; set; }

        public TimeSpan Duration { get; set; }

        /// <summary>
        /// When true, the entry covers full calendar days. The server treats <see cref="StartDate"/>
        /// as the first day and <c>EndDate</c> as the last day inclusive; <see cref="Duration"/>
        /// is derived as <c>WorkdayHours × days</c> at save time.
        /// </summary>
        public bool IsAllDay { get; set; }

        /// <summary>
        /// Last calendar day of the entry. Only honored when <see cref="IsAllDay"/> is true.
        /// Null for timed entries — the server derives <c>EndDate</c> from <see cref="StartDate"/> + <see cref="Duration"/>.
        /// </summary>
        public DateTime? EndDate { get; set; }

        public string? ProjectId { get; set; }

        public ProjectDto? Project { get; set; }
    }
}
