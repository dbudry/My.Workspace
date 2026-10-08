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

    /// <summary>
    /// Used when the submitter has no timezone saved. The Functions host clock is UTC,
    /// which would file an evening submit on the last day of the month into the next month.
    /// </summary>
    public const string DefaultSubmitTimeZoneId = "America/New_York";

    public static (DateTime CoverStart, DateTime CoverEnd) DefaultCoverPeriod(int year, int month)
    {
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var end = start.AddMonths(1).AddDays(-1);
        return (start, end);
    }

    public static bool IsValidCoverPeriod(DateTime coverStart, DateTime coverEnd) =>
        coverStart != default
        && coverEnd != default
        && coverStart.Date <= coverEnd.Date;

    /// <summary>
    /// True when a line date falls outside the cover range. Save is still allowed;
    /// attaching a receipt warns so the cover range can be widened.
    /// </summary>
    public static bool IsLineDateOutsideCover(DateTime lineDate, DateTime coverStart, DateTime coverEnd)
    {
        if (coverStart == default || coverEnd == default) return false;
        var day = lineDate.Date;
        return day < coverStart.Date || day > coverEnd.Date;
    }

    public static string CoverRangeLabel(DateTime coverStart, DateTime coverEnd) =>
        $"{coverStart:MM/dd/yyyy} – {coverEnd:MM/dd/yyyy}";

    /// <summary>
    /// Filing month is the submit month. Drafts have none yet, even when Year/Month
    /// still hold a value from before this rule.
    /// </summary>
    public static string FilingMonthLabel(string? status, int year, int month)
    {
        if (!ExpenseStatusRules.IsLocked(status)) return "—";
        if (year is < 2000 or > 9999 || month is < 1 or > 12) return "—";
        return new DateTime(year, month, 1).ToString("MMMM yyyy");
    }

    public static string SubmitConfirmMessage(DateTime coverStart, DateTime coverEnd, DateTime today)
    {
        var filedAs = new DateTime(today.Year, today.Month, 1).ToString("MMMM yyyy");
        return $"Submit the report covering {CoverRangeLabel(coverStart, coverEnd)}? It will be filed as {filedAs}. This locks the report and files the statement PDF on Drive.";
    }

    /// <summary>
    /// Calendar month of <paramref name="submittedAtUtc"/> in the submitter's timezone.
    /// A blank timezone uses <see cref="DefaultSubmitTimeZoneId"/>.
    /// </summary>
    public static (int Year, int Month) SubmitMonth(DateTime submittedAtUtc, string? timeZoneId)
    {
        var utc = submittedAtUtc.Kind switch
        {
            DateTimeKind.Utc => submittedAtUtc,
            DateTimeKind.Local => submittedAtUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(submittedAtUtc, DateTimeKind.Utc)
        };
        var zoneId = string.IsNullOrWhiteSpace(timeZoneId) ? DefaultSubmitTimeZoneId : timeZoneId;
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, UserTimeZoneRules.Resolve(zoneId));
        return (local.Year, local.Month);
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
    /// Draft with lines whose cover period has already ended.
    /// </summary>
    public static bool IsOverdueDraft(string? status, DateTime coverEnd, int lineCount, DateTime today)
    {
        if (lineCount <= 0) return false;
        if (!ExpenseStatusRules.IsDraft(status)) return false;
        if (coverEnd == default) return false;
        return coverEnd.Date < today.Date;
    }
}
