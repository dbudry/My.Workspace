using My.Shared.Dtos.Intranet;
using My.Shared.Navigation;
using Xunit;

namespace My.Tests.Navigation;

public class SidebarNavExpansionRulesTests
{
    [Theory]
    [InlineData("tyme/submit", true)]
    [InlineData("admin/users", true)]
    [InlineData("intranet/pages", true)]
    [InlineData("intranet/editor/abc", true)]
    [InlineData("intranet/pages/bitlocker", false)]
    public void RouteClassifiers_MatchExpectedSections(string path, bool isBuiltIn)
    {
        Assert.Equal(isBuiltIn,
            SidebarNavExpansionRules.IsTymeRoute(path)
            || SidebarNavExpansionRules.IsAdminRoute(path)
            || SidebarNavExpansionRules.IsIntranetMaintenanceRoute(path));
    }

    [Fact]
    public void TryGetCuratedExpansion_FindsTopAndNestedKeys_ForGrandchildPage()
    {
        var tree = BuildKnowledgeBaseTree();

        var found = SidebarNavExpansionRules.TryGetCuratedExpansion(
            tree,
            "intranet/pages/bitlocker",
            out var topKey,
            out var nestedKeys);

        Assert.True(found);
        Assert.Equal("nav:kb", topKey);
        Assert.Equal("nav:security", nestedKeys["nav:kb"]);
    }

    [Fact]
    public void ItemMatchesPath_UsesSlugWhenPresent()
    {
        var item = new IntranetNavigationItemDto
        {
            Id = "bitlocker",
            PageId = "page-guid",
            PageSlug = "bitlocker"
        };

        Assert.True(SidebarNavExpansionRules.ItemMatchesPath(item, "intranet/pages/bitlocker"));
        Assert.False(SidebarNavExpansionRules.ItemMatchesPath(item, "intranet/pages/page-guid"));
    }

    private static List<IntranetNavigationItemDto> BuildKnowledgeBaseTree() =>
    [
        new()
        {
            Id = "kb",
            Title = "Knowledge Base",
            IsVisible = true,
            SortOrder = 1,
            PageId = "kb-page",
            PageSlug = "knowledge-base",
            Children =
            [
                new()
                {
                    Id = "security",
                    Title = "Security & Passwords",
                    IsVisible = true,
                    SortOrder = 1,
                    PageId = "security-page",
                    PageSlug = "security-passwords",
                    Children =
                    [
                        new()
                        {
                            Id = "bitlocker",
                            Title = "BitLocker",
                            IsVisible = true,
                            SortOrder = 1,
                            PageId = "bitlocker-page",
                            PageSlug = "bitlocker"
                        }
                    ]
                }
            ]
        }
    ];
}