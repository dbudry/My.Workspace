using System.Globalization;

namespace My.Shared.Rules;

/// <summary>
/// Resolves a human display name for an <c>ApplicationUser</c>-shaped record across
/// every Tyme surface that shows employee names. Never renders an email address
/// even when the underlying data has one stashed in <c>FirstName</c>.
///
/// Why this exists: at least one user has <c>FirstName="dbudry@example.com"</c>
/// in production from an old provisioning path, which surfaced "dbudry@example.com"
/// as the Employee column on Management. The fix is centralized here
/// so every endpoint that joins ApplicationUser → display name uses the same rule.
/// </summary>
public static class UserDisplayNameRules
{
    /// <summary>
    /// Builds the preferred display name from FirstName/LastName, falling back to
    /// the email's local part (title-cased) when both name fields are empty.
    /// If either name field accidentally contains an email, the @-suffix is stripped.
    /// Returns "Unknown" when nothing useful is available.
    /// </summary>
    public static string Resolve(string? firstName, string? lastName, string? email = null)
    {
        var first = StripEmailSuffix(firstName?.Trim());
        var last = StripEmailSuffix(lastName?.Trim());

        var combined = string.Join(" ", new[] { first, last }
            .Where(s => !string.IsNullOrEmpty(s)));

        if (!string.IsNullOrEmpty(combined))
            return combined;

        var fromEmail = LocalPartOf(email);
        return string.IsNullOrEmpty(fromEmail) ? "Unknown" : Prettify(fromEmail);
    }

    /// <summary>
    /// If <paramref name="value"/> looks like an email (contains '@'), returns the
    /// portion before the '@'. Otherwise returns the input unchanged.
    /// </summary>
    private static string? StripEmailSuffix(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var at = value.IndexOf('@');
        return at < 0 ? value : value.Substring(0, at);
    }

    private static string? LocalPartOf(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var at = email.IndexOf('@');
        return at < 0 ? email.Trim() : email.Substring(0, at).Trim();
    }

    /// <summary>
    /// Cheap prettifier for an email local part — splits on '.', title-cases each
    /// piece, rejoins with spaces. "dbudry" → "Dbudry"; "derek.budry" → "Derek Budry".
    /// </summary>
    private static string Prettify(string local)
    {
        if (string.IsNullOrEmpty(local)) return local;
        var pieces = local.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length == 0) return local;
        var ti = CultureInfo.InvariantCulture.TextInfo;
        return string.Join(" ", pieces.Select(p => ti.ToTitleCase(p.ToLowerInvariant())));
    }
}
