using System.Security.Claims;
using My.Client.Services;
using Xunit;

namespace My.Tests.Services;

public class RoleClaimsHelperTests
{
    [Fact]
    public void ApplyProvisionRoles_replaces_stale_roles_with_provision_set()
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.Role, "Manager:Tyme"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));

        RoleClaimsHelper.ApplyProvisionRoles(identity, new[] { "Admin", "User:Tyme", "Editor:Intranet" });

        var roles = identity.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Equal(3, roles.Count);
        Assert.DoesNotContain("Manager:Tyme", roles);
        Assert.Contains("User:Tyme", roles);
        Assert.Contains("Admin", roles);
        Assert.Contains("Editor:Intranet", roles);
    }

    [Fact]
    public void ApplyProvisionRoles_clears_roles_when_provision_returns_empty()
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.Role, "Manager:Tyme"));

        RoleClaimsHelper.ApplyProvisionRoles(identity, Array.Empty<string>());

        Assert.Empty(identity.FindAll(ClaimTypes.Role));
    }
}