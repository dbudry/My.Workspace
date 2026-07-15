using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

/// <summary>
/// Exercises <see cref="ProjectColorRules"/>. Before this rule existed each page rolled
/// its own color-source logic and the result diverged — ProjectManager fell back from
/// group to org, TaskCalendar used only the group color, Stopwatch/Tasks/Reports/Dashboard
/// showed no color at all. These tests lock the centralized behavior so all surfaces
/// stay aligned with the user's <see cref="ProjectColorSource"/> preference.
/// </summary>
public class ProjectColorRulesTests
{
    private const string OrgRed = "#c62828";
    private const string GroupBlue = "#1976d2";

    // ---------- GroupThenOrganization (default) ----------

    [Fact]
    public void GroupThenOrg_prefers_group_when_present()
    {
        Assert.Equal(GroupBlue,
            ProjectColorRules.Resolve(OrgRed, GroupBlue, ProjectColorSource.GroupThenOrganization));
    }

    [Fact]
    public void GroupThenOrg_falls_back_to_org_when_group_missing()
    {
        Assert.Equal(OrgRed,
            ProjectColorRules.Resolve(OrgRed, null, ProjectColorSource.GroupThenOrganization));
    }

    [Fact]
    public void GroupThenOrg_returns_null_when_both_missing()
    {
        Assert.Null(ProjectColorRules.Resolve(null, null, ProjectColorSource.GroupThenOrganization));
    }

    // ---------- Organization-only ----------

    [Fact]
    public void OrganizationOnly_ignores_group_color_even_when_present()
    {
        // Reason this case exists: a user picks "Organization" because they think in
        // client buckets, not project groups. The group's color must not bleed through.
        Assert.Equal(OrgRed,
            ProjectColorRules.Resolve(OrgRed, GroupBlue, ProjectColorSource.Organization));
    }

    [Fact]
    public void OrganizationOnly_returns_null_when_org_color_missing()
    {
        // No fallback to group — the user opted out of group-as-color.
        Assert.Null(ProjectColorRules.Resolve(null, GroupBlue, ProjectColorSource.Organization));
    }

    // ---------- ProjectGroup-only ----------

    [Fact]
    public void ProjectGroupOnly_ignores_org_color_even_when_present()
    {
        Assert.Equal(GroupBlue,
            ProjectColorRules.Resolve(OrgRed, GroupBlue, ProjectColorSource.ProjectGroup));
    }

    [Fact]
    public void ProjectGroupOnly_returns_null_when_group_color_missing()
    {
        Assert.Null(ProjectColorRules.Resolve(OrgRed, null, ProjectColorSource.ProjectGroup));
    }

    // ---------- None ----------

    [Theory]
    [InlineData(OrgRed, GroupBlue)]
    [InlineData(null, GroupBlue)]
    [InlineData(OrgRed, null)]
    [InlineData(null, null)]
    public void None_always_returns_null(string? org, string? group)
    {
        // Reason: users who find color clutter distracting explicitly opted out.
        // No "but they have a group color" fallback — None means none.
        Assert.Null(ProjectColorRules.Resolve(org, group, ProjectColorSource.None));
    }

