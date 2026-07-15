using Microsoft.EntityFrameworkCore;
using My.DAL.Data;
using My.DAL.Models;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Integration;

public class StopwatchItemTests
{

    [Fact]
    public void RoundUpToMinute_rounds_up_sub_minute_elapsed()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), StopwatchRules.RoundUpToMinute(TimeSpan.FromSeconds(10)));
        Assert.Equal(TimeSpan.Zero, StopwatchRules.RoundUpToMinute(TimeSpan.Zero));
    }

    [Fact]
    public void Billed_duration_uses_stored_rounded_value_not_raw_clock_delta()
    {
        var start = new DateTime(2026, 6, 30, 14, 39, 0);
        var end = start.AddSeconds(8);
        var billed = StopwatchRules.RoundUpToMinute(end - start);

        Assert.Equal(TimeSpan.FromMinutes(1), billed);
        Assert.True(end > start);
        Assert.NotEqual(billed, end - start);
    }

    [SqlServerFact]
    public async Task Stopwatch_item_sessions_aggregate_completed_duration()
    {
        await using var db = IntegrationTestConnection.NewContext();

        var userId = await IntegrationTestFixtures.EnsureTestUserIdAsync(db);

        var now = DateTime.UtcNow;
        var item = new StopwatchItem
        {
            UserId = userId,
            Name = "Test stopwatch aggregation",
            CreatedAt = now,
            LastWorkedAt = now
        };
        db.StopwatchItems.Add(item);
        await db.SaveChangesAsync();

        try
        {
            db.TrackedTasks.AddRange(
                new TrackedTask
                {
                    UserId = userId,
                    StopwatchItemId = item.StopwatchItemId,
                    Name = item.Name,
                    StartDate = now.AddHours(-2),
                    EndDate = now.AddHours(-1),
                    Duration = TimeSpan.FromMinutes(30)
                },
                new TrackedTask
                {
                    UserId = userId,
                    StopwatchItemId = item.StopwatchItemId,
                    Name = item.Name,
                    StartDate = now.AddMinutes(-10),
                    EndDate = null,
                    Duration = TimeSpan.Zero
                });
            await db.SaveChangesAsync();

            var sessions = await db.TrackedTasks
                .Where(t => t.StopwatchItemId == item.StopwatchItemId)
                .ToListAsync();

            var completedTotal = sessions
                .Where(t => t.EndDate != null)
                .Aggregate(TimeSpan.Zero, (sum, t) => sum + t.Duration);
            var active = sessions.FirstOrDefault(t => t.EndDate == null);

            Assert.Equal(TimeSpan.FromMinutes(30), completedTotal);
            Assert.NotNull(active);
            Assert.Single(sessions, t => t.EndDate == null);
        }
        finally
        {
            var sessions = await db.TrackedTasks.Where(t => t.StopwatchItemId == item.StopwatchItemId).ToListAsync();
            db.TrackedTasks.RemoveRange(sessions);
            db.StopwatchItems.Remove(item);
            await db.SaveChangesAsync();
        }
    }

    [SqlServerFact]
    public async Task Deleting_stopwatch_item_removes_only_its_own_sessions()
    {
        await using var db = IntegrationTestConnection.NewContext();

        var userId = await IntegrationTestFixtures.EnsureTestUserIdAsync(db);

        var now = DateTime.UtcNow;
        var target = new StopwatchItem { UserId = userId, Name = "Delete target", CreatedAt = now, LastWorkedAt = now };
        var keep = new StopwatchItem { UserId = userId, Name = "Keep me", CreatedAt = now, LastWorkedAt = now };
        db.StopwatchItems.AddRange(target, keep);
        await db.SaveChangesAsync();

        db.TrackedTasks.AddRange(
            new TrackedTask { UserId = userId, StopwatchItemId = target.StopwatchItemId, Name = target.Name, StartDate = now, Duration = TimeSpan.FromMinutes(5) },
            new TrackedTask { UserId = userId, StopwatchItemId = target.StopwatchItemId, Name = target.Name, StartDate = now, Duration = TimeSpan.FromMinutes(5) },
            new TrackedTask { UserId = userId, StopwatchItemId = keep.StopwatchItemId, Name = keep.Name, StartDate = now, Duration = TimeSpan.FromMinutes(5) });
        await db.SaveChangesAsync();

        try
        {
            // Mirrors DeleteStopwatchItemAsync: bulk-delete this item's sessions only.
            var deleted = await db.TrackedTasks
                .Where(t => t.StopwatchItemId == target.StopwatchItemId)
                .ExecuteDeleteAsync();

            Assert.Equal(2, deleted);
            Assert.False(await db.TrackedTasks.AnyAsync(t => t.StopwatchItemId == target.StopwatchItemId));
            Assert.Equal(1, await db.TrackedTasks.CountAsync(t => t.StopwatchItemId == keep.StopwatchItemId));
        }
        finally
        {
            await db.TrackedTasks
                .Where(t => t.StopwatchItemId == target.StopwatchItemId || t.StopwatchItemId == keep.StopwatchItemId)
                .ExecuteDeleteAsync();
            await db.StopwatchItems
                .Where(i => i.StopwatchItemId == target.StopwatchItemId || i.StopwatchItemId == keep.StopwatchItemId)
                .ExecuteDeleteAsync();
        }
    }
}