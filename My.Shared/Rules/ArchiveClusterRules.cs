namespace My.Shared.Rules;

/// <summary>
/// Organization, department, project, and time logged on those projects are one
/// archive cluster on the way <b>in</b> (archive org takes departments and projects).
/// Unarchive org always restores departments; projects restore only when asked.
///
/// Locked by <c>ArchiveClusterRulesTests</c> — do not weaken these rules without
/// an explicit product change.
///
/// Time has no own <c>IsArchived</c> column. Existing entries stay on Tasks /
/// Stopwatch / Calendar / Reports so totals do not change. New time cannot be
/// logged while the project (or its org/dept) is archived.
/// </summary>
public static class ArchiveClusterRules
{
    public sealed class Node
    {
        public string Id { get; init; } = "";
        public bool IsArchived { get; set; }
        public bool IsActive { get; set; }
        public string? OrganizationId { get; init; }
        public string? DepartmentId { get; init; }
    }

    /// <summary>Time rides with the project; there is no separate time-archive flag.</summary>
    public static bool TimeFollowsProjectArchive => true;

    public static bool CanLogNewTime(bool projectArchived, bool organizationArchived, bool departmentArchived) =>
        !projectArchived && !organizationArchived && !departmentArchived;

    public static bool ProjectBelongsToOrganization(Node project, string organizationId, IEnumerable<Node> departments)
    {
        if (project.OrganizationId == organizationId)
            return true;

        if (string.IsNullOrEmpty(project.DepartmentId))
            return false;

        return departments.Any(d =>
            d.Id == project.DepartmentId && d.OrganizationId == organizationId);
    }

    public static void PutInArchivedBucket(Node node)
    {
        node.IsArchived = true;
        node.IsActive = false;
    }

    public static void RestoreFromArchivedBucket(Node node, bool setActive)
    {
        node.IsArchived = false;
        node.IsActive = setActive;
    }

    /// <summary>
    /// Archive org → archive every department and every project that uses it.
    /// </summary>
    public static void ArchiveFromOrganization(Node organization, IList<Node> departments, IList<Node> projects)
    {
        PutInArchivedBucket(organization);
        foreach (var dept in departments.Where(d => d.OrganizationId == organization.Id))
            PutInArchivedBucket(dept);
        foreach (var project in projects.Where(p => ProjectBelongsToOrganization(p, organization.Id, departments)))
            PutInArchivedBucket(project);
    }

    /// <summary>
    /// Unarchive org → unarchive every department that uses it. Projects restore
    /// only when <paramref name="unarchiveProjects"/> is true.
    /// </summary>
    public static void UnarchiveFromOrganization(
        Node organization, IList<Node> departments, IList<Node> projects, bool setActive, bool unarchiveProjects = true)
    {
        RestoreFromArchivedBucket(organization, setActive);
        foreach (var dept in departments.Where(d => d.OrganizationId == organization.Id))
            RestoreFromArchivedBucket(dept, setActive);
        if (!unarchiveProjects) return;
        foreach (var project in projects.Where(p => ProjectBelongsToOrganization(p, organization.Id, departments)))
            RestoreFromArchivedBucket(project, setActive);
    }

    /// <summary>Archive department → archive projects under that department. Does not archive the org.</summary>
    public static void ArchiveFromDepartment(Node department, IList<Node> projects)
    {
        PutInArchivedBucket(department);
        foreach (var project in projects.Where(p => p.DepartmentId == department.Id))
            PutInArchivedBucket(project);
    }

    /// <summary>
    /// Unarchive department → unarchive the org (if archived), the department, and
    /// projects under that department.
    /// </summary>
    public static void UnarchiveFromDepartment(
        Node department, Node? organization, IList<Node> projects, bool setActive)
    {
        RestoreFromArchivedBucket(department, setActive);
        if (organization != null)
            RestoreFromArchivedBucket(organization, setActive);
        foreach (var project in projects.Where(p => p.DepartmentId == department.Id))
            RestoreFromArchivedBucket(project, setActive);
    }

    /// <summary>Archive project only. Does not archive the org or sibling projects.</summary>
    public static void ArchiveFromProject(Node project) => PutInArchivedBucket(project);

    /// <summary>
    /// Unarchive project → unarchive the org and department that it uses (if archived),
    /// plus the project. Sibling projects stay archived.
    /// </summary>
    public static void UnarchiveFromProject(Node project, Node? organization, Node? department, bool setActive)
    {
        RestoreFromArchivedBucket(project, setActive);
        if (organization != null)
            RestoreFromArchivedBucket(organization, setActive);
        if (department != null)
            RestoreFromArchivedBucket(department, setActive);
    }
}
