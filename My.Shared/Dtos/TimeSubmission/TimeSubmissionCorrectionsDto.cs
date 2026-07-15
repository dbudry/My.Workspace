namespace My.Shared.Dtos.TimeSubmission;

public class TimeSubmissionCorrectionItemDto
{
    public string TaskId { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public string TaskName { get; set; } = null!;
    public string OriginalName { get; set; } = null!;
    public DateTime OriginalStartDate { get; set; }
    public double OriginalDurationSeconds { get; set; }
    public string? OriginalProjectName { get; set; }
    public string AdjustedName { get; set; } = null!;
    public DateTime AdjustedStartDate { get; set; }
    public double AdjustedDurationSeconds { get; set; }
    public string? AdjustedProjectName { get; set; }
}

public class TimeSubmissionCorrectionsDto
{
    public List<TimeSubmissionCorrectionItemDto> Items { get; set; } = new();
    public int DirectCorrectionCount { get; set; }
}

public class UnsubmitTimeSubmissionDto
{
    /// <summary>ApplyToTasks or KeepOriginals — only applies when alias corrections exist.</summary>
    public string? AliasReconciliation { get; set; }

    /// <summary>Task ids whose alias rows should be removed. Null or empty = all aliases in the month.</summary>
    public List<string>? DeleteAliasTaskIds { get; set; }
}