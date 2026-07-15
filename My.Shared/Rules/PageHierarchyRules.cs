namespace My.Shared.Rules;

/// <summary>
/// Rules for the intranet page tree. Pages form a parent/child hierarchy
/// (<c>ParentPageId</c>), and the tree is rendered recursively, so a page must never
/// become its own ancestor — a cycle would loop the renderer forever. Moving a page is
/// the only operation that can introduce one, so the move endpoint guards with
/// <see cref="WouldCreateCycle"/>.
/// </summary>
public static class PageHierarchyRules
{
    /// <summary>
    /// True if re-parenting <paramref name="pageId"/> under <paramref name="newParentId"/>
    /// would make the page its own ancestor. Walks up the parent chain from the proposed
    /// parent; reaching <paramref name="pageId"/> means a cycle (self-parent is the depth-0
    /// case). <paramref name="parentById"/> maps every page id to its parent id (null for
    /// roots). A safety net also reports a cycle if the walk exceeds the node count, which
    /// only happens when the data already contains one.
    /// </summary>
    public static bool WouldCreateCycle(
        string pageId,
        string newParentId,
        IReadOnlyDictionary<string, string?> parentById)
    {
        var current = (string?)newParentId;
        var steps = 0;
        while (current != null)
        {
            if (string.Equals(current, pageId, StringComparison.Ordinal))
                return true;
            if (++steps > parentById.Count)
                return true;
            if (!parentById.TryGetValue(current, out var next))
                break;
            current = next;
        }

        return false;
    }
}
