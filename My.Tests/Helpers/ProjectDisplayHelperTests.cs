using My.Client.Helpers;
using Xunit;

namespace My.Tests.Helpers;

public class ProjectDisplayHelperTests
{
    [Fact]
    public void AffiliationCaption_both_joins_with_middle_dot()
    {
        var caption = ProjectDisplayHelper.AffiliationCaption("Sample Organization", "Client Work");
        Assert.Equal("Sample Organization · Client Work", caption);
    }

    [Fact]
    public void AffiliationCaption_org_only()
    {
        Assert.Equal("SampleOrg", ProjectDisplayHelper.AffiliationCaption("SampleOrg", null));
        Assert.Equal("SampleOrg", ProjectDisplayHelper.AffiliationCaption("SampleOrg", "  "));
    }

    [Fact]
    public void AffiliationCaption_group_only()
    {
        Assert.Equal("Internal", ProjectDisplayHelper.AffiliationCaption(null, "Internal"));
        Assert.Equal("Internal", ProjectDisplayHelper.AffiliationCaption("   ", "Internal"));
    }

    [Fact]
    public void AffiliationCaption_empty_returns_null()
    {
        Assert.Null(ProjectDisplayHelper.AffiliationCaption(null, null));
        Assert.Null(ProjectDisplayHelper.AffiliationCaption("  ", ""));
    }

    [Fact]
    public void AffiliationCaption_trims_whitespace()
    {
        var caption = ProjectDisplayHelper.AffiliationCaption("  Org  ", "  Group  ");
        Assert.Equal("Org · Group", caption);
    }
}
