namespace My.Shared.Dtos.Organization;

/// <summary>
/// Optional body for <c>POST /organizations/{id}/setactive</c>.
/// Used when turning the organization <b>off</b>.
/// </summary>
public class SetActiveOrganizationRequestDto
{
    /// <summary>
    /// When deactivating, also mark projects under this org inactive.
    /// Departments are always marked inactive when the org becomes inactive.
    /// </summary>
    public bool CascadeProjects { get; set; }
}
