using Ganss.Xss;

namespace My.Shared.Rules;

/// <summary>
/// Server-side allowlist for intranet page HTML persisted from the Quill editor.
/// </summary>
public static class IntranetHtmlSanitizer
{
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    public static string? SanitizeForStorage(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html;

        return Sanitizer.Sanitize(html);
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();

        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[]
                 {
                     "p", "h1", "h2", "h3", "h4", "h5", "h6",
                     "strong", "b", "em", "i", "u", "s", "strike", "del",
                     "a", "ul", "ol", "li", "br", "img", "blockquote", "pre", "code", "span"
                 })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.AllowedAttributes.Clear();
        foreach (var attr in new[]
                 {
                     "href", "target", "rel", "src", "alt", "width", "height", "style", "class", "referrerpolicy",
                     "data-drive-file-id", "data-intranet-media", "data-external-image", "data-external-src",
                     "data-intranet-hydrated"
                 })
        {
            sanitizer.AllowedAttributes.Add(attr);
        }

        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("data");

        foreach (var css in new[] { "max-width", "height", "width", "auto" })
            sanitizer.AllowedCssProperties.Add(css);

        sanitizer.FilterUrl += (_, e) =>
        {
            if (e.OriginalUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                && !e.OriginalUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                e.SanitizedUrl = string.Empty;
            }
        };

        return sanitizer;
    }
}