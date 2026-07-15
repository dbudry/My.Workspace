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
