namespace My.Shared.Rules;

/// <summary>
/// Server-side checks for Google OIDC identities. The SPA may send an optional
/// <c>hd</c> hint for a single configured domain — these rules are the enforcement layer.
/// </summary>
public static class GoogleIdentityRules
{
    /// <summary>Env var: comma-separated allowed email domains, or <c>*</c> for any verified Google email.</summary>
    public const string AllowedEmailDomainsEnvKey = "Auth__AllowedEmailDomains";

    /// <summary>AppSettings / setup key for the same policy (DB-backed after wizard). Keep in sync with Constants.SettingKeys.AuthAllowedEmailDomains.</summary>
    public const string AllowedEmailDomainsSettingKey = "Auth:AllowedEmailDomains";

    /// <summary>Default when nothing is configured (setup incomplete). Empty = reject all until configured.</summary>
    public const string DefaultAllowedEmailDomains = "";

    /// <summary>
    /// Parses a domains config string into a normalized list.
    /// <c>*</c> or <c>any</c> alone means allow any verified email domain.
    /// </summary>
    public static IReadOnlyList<string> ParseAllowedDomains(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return Array.Empty<string>();

        var parts = configured
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Trim().TrimStart('@').ToLowerInvariant())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return parts;
    }

    public static bool AllowsAnyDomain(IReadOnlyList<string> domains) =>
        domains.Count > 0 && domains.Any(d =>
            d is "*" or "any" or "all");

    public static bool IsDomainPolicyConfigured(string? configured)
    {
        var domains = ParseAllowedDomains(configured);
        return domains.Count > 0;
    }

    /// <summary>
    /// When exactly one real domain is configured (not allow-any), returns it for OIDC <c>hd</c>.
    /// </summary>
    public static string? GetSingleHostedDomainHint(string? configured)
    {
        var domains = ParseAllowedDomains(configured);
        if (AllowsAnyDomain(domains))
            return null;
        if (domains.Count == 1)
            return domains[0];
        return null;
    }

    public static bool IsAllowedEmail(string? email, string? configuredDomains)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return false;

        var domains = ParseAllowedDomains(configuredDomains);
        if (domains.Count == 0)
            return false;

        if (AllowsAnyDomain(domains))
            return true;

        var at = email.LastIndexOf('@');
        if (at < 0 || at >= email.Length - 1)
            return false;

        var domain = email[(at + 1)..].Trim().ToLowerInvariant();
        return domains.Any(d => string.Equals(d, domain, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Accepts Google's claim shapes after normalization to a string (typically "true"/"false").
    /// </summary>
    public static bool IsEmailVerified(string? emailVerifiedClaim)
    {
        if (string.IsNullOrWhiteSpace(emailVerifiedClaim))
            return false;

        return string.Equals(emailVerifiedClaim.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAllowedGoogleIdentity(string? email, string? emailVerified, string? configuredDomains) =>
        IsAllowedEmail(email, configuredDomains) && IsEmailVerified(emailVerified);

    /// <summary>
    /// Resolves domain policy from environment, then optional DB/setup override.
    /// Environment wins when set (ops control); otherwise uses <paramref name="databaseOrSetupValue"/>.
    /// </summary>
    public static string ResolveConfiguredDomains(string? databaseOrSetupValue = null)
    {
        var env = Environment.GetEnvironmentVariable(AllowedEmailDomainsEnvKey);
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        if (!string.IsNullOrWhiteSpace(databaseOrSetupValue))
            return databaseOrSetupValue.Trim();

        return DefaultAllowedEmailDomains;
    }

    /// <summary>
    /// Coerces Google <c>email_verified</c> from bool or string into a claim string.
    /// Google's tokeninfo endpoint returns a JSON boolean; JWT claims are usually strings.
    /// </summary>
    public static string? CoerceEmailVerified(bool? value) =>
        value is null ? null : value.Value ? "true" : "false";

    public static string? CoerceEmailVerified(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
