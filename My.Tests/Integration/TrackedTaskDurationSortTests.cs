using Microsoft.EntityFrameworkCore;
using My.DAL.Models;
using My.Functions.Helpers;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Integration;

public class TrackedTaskDurationSortTests
{
    [SqlServerFact]
    public async Task OrderBy_duration_translates_and_places_long_all_day_after_timed()
    {
        var userId = Guid.NewGuid().ToString();
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var start = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Unspecified);

        await using (var db = IntegrationTestConnection.NewContext())
        {
            db.Users.Add(NewUser(userId, stamp));
            db.TrackedTasks.AddRange(
                new TrackedTask
                {
                    TaskId = Guid.NewGuid().ToString(),
                    UserId = userId,
                    Details = "timed-short",
                    StartDate = start.AddHours(9),
                    EndDate = start.AddHours(10),
                    Duration = TimeSpan.FromHours(1),
                    IsAllDay = false
                },
                new TrackedTask
                {
                    TaskId = Guid.NewGuid().ToString(),
                    UserId = userId,
                    Details = "all-day-three-days",
                    StartDate = start,
                    EndDate = start.AddDays(2).AddHours(23).AddMinutes(59),
                    Duration = TimeSpan.Zero,
                    IsAllDay = true
                });
            await db.SaveChangesAsync();
        }

        await using var readDb = IntegrationTestConnection.NewContext();
        var query = readDb.TrackedTasks.AsNoTracking().Where(t => t.UserId == userId);

        var listOrder = await TrackedTaskListFilters.OrderBy("duration", sortDescending: true)(query)
            .Select(t => t.Details)
            .ToListAsync();
        Assert.Equal(new[] { "all-day-three-days", "timed-short" }, listOrder);

        var taskListOrder = await TaskListManualOrder.ForTaskList(TaskListRules.SortDuration, sortDescending: true)(query)
            .Select(t => t.Details)
            .ToListAsync();
        Assert.Equal(new[] { "all-day-three-days", "timed-short" }, taskListOrder);
    }

    private static ApplicationUser NewUser(string userId, string stamp) => new()
    {
        Id = userId,
        UserName = $"dur-sort-{stamp}@local",
        NormalizedUserName = $"DUR-SORT-{stamp.ToUpperInvariant()}@LOCAL",
        Email = $"dur-sort-{stamp}@local",
        NormalizedEmail = $"DUR-SORT-{stamp.ToUpperInvariant()}@LOCAL",
        EmailConfirmed = true,
        FirstName = "Dur",
        LastName = "Sort",
        SecurityStamp = Guid.NewGuid().ToString(),
        ConcurrencyStamp = Guid.NewGuid().ToString()
    };
}
