namespace My.Shared.Rules
{
    public static class UserTimeZoneRules
    {
        /// <summary>
        /// IANA-first list for timezone pickers. Includes Windows IDs as fallback on platforms
        /// that cannot convert (so existing saved values still appear).
        /// </summary>
        public static IReadOnlyList<string> GetSelectableTimeZoneIds()
        {
            var zones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
            {
                if (TimeZoneInfo.TryConvertWindowsIdToIanaId(tz.Id, out var iana))
                    zones.Add(iana);
                else
                    zones.Add(tz.Id);
            }

            return zones.OrderBy(z => z, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Resolves a stored timezone id (IANA or Windows) to a TimeZoneInfo.
        /// </summary>
        public static TimeZoneInfo Resolve(string? timeZoneId)
        {
            if (!string.IsNullOrWhiteSpace(timeZoneId))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                }
                catch (TimeZoneNotFoundException) { }

                if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsId))
                {
                    try
                    {
                        return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
                    }
                    catch (TimeZoneNotFoundException) { }
                }
            }

            return TimeZoneInfo.Local;
        }
    }
}