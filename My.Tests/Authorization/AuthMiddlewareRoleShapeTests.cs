using My.Functions;
using My.Shared.Constants;
using Xunit;

namespace My.Tests.Authorization;

/// <summary>
/// Exercises <see cref="AuthMiddleware.IsValidRoleShape"/> — the allowlist that decides
/// which strings in the <c>X-Impersonate-Role</c> header are honored.
///
/// Regression coverage: this allowlist originally only recognized
/// <c>Admin</c>/<c>Manager</c>/<c>User</c>. When <c>Editor</c> was added as a real
/// assignable role (see <c>Constants.Roles.Assignable</c>), the impersonate dialog
/// correctly offered "Editor:Tyme", but this allowlist silently rejected it — an
/// Admin impersonating Editor:Tyme ended up with *zero* roles server-side (every
/// requested role failed the shape check and was filtered out), producing 403s across
/// every Tyme endpoint even though the client-side role picker looked correct.
/// </summary>
public class AuthMiddlewareRoleShapeTests
{
    [Theory]
    [InlineData(Constants.Roles.Admin)]
    [InlineData(Constants.Roles.Manager)]
    [InlineData(Constants.Roles.Editor)]
    [InlineData(Constants.Roles.User)]
    public void Bare_base_role_is_valid(string baseRole)
    {
        Assert.True(AuthMiddleware.IsValidRoleShape(baseRole));
    }

    [Theory]
    [InlineData("Admin:Tyme")]
    [InlineData("Manager:Tyme")]
    [InlineData("Editor:Tyme")] // the exact shape that regressed
    [InlineData("User:Tyme")]
    [InlineData("Editor:Intranet")]
    [InlineData("Admin:Organizations")]
    [InlineData("Admin:Some_Scope_1")]
    public void Scoped_role_with_recognized_base_is_valid(string role)
    {
        Assert.True(AuthMiddleware.IsValidRoleShape(role));
    }

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("root")]
    [InlineData("")]
    [InlineData("Admin:")]
    [InlineData("Admin:Tyme:Extra")]
    [InlineData("Admin:Ty me")]
    [InlineData("Admin:Ty-me")]
    public void Malformed_or_unrecognized_role_is_rejected(string role)
    {
        Assert.False(AuthMiddleware.IsValidRoleShape(role));
    }

    [Fact]
    public void Every_currently_assignable_role_passes_the_shape_check()
    {
        // Belt-and-suspenders: whatever Constants.Roles.Assignable() offers in the
        // impersonate dialog must always be honored server-side. This is the exact
        // class of bug that shipped — the two lists silently drifted apart.
        foreach (var role in Constants.Roles.Assignable())
        {
            Assert.True(AuthMiddleware.IsValidRoleShape(role), $"'{role}' is assignable but fails IsValidRoleShape.");
        }
    }
}
