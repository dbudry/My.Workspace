namespace My.Shared.Dtos.TimeSubmission;

/// <summary>
/// Manager/Admin:Tyme submits a month for another user (requires
/// <c>TymeAllowManagerSubmitOnBehalf</c> and team-scope visibility).
/// </summary>
public class CreateManagerTimeSubmissionDto
{
    public string UserId { get; set; } = null!;
    public int Year { get; set; }
    public int Month { get; set; }
}
