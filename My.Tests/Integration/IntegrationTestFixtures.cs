using Microsoft.EntityFrameworkCore;
using My.DAL.Data;
using My.DAL.Models;

namespace My.Tests.Integration;

internal static class IntegrationTestFixtures
{
    private const string TestUserName = "integration-test@local";
    private const string NormalizedTestUserName = "INTEGRATION-TEST@LOCAL";

    // SqlServerFact tests can run in parallel; serialize fixture user creation so only
    // one test inserts INTEGRATION-TEST@LOCAL on a fresh CI database.
    private static readonly SemaphoreSlim TestUserGate = new(1, 1);

    public static async Task<string> EnsureTestUserIdAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var existingId = await FindTestUserIdAsync(db, ct);
        if (!string.IsNullOrEmpty(existingId))
            return existingId;

        await TestUserGate.WaitAsync(ct);
        try
        {
            existingId = await FindTestUserIdAsync(db, ct);
            if (!string.IsNullOrEmpty(existingId))
                return existingId;

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = TestUserName,
                NormalizedUserName = NormalizedTestUserName,
                Email = TestUserName,
                NormalizedEmail = NormalizedTestUserName,
                EmailConfirmed = true,
                FirstName = "Integration",
                LastName = "Test",
                SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            return user.Id;
        }
        finally
        {
            TestUserGate.Release();
        }
    }

    private static Task<string?> FindTestUserIdAsync(ApplicationDbContext db, CancellationToken ct) =>
        db.Users.AsNoTracking()
            .Where(u => u.NormalizedUserName == NormalizedTestUserName)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);

    // Parallel SqlServerFact tests can race on "exists?" then insert for the same user.
    private static readonly SemaphoreSlim TestTaskGate = new(1, 1);

    /// <summary>
    /// Ensures at least one tracked task exists for <paramref name="userId"/> and returns its id.
    /// Serialized so concurrent integration tests don't race on the existence check.
    /// </summary>
    public static async Task<string> EnsureTrackedTaskForUserAsync(
        ApplicationDbContext db,
        string userId,
        CancellationToken ct = default)
    {
        await TestTaskGate.WaitAsync(ct);
        try
        {
            var existingId = await db.TrackedTasks.AsNoTracking()
                .Where(t => t.UserId == userId)
                .Select(t => t.TaskId)
                .FirstOrDefaultAsync(ct);
            if (!string.IsNullOrEmpty(existingId))
                return existingId;

            var now = DateTime.UtcNow;
            var taskId = Guid.NewGuid().ToString();
            db.TrackedTasks.Add(new TrackedTask
            {
                TaskId = taskId,
                UserId = userId,
                Name = "Integration mapping fixture",
                StartDate = now.AddHours(-1),
                EndDate = now,
                Duration = TimeSpan.FromHours(1)
            });
            await db.SaveChangesAsync(ct);
            return taskId;
        }
        finally
        {
            TestTaskGate.Release();
        }
    }
}