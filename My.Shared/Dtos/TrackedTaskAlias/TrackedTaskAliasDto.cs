namespace My.Shared.Dtos.TrackedTaskAlias
{
    public class TrackedTaskAliasDto
    {
        public string TrackedTaskAliasId { get; set; } = null!;
        public string TaskId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public DateTime StartDate { get; set; }
        public TimeSpan Duration { get; set; }
        public string? ProjectId { get; set; }
        public string? ProjectName { get; set; }
        public bool IsBillable { get; set; }
        public string CreatedByUserId { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class UpsertTrackedTaskAliasDto
    {
        public string Name { get; set; } = null!;
        public DateTime StartDate { get; set; }
        public TimeSpan Duration { get; set; }
        public string? ProjectId { get; set; }
        public bool IsBillable { get; set; }
    }
}
