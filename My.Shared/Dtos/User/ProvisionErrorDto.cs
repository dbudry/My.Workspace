namespace My.Shared.Dtos.User;

/// <summary>
/// Body returned by <c>POST /users/provision</c> on permanent failure (403 etc.)
/// so the SPA can show a real reason instead of a generic cold-start message.
/// </summary>
public class ProvisionErrorDto
{
    /// <summary>Stable machine code (e.g. <c>not_provisioned</c>).</summary>
    public string Code { get; set; } = null!;

    /// <summary>User-safe explanation suitable for the dashboard banner.</summary>
    public string Message { get; set; } = null!;
}
