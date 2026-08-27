namespace My.Shared.Rules;

/// <summary>
/// Pure helpers for the Tasks "week entry" grid: Monday-start week bounds, cell
/// create/update/delete decisions, and duration totals. The Blazor panel owns HTTP
/// and debounce; this class stays free of UI/IO so it is unit-testable.
/// </summary>
public static class WeekEntryGridRules
{
    public const int DaysInWeek = 7;
    public const int BusinessDaysInWeek = 5;
    public const int MaxTaskNameLength = TaskDetailsRules.MaxLength;
    public const int MinTaskNameLength = TaskDetailsRules.MinLength;

    public enum CellMutationKind
    {
        None,
        Create,
        Update,
        Delete
    }

    public enum DayBindKind
    {
        /// <summary>No editable manual entry for this day (cell empty or create-ready).</summary>
        Empty,
        /// <summary>Exactly one editable manual timed entry — bind to it.</summary>
        Single,
        /// <summary>Multiple manual timed entries — do not auto-edit; show sum read-only.</summary>
        Multiple,
        /// <summary>All-day entry covers this day — show workday hours read-only.</summary>
        AllDay
    }

    public readonly record struct CellMutation(
        CellMutationKind Kind,
        string? TaskId,
        TimeSpan Duration);

    public readonly record struct DayBinding(
        DayBindKind Kind,
        string? TaskId,
        string? TaskName,
        DateTime? StartDate,
        TimeSpan EditableDuration,
        TimeSpan TotalManualDuration);

    public readonly record struct WeekTotals(TimeSpan ProjectTotal, TimeSpan GrandTotal);

    /// <summary>
    /// Monday 00:00 of the ISO-style work week containing <paramref name="date"/>
    /// (Sunday maps to the previous Monday).
    /// </summary>
    public static DateTime GetWeekStartMonday(DateTime date)
    {
        var d = date.Date;
        // DayOfWeek: Sunday=0 … Saturday=6. Convert so Monday=0 … Sunday=6.
        var offset = ((int)d.DayOfWeek + 6) % 7;
        return d.AddDays(-offset);
    }

    /// <summary>Seven dates Mon–Sun starting at <paramref name="weekStartMonday"/>.</summary>
    public static IReadOnlyList<DateTime> GetWeekDays(DateTime weekStartMonday)
    {
        var start = weekStartMonday.Date;
        var days = new DateTime[DaysInWeek];
        for (var i = 0; i < DaysInWeek; i++)
            days[i] = start.AddDays(i);
        return days;
    }

    /// <summary>
    /// Visible day columns for the week grid.
    /// Business week = Mon–Fri; full week = Mon–Sun.
    /// </summary>
    public static IReadOnlyList<DateTime> GetVisibleWeekDays(DateTime weekStartMonday, bool businessWeekOnly)
    {
        var all = GetWeekDays(weekStartMonday);
        if (!businessWeekOnly)
            return all;
        return all.Take(BusinessDaysInWeek).ToList();
    }

    /// <summary>Inclusive range end (Sunday) for a Monday week start.</summary>
    public static DateTime GetWeekEndSunday(DateTime weekStartMonday) =>
        weekStartMonday.Date.AddDays(DaysInWeek - 1);

    /// <summary>Inclusive range end for visible columns (Friday or Sunday).</summary>
    public static DateTime GetVisibleWeekEnd(DateTime weekStartMonday, bool businessWeekOnly) =>
        weekStartMonday.Date.AddDays(businessWeekOnly ? BusinessDaysInWeek - 1 : DaysInWeek - 1);

    /// <summary>Sum durations whose local start date equals <paramref name="day"/>.</summary>
    public static TimeSpan SumForDay(IEnumerable<WeekEntryTaskSlice> tasks, DateTime day)
    {
        var d = day.Date;
        var total = TimeSpan.Zero;
        foreach (var t in tasks)
        {
            if (t.StartDate.Date == d)
                total += t.Duration;
        }
        return NormalizeDuration(total);
    }

    /// <summary>Sum durations with start date in [from, to] inclusive.</summary>
    public static TimeSpan SumForDateRange(IEnumerable<WeekEntryTaskSlice> tasks, DateTime from, DateTime to)
    {
        var f = from.Date;
        var end = to.Date;
        var total = TimeSpan.Zero;
        foreach (var t in tasks)
        {
            var sd = t.StartDate.Date;
            if (sd >= f && sd <= end)
                total += t.Duration;
        }
        return NormalizeDuration(total);
    }

