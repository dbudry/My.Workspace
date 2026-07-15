namespace My.DAL.Models;

/// <summary>
/// Manager direct correction audit — one row per TaskId. Stores who/when and
/// before/after values for manager review only; not exposed to employees.
/// </summary>
public class TrackedTaskCorrectionAudit
{
    public string TrackedTaskCorrectionAuditId { get; set; } = null!;

    public string TaskId { get; set; } = null!;
    public TrackedTask Task { get; set; } = null!;

    public string CorrectedByUserId { get; set; } = null!;
    public DateTime CorrectedAtUtc { get; set; }

    public string PreviousName { get; set; } = null!;
    public DateTime PreviousStartDate { get; set; }
    public TimeSpan PreviousDuration { get; set; }
    public string? PreviousProjectId { get; set; }
    public bool PreviousIsBillable { get; set; }

    public string NewName { get; set; } = null!;
    public DateTime NewStartDate { get; set; }
    public TimeSpan NewDuration { get; set; }
    public string? NewProjectId { get; set; }
    public bool NewIsBillable { get; set; }
}