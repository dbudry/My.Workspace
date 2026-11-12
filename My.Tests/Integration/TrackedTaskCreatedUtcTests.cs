using Microsoft.EntityFrameworkCore;
using My.DAL.Models;
using Xunit;

namespace My.Tests.Integration;

public class TrackedTaskCreatedUtcTests
{
    [SqlServerFact]
    public async Task New_row_gets_CreatedUtc_and_an_edit_keeps_it()
    {
        var userId = Guid.NewGuid().ToString();
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var taskId = Guid.NewGuid().ToString();
        var before = DateTime.UtcNow.AddSeconds(-1);

        await using (var db = IntegrationTestConnection.NewContext())
        {
            db.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = $"created-{stamp}@example.test",
                NormalizedUserName = $"CREATED-{stamp}@EXAMPLE.TEST",
                Email = $"created-{stamp}@example.test",
                NormalizedEmail = $"CREATED-{stamp}@EXAMPLE.TEST",
                EmailConfirmed = true,
                FirstName = "Created",
                LastName = "Utc",
                SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString()
            });
            db.TrackedTasks.Add(new TrackedTask
            {
                TaskId = taskId,
                UserId = userId,
                Details = "created-utc",
                StartDate = new DateTime(2026, 8, 10, 9, 0, 0),
                EndDate = new DateTime(2026, 8, 10, 10, 0, 0),
                Duration = TimeSpan.FromHours(1)
            });
            await db.SaveChangesAsync();
        }

        DateTime? created;
        await using (var db = IntegrationTestConnection.NewContext())
        {
            var row = await db.TrackedTasks.SingleAsync(t => t.TaskId == taskId);
            created = row.CreatedUtc;
            Assert.NotNull(created);
            Assert.InRange(created!.Value, before, DateTime.UtcNow.AddSeconds(1));

            row.Details = "edited";
            await db.SaveChangesAsync();
        }

        await using (var db = IntegrationTestConnection.NewContext())
        {
            var row = await db.TrackedTasks.AsNoTracking().SingleAsync(t => t.TaskId == taskId);
            Assert.Equal(created, row.CreatedUtc);
        }

        await using (var db = IntegrationTestConnection.NewContext())
        {
            db.TrackedTasks.RemoveRange(db.TrackedTasks.Where(t => t.UserId == userId));
            db.Users.RemoveRange(db.Users.Where(u => u.Id == userId));
            await db.SaveChangesAsync();
        }
    }
}