    /// <summary>
    /// Decide whether a cell change should create, update, delete, or no-op.
    /// Zero duration with no task → none; zero with task → delete; positive without task → create;
    /// positive with task and same duration → none; positive with task and different → update.
    /// </summary>
    public static CellMutation DecideMutation(string? boundTaskId, TimeSpan previousDuration, TimeSpan newDuration)
    {
        var normalized = NormalizeDuration(newDuration);
        var prev = NormalizeDuration(previousDuration);
        var hasTask = !string.IsNullOrEmpty(boundTaskId);

        if (normalized <= TimeSpan.Zero)
        {
            if (!hasTask)
                return new CellMutation(CellMutationKind.None, null, TimeSpan.Zero);
            return new CellMutation(CellMutationKind.Delete, boundTaskId, TimeSpan.Zero);
        }

        if (!hasTask)
            return new CellMutation(CellMutationKind.Create, null, normalized);

        if (normalized == prev)
            return new CellMutation(CellMutationKind.None, boundTaskId, normalized);

        return new CellMutation(CellMutationKind.Update, boundTaskId, normalized);
    }

    /// <summary>Clamp hours/minutes into a non-negative TimeSpan (minutes 0–59).</summary>
    public static TimeSpan DurationFromParts(int hours, int minutes)
    {
        if (hours < 0) hours = 0;
        if (minutes < 0) minutes = 0;
        if (minutes > 59) minutes = 59;
        if (hours > 99) hours = 99;
        return new TimeSpan(hours, minutes, 0);
    }

