using Microsoft.EntityFrameworkCore;
using My.DAL.Data;
using My.DAL.Models;
using Xunit;

namespace My.Tests.Integration;

/// <summary>
/// Covers the ownership check in <c>TrackedTaskFunctions.CreateTrackedTaskAsync</c>
/// (TrackedTaskFunction.cs): when a client supplies a <c>StopwatchItemId</c> to link a
/// manually-logged session to an existing work item, the server must reject the link
/// unless that StopwatchItem belongs to the calling user. TrackedTask.UserId is always
/// forced server-side to the caller and can't be spoofed, but StopwatchItemId is a
/// client-supplied foreign key into a *different* table with its own independent
/// UserId — nothing about stamping the new TrackedTask's owner implies anything about
/// who owns the StopwatchItem it points at, so that has to be checked explicitly.
///
/// This test exercises the query + comparison directly against the real DB rather than
/// invoking the Azure Function (no HttpRequestData/ClaimsPrincipal test harness exists
/// in this repo yet) — see TrackedTaskFunction.cs:427-430 for the production code this
/// mirrors. If that check is ever weakened or removed, this test's "cross-user" case
/// stays correct while the production code diverges from it, so a manual diff against
/// TrackedTaskFunction.cs is still needed to catch that; this test alone guards the
/// underlying data model / query semantics, not the function wiring.
/// </summary>
public class TrackedTaskStopwatchLinkOwnershipTests
{
    private static ApplicationUser NewUser(string label)
    {
        var stamp = Guid.NewGuid().ToString("N")[..8];
        return new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = $"{label}-{stamp}@local",
            NormalizedUserName = $"{label.ToUpperInvariant()}-{stamp.ToUpperInvariant()}@LOCAL",
            Email = $"{label}-{stamp}@local",
            NormalizedEmail = $"{label.ToUpperInvariant()}-{stamp.ToUpperInvariant()}@LOCAL",
            EmailConfirmed = true,
            FirstName = label,
            LastName = "Test",
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
    }

    /// <summary>
    /// Mirrors TrackedTaskFunction.cs:427-430 exactly: look up the StopwatchItem by id,
    /// reject when missing or when it belongs to someone other than the caller.
    /// </summary>
    private static async Task<bool> IsRejectedAsync(ApplicationDbContext db, string? stopwatchItemId, string callerUserId)
    {
        if (string.IsNullOrWhiteSpace(stopwatchItemId))
            return false; // no link requested — nothing to reject

        var sw = await db.StopwatchItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.StopwatchItemId == stopwatchItemId);
        return sw == null || sw.UserId != callerUserId;
    }

    [SqlServerFact]
    public async Task Linking_another_users_stopwatch_item_is_rejected()
    {
        await using var db = IntegrationTestConnection.NewContext();

        var owner = NewUser("owner");
        var attacker = NewUser("attacker");
        db.Users.AddRange(owner, attacker);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var ownersItem = new StopwatchItem
        {
            UserId = owner.Id,
            Name = "Owner's private work item",
            CreatedAt = now,
            LastWorkedAt = now
        };
        db.StopwatchItems.Add(ownersItem);
        await db.SaveChangesAsync();

        try
        {
            // Attacker tries to create a TrackedTask linked to owner's StopwatchItem.
            var rejectedForAttacker = await IsRejectedAsync(db, ownersItem.StopwatchItemId, attacker.Id);
            Assert.True(rejectedForAttacker, "A StopwatchItemId belonging to another user must be rejected.");

            // The actual owner linking their own item must succeed.
            var acceptedForOwner = await IsRejectedAsync(db, ownersItem.StopwatchItemId, owner.Id);
            Assert.False(acceptedForOwner, "A user must be able to link their own StopwatchItem.");
        }
        finally
        {
            db.StopwatchItems.Remove(ownersItem);
            db.Users.RemoveRange(owner, attacker);
            await db.SaveChangesAsync();
        }
    }

    [SqlServerFact]
    public async Task Linking_a_nonexistent_stopwatch_item_is_rejected()
    {
        await using var db = IntegrationTestConnection.NewContext();

        var caller = NewUser("caller");
        db.Users.Add(caller);
        await db.SaveChangesAsync();

        try
        {
            var rejected = await IsRejectedAsync(db, Guid.NewGuid().ToString(), caller.Id);
            Assert.True(rejected, "A StopwatchItemId that doesn't exist must be rejected, not silently ignored.");
        }
        finally
        {
            db.Users.Remove(caller);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public void No_stopwatch_item_requested_is_never_rejected()
    {
        // Pure branch, no DB needed: matches the `!string.IsNullOrWhiteSpace(...)` guard
        // in TrackedTaskFunction.cs — a plain manual task with no StopwatchItemId must
        // never be blocked by this check.
        Assert.False(IsRejectedAsync(null!, null, "any-user").GetAwaiter().GetResult());
    }
}
