using My.Shared.Dtos.Intranet;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class IntranetNavigationRulesTests
{
    [Fact]
    public void GetDepth_TopLevel_IsOne()
    {
        var parents = IntranetNavigationRules.BuildParentById(new[] { ("a", (string?)null) });
        Assert.Equal(1, IntranetNavigationRules.GetDepth("a", parents));
    }

    [Fact]
    public void GetDepth_ThreeLevels_IsThree()
    {
        var parents = IntranetNavigationRules.BuildParentById(new[]
        {
            ("root", (string?)null),
            ("mid", "root"),
            ("leaf", "mid")
        });
        Assert.Equal(3, IntranetNavigationRules.GetDepth("leaf", parents));
    }

    [Fact]
    public void ValidatePlacement_RejectsChildBeyondMaxDepth()
    {
        var rows = new[] { ("l1", (string?)null), ("l2", "l1"), ("l3", "l2") };
        var parents = IntranetNavigationRules.BuildParentById(rows);
        var children = IntranetNavigationRules.BuildChildrenById(rows);

        var error = IntranetNavigationRules.ValidatePlacement("l3", parents, children, maxDepth: 3);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidatePlacement_AllowsMoveWithinMaxDepth()
    {
        var rows = new[] { ("l1", (string?)null), ("l2", "l1") };
        var parents = IntranetNavigationRules.BuildParentById(rows);
        var children = IntranetNavigationRules.BuildChildrenById(rows);

        Assert.Null(IntranetNavigationRules.ValidatePlacement("l1", parents, children, maxDepth: 3, existingItemId: "l2"));
    }

    [Fact]
    public void TrimToMaxDepth_CutsDeepBranches()
    {
        var tree = new List<IntranetNavigationItemDto>
        {
            new()
            {
                Id = "root",
                Title = "Root",
                Children = new List<IntranetNavigationItemDto>
                {
                    new()
                    {
                        Id = "child",
                        Title = "Child",
                        Children = new List<IntranetNavigationItemDto>
                        {
                            new() { Id = "grand", Title = "Grand" }
                        }
                    }
                }
            }
        };

        var trimmed = IntranetNavigationRules.TrimToMaxDepth(tree, maxDepth: 2);
        Assert.Single(trimmed);
        Assert.Single(trimmed[0].Children);
        Assert.Empty(trimmed[0].Children[0].Children);
    }

    [Theory]
    [InlineData(null, 10)]
    [InlineData("5", 5)]
    [InlineData("0", 1)]
    [InlineData("99", 50)]
    public void ParseMaxDepth_ClampsAndDefaults(string? raw, int expected) =>
        Assert.Equal(expected, IntranetNavigationRules.ParseMaxDepth(raw));
}