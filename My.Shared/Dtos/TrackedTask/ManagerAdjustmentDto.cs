namespace My.Shared.Dtos.TrackedTask;

/// <summary>
/// Manager-corrected values shown to the employee alongside their original entry
/// (alias overlay or direct correction).
/// </summary>
public class ManagerAdjustmentDto
{
    public string Details { get; set; } = null!;
    public DateTime StartDate { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? OrganizationName { get; set; }
    public string? OrganizationColor { get; set; }
    public string? ProjectGroupName { get; set; }
    public string? ProjectGroupColor { get; set; }
}