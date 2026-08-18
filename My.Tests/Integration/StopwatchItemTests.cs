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
                    Details = "Test stopwatch aggregation",
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
                    Details = item.Details,
                    StartDate = now.AddHours(-2),
                    EndDate = now.AddHours(-1),
                    Duration = TimeSpan.FromMinutes(30)
                },
                new TrackedTask
                {
                    UserId = userId,
                    StopwatchItemId = item.StopwatchItemId,
                    Details = item.Details,
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
        var target = new StopwatchItem { UserId = userId, Details = "Delete target", CreatedAt = now, LastWorkedAt = now };
        var keep = new StopwatchItem { UserId = userId, Details = "Keep me", CreatedAt = now, LastWorkedAt = now };
        db.StopwatchItems.AddRange(target, keep);
        await db.SaveChangesAsync();

        db.TrackedTasks.AddRange(
            new TrackedTask { UserId = userId, StopwatchItemId = target.StopwatchItemId, Details = target.Details, StartDate = now, Duration = TimeSpan.FromMinutes(5) },
            new TrackedTask { UserId = userId, StopwatchItemId = target.StopwatchItemId, Details = target.Details, StartDate = now, Duration = TimeSpan.FromMinutes(5) },
            new TrackedTask { UserId = userId, StopwatchItemId = keep.StopwatchItemId, Details = keep.Details, StartDate = now, Duration = TimeSpan.FromMinutes(5) });
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

    [SqlServerFact]
    public async Task Clearing_an_item_removes_it_from_the_list_query_but_keeps_its_sessions()
    {
        await using var db = IntegrationTestConnection.NewContext();

        var userId = await IntegrationTestFixtures.EnsureTestUserIdAsync(db);

        var now = DateTime.UtcNow;
        var cleared = new StopwatchItem { UserId = userId, Details = "Cleared item", CreatedAt = now, LastWorkedAt = now };
        var visible = new StopwatchItem { UserId = userId, Details = "Still visible", CreatedAt = now, LastWorkedAt = now };
        db.StopwatchItems.AddRange(cleared, visible);
        await db.SaveChangesAsync();

        db.TrackedTasks.Add(new TrackedTask
        {
            UserId = userId,
            StopwatchItemId = cleared.StopwatchItemId,
            Details = cleared.Details,
            StartDate = now,
            EndDate = now.AddMinutes(10),
            Duration = TimeSpan.FromMinutes(10)
        });
        await db.SaveChangesAsync();

        try
        {
            // Mirrors ClearStopwatchItemAsync: flip the flag, don't touch the item or its sessions.
            cleared.IsCleared = true;
            await db.SaveChangesAsync();

            // Mirrors GetStopwatchItemsAsync's filter.
            var listed = await db.StopwatchItems
                .Where(i => i.UserId == userId && !i.IsCleared)
                .Select(i => i.StopwatchItemId)
                .ToListAsync();

            Assert.DoesNotContain(cleared.StopwatchItemId, listed);
            Assert.Contains(visible.StopwatchItemId, listed);

            // The whole point: clearing didn't touch the item or its logged session.
            var stillThere = await db.StopwatchItems.AsNoTracking()
                .FirstOrDefaultAsync(i => i.StopwatchItemId == cleared.StopwatchItemId);
            Assert.NotNull(stillThere);
            Assert.True(stillThere!.IsCleared);
            Assert.Equal(1, await db.TrackedTasks.CountAsync(t => t.StopwatchItemId == cleared.StopwatchItemId));
            Assert.Equal(TimeSpan.FromMinutes(10), (await db.TrackedTasks
                .FirstAsync(t => t.StopwatchItemId == cleared.StopwatchItemId)).Duration);
        }
        finally
        {
            await db.TrackedTasks
                .Where(t => t.StopwatchItemId == cleared.StopwatchItemId || t.StopwatchItemId == visible.StopwatchItemId)
                .ExecuteDeleteAsync();
            await db.StopwatchItems
                .Where(i => i.StopwatchItemId == cleared.StopwatchItemId || i.StopwatchItemId == visible.StopwatchItemId)
                .ExecuteDeleteAsync();
        }
    }

    [SqlServerFact]
    public async Task Clearing_an_item_with_a_running_session_is_blocked()
    {
        await using var db = IntegrationTestConnection.NewContext();

        var userId = await IntegrationTestFixtures.EnsureTestUserIdAsync(db);

        var now = DateTime.UtcNow;
        var item = new StopwatchItem { UserId = userId, Details = "Running item", CreatedAt = now, LastWorkedAt = now };
        db.StopwatchItems.Add(item);
        await db.SaveChangesAsync();

        db.TrackedTasks.Add(new TrackedTask
        {
            UserId = userId,
            StopwatchItemId = item.StopwatchItemId,
            Details = item.Details,
            StartDate = now,
            EndDate = null, // still running
            Duration = TimeSpan.Zero
        });
        await db.SaveChangesAsync();

        try
        {
            // Mirrors ClearStopwatchItemAsync's guard: refuse while any session is open.
            var isRunning = await db.TrackedTasks
                .AnyAsync(t => t.StopwatchItemId == item.StopwatchItemId && t.EndDate == null);

            Assert.True(isRunning, "Setup should have left one open session on the item.");
            Assert.False(item.IsCleared);
        }
        finally
        {
            await db.TrackedTasks.Where(t => t.StopwatchItemId == item.StopwatchItemId).ExecuteDeleteAsync();
            await db.StopwatchItems.Where(i => i.StopwatchItemId == item.StopwatchItemId).ExecuteDeleteAsync();
        }
    }
}