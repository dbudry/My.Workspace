namespace My.Shared.Rules;

public static class ExpenseReportRules
{
    public const string DefaultPlantOrLocation = "Home Office";
    public const int PurposeMaxLength = 2000;
    public const int PlantOrLocationMaxLength = 100;
    public const int ChargeToNoteMaxLength = 200;
    public const int AddressSnapshotMaxLength = 255;

    public static string? CombineHomeAddress(string? street, string? cityLine)
    {
        street = street?.Trim();
        cityLine = cityLine?.Trim();
        if (string.IsNullOrEmpty(street) && string.IsNullOrEmpty(cityLine))
            return null;
        if (string.IsNullOrEmpty(cityLine))
            return TruncateAddress(street!);
        if (string.IsNullOrEmpty(street))
            return TruncateAddress(cityLine!);
        return TruncateAddress(street + "\n" + cityLine);
    }

    public static (string Street, string CityLine) SplitHomeAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return ("", "");
        var parts = address.Replace("\r\n", "\n").Split('\n', 2, StringSplitOptions.None);
        if (parts.Length == 1)
            return (parts[0].Trim(), "");
        return (parts[0].Trim(), parts[1].Trim());
    }

    private static string TruncateAddress(string value) =>
        value.Length <= AddressSnapshotMaxLength ? value : value[..AddressSnapshotMaxLength];
    public const int EmployeeNameSnapshotMaxLength = 120;

    public const string MixedMonthLinesMessage =
        "All line dates on a report must be in the same month.";

    public const string LineDateMonthMessage =
        "Each line date must be in this report's month.";

    public const string CoverPeriodMonthMessage =
        "Cover start and cover end must be in this report's month.";

    public static (DateTime CoverStart, DateTime CoverEnd) DefaultCoverPeriod(int year, int month)
    {
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var end = start.AddMonths(1).AddDays(-1);
        return (start, end);
    }

    public static bool IsValidCoverPeriod(DateTime coverStart, DateTime coverEnd) =>
        coverStart.Date <= coverEnd.Date;

    /// <summary>True when a line's own date falls after the report's cover end - the submitter should be warned.</summary>
    public static bool IsLineDateAfterCoverEnd(DateTime lineDate, DateTime coverEnd) =>
        coverEnd != default && lineDate.Date > coverEnd.Date;

    /// <summary>
    /// One report is one calendar month. True when every date falls in the same month.
    /// False when <paramref name="dates"/> is empty or spans more than one month.
    /// </summary>
    public static bool TryPeriodFromLineDates(
        IEnumerable<DateTime> dates,
        out int year,
        out int month)
    {
        year = 0;
        month = 0;
        (int Year, int Month)? period = null;
        foreach (var raw in dates)
        {
            var date = ExpenseLineRules.CalendarDate(raw);
            var next = (date.Year, date.Month);
            if (period is null)
                period = next;
            else if (period != next)
                return false;
        }

        if (period is null)
            return false;

        (year, month) = period.Value;
        return true;
    }

    public static bool IsLineDateInMonth(DateTime date, int year, int month)
    {
        var day = ExpenseLineRules.CalendarDate(date);
        return day.Year == year && day.Month == month;
    }

    public static string EmployeeDisplayName(string firstName, string lastName)
    {
        var name = $"{firstName} {lastName}".Trim();
        if (name.Length <= EmployeeNameSnapshotMaxLength) return name;
        return name[..EmployeeNameSnapshotMaxLength];
    }

    public static string? MutateBlockedReason(string? status)
    {
        if (ExpenseStatusRules.IsReimbursed(status))
            return "This report is reimbursed and cannot be edited.";
        if (ExpenseStatusRules.IsSubmitted(status))
            return "This report is submitted and cannot be edited. A manager can unsubmit it.";
        return null;
    }

    public static string? SubmitBlockedReason(string? status, int lineCount)
    {
        if (!ExpenseStatusRules.IsDraft(status))
            return "Only a draft can be submitted.";
        if (lineCount <= 0)
            return "Add at least one line before submitting.";
        return null;
    }

    public static string? UnsubmitBlockedReason(string? status) =>
        ExpenseStatusRules.IsSubmitted(status)
            ? null
            : "Only a submitted report can be unsubmitted.";

    public static string? ReimburseBlockedReason(string? status) =>
        ExpenseStatusRules.IsSubmitted(status)
            ? null
            : "Only a submitted report can be marked reimbursed.";

    public static string? UndoReimburseBlockedReason(string? status) =>
        ExpenseStatusRules.IsReimbursed(status)
            ? null
            : "Only a reimbursed report can be undone.";

    public static bool CanView(string reportUserId, string callerUserId, bool isManager) =>
        isManager
        || string.Equals(reportUserId, callerUserId, StringComparison.Ordinal);

    /// <summary>
    /// Draft with lines for a month that has already ended — same idea as Tyme overdue
    /// (tracked time in a prior month that is not submitted).
    /// </summary>
    public static bool IsOverdueDraft(string? status, int year, int month, int lineCount, DateTime today)
    {
        if (lineCount <= 0) return false;
        if (!ExpenseStatusRules.IsDraft(status)) return false;
        if (year < today.Year) return true;
        return year == today.Year && month < today.Month;
    }
}
