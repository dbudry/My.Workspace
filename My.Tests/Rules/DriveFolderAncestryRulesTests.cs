using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class DriveFolderAncestryRulesTests
{
    [Fact]
    public void DirectParentIsAncestor_true_when_listed()
    {
        Assert.True(DriveFolderAncestryRules.DirectParentIsAncestor(
            new[] { "other", "intranet-root" }, "intranet-root"));
        Assert.False(DriveFolderAncestryRules.DirectParentIsAncestor(
            new[] { "expenses-root" }, "intranet-root"));
        Assert.False(DriveFolderAncestryRules.DirectParentIsAncestor(null, "intranet-root"));
    }
}
