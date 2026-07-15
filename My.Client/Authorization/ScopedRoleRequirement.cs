using Microsoft.AspNetCore.Authorization;

namespace My.Client.Authorization;

public class ScopedRoleRequirement : IAuthorizationRequirement
{
    public string Scope { get; }
    public string MinimumRole { get; }

    /// <summary>
    /// When true, global roles do NOT satisfy this requirement — only scoped roles in
    /// <see cref="Scope"/>. Set via policy names ending in <c>:Scoped</c>, e.g.
    /// <c>Tyme:Manager:Scoped</c>.
    /// </summary>
    public bool ScopedOnly { get; }

    public ScopedRoleRequirement(string scope, string minimumRole, bool scopedOnly = false)
    {
        Scope = scope;
        MinimumRole = minimumRole;
        ScopedOnly = scopedOnly;
    }
}
