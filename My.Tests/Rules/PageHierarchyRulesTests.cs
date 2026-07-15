using System.Collections.Generic;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class PageHierarchyRulesTests
{
    // Chain: A (root) -> B -> C  (C is the deepest descendant of A)
    private static Dictionary<string, string?> Chain() => new()
    {
        ["A"] = null,
        ["B"] = "A",
        ["C"] = "B",
    };

    [Fact]
    public void Moving_page_under_itself_is_a_cycle()
    {
        Assert.True(PageHierarchyRules.WouldCreateCycle("B", "B", Chain()));
    }

    [Fact]
    public void Moving_page_under_its_direct_child_is_a_cycle()
    {
        Assert.True(PageHierarchyRules.WouldCreateCycle("A", "B", Chain()));
    }

    [Fact]
    public void Moving_page_under_a_deep_descendant_is_a_cycle()
    {
        Assert.True(PageHierarchyRules.WouldCreateCycle("A", "C", Chain()));
    }

    [Fact]
    public void Moving_descendant_under_its_ancestor_is_allowed()
    {
        // C already sits under A transitively; re-parenting it directly under A is fine.
        Assert.False(PageHierarchyRules.WouldCreateCycle("C", "A", Chain()));
    }

    [Fact]
    public void Moving_under_an_unrelated_root_is_allowed()
    {
        var pages = Chain();
        pages["D"] = null; // a separate root
        Assert.False(PageHierarchyRules.WouldCreateCycle("A", "D", pages));
    }

    [Fact]
    public void Unknown_new_parent_is_allowed()
    {
        // Parent id not present in the map (e.g. brand-new/foreign id) — no chain, no cycle.
        Assert.False(PageHierarchyRules.WouldCreateCycle("A", "ghost", Chain()));
    }

    [Fact]
    public void Preexisting_data_cycle_is_reported_rather_than_looping_forever()
    {
        // Corrupt data: X <-> Y point at each other. Any walk through them must terminate.
        var pages = new Dictionary<string, string?> { ["X"] = "Y", ["Y"] = "X", ["Z"] = null };
        Assert.True(PageHierarchyRules.WouldCreateCycle("Z", "X", pages));
    }
}