    // ---------- Whitespace and empty-string handling ----------
    //
    // Empty strings have shown up in our DB before (legacy data, dialog "Save" pressed
    // with no picker interaction). Treat them as "no color set" rather than as a valid
    // color "" which would crash the picker style attribute.

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Whitespace_group_color_treated_as_missing(string blank)
    {
        Assert.Equal(OrgRed,
            ProjectColorRules.Resolve(OrgRed, blank, ProjectColorSource.GroupThenOrganization));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Whitespace_org_color_treated_as_missing(string blank)
    {
        Assert.Null(ProjectColorRules.Resolve(blank, null, ProjectColorSource.Organization));
    }

    // ---------- ResolveOrFallback ----------

    [Fact]
    public void ResolveOrFallback_substitutes_fallback_when_result_would_be_null()
    {
        Assert.Equal(ProjectColorRules.FallbackGray,
            ProjectColorRules.ResolveOrFallback(null, null, ProjectColorSource.GroupThenOrganization));
    }

    [Fact]
    public void ResolveOrFallback_substitutes_fallback_for_None_source()
    {
        // None always resolves to null; ResolveOrFallback must still return *something*.
        Assert.Equal(ProjectColorRules.FallbackGray,
            ProjectColorRules.ResolveOrFallback(OrgRed, GroupBlue, ProjectColorSource.None));
    }

    [Fact]
    public void ResolveOrFallback_returns_resolved_color_when_present()
    {
        Assert.Equal(GroupBlue,
            ProjectColorRules.ResolveOrFallback(OrgRed, GroupBlue, ProjectColorSource.GroupThenOrganization));
    }

    [Fact]
    public void ResolveOrFallback_honors_custom_fallback()
    {
        Assert.Equal("#000000",
            ProjectColorRules.ResolveOrFallback(null, null, ProjectColorSource.Organization, "#000000"));
    }

    // ---------- Default-enum-value sanity ----------

    [Fact]
    public void Default_enum_value_is_GroupThenOrganization()
    {
        // The DB default for the column is 0 (int) — and that maps to GroupThenOrganization.
        // If anyone reorders the enum the migration's default suddenly means something else,
        // which is exactly the kind of silent change this assertion catches.
        Assert.Equal(0, (int)ProjectColorSource.GroupThenOrganization);
        Assert.Equal(default(ProjectColorSource), ProjectColorSource.GroupThenOrganization);
    }

    // ---------- ResolveLabel — must mirror Resolve's source chain so the tooltip
    //            text on a color bar matches the entity whose color is shown.

    [Fact]
    public void ResolveLabel_returns_group_name_for_GroupThenOrg_when_group_color_set()
    {
        Assert.Equal("Profit Network",
            ProjectColorRules.ResolveLabel("Ball", "Profit Network", OrgRed, GroupBlue,
                ProjectColorSource.GroupThenOrganization));
    }

    [Fact]
    public void ResolveLabel_returns_org_name_for_GroupThenOrg_when_group_color_missing()
    {
        // Color resolution fell back to org → label must follow it; the user shouldn't
        // see the group's name on a bar drawn with the org's color.
        Assert.Equal("Ball",
            ProjectColorRules.ResolveLabel("Ball", "Profit Network", OrgRed, null,
                ProjectColorSource.GroupThenOrganization));
    }

    [Fact]
    public void ResolveLabel_returns_org_name_for_Organization_source()
    {
        Assert.Equal("Ball",
            ProjectColorRules.ResolveLabel("Ball", "Profit Network", OrgRed, GroupBlue,
                ProjectColorSource.Organization));
    }

    [Fact]
    public void ResolveLabel_returns_group_name_for_ProjectGroup_source()
    {
        Assert.Equal("Profit Network",
            ProjectColorRules.ResolveLabel("Ball", "Profit Network", OrgRed, GroupBlue,
                ProjectColorSource.ProjectGroup));
    }

    [Theory]
    [InlineData(ProjectColorSource.None)]
    public void ResolveLabel_returns_null_for_None(ProjectColorSource source)
    {
        Assert.Null(ProjectColorRules.ResolveLabel("Ball", "Profit Network", OrgRed, GroupBlue, source));
    }

    [Fact]
    public void ResolveLabel_returns_null_when_no_color_would_be_drawn()
    {
        // No color → no label. The bar wouldn't be rendered, so there's nothing to tooltip.
        Assert.Null(ProjectColorRules.ResolveLabel("Ball", "Profit Network", null, null,
            ProjectColorSource.GroupThenOrganization));
    }

    [Fact]
    public void ResolveLabel_returns_null_when_name_is_blank()
    {
        // Color exists but the entity's display name is empty — return null rather than
        // an empty tooltip, which would render an awkward zero-width bubble.
        Assert.Null(ProjectColorRules.ResolveLabel(null, null, OrgRed, GroupBlue,
            ProjectColorSource.Organization));
    }
}
