using System.Text;
using System.Text.RegularExpressions;

namespace My.Shared.Rules;

public static class ExpenseDriveNamingRules
{
    public const int SlugMaxLength = 40;
    private static readonly Regex LeadingDatePrefix = new(
        @"^(?<date>\d{4}[-_]\d{2}[-_]\d{2})(?<rest>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string UserFolderName(string lastName, string firstName)
    {
        var last = SanitizeSegment(lastName);
        var first = SanitizeSegment(firstName);
        if (last.Length == 0 && first.Length == 0) return "Unknown";
        if (last.Length == 0) return first;
        if (first.Length == 0) return last;
        return $"{last}_{first}";
    }

    /// <summary>
    /// Per-person folder under Expenses. Suffixes the user id so two people with
    /// the same name never share a folder, including concurrent first uploads.
    /// </summary>
    public static string UserFolderName(string lastName, string firstName, string userId)
    {
        var baseName = UserFolderName(lastName, firstName);
        var suffix = UserIdSuffix(userId);
        return string.IsNullOrEmpty(suffix) ? baseName : $"{baseName}_{suffix}";
    }

    public static string UserIdSuffix(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return "";
        var compact = userId.Replace("-", "", StringComparison.Ordinal);
        return compact.Length <= 6 ? compact : compact[..6];
    }

    public static string PeriodFolderName(int year, int month) =>
        $"{year:D4}_{Math.Clamp(month, 1, 12):D2}";

    /// <summary>
    /// Sibling of per-user receipt folders. Submitted statement PDFs live here so
    /// user-delete can remove <c>{Last}_{First}</c> without discarding the filed packet.
    /// </summary>
    public const string FiledFolderName = "Filed";

    public static string FiledPacketFileName(int year, int month, string? employeeName, string reportId)
    {
        var who = SanitizeSegment(employeeName);
        if (who.Length == 0) who = "Employee";
        var id = (reportId ?? "").Replace("-", "", StringComparison.Ordinal);
        if (id.Length > 8) id = id[..8];
        if (id.Length == 0) id = "report";
        return $"{PeriodFolderName(year, month)}_{who}_{id}_Expenses.pdf";
    }

    public static string ReceiptFileName(DateTime date, string? description, string extension)
    {
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        var slug = SanitizeSegment(description);
        if (slug.Length > SlugMaxLength)
            slug = slug[..SlugMaxLength].Trim('_');
        if (slug.Length == 0) slug = "Receipt";
        return $"{date:yyyy_MM_dd}_{slug}{ext}";
    }

    /// <summary>
    /// If the uploaded name starts with a calendar date, rewrite that prefix to
    /// <paramref name="date"/>. Other names are left alone.
    /// </summary>
    public static string ReplaceLeadingDate(string originalFileName, DateTime date)
    {
        var name = Path.GetFileName(originalFileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name))
            return originalFileName;

        var ext = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var match = LeadingDatePrefix.Match(stem);
        if (!match.Success)
            return name;

        var rest = match.Groups["rest"].Value.TrimStart('_', '-', ' ', '.');
        var prefix = date.ToString("yyyy_MM_dd");
        return string.IsNullOrEmpty(rest) ? prefix + ext : $"{prefix}_{rest}{ext}";
    }

    public static string SanitizeSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var sb = new StringBuilder(value.Length);
        var lastUnderscore = false;
        foreach (var c in value.Trim())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                lastUnderscore = false;
                continue;
            }

            if (lastUnderscore) continue;
            sb.Append('_');
            lastUnderscore = true;
        }

        return sb.ToString().Trim('_');
    }
}
