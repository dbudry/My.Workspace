using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class PickerNameFilterTests
{
    private static readonly (string Id, string Name)[] Orgs =
    [
        ("1", "Organization Alpha"),
        ("2", "Organization Beta"),
        ("3", "Organization Gamma"),
        ("4", "Organization Delta"),
    ];

    [Fact]
    public void Empty_query_returns_all()
    {
        var result = PickerNameFilter.Match(Orgs, "  ", o => o.Name);
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void Query_matches_case_insensitive_substring()
    {
        var result = PickerNameFilter.Match(Orgs, "beta", o => o.Name);
        Assert.Single(result);
        Assert.Equal("Organization Beta", result[0].Name);
    }

    [Fact]
    public void Query_finds_orgs_beyond_the_visible_head_of_the_list()
    {
        var result = PickerNameFilter.Match(Orgs, "delta", o => o.Name);
        Assert.Single(result);
        Assert.Equal("Organization Delta", result[0].Name);
    }
}
