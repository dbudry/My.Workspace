using System.Text;

namespace My.Shared.Rules;

/// <summary>
/// CSV of project names and calendar slugs for a printable/reference download.
/// </summary>
public static class ProjectSlugListingRules
{
    public const string CsvHeader = "Name,Slug,Organization";

    public readonly record struct Row(string Name, string? Slug, string? Organization);

    public static string ToCsv(IEnumerable<Row> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CsvHeader);
        foreach (var row in rows)
        {
            sb.Append(Quote(row.Name));
            sb.Append(',');
            sb.Append(Quote(row.Slug));
            sb.Append(',');
            sb.Append(Quote(row.Organization));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string Quote(string? value)
    {
        var v = value ?? "";
        return $"\"{v.Replace("\"", "\"\"")}\"";
    }
}
