using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpenseCodeRemovalRulesTests
{
    [Fact]
    public void Letter_removal_includes_its_subcategories_first()
    {
        var codes = ExpenseCodeRemovalRules.CodesToRemove("G", ["G", "G.02", "H", "G.01", "Z"]);

        Assert.Equal(["G.01", "G.02", "G"], codes);
    }

    [Fact]
    public void Subcategory_removal_is_only_that_code()
    {
        var codes = ExpenseCodeRemovalRules.CodesToRemove("g.01", ["G", "G.01", "G.02"]);

        Assert.Equal(["G.01"], codes);
    }

    [Fact]
    public void Software_code_is_not_removed()
    {
        Assert.True(ExpenseCodeRemovalRules.IsSoftwareCode("z"));
        Assert.Empty(ExpenseCodeRemovalRules.CodesToRemove("Z", ["Z", "Z.01"]));
    }

    [Fact]
    public void Software_edit_rejects_a_rename_or_retire_before_the_row_changes()
    {
        Assert.Null(ExpenseCodeRemovalRules.RejectSoftwareEdit("Z", "Z", true));
        Assert.Null(ExpenseCodeRemovalRules.RejectSoftwareEdit("A", "B", false));
        Assert.Equal(
            ExpenseCodeRemovalRules.SoftwareKeptMessage,
            ExpenseCodeRemovalRules.RejectSoftwareEdit("Z", "A", false));
        Assert.Equal(
            "Z stays active because software expenses print as Z.",
            ExpenseCodeRemovalRules.RejectSoftwareEdit("Z", "Z", false));
    }
}
