using System.Text.RegularExpressions;

namespace My.Shared;

/// <summary>
/// Intranet page search: query parsing, HTML stripping, scoring, and excerpt building.
/// </summary>
public static class IntranetSearchHelper
{
    public const int DefaultResultLimit = 20;
    public const int MaxResultLimit = 50;
    public const int MinQueryLength = 2;

    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public sealed record PageSearchRecord(
        string PageId,
        string Title,
        string? Slug,
        string? ContentHtml,
        bool IsPublished,
        DateTime UpdatedAt,
        string? AttachmentSearchText = null);

    public sealed record PageSearchHit(
        PageSearchRecord Page,
        int Score,
        string Excerpt);

    public static string[] ParseQueryTerms(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        return query
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .ToArray();
    }

    public static string StripHtmlForSearch(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        var text = HtmlTagRegex.Replace(html, " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return WhitespaceRegex.Replace(text, " ").Trim();
    }

    /// <summary>
    /// Builds searchable text from non-image attachment file names (e.g. flex-seal-guide.pdf).
    /// Includes both hyphenated and space-separated forms so "silly dog" and "silly-dog" match.
    /// </summary>
    public static string BuildAttachmentSearchText(IEnumerable<(string Name, string? MimeType)> attachments)
    {
        if (attachments == null)
            return "";

        var parts = new List<string>();
        foreach (var (name, mimeType) in attachments)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            if (IntranetFileHelper.ClassifyMimeType(mimeType, name) == IntranetFileHelper.FileTypeImage)
                continue;

            var baseName = Path.GetFileNameWithoutExtension(name.Trim()).ToLowerInvariant();
            if (string.IsNullOrEmpty(baseName))
                continue;

            parts.Add(baseName);
            var spaced = baseName.Replace('-', ' ').Replace('_', ' ');
            if (spaced != baseName)
                parts.Add(spaced);
        }

        return string.Join(' ', parts);
    }

    public static string BuildExcerpt(string? plainText, int maxLength = 140)
    {
        if (string.IsNullOrWhiteSpace(plainText))
            return "";

        var trimmed = plainText.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength].TrimEnd() + "…";
    }

    /// <summary>
    /// Scores pages where every query term appears somewhere (AND). Title matches weigh highest.
    /// </summary>
    public static IReadOnlyList<PageSearchHit> SearchPages(
        IEnumerable<PageSearchRecord> pages,
        string[] terms,
        int limit = DefaultResultLimit)
    {
        if (terms.Length == 0)
            return [];

        limit = Math.Clamp(limit, 1, MaxResultLimit);
        var hits = new List<PageSearchHit>();

        foreach (var page in pages)
        {
            var plainBody = StripHtmlForSearch(page.ContentHtml);
            var attachmentText = page.AttachmentSearchText ?? "";
            var scoreFields = new List<(string Text, int Weight)>
            {
                (page.Title ?? "", 10),
                (page.Slug ?? "", 8),
            };
            if (!string.IsNullOrEmpty(attachmentText))
                scoreFields.Add((attachmentText, 6));
            scoreFields.Add((plainBody, 2));

            var (score, matchedTerms) = ScoreFields(terms, scoreFields.ToArray());

            if (score <= 0 || matchedTerms != terms.Length)
                continue;

            var excerptSource = !string.IsNullOrWhiteSpace(plainBody)
                ? plainBody
                : attachmentText;
            hits.Add(new PageSearchHit(page, score, BuildExcerpt(excerptSource)));
        }

        return hits
            .OrderByDescending(h => h.Score)
            .ThenByDescending(h => h.Page.UpdatedAt)
            .ThenBy(h => h.Page.Title, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    private static (int Score, int MatchedTerms) ScoreFields(string[] terms, params (string Text, int Weight)[] fields)
    {
        var total = 0;
        var matched = new bool[terms.Length];

        foreach (var (text, weight) in fields)
        {
            if (string.IsNullOrEmpty(text))
                continue;

            var lower = text.ToLowerInvariant();
            for (var i = 0; i < terms.Length; i++)
            {
                if (!lower.Contains(terms[i], StringComparison.Ordinal))
                    continue;

                total += weight;
                matched[i] = true;
            }
        }

        return (total, matched.Count(m => m));
    }
}