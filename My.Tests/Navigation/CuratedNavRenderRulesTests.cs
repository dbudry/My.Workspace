using My.Shared.Navigation;
using Xunit;

namespace My.Tests.Navigation;

public class CuratedNavRenderRulesTests
{
    [Theory]
    [InlineData(0, 16)]
    [InlineData(1, 20)]
    [InlineData(2, 24)]
    [InlineData(3, 28)]
    public void GetLinkPaddingPx_IncreasesByStepPerDepth(int depth, int expectedPx)
    {
        Assert.Equal(expectedPx, CuratedNavRenderRules.GetLinkPaddingPx(depth));
    }

    [Fact]
    public void GetDepthStyle_EmitsPaddingVariable()
    {
        var style = CuratedNavRenderRules.GetDepthStyle(2);

        Assert.Contains("--curated-nav-depth: 2", style);
        Assert.Contains("--curated-nav-link-padding: 24px", style);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public void IsStrictlyIncreasingPadding_TrueForEachChildDepth(int parentDepth, int childDepth)
    {
        Assert.True(CuratedNavRenderRules.IsStrictlyIncreasingPadding(parentDepth, childDepth));
    }

    [Fact]
    public void IsStrictlyIncreasingPadding_FalseWhenSameDepth()
    {
        Assert.False(CuratedNavRenderRules.IsStrictlyIncreasingPadding(2, 2));
    }
}