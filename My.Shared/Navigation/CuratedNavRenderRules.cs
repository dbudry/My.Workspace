namespace My.Shared.Navigation;

/// <summary>
/// Sidebar curated-nav indent math. <see cref="My.Client.Components.Layout.CuratedNavItem"/>
/// must use these values so depth padding cannot drift from CSS.
/// </summary>
public static class CuratedNavRenderRules
{
    public const int BasePaddingPx = 16;
    public const int DepthStepPx = 4;

    public static int GetLinkPaddingPx(int depth) =>
        BasePaddingPx + Math.Max(0, depth) * DepthStepPx;

    public static string GetDepthStyle(int depth) =>
        $"--curated-nav-depth: {depth}; --curated-nav-link-padding: {GetLinkPaddingPx(depth)}px;";

    /// <summary>Each depth must indent further than its parent (guards flat nesting).</summary>
    public static bool IsStrictlyIncreasingPadding(int parentDepth, int childDepth) =>
        GetLinkPaddingPx(childDepth) > GetLinkPaddingPx(parentDepth);
}