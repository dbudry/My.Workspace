namespace My.Shared.Dtos.TrackedTask;

/// <summary>Payload for manager direct in-place correction of a submitted task.</summary>
public class ManagerTimeCorrectionDto
{
    public string Name { get; set; } = null!;
    public DateTime StartDate { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ProjectId { get; set; }
    public bool IsBillable { get; set; }
}