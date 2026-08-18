using My.Shared.Dtos.Intranet;

namespace My.Shared.Navigation;

/// <summary>
/// Route-to-sidebar expansion for curated intranet nav. Keeps
/// <see cref="My.Client.Layout.NavMenu"/> sync logic testable.
/// </summary>
public static class SidebarNavExpansionRules
{
    public static bool IsTymeRoute(string relativePath) =>
        relativePath.Equals("tyme", StringComparison.OrdinalIgnoreCase)
        || relativePath.StartsWith("tyme/", StringComparison.OrdinalIgnoreCase);

    public static bool IsAdminRoute(string relativePath) =>
        relativePath.Equals("admin", StringComparison.OrdinalIgnoreCase)
        || relativePath.StartsWith("admin/", StringComparison.OrdinalIgnoreCase);

    public static bool IsIntranetMaintenanceRoute(string relativePath) =>
        relativePath.Equals("intranet/pages", StringComparison.OrdinalIgnoreCase)
        || relativePath.Equals("intranet/navigation", StringComparison.OrdinalIgnoreCase)
        || relativePath.Equals("intranet/documents", StringComparison.OrdinalIgnoreCase)
        || relativePath.StartsWith("intranet/editor", StringComparison.OrdinalIgnoreCase);

    public static string NavKey(IntranetNavigationItemDto item) => $"nav:{item.Id}";

    public static bool ItemMatchesPath(IntranetNavigationItemDto item, string relativePath)
    {
        if (!string.IsNullOrWhiteSpace(item.ExternalUrl) || string.IsNullOrWhiteSpace(item.PageId))
            return false;

        var segment = string.IsNullOrWhiteSpace(item.PageSlug) ? item.PageId : item.PageSlug.Trim();
        var itemPath = $"intranet/pages/{segment}";
        return string.Equals(relativePath, itemPath, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryGetCuratedExpansion(
        IEnumerable<IntranetNavigationItemDto> topLevelItems,
        string relativePath,
        out string? topKey,
        out Dictionary<string, string> nestedKeys)
    {
        topKey = null;
        nestedKeys = new(StringComparer.Ordinal);

        foreach (var item in VisibleNavChildren(topLevelItems))
        {
            var ancestors = new List<(string ParentKey, string ChildKey)>();
            if (FindCuratedMatch(item, NavKey(item), relativePath, ancestors))
            {
                topKey = NavKey(item);
                foreach (var (parentKey, childKey) in ancestors)
                    nestedKeys[parentKey] = childKey;
                return true;
            }
        }

        return false;
    }

    private static bool FindCuratedMatch(
        IntranetNavigationItemDto item,
        string groupKey,
        string relativePath,
        List<(string ParentKey, string ChildKey)> ancestors)
    {
        if (ItemMatchesPath(item, relativePath))
            return true;

        var children = VisibleNavChildren(item);
        if (children.Count == 0)
            return false;

        foreach (var child in children)
        {
            var childKey = NavKey(child);
            if (FindCuratedMatch(child, childKey, relativePath, ancestors))
            {
                if (VisibleNavChildren(child).Count > 0)
                    ancestors.Insert(0, (groupKey, childKey));
                return true;
            }
        }

        return false;
    }

    private static List<IntranetNavigationItemDto> VisibleNavChildren(IntranetNavigationItemDto item) =>
        (item.Children ?? [])
            .Where(c => c.IsVisible)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Title)
            .ToList();

    private static List<IntranetNavigationItemDto> VisibleNavChildren(IEnumerable<IntranetNavigationItemDto> items) =>
        items
            .Where(i => i.IsVisible)
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Title)
            .ToList();
}