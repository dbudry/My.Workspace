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

    public string PreviousDetails { get; set; } = null!;
    public DateTime PreviousStartDate { get; set; }

    /// <summary>
    /// SQL <c>time</c> — same 24h ceiling as <see cref="TrackedTask.Duration"/>. For a
    /// long all-day original (<see cref="PreviousIsAllDay"/> true, 24h+), this reads 0;
    /// use <see cref="My.Shared.Rules.AllDayEntryRules.EffectiveDuration"/> with
    /// <see cref="PreviousStartDate"/>/<see cref="PreviousEndDate"/>/<see cref="PreviousIsAllDay"/>
    /// to get the real value, never this field directly.
    /// </summary>
    public TimeSpan PreviousDuration { get; set; }

    /// <summary>Only meaningful when <see cref="PreviousIsAllDay"/> is true.</summary>
    public DateTime? PreviousEndDate { get; set; }

    public bool PreviousIsAllDay { get; set; }

    public string? PreviousProjectId { get; set; }
    public bool PreviousIsBillable { get; set; }

    public string NewDetails { get; set; } = null!;
    public DateTime NewStartDate { get; set; }
    public TimeSpan NewDuration { get; set; }
    public string? NewProjectId { get; set; }
    public bool NewIsBillable { get; set; }
}