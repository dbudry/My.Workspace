using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class MultiSelectRulesTests
{
    private static readonly string[] All = ["a", "b", "c"];
    private static readonly (string Key, string Text)[] Options =
        [("a", "Alice Adams"), ("b", "Bob Brown"), ("c", "Cy Cole"), ("d", "Di Dunn")];

    [Fact]
    public void Null_selection_is_all_and_every_box_shows_checked()
    {
        Assert.True(MultiSelectRules.IsAll(null, All));
        Assert.True(MultiSelectRules.IsChecked(null, All, "b"));
    }

    [Fact]
    public void Empty_selection_is_none_and_no_box_shows_checked()
    {
        Assert.False(MultiSelectRules.IsAll([], All));
        Assert.False(MultiSelectRules.IsChecked([], All, "b"));
    }

    [Fact]
    public void Clearing_one_box_while_all_selected_keeps_the_others()
    {
        var result = MultiSelectRules.Toggle(null, All, "b", false);

        Assert.Equal(["a", "c"], result);
    }

    [Fact]
    public void Ticking_the_last_missing_box_collapses_to_all()
    {
        var result = MultiSelectRules.Toggle(["a", "c"], All, "b", true);

        Assert.Null(result);
    }

    [Fact]
    public void Clearing_the_only_selected_box_gives_none()
    {
        var result = MultiSelectRules.Toggle(["b"], All, "b", false);

        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public void Ticking_a_box_from_none_selects_just_that_one()
    {
        var result = MultiSelectRules.Toggle([], All, "b", true);

        Assert.Equal(["b"], result);
    }

    [Fact]
    public void Ticking_a_box_adds_it_in_option_order()
    {
        var result = MultiSelectRules.Toggle(["c"], All, "a", true);

        Assert.Equal(["a", "c"], result);
        Assert.False(MultiSelectRules.IsAll(result, All));
    }

    [Fact]
    public void Summary_uses_names_never_ids()
    {
        var text = MultiSelectRules.Summary(["b"], Options, "All employees");

        Assert.Equal("Bob Brown", text);
    }

    [Fact]
    public void Summary_lists_up_to_three_names_then_counts()
    {
        Assert.Equal("Alice Adams, Bob Brown", MultiSelectRules.Summary(["a", "b"], Options, "All"));
        Assert.Equal("Alice Adams, Bob Brown, Cy Cole", MultiSelectRules.Summary(["a", "b", "c"], Options, "All"));

        (string Key, string Text)[] five = [.. Options, ("e", "Eve Estes")];
        Assert.Equal("4 selected", MultiSelectRules.Summary(["a", "b", "c", "d"], five, "All"));
    }

    [Fact]
    public void Summary_shows_all_label_for_null_or_everything_picked()
    {
        Assert.Equal("All employees", MultiSelectRules.Summary(null, Options, "All employees"));
        Assert.Equal("All employees", MultiSelectRules.Summary(["a", "b", "c", "d"], Options, "All employees"));
    }

    [Fact]
    public void Summary_says_none_when_nothing_is_picked()
    {
        Assert.Equal("None selected", MultiSelectRules.Summary([], Options, "All employees"));
    }

    [Fact]
    public void Summary_never_shows_an_id_for_a_key_missing_from_options()
    {
        Assert.Equal("None selected", MultiSelectRules.Summary(["zzz"], Options, "All"));
    }
}
