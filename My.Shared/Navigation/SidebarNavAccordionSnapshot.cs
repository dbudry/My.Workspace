namespace My.Shared.Navigation;

/// <summary>
/// Serializable sidebar accordion state for localStorage restore across browser sessions.
/// </summary>
public sealed class SidebarNavAccordionSnapshot
{
    public string? BuiltInGroupKey { get; init; }

    public string? CuratedTopLevelKey { get; init; }

    public Dictionary<string, string>? NestedChildByParent { get; init; }
}