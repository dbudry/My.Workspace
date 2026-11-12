using System.Net;
using System.Text;
using My.Shared.Dtos.TimeSubmission;

namespace My.Shared.Rules;

/// <summary>
/// Builds the "you may have forgotten to submit your time" reminder email. Pure so it is unit tested.
/// Styles are inline because email clients drop CSS classes and style blocks.
/// </summary>
public static class UnsubmittedReminderEmailBuilder
{
    public const string AppTitle = "Tyme";

    public static string Subject => "Please submit your time";

    /// <summary>The grid's months in which the employee's cell is Not submitted. Only these are named in the text.</summary>
    public static List<SubmissionGridMonthDto> UnsubmittedMonths(SubmissionGridDto grid)
    {
        var row = grid.Rows.FirstOrDefault();
        if (row == null) return [];
        return grid.Months.Where((_, i) => i < row.Cells.Count && UnsubmittedReminderRules.IsUnsubmitted(row.Cells[i])).ToList();
    }

    public static string IntroText(string employeeName, IReadOnlyList<SubmissionGridMonthDto> months)
    {
        var label = months.Count == 1 ? "month" : "months";
        return $"You may have forgotten to submit your time using {AppTitle} for the {label} "
            + $"{UnsubmittedReminderRules.MonthList(months)}. At your next availability, can you take a moment to address this?";
    }

    /// <param name="grid">One row (the employee), one column per month in the coverage whose status is shown.</param>
    /// <param name="appUrl">Link behind the app title: the Submit page, for example https://app.example.com/tyme/submit.</param>
    /// <param name="logoUrl">Absolute URL of the logo image, or null for none.</param>
    public static string Html(
        string employeeName,
        string managerName,
        SubmissionGridDto grid,
        string appUrl,
        string? logoUrl)
    {
        static string enc(string? s) => WebUtility.HtmlEncode(s) ?? "";
        var months = UnsubmittedMonths(grid);
        var label = months.Count == 1 ? "month" : "months";
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#222\">");
        sb.Append("<p style=\"margin:0 0 12px\">").Append(enc(employeeName)).Append(",</p>");
        sb.Append("<p style=\"margin:0 0 12px\">You may have forgotten to submit your time using <a href=\"")
          .Append(enc(appUrl)).Append("\">").Append(enc(AppTitle)).Append("</a> for the ").Append(label).Append(' ')
          .Append(enc(UnsubmittedReminderRules.MonthList(months))).Append(". At your next availability, can you take a moment to address this?</p>");
        sb.Append("<div style=\"margin:0 0 16px\">").Append(SubmissionGridEmailBuilder.TableHtml(grid, null)).Append("</div>");
        sb.Append("<p style=\"margin:0 0 12px\">Thanks,<br>").Append(enc(managerName)).Append("</p>");
        sb.Append("<table role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\"><tr>");
        if (!string.IsNullOrWhiteSpace(logoUrl))
        {
            sb.Append("<td style=\"padding-right:8px;vertical-align:middle\"><img src=\"").Append(enc(logoUrl))
              .Append("\" alt=\"Tyme\" width=\"32\" height=\"32\" style=\"display:block\"></td>");
        }
        sb.Append("<td style=\"vertical-align:middle;font-weight:bold\"><a href=\"").Append(enc(appUrl))
          .Append("\" style=\"color:#222;text-decoration:none\">").Append(enc(AppTitle)).Append("</a></td>");
        sb.Append("</tr></table>");
        sb.Append("</div>");
        return sb.ToString();
    }

    public static string PlainText(
        string employeeName,
        string managerName,
        SubmissionGridDto grid,
        string appUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine(employeeName + ",");
        sb.AppendLine();
        sb.AppendLine(IntroText(employeeName, UnsubmittedMonths(grid)));
        sb.AppendLine(appUrl);
        sb.AppendLine();
        sb.AppendLine("Thanks,");
        sb.AppendLine(managerName);
        sb.AppendLine(AppTitle);
        return sb.ToString();
    }
}
