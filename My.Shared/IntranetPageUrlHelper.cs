namespace My.Shared;

/// <summary>
/// Builds intranet page URLs and resolves route segments (page id vs slug).
/// </summary>
public static class IntranetPageUrlHelper
{
    public static bool LooksLikePageId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (Guid.TryParseExact(value, "N", out _)
            || Guid.TryParse(value, out _))
            return true;

        // PageIds are stored as Guid.ToString("N") (32 hex digits). Fixed seed ids in
        // Import-Intranet-ItKnowledgeBase.sql are 31 hex digits — still page ids, not slugs.
        var trimmed = value.Trim();
        if (trimmed.Length is 31 or 32
            && trimmed.All(static c => (c >= '0' && c <= '9')
                || (c >= 'a' && c <= 'f')
                || (c >= 'A' && c <= 'F')))
            return true;

        return false;
    }

    /// <summary>
    /// Browser path for viewing a page. Prefers slug when set (e.g. /intranet/pages/it).
    /// </summary>
    public static string GetViewPath(string pageId, string? slug = null)
    {
        var segment = string.IsNullOrWhiteSpace(slug) ? pageId : slug.Trim();
        return $"/intranet/pages/{segment}";
    }
}