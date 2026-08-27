using Microsoft.EntityFrameworkCore;
using My.DAL.Data;
using My.DAL.Models;
using My.Shared.Rules;

namespace My.Functions.Helpers;

/// <summary>
/// Loads the org / department / project cluster and applies
/// <see cref="ArchiveClusterRules"/>. Time has no archive flag — it follows the project.
/// </summary>
internal static class ArchiveClusterApplier
{
    internal static async Task ArchiveFromOrganizationAsync(ApplicationDbContext db, Organization org)
    {
        var (depts, deptNodes, projects, projectNodes) = await LoadOrgGraphAsync(db, org.OrganizationId);
        var orgNode = ToNode(org);
        ArchiveClusterRules.ArchiveFromOrganization(orgNode, deptNodes, projectNodes);
        WriteBack(org, orgNode);
        WriteBack(depts, deptNodes);
        WriteBack(projects, projectNodes);
    }

    internal static async Task UnarchiveFromOrganizationAsync(
        ApplicationDbContext db, Organization org, bool setActive, bool unarchiveProjects)
    {
        var (depts, deptNodes, projects, projectNodes) = await LoadOrgGraphAsync(db, org.OrganizationId);
        var orgNode = ToNode(org);
        ArchiveClusterRules.UnarchiveFromOrganization(orgNode, deptNodes, projectNodes, setActive, unarchiveProjects);
        WriteBack(org, orgNode);
        WriteBack(depts, deptNodes);
        WriteBack(projects, projectNodes);
    }

    internal static async Task ArchiveFromDepartmentAsync(ApplicationDbContext db, Department dept)
    {
        var projects = await LoadDeptProjectsAsync(db, dept.DepartmentId);
        var deptNode = ToNode(dept);
        var projectNodes = projects.Select(ToNode).ToList();
        ArchiveClusterRules.ArchiveFromDepartment(deptNode, projectNodes);
        WriteBack(dept, deptNode);
        WriteBack(projects, projectNodes);
    }

    internal static async Task UnarchiveFromDepartmentAsync(
        ApplicationDbContext db, Department dept, Organization? org, bool setActive)
    {
        var projects = await LoadDeptProjectsAsync(db, dept.DepartmentId);
        var deptNode = ToNode(dept);
        var orgNode = org == null ? null : ToNode(org);
        var projectNodes = projects.Select(ToNode).ToList();
        ArchiveClusterRules.UnarchiveFromDepartment(deptNode, orgNode, projectNodes, setActive);
        WriteBack(dept, deptNode);
        if (org != null && orgNode != null)
            WriteBack(org, orgNode);
        WriteBack(projects, projectNodes);
    }

    internal static void ArchiveFromProject(Project project)
    {
        var node = ToNode(project);
        ArchiveClusterRules.ArchiveFromProject(node);
        WriteBack(project, node);
    }

    internal static void UnarchiveFromProject(Project project, Organization? org, Department? dept, bool setActive)
    {
        var projectNode = ToNode(project);
        var orgNode = org == null ? null : ToNode(org);
        var deptNode = dept == null ? null : ToNode(dept);
        ArchiveClusterRules.UnarchiveFromProject(projectNode, orgNode, deptNode, setActive);
        WriteBack(project, projectNode);
        if (org != null && orgNode != null)
            WriteBack(org, orgNode);
        if (dept != null && deptNode != null)
            WriteBack(dept, deptNode);
    }

    private static async Task<(
        List<Department> Depts,
        List<ArchiveClusterRules.Node> DeptNodes,
        List<Project> Projects,
        List<ArchiveClusterRules.Node> ProjectNodes)> LoadOrgGraphAsync(
        ApplicationDbContext db, string organizationId)
    {
        var depts = await db.Departments
            .Where(d => d.OrganizationId == organizationId)
            .ToListAsync();
        var deptIds = depts.Select(d => d.DepartmentId).ToList();
        var projects = await db.Projects
            .Where(p => p.OrganizationId == organizationId
                || (p.DepartmentId != null && deptIds.Contains(p.DepartmentId)))
            .ToListAsync();
        return (depts, depts.Select(ToNode).ToList(), projects, projects.Select(ToNode).ToList());
    }

    private static Task<List<Project>> LoadDeptProjectsAsync(ApplicationDbContext db, string departmentId) =>
        db.Projects.Where(p => p.DepartmentId == departmentId).ToListAsync();

    private static ArchiveClusterRules.Node ToNode(Organization o) => new()
    {
        Id = o.OrganizationId,
        IsArchived = o.IsArchived,
        IsActive = o.IsActive
    };

    private static ArchiveClusterRules.Node ToNode(Department d) => new()
    {
        Id = d.DepartmentId,
        IsArchived = d.IsArchived,
        IsActive = d.IsActive,
        OrganizationId = d.OrganizationId
    };

    private static ArchiveClusterRules.Node ToNode(Project p) => new()
    {
        Id = p.ProjectId,
        IsArchived = p.IsArchived,
        IsActive = p.IsActive,
        OrganizationId = p.OrganizationId,
        DepartmentId = p.DepartmentId
    };

    private static void WriteBack(Organization o, ArchiveClusterRules.Node n)
    {
        o.IsArchived = n.IsArchived;
        o.IsActive = n.IsActive;
    }

    private static void WriteBack(Department d, ArchiveClusterRules.Node n)
    {
        d.IsArchived = n.IsArchived;
        d.IsActive = n.IsActive;
    }

    private static void WriteBack(Project p, ArchiveClusterRules.Node n)
    {
        p.IsArchived = n.IsArchived;
        p.IsActive = n.IsActive;
    }

    private static void WriteBack(IReadOnlyList<Department> entities, IReadOnlyList<ArchiveClusterRules.Node> nodes)
    {
        var byId = nodes.ToDictionary(n => n.Id);
        foreach (var e in entities)
            if (byId.TryGetValue(e.DepartmentId, out var n))
                WriteBack(e, n);
    }

    private static void WriteBack(IReadOnlyList<Project> entities, IReadOnlyList<ArchiveClusterRules.Node> nodes)
    {
        var byId = nodes.ToDictionary(n => n.Id);
        foreach (var e in entities)
            if (byId.TryGetValue(e.ProjectId, out var n))
                WriteBack(e, n);
    }
}
