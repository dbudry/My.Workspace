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

    /// <summary>
    /// When unarchiving, also restore projects under this org. Default false —
    /// departments always come back; projects only when the caller has Manager:Tyme+
    /// and confirmed in the UI. Ignored when archiving.
    /// </summary>
    public bool UnarchiveProjects { get; set; }
}

