using System.Security.Claims;

namespace My.Client.Services;

/// <summary>
/// Applies provision roles to a <see cref="ClaimsIdentity"/>. Provision is the sole
/// source of truth — any prior role claims (from OIDC storage or earlier factory
/// runs) are removed before the fresh set is added.
/// </summary>
public static class RoleClaimsHelper
{
    public static void ApplyProvisionRoles(ClaimsIdentity identity, IEnumerable<string> roles)
    {
        foreach (var claim in identity.FindAll(ClaimTypes.Role).ToList())
            identity.RemoveClaim(claim);

        foreach (var role in roles)
        {
            if (!string.IsNullOrWhiteSpace(role))
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }
    }
}