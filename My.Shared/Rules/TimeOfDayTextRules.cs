using System.Globalization;

namespace My.Shared.Rules;

/// <summary>
/// Pure parse/validate for free-text start times (inline Tasks edit, task dialog).
/// Accepts what people actually type: 9, 9pm, 9:30 AM, 14:30.
/// </summary>
public static class TimeOfDayTextRules
{
    public static string RequiredMessage => "Start time is required.";

    public static string InvalidMessage(bool use24Hour) =>
        use24Hour
            ? "Couldn't read that time. Try 14:30."
            : "Couldn't read that time. Try 2:30 PM.";

    /// <summary>
    /// Returns null when valid; otherwise a user-facing error. Does not throw.
    /// When valid and <paramref name="parsed"/> is provided, sets the TimeSpan.
    /// </summary>
    public static string? Validate(string? raw, out TimeSpan parsed)
    {
        parsed = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(raw))
            return RequiredMessage;

        if (!TryParse(raw, out parsed))
            return InvalidMessage(use24Hour: false); // generic; UI can override for 24h

        return null;
    }

    public static string? Validate(string? raw, bool use24Hour, out TimeSpan parsed)
    {
        parsed = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(raw))
            return RequiredMessage;

        if (!TryParse(raw, out parsed))
            return InvalidMessage(use24Hour);

        return null;
    }

    public static bool TryParse(string? raw, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        try
        {
            return TryParseCore(raw, out result);
        }
        catch
        {
            // Never throw from parse — invalid input is a validation result, not a crash.
            result = TimeSpan.Zero;
            return false;
        }
    }

    private static bool TryParseCore(string raw, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        var s = raw.Trim().ToUpperInvariant().Replace(" ", "");
        if (s.Length == 0)
            return false;

        // Bare hour first: "9", "14" (must not rely on TryParseExact "H" alone).
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hourOnly)
            && hourOnly is >= 0 and <= 23)
        {
            result = new TimeSpan(hourOnly, 0, 0);
            return true;
        }

        // Bare hour + meridiem without minutes: "9P", "9PM", "12A".
        // Handle before DateTime.TryParseExact — single-letter meridiem is not a valid "tt".
        if (TryParseBareHourMeridiem(s, out result))
            return true;

        // Explicit seconds first so "09:30:00" is not lost if later steps throw.
        if (TimeSpan.TryParseExact(s, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out var withSeconds)
            || TimeSpan.TryParseExact(s, @"h\:mm\:ss", CultureInfo.InvariantCulture, out withSeconds))
        {
            if (withSeconds >= TimeSpan.Zero && withSeconds < TimeSpan.FromDays(1))
            {
                result = withSeconds;
                return true;
            }
        }

        string[] formats =
        {
            "h:mmtt", "h:mtt", "htt",
            "hh:mmtt", "hh:mtt", "hhtt",
            "H:mm", "H:m", "H",
            "HH:mm", "HH:m", "HH",
            "h:mm", "h:m", "h",
            "hh:mm", "hh:m", "hh",
            "HH:mm:ss", "H:mm:ss", "hh:mm:ss", "h:mm:ss"
        };

        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt))
        {
            result = dt.TimeOfDay;
            return true;
        }

        if (TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out withSeconds)
            && withSeconds >= TimeSpan.Zero
            && withSeconds < TimeSpan.FromDays(1))
        {
            result = withSeconds;
            return true;
        }

        var trimmed = raw.Trim();
        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture,
                DateTimeStyles.NoCurrentDateDefault | DateTimeStyles.AllowWhiteSpaces, out var dtInv))
        {
            result = dtInv.TimeOfDay;
            return true;
        }

        if (DateTime.TryParse(trimmed, CultureInfo.CurrentCulture,
                DateTimeStyles.NoCurrentDateDefault | DateTimeStyles.AllowWhiteSpaces, out var dt2))
        {
            result = dt2.TimeOfDay;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parses "9P", "9PM", "12A", "12AM" style tokens already uppercased and space-stripped.
    /// </summary>
    private static bool TryParseBareHourMeridiem(string s, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (s.Length < 2)
            return false;

        string? meridiem = null;
        string hourPart;
        if (s.EndsWith("AM", StringComparison.Ordinal))
        {
            meridiem = "AM";
            hourPart = s[..^2];
        }
        else if (s.EndsWith("PM", StringComparison.Ordinal))
        {
            meridiem = "PM";
            hourPart = s[..^2];
        }
        else if (s[^1] is 'A' or 'P')
        {
            meridiem = s[^1] == 'A' ? "AM" : "PM";
            hourPart = s[..^1];
        }
        else
        {
            return false;
        }

        // Reject leftovers that look like a clock with minutes ("9:30PM" goes through formats).
        if (hourPart.Contains(':', StringComparison.Ordinal))
            return false;

        if (!int.TryParse(hourPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)
            || h is < 1 or > 12)
            return false;

        var hour24 = h switch
        {
            12 when meridiem == "AM" => 0,
            12 when meridiem == "PM" => 12,
            _ when meridiem == "PM" => h + 12,
            _ => h
        };
        result = new TimeSpan(hour24, 0, 0);
        return true;
    }
}
