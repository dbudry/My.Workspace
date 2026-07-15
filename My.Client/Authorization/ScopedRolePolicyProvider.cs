using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace My.Client.Authorization;

public class ScopedRolePolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public ScopedRolePolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    /// <summary>
    /// Recognizes policy names in the format <c>Scope:MinRole</c> (e.g. <c>Tyme:Manager</c>)
    /// or the scope-strict variant <c>Scope:MinRole:Scoped</c> (e.g. <c>Tyme:Manager:Scoped</c>),
    /// which rejects global Admin/Manager. Falls back to the default provider otherwise.
    /// </summary>
    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var parts = policyName.Split(':');
        if (parts.Length >= 2 && parts.Length <= 3)
        {
            var scope = parts[0];
            var minRole = parts[1];
            var scopedOnly = parts.Length == 3 && parts[2].Equals("Scoped", StringComparison.Ordinal);

            // If the third segment is present but isn't the literal "Scoped" suffix, fall
            // through to the default provider rather than silently treating it as scoped.
            if (parts.Length == 3 && !scopedOnly)
                return _fallback.GetPolicyAsync(policyName);

            var validRoles = new[] { "User", "Manager", "Admin" };
            if (validRoles.Contains(minRole))
            {
                var policy = new AuthorizationPolicyBuilder()
                    .AddRequirements(new ScopedRoleRequirement(scope, minRole, scopedOnly))
                    .Build();
                return Task.FromResult<AuthorizationPolicy?>(policy);
            }
        }

        return _fallback.GetPolicyAsync(policyName);
    }
}
