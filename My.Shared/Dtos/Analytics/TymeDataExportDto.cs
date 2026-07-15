namespace My.Shared.Dtos.Analytics;

/// <summary>
/// Normalized table extract for Data Extraction Excel export — one sheet per entity.
/// </summary>
public sealed class TymeDataExportDto
{
    public List<ApplicationUserExportRow> ApplicationUsers { get; set; } = [];
    public List<OrganizationExportRow> Organizations { get; set; } = [];
    public List<ProjectGroupExportRow> ProjectGroups { get; set; } = [];
    public List<ProjectExportRow> Projects { get; set; } = [];
    public List<TrackedTaskExportRow> TrackedTasks { get; set; } = [];
    public List<TrackedTaskAliasExportRow> TrackedTaskAliases { get; set; } = [];
    public List<TrackedTaskCorrectionAuditExportRow> TrackedTaskCorrectionAudits { get; set; } = [];
    public List<TimeSubmissionExportRow> TimeSubmissions { get; set; } = [];
}

public sealed class ApplicationUserExportRow
{
    public string Id { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string? Email { get; set; }
    public string? UserName { get; set; }
    public bool IsActive { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset? LastLoginDate { get; set; }
    public DateTimeOffset? LastSignInAt { get; set; }
}

public sealed class OrganizationExportRow
{
    public string OrganizationId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? Note { get; set; }
    public string? Color { get; set; }
    public bool IsActive { get; set; }
    public bool IsArchived { get; set; }
}

public sealed class ProjectGroupExportRow
{
    public string ProjectGroupId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Color { get; set; } = null!;
}

public sealed class ProjectExportRow
{
    public string ProjectId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Slug { get; set; }
    public string? OrganizationId { get; set; }
    public string? DepartmentId { get; set; }
    public string? ProjectGroupId { get; set; }
    public bool IsActive { get; set; }
    public bool IsArchived { get; set; }
    public bool IsSharedAvailability { get; set; }
    public bool IsBillable { get; set; }
}

public sealed class TrackedTaskExportRow
{
    public string TaskId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public double DurationSeconds { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsBillable { get; set; }
    public string? ProjectId { get; set; }
    public string UserId { get; set; } = null!;
    public string? StopwatchItemId { get; set; }
    public string? GoogleEventId { get; set; }
    public bool IsAllDay { get; set; }
    public string? TeamAvailabilityEventId { get; set; }

    // Denormalized for client-side filtering against page criteria.
    public string? OrganizationId { get; set; }
    public string? ProjectGroupId { get; set; }
    public bool IsMonthSubmitted { get; set; }
}

public sealed class TrackedTaskAliasExportRow
{
    public string TrackedTaskAliasId { get; set; } = null!;
    public string TaskId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public DateTime StartDate { get; set; }
    public double DurationSeconds { get; set; }
    public string? ProjectId { get; set; }
    public bool IsBillable { get; set; }
    public string CreatedByUserId { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class TrackedTaskCorrectionAuditExportRow
{
    public string TrackedTaskCorrectionAuditId { get; set; } = null!;
    public string TaskId { get; set; } = null!;
    public string CorrectedByUserId { get; set; } = null!;
    public DateTime CorrectedAtUtc { get; set; }
    public string PreviousName { get; set; } = null!;
    public DateTime PreviousStartDate { get; set; }
    public double PreviousDurationSeconds { get; set; }
    public string? PreviousProjectId { get; set; }
    public bool PreviousIsBillable { get; set; }
    public string NewName { get; set; } = null!;
    public DateTime NewStartDate { get; set; }
    public double NewDurationSeconds { get; set; }
    public string? NewProjectId { get; set; }
    public bool NewIsBillable { get; set; }
}

public sealed class TimeSubmissionExportRow
{
    public string TimeSubmissionId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string SubmittedByUserId { get; set; } = null!;
}