namespace My.Shared.Navigation;

/// <summary>
/// Central accordion rules for the main sidebar. <see cref="My.Client.Layout.NavMenu"/> must
/// delegate all expand/collapse mutations here so behavior stays testable and cannot drift silently.
/// </summary>
public sealed class SidebarNavAccordionState
{
    public const string BuiltInTymeKey = "tyme";
    public const string BuiltInAdminKey = "admin";
    public const string BuiltInIntranetMaintenanceKey = "intranet-maintenance";

    private readonly Dictionary<string, string> _expandedNestedChildByParent = new(StringComparer.Ordinal);

    public string? ExpandedBuiltInGroupKey { get; private set; }

    /// <summary>At most one curated top-level intranet group may be expanded (accordion).</summary>
    public string? ExpandedCuratedTopLevelKey { get; private set; }

    public IReadOnlyDictionary<string, string> ExpandedNestedChildByParent => _expandedNestedChildByParent;

    public void ToggleBuiltInGroup(string key, bool expanded)
    {
        ExpandedBuiltInGroupKey = expanded ? key : (ExpandedBuiltInGroupKey == key ? null : ExpandedBuiltInGroupKey);
        if (expanded)
        {
            ExpandedCuratedTopLevelKey = null;
            _expandedNestedChildByParent.Clear();
        }
    }

    public void ToggleCuratedTopLevel(string key, bool expanded)
    {
        ExpandedCuratedTopLevelKey = expanded ? key : (ExpandedCuratedTopLevelKey == key ? null : ExpandedCuratedTopLevelKey);
        if (expanded)
        {
            ExpandedBuiltInGroupKey = null;
            _expandedNestedChildByParent.Clear();
        }
    }

    public void ToggleNestedChild(string parentKey, string childKey, bool expanded)
    {
        if (expanded)
            _expandedNestedChildByParent[parentKey] = childKey;
        else if (_expandedNestedChildByParent.TryGetValue(parentKey, out var current) && current == childKey)
            _expandedNestedChildByParent.Remove(parentKey);
    }

    public void ApplyRouteExpansion(
        string? builtInKey,
        string? curatedTopKey,
        IReadOnlyDictionary<string, string>? nestedKeys)
    {
        ExpandedBuiltInGroupKey = builtInKey;
        ExpandedCuratedTopLevelKey = curatedTopKey;
        _expandedNestedChildByParent.Clear();
        if (nestedKeys == null) return;
        foreach (var (parentKey, childKey) in nestedKeys)
            _expandedNestedChildByParent[parentKey] = childKey;
    }

    public void ClearAll()
    {
        ExpandedBuiltInGroupKey = null;
        ExpandedCuratedTopLevelKey = null;
        _expandedNestedChildByParent.Clear();
    }

    public SidebarNavAccordionSnapshot CreateSnapshot() => new()
    {
        BuiltInGroupKey = ExpandedBuiltInGroupKey,
        CuratedTopLevelKey = ExpandedCuratedTopLevelKey,
        NestedChildByParent = new Dictionary<string, string>(_expandedNestedChildByParent, StringComparer.Ordinal)
    };

    public void ApplySnapshot(SidebarNavAccordionSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            ClearAll();
            return;
        }

        ApplyRouteExpansion(
            snapshot.BuiltInGroupKey,
            snapshot.CuratedTopLevelKey,
            snapshot.NestedChildByParent);
    }

    public bool IsBuiltInExpanded(string key) =>
        string.Equals(ExpandedBuiltInGroupKey, key, StringComparison.Ordinal);

    public bool IsCuratedTopLevelExpanded(string key) =>
        string.Equals(ExpandedCuratedTopLevelKey, key, StringComparison.Ordinal);

    public bool IsNestedChildExpanded(string parentKey, string childKey) =>
        _expandedNestedChildByParent.TryGetValue(parentKey, out var current)
        && string.Equals(current, childKey, StringComparison.Ordinal);

    /// <summary>Used by tests to guard the curated top-level accordion invariant.</summary>
    public int CuratedTopLevelExpandedCount =>
        string.IsNullOrEmpty(ExpandedCuratedTopLevelKey) ? 0 : 1;

    /// <summary>
    /// Structural invariants that must always hold (toggle, route sync, or snapshot restore).
    /// Tests call this after mutation sequences to catch accordion regressions early.
    /// </summary>
    public void AssertStructuralInvariants()
    {
        if (CuratedTopLevelExpandedCount > 1)
            throw new InvalidOperationException("At most one curated top-level group may be expanded.");

        if (ExpandedBuiltInGroupKey is not null && string.IsNullOrWhiteSpace(ExpandedBuiltInGroupKey))
            throw new InvalidOperationException("Built-in expanded key must be null or non-empty.");

        if (ExpandedCuratedTopLevelKey is not null && string.IsNullOrWhiteSpace(ExpandedCuratedTopLevelKey))
            throw new InvalidOperationException("Curated top-level expanded key must be null or non-empty.");
    }

    /// <summary>
    /// Invariant after a user-driven top-level expand (click). Built-in and curated groups
    /// are mutually exclusive; persisted dashboard snapshots may still hold both by design.
    /// </summary>
    public void AssertUserToggleTopLevelInvariant()
    {
        AssertStructuralInvariants();

        if (ExpandedBuiltInGroupKey is not null && ExpandedCuratedTopLevelKey is not null)
            throw new InvalidOperationException(
                "User toggles must not leave both a built-in group and a curated top-level group expanded.");
    }
}