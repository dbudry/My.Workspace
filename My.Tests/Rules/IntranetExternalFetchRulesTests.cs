using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class IntranetExternalFetchRulesTests
{
    [Theory]
    [InlineData("https://cdn.example.com/photo.png")]
    [InlineData("https://lh3.googleusercontent.com/abc123")]
    public void TryValidateFetchUrl_allows_public_https(string url)
    {
        Assert.True(IntranetExternalFetchRules.TryValidateFetchUrl(url, out var uri, out _));
        Assert.NotNull(uri);
    }

    [Theory]
    [InlineData("http://localhost/image.png")]
    [InlineData("https://127.0.0.1/photo.jpg")]
    [InlineData("ftp://example.com/a.png")]
    public void TryValidateFetchUrl_blocks_unsafe_urls(string url)
    {
        Assert.False(IntranetExternalFetchRules.TryValidateFetchUrl(url, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void InferFileNameFromUrl_uses_path_or_content_type()
    {
        var uri = new Uri("https://cdn.example.com/assets/report.png");
        Assert.Equal("report.png", IntranetExternalFetchRules.InferFileNameFromUrl(uri, "image/png"));

        var cdn = new Uri("https://lh3.googleusercontent.com/abc123");
        Assert.Equal("external-image.jpg", IntranetExternalFetchRules.InferFileNameFromUrl(cdn, "image/jpeg"));
    }

    [Fact]
    public void IsGoogleHostedImageUrl_detects_google_cdn_hosts()
    {
        Assert.True(IntranetExternalFetchRules.IsGoogleHostedImageUrl(
            new Uri("https://lh3.googleusercontent.com/sitesv/abc=w1280")));
        Assert.False(IntranetExternalFetchRules.IsGoogleHostedImageUrl(
            new Uri("https://cdn.example.com/photo.png")));
    }

    [Fact]
    public void IsDirectlyLinkable_blocks_google_urls()
    {
        const string googleSites =
            "https://lh3.googleusercontent.com/sitesv/AA5AbUC5q30GdLom22p7NmFK_7K5uhBR6TgcaoPuqyqrEfPMmhU0jYJ=w1280";

        Assert.False(IntranetExternalFetchRules.IsDirectlyLinkable(googleSites, out var reason));
        Assert.Contains("403", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsDirectlyLinkable_allows_public_cdn_urls()
    {
        Assert.True(IntranetExternalFetchRules.IsDirectlyLinkable(
            "https://cdn.example.com/assets/photo.png", out var reason));
        Assert.Null(reason);
    }

    [Fact]
    public void GetCopyRequiredNotice_explains_google_requires_library_copy()
    {
        const string googleSites =
            "https://lh3.googleusercontent.com/sitesv/AA5AbUC5q30GdLom22p7NmFK_7K5uhBR6TgcaoPuqyqrEfPMmhU0jYJ=w1280";

        var notice = IntranetExternalFetchRules.GetCopyRequiredNotice(googleSites);
        Assert.Contains("blocks direct linking", notice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("library", notice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetCopyRequiredNotice_null_for_linkable_urls()
    {
        Assert.Null(IntranetExternalFetchRules.GetCopyRequiredNotice("https://cdn.example.com/photo.png"));
    }

    [Fact]
    public void RequiresLibraryCopy_true_for_google_false_for_cdn()
    {
        Assert.True(IntranetExternalFetchRules.RequiresLibraryCopy(
            "https://lh3.googleusercontent.com/sitesv/abc=w1280"));
        Assert.False(IntranetExternalFetchRules.RequiresLibraryCopy("https://cdn.example.com/photo.png"));
    }

    [Fact]
    public void GetFetchUrlCandidates_prefers_smaller_widths_for_small_limits()
    {
        var uri = new Uri("https://lh3.googleusercontent.com/abc=w1280");
        var candidates = IntranetExternalFetchRules.GetFetchUrlCandidates(uri, 500_000).Select(u => u.ToString()).ToList();

        Assert.Equal(4, candidates.Count);
        Assert.StartsWith("https://lh3.googleusercontent.com/abc=w400", candidates[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00 }, "image/png")]
    public void TryDetectImageMimeFromBytes_recognizes_signatures(byte[] bytes, string expectedMime)
    {
        Assert.True(IntranetExternalFetchRules.TryDetectImageMimeFromBytes(bytes, out var mime));
        Assert.Equal(expectedMime, mime);
    }

    [Fact]
    public void LooksLikeImageResponse_accepts_magic_bytes_without_content_type()
    {
        var pngHeader = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00 };
        Assert.True(IntranetExternalFetchRules.LooksLikeImageResponse("text/html", pngHeader));
    }
}