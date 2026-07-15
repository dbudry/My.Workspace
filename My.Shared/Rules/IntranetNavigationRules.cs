using My.Shared.Dtos.Intranet;

namespace My.Shared.Rules
{
    /// <summary>
    /// Curated intranet sidebar navigation: depth limits and tree shaping.
    /// Top-level items (no parent) are depth 1; each child adds one level.
    /// </summary>
    public static class IntranetNavigationRules
    {
        public const int DefaultMaxDepth = 10;
        public const int AbsoluteMinMaxDepth = 1;
        public const int AbsoluteMaxMaxDepth = 50;

        public static int ParseMaxDepth(string? value)
        {
            if (int.TryParse(value, out var parsed))
                return Math.Clamp(parsed, AbsoluteMinMaxDepth, AbsoluteMaxMaxDepth);
            return DefaultMaxDepth;
        }

        /// <summary>1-based depth. Returns 0 when <paramref name="itemId"/> is null/empty.</summary>
        public static int GetDepth(string? itemId, IReadOnlyDictionary<string, string?> parentById)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return 0;

            var depth = 1;
            var current = itemId;
            var visited = new HashSet<string>(StringComparer.Ordinal);

            while (parentById.TryGetValue(current, out var parentId) && !string.IsNullOrWhiteSpace(parentId))
            {
                if (!visited.Add(parentId))
                    break;
                depth++;
                current = parentId;
            }

            return depth;
        }

        public static int GetDepthForNewItem(string? parentId, IReadOnlyDictionary<string, string?> parentById) =>
            string.IsNullOrWhiteSpace(parentId) ? 1 : GetDepth(parentId, parentById) + 1;

        /// <summary>Levels below <paramref name="itemId"/> to the deepest descendant (0 = no children).</summary>
        public static int GetDeepestRelativeLevel(string itemId, IReadOnlyDictionary<string, List<string>> childrenById)
        {
            if (!childrenById.TryGetValue(itemId, out var kids) || kids.Count == 0)
                return 0;

            var max = 0;
            foreach (var kid in kids)
                max = Math.Max(max, 1 + GetDeepestRelativeLevel(kid, childrenById));
            return max;
        }

        public static Dictionary<string, string?> BuildParentById(IEnumerable<(string Id, string? ParentId)> items)
        {
            var map = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var (id, parentId) in items)
                map[id] = parentId;
            return map;
        }

        public static Dictionary<string, List<string>> BuildChildrenById(IEnumerable<(string Id, string? ParentId)> items)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var (id, parentId) in items)
            {
                if (string.IsNullOrWhiteSpace(parentId))
                    continue;
                if (!map.TryGetValue(parentId, out var list))
                {
                    list = new List<string>();
                    map[parentId] = list;
                }
                list.Add(id);
            }
            return map;
        }

        /// <summary>
        /// Validates placing an item (new or moved) under <paramref name="parentId"/>.
        /// When <paramref name="existingItemId"/> is set, descendants are included in the check.
        /// </summary>
        public static string? ValidatePlacement(
            string? parentId,
            IReadOnlyDictionary<string, string?> parentById,
            IReadOnlyDictionary<string, List<string>> childrenById,
            int maxDepth,
            string? existingItemId = null)
        {
            if (maxDepth < AbsoluteMinMaxDepth)
                return $"Navigation max depth must be at least {AbsoluteMinMaxDepth}.";

            var newItemDepth = GetDepthForNewItem(parentId, parentById);
            var deepestRelative = string.IsNullOrWhiteSpace(existingItemId)
                ? 0
                : GetDeepestRelativeLevel(existingItemId, childrenById);

            if (newItemDepth + deepestRelative > maxDepth)
            {
                return $"Navigation cannot nest deeper than {maxDepth} level(s). Choose a shallower parent " +
                       $"or increase Intranet Navigation Max Depth in App Settings.";
            }

            return null;
        }

        /// <summary>Removes descendants that would render below <paramref name="maxDepth"/> (1-based).</summary>
        public static List<IntranetNavigationItemDto> TrimToMaxDepth(
            IEnumerable<IntranetNavigationItemDto> roots,
            int maxDepth,
            int currentDepth = 1)
        {
            var result = new List<IntranetNavigationItemDto>();
            foreach (var item in roots)
            {
                var clone = new IntranetNavigationItemDto
                {
                    Id = item.Id,
                    Title = item.Title,
                    Icon = item.Icon,
                    PageId = item.PageId,
                    PageTitle = item.PageTitle,
                    PageSlug = item.PageSlug,
                    PageIsPublished = item.PageIsPublished,
                    ExternalUrl = item.ExternalUrl,
                    SortOrder = item.SortOrder,
                    IsVisible = item.IsVisible,
                    ParentId = item.ParentId,
                    Children = currentDepth < maxDepth
                        ? TrimToMaxDepth(item.Children ?? new(), maxDepth, currentDepth + 1)
                        : new List<IntranetNavigationItemDto>()
                };
                result.Add(clone);
            }
            return result;
        }
    }
}