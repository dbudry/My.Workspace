using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class StopwatchMutationRulesTests
{
    [Fact]
    public void Stop_after_delete_is_item_gone()
    {
        Assert.Equal(
            StopwatchMutationRules.StopConflict.ItemGone,
            StopwatchMutationRules.ClassifyStopConflict(itemStillExists: false));
    }

    [Fact]
    public void Stop_after_concurrent_stop_is_already_stopped()
    {
        Assert.Equal(
            StopwatchMutationRules.StopConflict.SessionAlreadyStopped,
            StopwatchMutationRules.ClassifyStopConflict(itemStillExists: true));
    }
}