    public static TimeSpan NormalizeDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) return TimeSpan.Zero;
        // Drop seconds/subseconds for timesheet cells.
        return new TimeSpan((int)duration.TotalHours, duration.Minutes, 0);
    }

    /// <summary>
    /// Maximum duration for a single day cell. Capped below 24h so it fits SQL <c>time</c>
    /// (see <see cref="DurationStorageRules.MaxStoredDuration"/>).
    /// </summary>
    public static readonly TimeSpan MaxDayDuration = DurationStorageRules.MaxStoredDuration;

    /// <summary>Mud PatternMask shape for day cells: two hour digits, colon, two minute digits.</summary>
    public const string DayDurationMaskPattern = "00:00";

    /// <summary>
    /// Formats a duration for a single H:MM input (e.g. 2:30, 0:45). Empty when zero.
    /// Prefer <see cref="FormatDayDurationInput"/> for day-grid cells (always HH:MM).
    /// </summary>
    public static string FormatDurationInput(TimeSpan duration)
    {
        var d = NormalizeDuration(duration);
        if (d <= TimeSpan.Zero) return string.Empty;
        return $"{(int)d.TotalHours}:{d.Minutes:D2}";
    }

    /// <summary>
    /// Formats a day-cell duration as <c>HH:MM</c> (e.g. 08:00, 23:59). Empty when zero.
    /// </summary>
    public static string FormatDayDurationInput(TimeSpan duration)
    {
        var d = NormalizeDuration(duration);
        if (d <= TimeSpan.Zero) return string.Empty;
        if (d > MaxDayDuration) d = MaxDayDuration;
        return $"{(int)d.TotalHours:D2}:{d.Minutes:D2}";
    }

    /// <summary>
    /// Soft filter while the user is typing or selecting text: digits, at most one colon
    /// (including a leading <c>:</c> for minutes-only), and optional <c>h</c>/<c>m</c>
    /// (<c>15m</c>, <c>2h</c>, <c>2h30m</c>). Does <b>not</b> re-pad or re-order the string
    /// (that fights caret/selection). Use <see cref="NormalizeDayDurationText"/> /
    /// <see cref="TryCommitDayDurationText"/> only on blur/save.
    /// </summary>
    public static string FilterDurationInputChars(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        Span<char> buf = stackalloc char[7]; // 23h59m or HH:MM
        var n = 0;
        var colon = false;
        var hasH = false;
        var hasM = false;
        var digitsBefore = 0;
        var digitsAfter = 0;

        foreach (var c in raw)
        {
            if (n == 7) break;

            if (c is >= '0' and <= '9')
            {
                if (hasM) continue;
                if (!colon && !hasH)
                {
                    if (digitsBefore >= 4) continue;
                    digitsBefore++;
                }
                else
                {
                    if (digitsAfter >= 2) continue;
                    digitsAfter++;
                }

                buf[n++] = c;
            }
            else if (c == ':' && !colon && !hasH && !hasM)
            {
                colon = true;
                buf[n++] = c;
            }
            else if ((c is 'h' or 'H') && !hasH && !hasM && !colon)
            {
                hasH = true;
                digitsAfter = 0;
                buf[n++] = 'h';
            }
            else if ((c is 'm' or 'M') && !hasM && !colon)
            {
                hasM = true;
                buf[n++] = 'm';
            }
        }

        return n == 0 ? string.Empty : new string(buf[..n]);
    }

    /// <summary>
    /// Keeps only digits and a single colon; formats as progressive <c>H</c> / <c>HH</c> /
    /// <c>HH:M</c> / <c>HH:MM</c>. Complete values are clamped (minutes ≤ 59, total ≤ 24:00).
    /// Letters and other characters are dropped. Prefer for blur/save, not mid-keystroke.
    /// </summary>
    public static string NormalizeDayDurationText(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        if (ContainsHourOrMinuteSuffix(raw)
            && TryParseHourMinuteSuffix(raw, requireComplete: false, out var fromSuffix))
            return FormatDayDurationInput(fromSuffix);

        var colon = raw.IndexOf(':');
        if (colon >= 0)
        {
            var hDigits = DigitsOnly(raw.AsSpan(0, colon), max: 2);
            var mDigits = DigitsOnly(raw.AsSpan(colon + 1), max: 2);
            if (hDigits.Length == 0 && mDigits.Length == 0)
                return string.Empty;

            // Minutes not started — return hour digits only (mask re-inserts colon as needed).
            if (mDigits.Length == 0)
                return hDigits;

            var h = hDigits.Length == 0 ? 0 : int.Parse(hDigits);
            if (mDigits.Length == 1)
                return $"{h:D2}:{mDigits}";

            var m = int.Parse(mDigits);
            return FormatClampedDayHhmm(h, m);
        }

        // No colon: progressive digit entry (1–4 digits → H / HH / HH:M / HH:MM).
        var digits = DigitsOnly(raw.AsSpan(), max: 4);
        if (digits.Length == 0) return string.Empty;
        if (digits.Length <= 2) return digits;
        if (digits.Length == 3)
            return string.Concat(digits.AsSpan(0, 2), ":", digits.AsSpan(2, 1));

        var hours = int.Parse(digits.AsSpan(0, 2));
        var mins = int.Parse(digits.AsSpan(2, 2));
        return FormatClampedDayHhmm(hours, mins);
    }

    private static string DigitsOnly(ReadOnlySpan<char> s, int max)
    {
        Span<char> buf = stackalloc char[max];
        var n = 0;
        foreach (var c in s)
        {
            if (c is < '0' or > '9') continue;
            buf[n++] = c;
            if (n == max) break;
        }

        return n == 0 ? string.Empty : new string(buf[..n]);
    }

    private static string FormatClampedDayHhmm(int hours, int minutes)
    {
        if (minutes > 59) minutes = 59;
        if (minutes < 0) minutes = 0;
        if (hours < 0) hours = 0;
        // Cap at MaxStoredDuration (23:59) so normalized text never promises a non-storable value.
        if (hours > DurationStorageRules.MaxHoursComponent
            || (hours == DurationStorageRules.MaxHoursComponent && minutes > DurationStorageRules.MaxStoredDuration.Minutes))
        {
            hours = DurationStorageRules.MaxHoursComponent;
            minutes = DurationStorageRules.MaxStoredDuration.Minutes;
        }

        return $"{hours:D2}:{minutes:D2}";
    }

    /// <summary>
    /// Parses a day-cell duration while typing. Accepts empty (zero) and complete
    /// <c>:MM</c> / <c>H:MM</c> / <c>HH:MM</c> / <c>15m</c> / <c>2h</c> / <c>2h30m</c>
    /// up to 23:59. Partials (e.g. <c>8</c>, <c>08:3</c>, <c>:1</c>) return false
    /// so autosave can wait — use <see cref="TryCommitDayDurationText"/> on blur/commit.
    /// </summary>
    public static bool TryParseDayDurationText(string? raw, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        var s = raw.Trim();
        var colon = s.IndexOf(':');
        if (colon < 0)
            return TryParseHourMinuteSuffix(s, requireComplete: true, out duration);
        if (colon != s.LastIndexOf(':'))
            return false;

        var hourPart = s[..colon];
        var minPart = s[(colon + 1)..];
        if (hourPart.Length > 2 || minPart.Length != 2)
            return false;

        var h = 0;
        if (hourPart.Length > 0 && !int.TryParse(hourPart, out h))
            return false;
        if (!int.TryParse(minPart, out var m))
            return false;
        return TryBuildDayDuration(h, m, out duration);
    }

    /// <summary>
    /// Whether the day-grid should debounce-save <b>while typing</b>.
    /// Empty = user cleared the cell. Complete <c>H:MM</c>/<c>HH:MM</c> (two minute
    /// digits) is finished. Partials like <c>00</c>, <c>00:</c>, <c>00:3</c>, or
    /// bare <c>4</c> must wait for blur — otherwise save reformats <c>00:</c> to
    /// empty and <c>00:3</c> to <c>00:03</c>, so 00:30 cannot be typed.
    /// </summary>
    public static bool ShouldAutosaveDayDurationText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        return TryParseDayDurationText(raw, out _);
    }

    /// <summary>
    /// Parses duration on blur/save. Accepts the same as <see cref="TryParseDayDurationText"/>,
    /// plus common partials people leave in the field:
    /// <list type="bullet">
    ///   <item><c>:15</c> / <c>:5</c> / <c>15m</c> → 15 minutes / 5 minutes</item>
    ///   <item><c>4</c> / <c>04</c> → 4 hours</item>
    ///   <item><c>4:</c> → 4 hours</item>
    ///   <item><c>4:3</c> → 4 hours 3 minutes</item>
    /// </list>
    /// Empty → zero (success). Values over 23:59 fail (callers should normalize first).
    /// </summary>
    public static bool TryCommitDayDurationText(string? raw, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        // Complete HH:MM / H:MM / :MM / 15m / 2h / 2h30m
        if (TryParseDayDurationText(raw, out duration))
            return true;

        var s = raw.Trim();
        if (TryParseHourMinuteSuffix(s, requireComplete: false, out duration))
            return true;

        var colon = s.IndexOf(':');

        // Bare hours: "4", "04", "12"
        if (colon < 0)
        {
            if (!int.TryParse(s, out var bareH))
                return false;
            return TryBuildDayDuration(bareH, 0, out duration);
        }

        if (colon != s.LastIndexOf(':'))
            return false;

        // ":15" / ":5" → minutes only (leading colon). Lone ":" is still typing.
        if (colon == 0)
        {
            var leadingMins = s[1..];
            if (leadingMins.Length is < 1 or > 2)
                return false;
            if (!int.TryParse(leadingMins, out var leadingM))
                return false;
            return TryBuildDayDuration(0, leadingM, out duration);
        }

        var hourPart = s[..colon];
        var minPart = s[(colon + 1)..];
        if (hourPart.Length is < 1 or > 2)
            return false;
        if (!int.TryParse(hourPart, out var h))
            return false;

        // "4:" or "04:" → whole hours
        if (minPart.Length == 0)
            return TryBuildDayDuration(h, 0, out duration);

        // "4:3" or "04:30" — one or two minute digits
        if (minPart.Length is < 1 or > 2)
            return false;
        if (!int.TryParse(minPart, out var m))
            return false;
        return TryBuildDayDuration(h, m, out duration);
    }

    private static bool ContainsHourOrMinuteSuffix(string raw)
    {
        foreach (var c in raw)
        {
            if (c is 'h' or 'H' or 'm' or 'M')
                return true;
        }

        return false;
    }

    /// <summary>
    /// <c>15m</c>, <c>2h</c>, <c>2h30m</c>. When <paramref name="requireComplete"/> is false,
    /// <c>2h30</c> (no trailing m) is also accepted on blur.
    /// </summary>
    private static bool TryParseHourMinuteSuffix(string raw, bool requireComplete, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var s = raw.Trim().ToLowerInvariant().Replace(" ", "");
        if (s.Length == 0) return false;

        if (s.EndsWith('m') && !s.Contains('h'))
        {
            var num = s[..^1];
            if (num.Length is < 1 or > 4) return false;
            if (!int.TryParse(num, out var mins)) return false;
            return TryBuildFromTotalMinutes(mins, out duration);
        }

        if (s.EndsWith('h'))
        {
            var num = s[..^1];
            if (num.Length is < 1 or > 2) return false;
            if (!int.TryParse(num, out var hoursOnly)) return false;
            return TryBuildDayDuration(hoursOnly, 0, out duration);
        }

        var hIdx = s.IndexOf('h');
        if (hIdx <= 0) return false;

        var hourPart = s[..hIdx];
        var rest = s[(hIdx + 1)..];
        if (requireComplete && !rest.EndsWith('m'))
            return false;
        if (rest.EndsWith('m'))
            rest = rest[..^1];
        if (hourPart.Length is < 1 or > 2 || rest.Length is < 1 or > 2)
            return false;
        if (!int.TryParse(hourPart, out var hh) || !int.TryParse(rest, out var mm))
            return false;
        return TryBuildDayDuration(hh, mm, out duration);
    }

    private static bool TryBuildFromTotalMinutes(int totalMinutes, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (totalMinutes < 0) return false;
        var ts = TimeSpan.FromMinutes(totalMinutes);
        if (!DurationStorageRules.IsWithinStorageLimit(ts))
            return false;
        duration = NormalizeDuration(ts);
        return true;
    }

    private static bool TryBuildDayDuration(int hours, int minutes, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (hours < 0 || minutes < 0 || minutes > 59)
            return false;
        // Must fit SQL time storage (max 23:59). 24:00 used to parse then crash on insert.
        if (hours > DurationStorageRules.MaxHoursComponent)
            return false;
        duration = DurationFromParts(hours, minutes);
        return DurationStorageRules.IsWithinStorageLimit(duration);
    }

    /// <summary>
    /// Parses worked duration from one field. Accepts:
    /// <c>2:30</c>, <c>2.5</c> (hours), <c>2h30m</c>, <c>2h</c>, <c>150m</c>, bare <c>2</c> (hours).
    /// Empty/whitespace → zero (success). Invalid → false.
    /// Prefer <see cref="TryParseDayDurationText"/> for day-grid cells (HH:MM, max 24:00).
    /// </summary>
    public static bool TryParseDurationText(string? raw, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        var s = raw.Trim().ToLowerInvariant().Replace(" ", "");

        // 2:30, 02:30, or :15 (minutes only)
        if (s.Contains(':'))
        {
            var parts = s.Split(':');
            if (parts.Length != 2) return false;
            var h = 0;
            if (!string.IsNullOrEmpty(parts[0]) && !int.TryParse(parts[0], out h))
                return false;
            if (!int.TryParse(parts[1], out var m))
                return false;
            if (h < 0 || h > 99 || m < 0 || m > 59) return false;
            duration = DurationFromParts(h, m);
            return true;
        }

        // 2h30m / 2h30 / 2h / 30m
        if (s.Contains('h') || s.EndsWith('m'))
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                s, @"^(?:(\d+)h)?(?:(\d+)m)?$");
            if (!match.Success || match.Length == 0 || s.Length == 0)
                return false;
            if (!match.Groups[1].Success && !match.Groups[2].Success)
                return false;

            var hh = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
            var mm = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;

            if (!match.Groups[1].Success && match.Groups[2].Success)
            {
                // pure minutes e.g. 150m
                if (mm < 0) return false;
                duration = NormalizeDuration(TimeSpan.FromMinutes(mm));
                return duration.TotalHours <= 99;
            }

            if (mm > 59 || hh < 0 || hh > 99) return false;
            duration = DurationFromParts(hh, mm);
            return true;
        }

        // 2.5 hours
        if (s.Contains('.') || s.Contains(','))
        {
            var inv = s.Replace(',', '.');
            if (!double.TryParse(inv, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var hours))
                return false;
            if (hours < 0 || hours > 99) return false;
            var whole = (int)Math.Floor(hours);
            var fracMin = (int)Math.Round((hours - whole) * 60);
            if (fracMin == 60) { whole++; fracMin = 0; }
            duration = DurationFromParts(whole, fracMin);
            return true;
        }

        // bare hours
        if (int.TryParse(s, out var bareH))
        {
            if (bareH < 0 || bareH > 99) return false;
            duration = DurationFromParts(bareH, 0);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parses clock time of day. Accepts 9, 9p, 9:30 AM, 14:30, etc.
    /// Empty → false (start time required when saving duration).
    /// </summary>
    public static bool TryParseClockTime(string? raw, out TimeSpan timeOfDay)
    {
        timeOfDay = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var s = raw.Trim().ToUpperInvariant().Replace(" ", "");

        // Prefer multi-char custom formats only — single-letter formats like "H"/"h"
        // throw FormatException under some .NET runtimes when used with TryParseExact.
        string[] formats =
        {
            "h:mmtt", "h:mtt", "htt",
            "hh:mmtt", "hh:mtt", "hhtt",
            "H:mm", "H:m",
            "HH:mm", "HH:m",
            "h:mm", "h:m",
            "hh:mm", "hh:m"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(s, format, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dtExact))
            {
                timeOfDay = dtExact.TimeOfDay;
                return true;
            }
        }

        // Bare hour: "9", "14", "9P", "9PM"
        var bare = System.Text.RegularExpressions.Regex.Match(s, @"^(\d{1,2})(AM|PM|A|P)?$");
        if (bare.Success && int.TryParse(bare.Groups[1].Value, out var hour))
        {
            var ampm = bare.Groups[2].Success ? bare.Groups[2].Value : "";
            if (ampm is "P" or "PM")
            {
                if (hour is >= 1 and < 12) hour += 12;
            }
            else if (ampm is "A" or "AM")
            {
                if (hour == 12) hour = 0;
            }

            if (hour is >= 0 and <= 23)
            {
                timeOfDay = TimeSpan.FromHours(hour);
                return true;
            }
        }

        if (DateTime.TryParse(raw, System.Globalization.CultureInfo.CurrentCulture,
                System.Globalization.DateTimeStyles.None, out var dt2))
        {
            timeOfDay = dt2.TimeOfDay;
            return true;
        }

        return false;
    }

    /// <summary>Formats time-of-day for the start input (12h or 24h).</summary>
    public static string FormatClockTime(TimeSpan timeOfDay, bool use24Hour)
    {
        var dt = DateTime.Today.Add(timeOfDay);
        return use24Hour
            ? dt.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            : dt.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Strip leading/trailing whitespace for storage and comparison.
    /// Always use this before persisting a task name.
    /// </summary>
    public static string SanitizeTaskDetails(string? name) => (name ?? string.Empty).Trim();

    /// <summary>
    /// New Week → Day (and similar) draft rows: duration is allowed once a project
    /// is chosen. Details are optional.
    /// </summary>
    public static bool CanEnterDraftDayDuration(bool hasProject) => hasProject;

    /// <summary>
    /// Case-insensitive substring match for Tasks search across week/project rows.
    /// Empty query matches everything.
    /// </summary>
    public static bool MatchesEntrySearch(string? query, params string?[] fields)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        var needle = query.Trim();
        foreach (var field in fields)
        {
            if (!string.IsNullOrEmpty(field)
                && field.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Validate task Details for create/update. Empty is allowed.
    /// Length rules apply to the trimmed name.
    /// </summary>
    public static string? ValidateTaskDetails(string? name)
    {
        var trimmed = SanitizeTaskDetails(name);
        if (trimmed.Length > MaxTaskNameLength)
            return TaskDetailsRules.MaxLengthMessage;
        return null;
    }

    /// <summary>
    /// Truncate a display label to fit the create name max length (e.g. project name default).
    /// </summary>
    public static string TruncateTaskName(string? name, string fallback = "Time")
    {
        var s = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        if (s.Length <= MaxTaskNameLength)
            return s;
        return s[..MaxTaskNameLength].TrimEnd();
    }

    /// <summary>Trim for display/create; empty becomes empty string.</summary>
    public static string NormalizeTaskDetailsKey(string? name) => (name ?? string.Empty).Trim();

    /// <summary>Case-insensitive task-name equality after trim.</summary>
    public static bool TaskNamesEqual(string? a, string? b) =>
        string.Equals(NormalizeTaskDetailsKey(a), NormalizeTaskDetailsKey(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Distinct manual timed task names for a project inside [from,to] inclusive,
    /// ordered alphabetically (case-insensitive). Used to rebuild Project-view rows
    /// from existing week data plus any blank draft row the UI keeps.
    /// </summary>
    public static IReadOnlyList<string> DistinctManualTaskDetails(
        IEnumerable<WeekEntryTaskSlice> weekTasks,
        string? projectId,
        DateTime from,
        DateTime to)
    {
        if (string.IsNullOrEmpty(projectId))
            return Array.Empty<string>();

        var f = from.Date;
        var end = to.Date;
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in weekTasks)
        {
            if (!string.IsNullOrEmpty(t.StopwatchItemId))
                continue;
            if (!string.Equals(t.ProjectId, projectId, StringComparison.Ordinal))
                continue;
            if (!OverlapsDayRange(t, f, end))
                continue;
            // Details is optional — an empty name is still a real row (see the
            // matching note in WeekDayAcrossGrid.RebuildRows). All-day entries
            // belong here too so Week → Day can show their 8h and count them.
            names.Add(NormalizeTaskDetailsKey(t.Details));
        }

        return names.ToList();
    }

    /// <summary>
    /// Bind one day for the selected project from week tasks.
    /// Timed manuals are editable. All-day entries bind as <see cref="DayBindKind.AllDay"/>
    /// (read-only workday hours). Stopwatch sessions stay out of the cell.
    /// Prefer <see cref="BindDayForTaskDetails"/> when the UI is multi-row.
    /// </summary>
    public static DayBinding BindDay(
        IEnumerable<WeekEntryTaskSlice> weekTasks,
        string? projectId,
        DateTime day) =>
        BindDayForTaskDetails(weekTasks, projectId, taskName: null, day, matchAnyDetails: true);

    /// <summary>
    /// Bind one day for a specific task Details under a project. Empty Details is a real key
    /// (optional name). When multiple manuals share the same project+details+day, returns
    /// <see cref="DayBindKind.Multiple"/> (read-only sum).
    /// </summary>
    public static DayBinding BindDayForTaskDetails(
        IEnumerable<WeekEntryTaskSlice> weekTasks,
        string? projectId,
        string? taskName,
        DateTime day,
        bool matchAnyDetails = false)
    {
        if (string.IsNullOrEmpty(projectId))
            return new DayBinding(DayBindKind.Empty, null, null, null, TimeSpan.Zero, TimeSpan.Zero);

        // Empty Details is a real key (optional) — do not treat blank as "no row".

        var dayDate = day.Date;
        var workdayHours = AllDayEntryRules.DefaultWorkdayHours;
        var manuals = weekTasks
            .Where(t =>
                !t.IsAllDay
                && string.IsNullOrEmpty(t.StopwatchItemId)
                && string.Equals(t.ProjectId, projectId, StringComparison.Ordinal)
                && t.StartDate.Date == dayDate
                && (matchAnyDetails || TaskNamesEqual(t.Details, taskName)))
            .OrderBy(t => t.StartDate)
            .ToList();

        var allDays = weekTasks
            .Where(t =>
                t.IsAllDay
                && string.Equals(t.ProjectId, projectId, StringComparison.Ordinal)
                && AllDayCoversDate(t, dayDate)
                && (matchAnyDetails || TaskNamesEqual(t.Details, taskName)))
            .OrderBy(t => t.StartDate)
            .ToList();

        var allDayHours = TimeSpan.Zero;
        foreach (var a in allDays)
            allDayHours += HoursOnDay(a, dayDate, workdayHours);

        var totalManual = TimeSpan.Zero;
        foreach (var m in manuals)
            totalManual += m.Duration;
        totalManual += allDayHours;

        if (manuals.Count == 0 && allDays.Count == 0)
            return new DayBinding(DayBindKind.Empty, null, null, null, TimeSpan.Zero, TimeSpan.Zero);

        if (manuals.Count == 0)
        {
            var first = allDays[0];
            return new DayBinding(
                DayBindKind.AllDay,
                first.TaskId,
                first.Details,
                first.StartDate,
                TimeSpan.Zero,
                NormalizeDuration(totalManual));
        }

        if (manuals.Count > 1 || allDays.Count > 0)
        {
            return new DayBinding(
                DayBindKind.Multiple,
                null,
                manuals[0].Details,
                null,
                TimeSpan.Zero,
                NormalizeDuration(totalManual));
        }

        var one = manuals[0];
        return new DayBinding(
            DayBindKind.Single,
            one.TaskId,
            one.Details,
            one.StartDate,
            NormalizeDuration(one.Duration),
            NormalizeDuration(totalManual));
    }

    /// <summary>
    /// Hours this task contributes to a calendar day. All-day entries count
    /// <paramref name="workdayHours"/> on each weekday they cover (0 on weekends).
    /// Timed entries count their duration on the start date only.
    /// </summary>
    public static TimeSpan HoursOnDay(
        WeekEntryTaskSlice task,
        DateTime day,
        double workdayHours = AllDayEntryRules.DefaultWorkdayHours)
    {
        var dayDate = day.Date;
        if (task.IsAllDay)
        {
            if (dayDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                return TimeSpan.Zero;
            if (!AllDayCoversDate(task, dayDate))
                return TimeSpan.Zero;
            var hours = workdayHours > 0 ? workdayHours : AllDayEntryRules.DefaultWorkdayHours;
            return TimeSpan.FromHours(hours);
        }

        if (task.StartDate.Date != dayDate)
            return TimeSpan.Zero;
        return task.Duration < TimeSpan.Zero ? TimeSpan.Zero : task.Duration;
    }

    /// <summary>Sum of <see cref="HoursOnDay"/> for every task (timed, all-day, stopwatch).</summary>
    public static TimeSpan SumDayDuration(
        IEnumerable<WeekEntryTaskSlice> weekTasks,
        DateTime day,
        double workdayHours = AllDayEntryRules.DefaultWorkdayHours)
    {
        var total = TimeSpan.Zero;
        foreach (var t in weekTasks)
            total += HoursOnDay(t, day, workdayHours);
        return NormalizeDuration(total);
    }

    public static bool AllDayCoversDate(WeekEntryTaskSlice task, DateTime day)
    {
        if (!task.IsAllDay)
            return false;
        var start = task.StartDate.Date;
        var end = (task.EndDate ?? task.StartDate).Date;
        var d = day.Date;
        return d >= start && d <= end;
    }

    public static bool OverlapsDayRange(WeekEntryTaskSlice task, DateTime from, DateTime to)
    {
        var f = from.Date;
        var end = to.Date;
        if (task.IsAllDay)
        {
            var start = task.StartDate.Date;
            var last = (task.EndDate ?? task.StartDate).Date;
            return last >= f && start <= end;
        }

        var sd = task.StartDate.Date;
        return sd >= f && sd <= end;
    }

    /// <summary>
    /// Sum durations for the week. Project total filters by <paramref name="projectId"/>;
    /// grand total includes every task in the list. All-day and stopwatch sessions count.
    /// </summary>
    public static WeekTotals SumTotals(IEnumerable<WeekEntryTaskSlice> weekTasks, string? projectId)
    {
        var grand = TimeSpan.Zero;
        var project = TimeSpan.Zero;
        foreach (var t in weekTasks)
        {
            grand += t.Duration;
            if (!string.IsNullOrEmpty(projectId)
                && string.Equals(t.ProjectId, projectId, StringComparison.Ordinal))
            {
                project += t.Duration;
            }
        }

        return new WeekTotals(NormalizeDuration(project), NormalizeDuration(grand));
    }

    /// <summary>
    /// True when the day falls in a submitted (year, month) pair.
    /// </summary>
    public static bool IsDaySubmitted(DateTime day, IEnumerable<(int Year, int Month)> submittedMonths)
    {
        var y = day.Year;
        var m = day.Month;
        foreach (var sm in submittedMonths)
        {
            if (sm.Year == y && sm.Month == m)
                return true;
        }

        return false;
    }

    public static string FormatDuration(TimeSpan duration)
    {
        var d = NormalizeDuration(duration);
        var hours = (int)d.TotalHours;
        var minutes = d.Minutes;
        if (hours == 0 && minutes == 0)
            return "0h";
        if (minutes == 0)
            return $"{hours}h";
        if (hours == 0)
            return $"{minutes}m";
        return $"{hours}h {minutes}m";
    }

    /// <summary>Minimal task shape for binding/totals without pulling client models into Shared.</summary>
    public readonly record struct WeekEntryTaskSlice(
        string TaskId,
        string Details,
        string? ProjectId,
        DateTime StartDate,
        TimeSpan Duration,
        bool IsAllDay,
        string? StopwatchItemId,
        DateTime? EndDate = null);
}
