namespace My.Shared.Rules;

public static class ExpenseReportRules
{
    public const string DefaultPlantOrLocation = "Home Office";
    public const int PurposeMaxLength = 2000;
    public const int PlantOrLocationMaxLength = 100;
    public const int ChargeToNoteMaxLength = 200;
    public const int AddressSnapshotMaxLength = 255;

    public static string? CombineHomeAddress(string? street, string? cityLine, string? country = null)
    {
        street = street?.Trim() ?? "";
        cityLine = cityLine?.Trim() ?? "";
        country = country?.Trim() ?? "";
        if (street.Length == 0 && cityLine.Length == 0 && country.Length == 0)
            return null;
        if (country.Length == 0)
        {
            if (cityLine.Length == 0)
                return TruncateAddress(street);
            if (street.Length == 0)
                return TruncateAddress(cityLine);
            return TruncateAddress(street + "\n" + cityLine);
        }

        return TruncateAddress(street + "\n" + cityLine + "\n" + country);
    }

    /// <summary>
    /// Street, city line, then country. Older addresses have no country line.
    /// </summary>
    public static (string Street, string CityLine, string Country) SplitHomeAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return ("", "", "");
        var parts = address.Replace("\r\n", "\n").Split('\n');
        var street = parts[0].Trim();
        if (parts.Length == 1)
            return (street, "", "");
        if (parts.Length == 2)
            return (street, parts[1].Trim(), "");
        return (street, parts[1].Trim(), string.Join(" ", parts.Skip(2)).Trim());
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
        $"{coverStart:MM/dd/yy} – {coverEnd:MM/dd/yy}";

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

    public readonly record struct SubmitConfirmCopy(string Question, string FiledAs, string Effect);

    public static SubmitConfirmCopy SubmitConfirm(DateTime coverStart, DateTime coverEnd, DateTime today)
    {
        var filedAs = new DateTime(today.Year, today.Month, 1).ToString("MMMM yyyy");
        return new SubmitConfirmCopy(
            $"Submit the report covering {CoverRangeLabel(coverStart, coverEnd)}?",
            $"It will be filed as {filedAs}.",
            "This locks the report and files the statement PDF on Drive.");
    }

    public static string SubmitConfirmMessage(DateTime coverStart, DateTime coverEnd, DateTime today)
    {
        var copy = SubmitConfirm(coverStart, coverEnd, today);
        return $"{copy.Question} {copy.FiledAs} {copy.Effect}";
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
            return "This report is paid and cannot be edited.";
        if (ExpenseStatusRules.IsSubmitted(status))
            return "This report is submitted and cannot be edited. A manager can unsubmit it.";
        return null;
    }

    public static string? SubmitBlockedReason(
        string? status,
        int lineCount,
        string? chargeToOrganizationId = null)
    {
        if (!ExpenseStatusRules.IsDraft(status))
            return "Only a draft can be submitted.";
        if (lineCount <= 0)
            return "Add at least one line before submitting.";
        if (string.IsNullOrWhiteSpace(chargeToOrganizationId))
            return "This expense needs a Charge to.";
        return null;
    }

    public static string? UnsubmitBlockedReason(string? status) =>
        ExpenseStatusRules.IsSubmitted(status)
            ? null
            : "Only a submitted report can be unsubmitted.";

    public static string? ReimburseBlockedReason(string? status) =>
        ExpenseStatusRules.IsSubmitted(status)
            ? null
            : "Only a submitted report can be marked paid.";

    public static string? UndoReimburseBlockedReason(string? status) =>
        ExpenseStatusRules.IsReimbursed(status)
            ? null
            : "Only a paid report can be undone.";

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
