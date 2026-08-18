using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class TrackedTaskBillableRulesTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    public void FromProject_respects_availability_override(bool projectIsBillable, bool isSharedAvailability, bool expected)
    {
        Assert.Equal(expected, TrackedTaskBillableRules.FromProject(projectIsBillable, isSharedAvailability));
    }
}