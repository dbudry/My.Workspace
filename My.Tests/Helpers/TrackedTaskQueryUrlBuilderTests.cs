using My.Shared.Constants;
using My.Shared.Helpers;
using Xunit;

namespace My.Tests.Helpers;

public class TrackedTaskQueryUrlBuilderTests
{
    [Fact]
    public void BuildRange_uses_single_query_string_without_paging()
    {
        var from = new DateTime(2025, 6, 1);
        var url = TrackedTaskQueryUrlBuilder.BuildRange(
            Constants.API.TrackedTask.GetRange,
            from,
            search: "test",
            excludeStopwatchSessions: true);

        Assert.StartsWith("trackedtasks/range?", url);
        Assert.Contains("From=2025-06-01", url);
        Assert.Contains("Search=test", url);
        Assert.Contains("excludeStopwatchSessions=true", url);
        Assert.DoesNotContain("PageNumber", url);
    }
}