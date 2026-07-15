using My.Shared.Navigation;
using Xunit;

namespace My.Tests.Navigation;

public class SidebarNavAccordionStateTests
{
    [Fact]
    public void CuratedTopLevel_ExpandingSecondGroup_CollapsesFirst()
    {
        var state = new SidebarNavAccordionState();

        state.ToggleCuratedTopLevel("nav:it", expanded: true);
        state.ToggleCuratedTopLevel("nav:hr", expanded: true);

        Assert.False(state.IsCuratedTopLevelExpanded("nav:it"));
        Assert.True(state.IsCuratedTopLevelExpanded("nav:hr"));
        Assert.Equal(1, state.CuratedTopLevelExpandedCount);
    }

    [Fact]
    public void CuratedTopLevel_CollapsingActiveGroup_LeavesNoneExpanded()
    {
        var state = new SidebarNavAccordionState();

        state.ToggleCuratedTopLevel("nav:it", expanded: true);
        state.ToggleCuratedTopLevel("nav:it", expanded: false);

        Assert.Null(state.ExpandedCuratedTopLevelKey);
        Assert.Equal(0, state.CuratedTopLevelExpandedCount);
    }

    [Fact]
    public void NestedChild_ExpandingSecondSibling_UnderSameParent_ReplacesFirst()
    {
        var state = new SidebarNavAccordionState();

        state.ToggleNestedChild("nav:it", "nav:it-child-a", expanded: true);
        state.ToggleNestedChild("nav:it", "nav:it-child-b", expanded: true);

        Assert.False(state.IsNestedChildExpanded("nav:it", "nav:it-child-a"));
        Assert.True(state.IsNestedChildExpanded("nav:it", "nav:it-child-b"));
    }

    [Fact]
    public void CuratedTopLevel_Expand_ClearsNestedChildren()
    {
        var state = new SidebarNavAccordionState();
        state.ToggleNestedChild("nav:it", "nav:it-child-a", expanded: true);

        state.ToggleCuratedTopLevel("nav:hr", expanded: true);

        Assert.Empty(state.ExpandedNestedChildByParent);
    }

    [Fact]
    public void BuiltInGroup_ExpandingSecondGroup_CollapsesFirst()
    {
        var state = new SidebarNavAccordionState();

        state.ToggleBuiltInGroup(SidebarNavAccordionState.BuiltInTymeKey, expanded: true);
        state.ToggleBuiltInGroup(SidebarNavAccordionState.BuiltInAdminKey, expanded: true);

        Assert.False(state.IsBuiltInExpanded(SidebarNavAccordionState.BuiltInTymeKey));
        Assert.True(state.IsBuiltInExpanded(SidebarNavAccordionState.BuiltInAdminKey));
    }

    [Fact]
    public void BuiltInGroup_Expand_CollapsesCuratedTopLevel()
    {
        var state = new SidebarNavAccordionState();
        state.ToggleCuratedTopLevel("nav:kb", expanded: true);

        state.ToggleBuiltInGroup(SidebarNavAccordionState.BuiltInTymeKey, expanded: true);

        Assert.Null(state.ExpandedCuratedTopLevelKey);
        Assert.True(state.IsBuiltInExpanded(SidebarNavAccordionState.BuiltInTymeKey));
    }

    [Fact]
    public void CuratedTopLevel_Expand_CollapsesBuiltInGroup()
    {
        var state = new SidebarNavAccordionState();
        state.ToggleBuiltInGroup(SidebarNavAccordionState.BuiltInIntranetMaintenanceKey, expanded: true);

        state.ToggleCuratedTopLevel("nav:kb", expanded: true);

        Assert.Null(state.ExpandedBuiltInGroupKey);
        Assert.True(state.IsCuratedTopLevelExpanded("nav:kb"));
    }

    [Fact]
    public void ApplyRouteExpansion_SetsSingleCuratedTopLevelKey()
    {
        var state = new SidebarNavAccordionState();
        state.ToggleCuratedTopLevel("nav:it", expanded: true);

        state.ApplyRouteExpansion(
            builtInKey: null,
            curatedTopKey: "nav:hr",
            nestedKeys: new Dictionary<string, string> { ["nav:hr"] = "nav:hr-child" });

        Assert.Equal("nav:hr", state.ExpandedCuratedTopLevelKey);
        Assert.Equal(1, state.CuratedTopLevelExpandedCount);
        Assert.True(state.IsNestedChildExpanded("nav:hr", "nav:hr-child"));
    }

    [Fact]
    public void CreateSnapshot_AndApplySnapshot_RoundTripsState()
    {
        var state = new SidebarNavAccordionState();
        state.ApplyRouteExpansion(
            SidebarNavAccordionState.BuiltInTymeKey,
            "nav:it",
            new Dictionary<string, string> { ["nav:it"] = "nav:it-child" });

        var restored = new SidebarNavAccordionState();
        restored.ApplySnapshot(state.CreateSnapshot());

        Assert.True(restored.IsBuiltInExpanded(SidebarNavAccordionState.BuiltInTymeKey));
        Assert.True(restored.IsCuratedTopLevelExpanded("nav:it"));
        Assert.True(restored.IsNestedChildExpanded("nav:it", "nav:it-child"));
    }

    [Fact]
    public void ApplySnapshot_Null_ClearsState()
    {
        var state = new SidebarNavAccordionState();
        state.ToggleBuiltInGroup(SidebarNavAccordionState.BuiltInAdminKey, expanded: true);

        state.ApplySnapshot(null);

        Assert.Null(state.ExpandedBuiltInGroupKey);
        Assert.Null(state.ExpandedCuratedTopLevelKey);
        Assert.Empty(state.ExpandedNestedChildByParent);
    }

    [Fact]
    public void UserClickSequence_KbThenTymeThenAdmin_MaintainsSingleTopLevelExpansion()
    {
        var state = new SidebarNavAccordionState();

        state.ToggleCuratedTopLevel("nav:kb", expanded: true);
        state.AssertUserToggleTopLevelInvariant();

        state.ToggleBuiltInGroup(SidebarNavAccordionState.BuiltInTymeKey, expanded: true);
        state.AssertUserToggleTopLevelInvariant();
        Assert.Null(state.ExpandedCuratedTopLevelKey);

        state.ToggleBuiltInGroup(SidebarNavAccordionState.BuiltInAdminKey, expanded: true);
        state.AssertUserToggleTopLevelInvariant();
        Assert.False(state.IsBuiltInExpanded(SidebarNavAccordionState.BuiltInTymeKey));
        Assert.True(state.IsBuiltInExpanded(SidebarNavAccordionState.BuiltInAdminKey));

        state.ToggleCuratedTopLevel("nav:kb", expanded: true);
        state.AssertUserToggleTopLevelInvariant();
        Assert.Null(state.ExpandedBuiltInGroupKey);
        Assert.True(state.IsCuratedTopLevelExpanded("nav:kb"));
    }

    [Fact]
    public void AssertStructuralInvariants_AllowsSnapshotWithBothTopLevelKeys()
    {
        var state = new SidebarNavAccordionState();
        state.ApplyRouteExpansion(
            SidebarNavAccordionState.BuiltInTymeKey,
            "nav:kb",
            new Dictionary<string, string> { ["nav:kb"] = "nav:security" });

        state.AssertStructuralInvariants();
    }
}