namespace My.DAL.Models
{
    public class TimeSubmission
    {
        public string TimeSubmissionId { get; set; } = null!;
        public string UserId { get; set; } = null!;
        public ApplicationUser User { get; set; } = null!;
        public int Year { get; set; }
        public int Month { get; set; }
        public DateTime SubmittedAt { get; set; }
        public string SubmittedByUserId { get; set; } = null!;
    }
}
