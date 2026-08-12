namespace My.Shared.Dtos.TimeSubmission
{
    public class TimeSubmissionDto
    {
        public string TimeSubmissionId { get; set; } = null!;
        public string UserId { get; set; } = null!;
        public int Year { get; set; }
        public int Month { get; set; }
        public DateTime SubmittedAt { get; set; }
        public string SubmittedByUserId { get; set; } = null!;
    }

    public class CreateTimeSubmissionDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
    }

    public class OverdueMonthDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
    }

    /// <summary>
    /// A month the current user can submit: they have at least one tracked task in it
    /// and it isn't submitted yet. Unlike <see cref="OverdueMonthDto"/> this is not
    /// restricted to months that have already ended — <see cref="IsEarly"/> flags a
    /// current/future month so the UI can label it distinctly from a normal overdue one.
    /// Returned by GET /api/timesubmissions/eligible.
    /// </summary>
    public class EligibleMonthDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public bool IsEarly { get; set; }
    }

    /// <summary>
    /// One row in the manager team-submissions view: a single (user × month) pairing
    /// with its submitted/unsubmitted status. Returned by GET /api/timesubmissions/team.
    /// </summary>
    public class TeamSubmissionRowDto
    {
        public string UserId { get; set; } = null!;
        public string UserName { get; set; } = null!;
        public int Year { get; set; }
        public int Month { get; set; }
        public bool IsSubmitted { get; set; }
        /// <summary>When IsSubmitted=true. Null otherwise.</summary>
        public DateTime? SubmittedAt { get; set; }
        /// <summary>Set when IsSubmitted=true — needed by the manager Unsubmit button.</summary>
        public string? TimeSubmissionId { get; set; }
    }
}
