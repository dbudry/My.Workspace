using System.Linq.Expressions;
using My.DAL.Models;
using My.Functions.Helpers;
using Xunit;

namespace My.Tests.Rules;

public class TrackedTaskListFiltersTests
{
    [Fact]
    public void ExcludeStopwatchSessions_omits_linked_rows()
    {
        var filter = TrackedTaskListFilters.Build("user-1", null, null, null, excludeStopwatchSessions: true);
        var matches = filter.Compile();

        Assert.False(matches(new TrackedTask
        {
            UserId = "user-1",
            StopwatchItemId = "sw-1",
            Name = "Session",
            StartDate = DateTime.UtcNow
        }));

        Assert.True(matches(new TrackedTask
        {
            UserId = "user-1",
            StopwatchItemId = null,
            Name = "Manual",
            StartDate = DateTime.UtcNow
        }));
    }

    [Fact]
    public void IncludeStopwatchSessions_by_default()
    {
        var filter = TrackedTaskListFilters.Build("user-1", null, null, null);
        var matches = filter.Compile();

        Assert.True(matches(new TrackedTask
        {
            UserId = "user-1",
            StopwatchItemId = "sw-1",
            Name = "Session",
            StartDate = DateTime.UtcNow
        }));
    }
}