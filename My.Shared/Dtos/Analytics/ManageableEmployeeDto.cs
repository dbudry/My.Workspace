namespace My.Shared.Dtos.Analytics;

/// <summary>Employee picker row for manager Tyme reports (UserId + display name).</summary>
public sealed class ManageableEmployeeDto
{
    public string UserId { get; set; } = null!;

    public string UserName { get; set; } = null!;
}