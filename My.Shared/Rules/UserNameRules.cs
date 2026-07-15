using System.Globalization;

namespace My.Shared.Rules;

/// <summary>
/// Heuristics for filling in a sensible FirstName / LastName when Google's <c>name</c>
/// claim isn't available (typically when the SPA's Bearer token is an opaque
/// access_token rather than an id_token — Google's tokeninfo endpoint doesn't return
/// <c>name</c>). The real fix is to fetch <c>given_name</c>/<c>family_name</c> from
/// Google's <c>/userinfo</c> endpoint; this helper is the fallback used when that
/// fails AND on the AddUser admin form, where we have only the email to work with.
///
/// Output is always a best-effort guess. The provision endpoint's heal logic upgrades
/// the stored name on a subsequent sign-in when Google does return a real <c>name</c>.
/// </summary>
public static class UserNameRules
{
    /// <summary>
    /// Splits an email's local part on '.' or '_' and title-cases each segment.
    /// <list type="bullet">
    ///   <item><c>"john.doe@x.com"</c> → <c>("John", "Doe")</c></item>
    ///   <item><c>"jane_smith@x.com"</c> → <c>("Jane", "Smith")</c></item>
    ///   <item><c>"dbudry@x.com"</c> → <c>("Dbudry", "")</c></item>
    ///   <item><c>"abc.def.ghi@x.com"</c> → <c>("Abc", "Def Ghi")</c> (everything after the first delimiter becomes the last name)</item>
    ///   <item>Empty / null / no '@' → <c>("", "")</c> — caller decides on a placeholder.</item>
    /// </list>
    /// </summary>
    public static (string FirstName, string LastName) ParseFromEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return (string.Empty, string.Empty);

        var atIndex = email.IndexOf('@');
        var local = atIndex < 0 ? email : email[..atIndex];
        if (string.IsNullOrWhiteSpace(local)) return (string.Empty, string.Empty);

        // Split on both '.' and '_', drop empties so "a..b" doesn't yield a phantom middle part.
        var parts = local.Split(new[] { '.', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return (string.Empty, string.Empty);

        var first = TitleCase(parts[0]);
        // Everything after parts[0] joins into LastName so "first.middle.last" doesn't lose the tail.
        var last = parts.Length > 1
            ? string.Join(' ', parts.Skip(1).Select(TitleCase))
            : string.Empty;

        return (first, last);
    }

    /// <summary>True when the value looks like an email rather than a real name —
    /// used to decide whether to overwrite a stored value with one from the heuristic
    /// or from a fresh Google claim.</summary>
    public static bool LooksLikeEmail(string? value) =>
        !string.IsNullOrEmpty(value) && value.Contains('@');

    private static string TitleCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        // Use invariant culture so a Turkish locale doesn't turn 'i' into 'İ'.
        return char.ToUpper(s[0], CultureInfo.InvariantCulture)
            + s[1..].ToLower(CultureInfo.InvariantCulture);
    }
}
