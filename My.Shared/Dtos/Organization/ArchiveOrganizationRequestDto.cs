namespace My.Shared.Dtos.Organization;

/// <summary>
/// Body for <c>POST /organizations/{id}/archive</c>.
/// </summary>
public class ArchiveOrganizationRequestDto
{
    /// <summary>
    /// When unarchiving, set the organization Active. Default true.
    /// Ignored when the request is archiving. Archiving always takes departments
    /// and projects with the org (no flag).
    /// </summary>
    public bool SetActive { get; set; } = true;
}

