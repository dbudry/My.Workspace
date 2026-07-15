using Microsoft.EntityFrameworkCore;
using My.DAL.Data;
using My.DAL.Models;
using My.DAL.Models.Paging;
using My.DAL.Repository;
using My.Functions;
using Xunit;

namespace My.Tests.Integration;

public class TrackedTaskListMappingTests
{
    /// <summary>
    /// Self-contained: unique user + task per run so parallel SqlServerFact tests
    /// cannot race on the shared INTEGRATION-TEST@LOCAL fixture and empty the page.
    /// </summary>
    [SqlServerFact]
    public async Task GetPaged_tracked_tasks_map_without_throwing()
    {
        var userId = Guid.NewGuid().ToString();
        var taskId = Guid.NewGuid().ToString();
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var now = DateTime.UtcNow;

        await using (var db = IntegrationTestConnection.NewContext())
        {
            db.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = $"map-test-{stamp}@local",
                NormalizedUserName = $"MAP-TEST-{stamp.ToUpperInvariant()}@LOCAL",
                Email = $"map-test-{stamp}@local",
                NormalizedEmail = $"MAP-TEST-{stamp.ToUpperInvariant()}@LOCAL",
                EmailConfirmed = true,
                FirstName = "Map",
                LastName = "Test",
                SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString()
            });
            db.TrackedTasks.Add(new TrackedTask
            {
                TaskId = taskId,
                UserId = userId,
                Name = $"Mapping fixture {stamp}",
                StartDate = now.AddHours(-1),
                EndDate = now,
                Duration = TimeSpan.FromHours(1)
            });
            await db.SaveChangesAsync();
        }

        // Fresh context so we don't depend on the insert context's change tracker.
        await using var readDb = IntegrationTestConnection.NewContext();

        var persisted = await readDb.TrackedTasks.AsNoTracking()
            .AnyAsync(t => t.TaskId == taskId && t.UserId == userId);
        Assert.True(persisted, "Fixture task should be visible on a new DbContext before GetPaged.");

        var readRepo = new BaseRepository<TrackedTask>(readDb);
        var mapper = new AppMapper();

        var parameters = new PagingParameters
        {
            PageNumber = 1,
            PageSize = 10,
            SortBy = "StartDate",
            SortDescending = true
        };

        var page = await readRepo.GetPaged(
            parameters,
            filter: t => t.UserId == userId,
            orderBy: q => q.OrderByDescending(t => t.StartDate),
            includeProperties: "Project.ProjectGroup,Project.Organization");

        Assert.NotEmpty(page);
        Assert.Contains(page, t => t.TaskId == taskId);

        foreach (var task in page)
            mapper.TrackedTaskToDto(task);
    }
}
