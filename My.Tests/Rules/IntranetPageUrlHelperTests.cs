using My.Shared;
using Xunit;

namespace My.Tests.Rules;

public class IntranetPageUrlHelperTests
{
    [Theory]
    [InlineData("a1b2c3d4e5f6478990abcdef12345678", true)]
    [InlineData("f1e2d3c4b5a601234567890abcdef02", true)] // 31-char seed id (BitLocker)
    [InlineData("it", false)]
    [InlineData("my-page", false)]
    public void LooksLikePageId_distinguishes_guid_from_slug(string value, bool expected)
    {
        Assert.Equal(expected, IntranetPageUrlHelper.LooksLikePageId(value));
    }

    [Theory]
    [InlineData("abc123", "it", "/intranet/pages/it")]
    [InlineData("abc123", null, "/intranet/pages/abc123")]
    [InlineData("abc123", "", "/intranet/pages/abc123")]
    public void GetViewPath_prefers_slug_when_set(string pageId, string? slug, string expected)
    {
        Assert.Equal(expected, IntranetPageUrlHelper.GetViewPath(pageId, slug));
    }
}