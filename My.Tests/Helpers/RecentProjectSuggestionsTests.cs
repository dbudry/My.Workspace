using My.Client.Helpers;
using My.Shared.Dtos.Project;
using My.Shared.Dtos.StopwatchItem;
using Xunit;

namespace My.Tests.Helpers;

public class RecentProjectSuggestionsTests
{
    private static StopwatchItemDto Item(
        string id,
        string projectId,
        string projectName,
        DateTime lastWorked,
        bool isActive = true,
        bool isArchived = false)
        => new()
        {
            StopwatchItemId = id,
            ProjectId = projectId,
            LastWorkedAt = lastWorked,
            Project = new ProjectDto
            {
                ProjectId = projectId,
                Name = projectName,
                IsActive = isActive,
                IsArchived = isArchived
            }
        };

    [Fact]
    public void FromStopwatchItems_returns_empty_when_no_items()
    {
        var results = RecentProjectSuggestions.FromStopwatchItems(Array.Empty<StopwatchItemDto>());
        Assert.Empty(results);
    }

    [Fact]
    public void FromStopwatchItems_orders_by_most_recent_work_and_dedupes_projects()
    {
        var items = new[]
        {
            Item("sw-1", "p-a", "Alpha", new DateTime(2026, 7, 1)),
            Item("sw-2", "p-b", "Beta", new DateTime(2026, 7, 3)),
            Item("sw-3", "p-a", "Alpha", new DateTime(2026, 7, 2)),
            Item("sw-4", "p-c", "Gamma", new DateTime(2026, 6, 30))
        };

        var results = RecentProjectSuggestions.FromStopwatchItems(items);

        Assert.Equal(3, results.Count);
        Assert.Equal("p-b", results[0].ProjectId);
        Assert.Equal("p-a", results[1].ProjectId);
        Assert.Equal("p-c", results[2].ProjectId);
    }

    [Fact]
    public void FromStopwatchItems_caps_at_five_and_skips_inactive_or_archived()
    {
        var items = Enumerable.Range(1, 8)
            .Select(i => Item(
                $"sw-{i}",
                $"p-{i}",
                $"Project {i}",
                new DateTime(2026, 7, i),
                isActive: i != 3,
                isArchived: i == 5))
            .ToArray();

        var results = RecentProjectSuggestions.FromStopwatchItems(items);

        Assert.Equal(5, results.Count);
        Assert.DoesNotContain(results, p => p.ProjectId == "p-3");
        Assert.DoesNotContain(results, p => p.ProjectId == "p-5");
        Assert.Equal("p-8", results[0].ProjectId);
        Assert.Equal("p-7", results[1].ProjectId);
    }
}