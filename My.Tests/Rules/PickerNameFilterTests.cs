using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class PickerNameFilterTests
{
    private static readonly (string Id, string Name)[] Orgs =
    [
        ("1", "Ball"),
        ("2", "BioRad"),
        ("3", "Body Armor"),
        ("4", "Reliance Crude"),
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
        var result = PickerNameFilter.Match(Orgs, "bio", o => o.Name);
        Assert.Single(result);
        Assert.Equal("BioRad", result[0].Name);
    }

    [Fact]
    public void Query_finds_orgs_beyond_the_visible_head_of_the_list()
    {
        var result = PickerNameFilter.Match(Orgs, "reliance", o => o.Name);
        Assert.Single(result);
        Assert.Equal("Reliance Crude", result[0].Name);
    }
}
