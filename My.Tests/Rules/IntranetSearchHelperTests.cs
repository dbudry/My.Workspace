using My.Shared;
using Xunit;

namespace My.Tests.Rules;

public class IntranetSearchHelperTests
{
    [Fact]
    public void ParseQueryTerms_splits_and_lowercases()
    {
        var terms = IntranetSearchHelper.ParseQueryTerms("  Profit  Network  ");
        Assert.Equal(["profit", "network"], terms);
    }

    [Fact]
    public void StripHtmlForSearch_removes_tags_and_decodes_entities()
    {
        var plain = IntranetSearchHelper.StripHtmlForSearch("<p>Hello <strong>IT</strong> &amp; Ops</p>");
        Assert.Equal("Hello IT & Ops", plain);
    }

    [Fact]
    public void SearchPages_requires_all_terms_and_prefers_title()
    {
        var pages = new[]
        {
            new IntranetSearchHelper.PageSearchRecord("1", "Information Technology", "it", "<p>Network docs</p>", true, DateTime.UtcNow),
            new IntranetSearchHelper.PageSearchRecord("2", "HR Onboarding", "hr", "<p>Welcome packet</p>", true, DateTime.UtcNow),
            new IntranetSearchHelper.PageSearchRecord("3", "Network", null, "<p>Only one term</p>", true, DateTime.UtcNow),
        };

        var hits = IntranetSearchHelper.SearchPages(pages, ["profit", "network"], limit: 10);

        Assert.Empty(hits);

        hits = IntranetSearchHelper.SearchPages(pages, ["information", "technology"], limit: 10);
        Assert.Single(hits);
        Assert.Equal("1", hits[0].Page.PageId);
    }

    [Fact]
    public void BuildAttachmentSearchText_includes_non_images_and_splits_hyphens()
    {
        var text = IntranetSearchHelper.BuildAttachmentSearchText([
            ("silly-dog.gif", "image/gif"),
            ("silly-ostrich.png", "image/png"),
            ("flex-seal-guide.pdf", "application/pdf"),
        ]);

        Assert.DoesNotContain("silly-dog", text);
        Assert.DoesNotContain("ostrich", text);
        Assert.Contains("flex-seal-guide", text);
        Assert.Contains("flex seal guide", text);
    }

    [Fact]
    public void SearchPages_matches_non_image_attachment_names()
    {
        var pages = new[]
        {
            new IntranetSearchHelper.PageSearchRecord(
                "it",
                "Information Technology",
                "it",
                "<p>Test page</p>",
                true,
                DateTime.UtcNow,
                "flex-seal-guide flex seal guide"),
        };

        var hits = IntranetSearchHelper.SearchPages(pages, ["flex", "guide"], limit: 5);
        Assert.Single(hits);
        Assert.Equal("it", hits[0].Page.PageId);

        hits = IntranetSearchHelper.SearchPages(pages, ["flex-seal"], limit: 5);
        Assert.Single(hits);

        hits = IntranetSearchHelper.SearchPages(pages, ["ostrich"], limit: 5);
        Assert.Empty(hits);
    }

    [Fact]
    public void SearchPages_finds_body_match_with_excerpt()
    {
        var pages = new[]
        {
            new IntranetSearchHelper.PageSearchRecord(
                "a",
                "Policies",
                null,
                "<p>See the <em>vacation</em> policy for PTO details.</p>",
                true,
                DateTime.UtcNow),
        };

        var hits = IntranetSearchHelper.SearchPages(pages, ["vacation"], limit: 5);

        Assert.Single(hits);
        Assert.Contains("vacation", hits[0].Excerpt, StringComparison.OrdinalIgnoreCase);
    }
}