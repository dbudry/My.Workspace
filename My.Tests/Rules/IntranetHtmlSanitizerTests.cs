using My.Shared;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class IntranetHtmlSanitizerTests
{
    [Fact]
    public void SanitizeForStorage_strips_script_tags()
    {
        var input = "<p>Hello</p><script>alert(1)</script>";
        var result = IntranetHtmlSanitizer.SanitizeForStorage(input);
        Assert.DoesNotContain("<script", result!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello", result!, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeForStorage_strips_event_handlers()
    {
        var input = """<p><img src="x" onerror="alert(1)" alt="bad" /></p>""";
        var result = IntranetHtmlSanitizer.SanitizeForStorage(input);
        Assert.DoesNotContain("onerror", result!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizeForStorage_preserves_drive_backed_image_markup()
    {
        var placeholder = IntranetFileHelper.ImagePlaceholderSrc;
        var input =
            $"""<p><img src="{placeholder}" alt="diagram" data-drive-file-id="abc123" data-intranet-media="true" style="max-width:100%;height:auto;" /></p>""";

        var result = IntranetHtmlSanitizer.SanitizeForStorage(input);

        Assert.Contains("data-drive-file-id", result!, StringComparison.Ordinal);
        Assert.Contains("abc123", result!, StringComparison.Ordinal);
        Assert.Contains("diagram", result!, StringComparison.Ordinal);
        Assert.Contains("max-width", result!, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeForStorage_preserves_external_image_reference()
    {
        var input =
            """<p><img src="https://cdn.example.com/a.png" alt="ref" data-external-image="true" data-external-src="https://cdn.example.com/a.png" referrerpolicy="no-referrer" /></p>""";

        var result = IntranetHtmlSanitizer.SanitizeForStorage(input);

        Assert.Contains("data-external-image", result!, StringComparison.Ordinal);
        Assert.Contains("https://cdn.example.com/a.png", result!, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeForStorage_preserves_links()
    {
        var input = """<p><a href="https://example.com/doc" target="_blank" rel="noopener noreferrer">Guide</a></p>""";
        var result = IntranetHtmlSanitizer.SanitizeForStorage(input);
        Assert.Contains("https://example.com/doc", result!, StringComparison.Ordinal);
        Assert.Contains("Guide", result!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizeForStorage_returns_empty_input_unchanged(string? input) =>
        Assert.Equal(input, IntranetHtmlSanitizer.SanitizeForStorage(input));
}