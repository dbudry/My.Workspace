using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

/// <summary>
/// Contract tests for the org / department / project / time archive cluster.
/// If these fail, archive/unarchive behavior was changed — that is not allowed
/// without an explicit product decision.
/// </summary>
public class ArchiveClusterRulesTests
{
    [Fact]
    public void Time_follows_project_archive_flag()
    {
        Assert.True(ArchiveClusterRules.TimeFollowsProjectArchive);
        Assert.False(ArchiveClusterRules.CanLogNewTime(projectArchived: true, organizationArchived: false, departmentArchived: false));
        Assert.False(ArchiveClusterRules.CanLogNewTime(projectArchived: false, organizationArchived: true, departmentArchived: false));
        Assert.False(ArchiveClusterRules.CanLogNewTime(projectArchived: false, organizationArchived: false, departmentArchived: true));
        Assert.True(ArchiveClusterRules.CanLogNewTime(projectArchived: false, organizationArchived: false, departmentArchived: false));
    }

    [Fact]
    public void Archive_organization_takes_departments_and_projects()
    {
        var (org, otherOrg, depts, projects) = SampleGraph();

        ArchiveClusterRules.ArchiveFromOrganization(org, depts, projects);

        Assert.True(org.IsArchived);
        Assert.False(org.IsActive);
        Assert.True(depts.Single(d => d.Id == "d1").IsArchived);
        Assert.True(depts.Single(d => d.Id == "d2").IsArchived);
        Assert.True(projects.Single(p => p.Id == "p1").IsArchived);
        Assert.True(projects.Single(p => p.Id == "p2").IsArchived);
        Assert.True(projects.Single(p => p.Id == "p-dept-only").IsArchived);

        Assert.False(otherOrg.IsArchived);
        Assert.False(depts.Single(d => d.Id == "d-other").IsArchived);
        Assert.False(projects.Single(p => p.Id == "p-other").IsArchived);
    }

    [Fact]
    public void Unarchive_organization_restores_departments_and_projects()
    {
        var (org, _, depts, projects) = SampleGraph();
        ArchiveClusterRules.ArchiveFromOrganization(org, depts, projects);

        ArchiveClusterRules.UnarchiveFromOrganization(org, depts, projects, setActive: true);

        Assert.False(org.IsArchived);
        Assert.True(org.IsActive);
        Assert.All(depts.Where(d => d.OrganizationId == org.Id), d =>
        {
            Assert.False(d.IsArchived);
            Assert.True(d.IsActive);
        });
        Assert.All(projects.Where(p => ArchiveClusterRules.ProjectBelongsToOrganization(p, org.Id, depts)), p =>
        {
            Assert.False(p.IsArchived);
            Assert.True(p.IsActive);
        });
    }

    [Fact]
    public void Unarchive_organization_can_restore_inactive()
    {
        var (org, _, depts, projects) = SampleGraph();
        ArchiveClusterRules.ArchiveFromOrganization(org, depts, projects);

        ArchiveClusterRules.UnarchiveFromOrganization(org, depts, projects, setActive: false);

        Assert.False(org.IsArchived);
        Assert.False(org.IsActive);
        Assert.All(depts.Where(d => d.OrganizationId == org.Id), d =>
        {
            Assert.False(d.IsArchived);
            Assert.False(d.IsActive);
        });
    }

    [Fact]
    public void Archive_project_does_not_archive_organization_or_siblings()
    {
        var (org, _, depts, projects) = SampleGraph();
        var p1 = projects.Single(p => p.Id == "p1");
        var p2 = projects.Single(p => p.Id == "p2");

        ArchiveClusterRules.ArchiveFromProject(p1);

        Assert.True(p1.IsArchived);
        Assert.False(p2.IsArchived);
        Assert.False(org.IsArchived);
        Assert.False(depts.Single(d => d.Id == "d1").IsArchived);
    }

    [Fact]
    public void Unarchive_project_unarchives_its_organization_and_department()
    {
        var (org, _, depts, projects) = SampleGraph();
        ArchiveClusterRules.ArchiveFromOrganization(org, depts, projects);

        var p1 = projects.Single(p => p.Id == "p1");
        var p2 = projects.Single(p => p.Id == "p2");
        var d1 = depts.Single(d => d.Id == "d1");

        ArchiveClusterRules.UnarchiveFromProject(p1, org, d1, setActive: true);

        Assert.False(p1.IsArchived);
        Assert.True(p1.IsActive);
        Assert.False(org.IsArchived);
        Assert.True(org.IsActive);
        Assert.False(d1.IsArchived);
        Assert.True(d1.IsActive);

        // Sibling project stays in the archived bucket.
        Assert.True(p2.IsArchived);
        Assert.True(depts.Single(d => d.Id == "d2").IsArchived);
    }

    [Fact]
    public void Archive_department_takes_its_projects_not_the_org()
    {
        var (org, _, depts, projects) = SampleGraph();
        var d1 = depts.Single(d => d.Id == "d1");

        ArchiveClusterRules.ArchiveFromDepartment(d1, projects);

        Assert.True(d1.IsArchived);
        Assert.True(projects.Single(p => p.Id == "p1").IsArchived);
        Assert.False(org.IsArchived);
        Assert.False(projects.Single(p => p.Id == "p2").IsArchived);
    }

    [Fact]
    public void Unarchive_department_unarchives_org_and_its_projects()
    {
        var (org, _, depts, projects) = SampleGraph();
        ArchiveClusterRules.ArchiveFromOrganization(org, depts, projects);

        var d1 = depts.Single(d => d.Id == "d1");
        ArchiveClusterRules.UnarchiveFromDepartment(d1, org, projects, setActive: true);

        Assert.False(d1.IsArchived);
        Assert.False(org.IsArchived);
        Assert.False(projects.Single(p => p.Id == "p1").IsArchived);
        Assert.True(projects.Single(p => p.Id == "p2").IsArchived);
    }

    private static (
        ArchiveClusterRules.Node Org,
        ArchiveClusterRules.Node OtherOrg,
        List<ArchiveClusterRules.Node> Depts,
        List<ArchiveClusterRules.Node> Projects) SampleGraph()
    {
        var org = Live("acme");
        var other = Live("other-org");
        var d1 = Live("d1", organizationId: org.Id);
        var d2 = Live("d2", organizationId: org.Id);
        var dOther = Live("d-other", organizationId: other.Id);
        var p1 = Live("p1", organizationId: org.Id, departmentId: d1.Id);
        var p2 = Live("p2", organizationId: org.Id, departmentId: d2.Id);
        var pDeptOnly = Live("p-dept-only", organizationId: null, departmentId: d1.Id);
        var pOther = Live("p-other", organizationId: other.Id, departmentId: dOther.Id);

        return (org, other,
            [d1, d2, dOther],
            [p1, p2, pDeptOnly, pOther]);
    }

    private static ArchiveClusterRules.Node Live(
        string id, string? organizationId = null, string? departmentId = null) =>
        new()
        {
            Id = id,
            IsArchived = false,
            IsActive = true,
            OrganizationId = organizationId,
            DepartmentId = departmentId
        };
}
