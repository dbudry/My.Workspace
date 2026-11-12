using System.Net;
using System.Text;
using My.Shared.Dtos.TimeSubmission;

namespace My.Shared.Rules;

/// <summary>
/// Builds the Tyme submit notification email (subject, HTML, plain text). Pure so it is unit tested.
/// Styles are inline because email clients drop CSS classes and style blocks.
/// </summary>
public static class SubmissionGridEmailBuilder
{
    public const string SubmitterRowBackground = "#fff3b0";

    public static string Subject(string submitterName) => $"{submitterName} submitted time";

    public static string IntroLine(string submitterName, int year, int month) =>
        $"{submitterName} has put their time in for {SubmissionGridRules.MonthLabel(year, month)}.";

    /// <summary>Cell background. Same colors as the Grid in the app: green, orange, blue, grey.</summary>
    public static string CellColor(SubmissionCellStatus status) => status switch
    {
        SubmissionCellStatus.Submitted => "#5fb374",
        SubmissionCellStatus.Unsubmitted => "#f0b060",
        SubmissionCellStatus.Open => "#5b8fe6",
        _ => "#e3e5e8"
    };

    /// <summary>Cell text color, chosen to stay readable on <see cref="CellColor"/>.</summary>
    public static string CellTextColor(SubmissionCellStatus status) => status switch
    {
        SubmissionCellStatus.Submitted => "#0b2a0d",
        SubmissionCellStatus.Unsubmitted => "#3a2400",
        SubmissionCellStatus.Open => "#ffffff",
        _ => "#5f6670"
    };

    /// <summary>
    /// The employees × months table with colored cells. The row of <paramref name="highlightUserId"/>
    /// (when not null) gets the highlight. Shared by the submit email and the reminder email.
    /// </summary>
    public static string TableHtml(SubmissionGridDto grid, string? highlightUserId)
    {
        static string enc(string? s) => WebUtility.HtmlEncode(s) ?? "";
        var sb = new StringBuilder();
        sb.Append("<table role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:collapse\">");
        sb.Append("<tr><th style=\"text-align:left;padding:6px 12px;border:1px solid #cfcfcf;background:#f5f5f5\">Employee</th>");
        foreach (var m in grid.Months)
        {
            sb.Append("<th style=\"padding:6px 12px;border:1px solid #cfcfcf;background:#f5f5f5\">")
              .Append(enc(SubmissionGridRules.MonthLabel(m.Year, m.Month))).Append("</th>");
        }
        sb.Append("</tr>");

        foreach (var row in grid.Rows)
        {
            var isHighlighted = highlightUserId != null
                && string.Equals(row.UserId, highlightUserId, StringComparison.Ordinal);
            var nameStyle = "text-align:left;padding:6px 12px;border:1px solid #cfcfcf;"
                + (isHighlighted ? $"background:{SubmitterRowBackground};font-weight:bold;" : "");
            sb.Append("<tr><td style=\"").Append(nameStyle).Append("\">").Append(enc(row.UserName)).Append("</td>");
            foreach (var cell in row.Cells)
            {
                var border = isHighlighted ? "2px solid #8a7a00" : "1px solid #cfcfcf";
                sb.Append("<td style=\"text-align:center;padding:6px 12px;font-size:12px;border:").Append(border)
                  .Append(";background:").Append(CellColor(cell.Status))
                  .Append(";color:").Append(CellTextColor(cell.Status)).Append("\">")
                  .Append(enc(SubmissionGridRules.StatusLabel(cell.Status))).Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</table>");
        return sb.ToString();
    }

    public static string Html(
        string submitterName,
        string submitterUserId,
        int year,
        int month,
        SubmissionGridDto grid,
        string? submitPageUrl)
    {
        static string enc(string? s) => WebUtility.HtmlEncode(s) ?? "";
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#222\">");
        sb.Append("<p style=\"margin:0 0 12px\">").Append(enc(IntroLine(submitterName, year, month))).Append("</p>");

        sb.Append(TableHtml(grid, submitterUserId));

        sb.Append("<p style=\"margin:12px 0 0;color:#666;font-size:12px\">")
          .Append("Orange: month is over and not submitted. Green: submitted. Blue: in progress (current or future month). Grey: no time tracked.")
          .Append("</p>");
        if (!string.IsNullOrWhiteSpace(submitPageUrl))
        {
            sb.Append("<p style=\"margin:8px 0 0\"><a href=\"").Append(enc(submitPageUrl))
              .Append("\">Open the Submit page</a></p>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    public static string PlainText(
        string submitterName,
        int year,
        int month,
        SubmissionGridDto grid,
        string? submitPageUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine(IntroLine(submitterName, year, month));
        sb.AppendLine();
        foreach (var row in grid.Rows)
        {
            sb.Append(row.UserName).Append(": ");
            sb.AppendLine(string.Join(", ", row.Cells.Select((c, i) =>
                $"{SubmissionGridRules.MonthLabel(grid.Months[i].Year, grid.Months[i].Month)} {SubmissionGridRules.StatusLabel(c.Status)}")));
        }
        if (!string.IsNullOrWhiteSpace(submitPageUrl))
        {
            sb.AppendLine();
            sb.AppendLine(submitPageUrl);
        }
        return sb.ToString();
    }
}
