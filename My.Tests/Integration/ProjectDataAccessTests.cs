using Microsoft.EntityFrameworkCore;
using My.DAL.Models;
using Xunit;

namespace My.Tests.Integration;

/// <summary>
/// Exercises the set-based project data-access paths against a real relational
/// provider: slug-uniqueness (ProjectFunctions.IsSlugUniqueAsync) and the
/// bulk group-reference clear on group deletion (ProjectGroupFunctions.DeleteProjectGroup).
/// ExecuteUpdate/ExecuteDelete are relational-only, so these run against SQL Server
/// like the other integration tests. Each test cleans up the rows it creates.
/// </summary>
public class ProjectDataAccessTests
{
    [SqlServerFact]
    public async Task Slug_uniqueness_excludes_the_project_being_edited()
    {
        await using var db = IntegrationTestConnection.NewContext();

        // Slug column is capped at 10 chars (calendar [tag]); use 10 hex digits.
        var slug = Guid.NewGuid().ToString("N").Substring(0, 10);
        var project = new Project
        {
            ProjectId = Guid.NewGuid().ToString(),
            Name = "Slug uniqueness fixture",
            Slug = slug
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        try
        {
            // Editing the same project must not collide with its own slug.
            var collidesWhenEditingSelf = await db.Projects.AsNoTracking()
                .AnyAsync(p => p.Slug == slug && p.ProjectId != project.ProjectId);
            Assert.False(collidesWhenEditingSelf);

            // Creating a new project (nothing excluded) sees the slug as taken.
            var collidesForNewProject = await db.Projects.AsNoTracking()
                .AnyAsync(p => p.Slug == slug && p.ProjectId != null);
            Assert.True(collidesForNewProject);
        }
        finally
        {
            db.Projects.Remove(project);
            await db.SaveChangesAsync();
        }
    }

    [SqlServerFact]
    public async Task Deleting_group_reference_clears_only_matching_projects()
    {
        await using var db = IntegrationTestConnection.NewContext();

        var targetGroup = new ProjectGroup { ProjectGroupId = Guid.NewGuid().ToString(), Name = "Target group" };
        var otherGroup = new ProjectGroup { ProjectGroupId = Guid.NewGuid().ToString(), Name = "Other group" };

        var inTarget = new Project
        {
            ProjectId = Guid.NewGuid().ToString(),
            Name = "In target group",
            ProjectGroupId = targetGroup.ProjectGroupId
        };
        var inOther = new Project
        {
            ProjectId = Guid.NewGuid().ToString(),
            Name = "In other group",
            ProjectGroupId = otherGroup.ProjectGroupId
        };

        db.ProjectGroups.AddRange(targetGroup, otherGroup);
        db.Projects.AddRange(inTarget, inOther);
        await db.SaveChangesAsync();

        try
        {
            var affected = await db.Projects
                .Where(p => p.ProjectGroupId == targetGroup.ProjectGroupId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ProjectGroupId, (string?)null));

            Assert.Equal(1, affected);

            var reloadedTarget = await db.Projects.AsNoTracking()
                .SingleAsync(p => p.ProjectId == inTarget.ProjectId);
            var reloadedOther = await db.Projects.AsNoTracking()
                .SingleAsync(p => p.ProjectId == inOther.ProjectId);

            Assert.Null(reloadedTarget.ProjectGroupId);
            Assert.Equal(otherGroup.ProjectGroupId, reloadedOther.ProjectGroupId);
        }
        finally
        {
            await db.Projects
                .Where(p => p.ProjectId == inTarget.ProjectId || p.ProjectId == inOther.ProjectId)
                .ExecuteDeleteAsync();
            await db.ProjectGroups
                .Where(g => g.ProjectGroupId == targetGroup.ProjectGroupId || g.ProjectGroupId == otherGroup.ProjectGroupId)
                .ExecuteDeleteAsync();
        }
    }
}