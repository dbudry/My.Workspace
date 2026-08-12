using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

/// <summary>
/// Exercises <see cref="ProjectPermissionRules.CanSetSharedAvailability"/> — the rule
/// that keeps Editor:Tyme (create/edit projects) from also being able to flip a
/// project's "share availability with team" flag, which is reserved for Manager+.
/// </summary>
public class ProjectPermissionRulesTests
{
    [Fact]
    public void Manager_can_turn_shared_availability_on_when_creating()
    {
        Assert.True(ProjectPermissionRules.CanSetSharedAvailability(
            requestedIsSharedAvailability: true,
            currentIsSharedAvailability: false,
            callerHasManagerAccess: true));
    }

    [Fact]
    public void Editor_cannot_turn_shared_availability_on_when_creating()
    {
        Assert.False(ProjectPermissionRules.CanSetSharedAvailability(
            requestedIsSharedAvailability: true,
            currentIsSharedAvailability: false,
            callerHasManagerAccess: false));
    }

    [Fact]
    public void Editor_creating_with_flag_off_is_fine()
    {
        // The common case: Editor creates a normal project, flag never set.
        Assert.True(ProjectPermissionRules.CanSetSharedAvailability(
            requestedIsSharedAvailability: false,
            currentIsSharedAvailability: false,
            callerHasManagerAccess: false));
    }

    [Fact]
    public void Editor_can_save_an_edit_that_leaves_shared_availability_unchanged()
    {
        // Editing a project a Manager previously flagged IsSharedAvailability=true:
        // the edit dialog round-trips the existing value without showing the toggle.
        // That must not be blocked just because the caller lacks Manager access.
        Assert.True(ProjectPermissionRules.CanSetSharedAvailability(
            requestedIsSharedAvailability: true,
            currentIsSharedAvailability: true,
            callerHasManagerAccess: false));
    }

    [Fact]
    public void Editor_cannot_turn_shared_availability_off_on_edit()
    {
        Assert.False(ProjectPermissionRules.CanSetSharedAvailability(
            requestedIsSharedAvailability: false,
            currentIsSharedAvailability: true,
            callerHasManagerAccess: false));
    }

    [Fact]
    public void Editor_cannot_turn_shared_availability_on_via_edit()
    {
        Assert.False(ProjectPermissionRules.CanSetSharedAvailability(
            requestedIsSharedAvailability: true,
            currentIsSharedAvailability: false,
            callerHasManagerAccess: false));
    }

    [Fact]
    public void Manager_can_change_shared_availability_either_direction()
    {
        Assert.True(ProjectPermissionRules.CanSetSharedAvailability(true, false, callerHasManagerAccess: true));
        Assert.True(ProjectPermissionRules.CanSetSharedAvailability(false, true, callerHasManagerAccess: true));
    }
}
