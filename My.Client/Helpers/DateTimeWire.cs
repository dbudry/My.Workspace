namespace My.Client.Helpers
{
    /// <summary>
    /// Wire-format DateTime helpers for Blazor WASM ↔ API.
    ///
    /// Timed entries in the database are UTC instants. Display and edit use the wall
    /// clock in <c>UserSettings.TimeZone</c> (IANA). All-day entries stay date-only
    /// (no zone shift).
    /// </summary>
    public static class DateTimeWire
    {
        /// <summary>
        /// DB/API UTC → wall clock in the user's configured timezone.
        /// Unspecified Kind is treated as UTC (JSON/EF often strip Kind).
        /// Result is <see cref="DateTimeKind.Unspecified"/> wall time for UI binding.
        /// </summary>
        public static DateTime ToUserTime(DateTime utcOrUnspecified, TimeZoneInfo userTimeZone)
        {
            var utc = utcOrUnspecified.Kind switch
            {
                DateTimeKind.Utc => utcOrUnspecified,
                DateTimeKind.Local => utcOrUnspecified.ToUniversalTime(),
                _ => DateTime.SpecifyKind(utcOrUnspecified, DateTimeKind.Utc),
            };

            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, userTimeZone);
            return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        }

        public static DateTime? ToUserTime(DateTime? utcOrUnspecified, TimeZoneInfo userTimeZone) =>
            utcOrUnspecified.HasValue ? ToUserTime(utcOrUnspecified.Value, userTimeZone) : null;

        /// <summary>
        /// UI wall clock in the user's configured timezone → UTC for create/update DTOs.
        /// </summary>
        public static DateTime ToUtc(DateTime userWallClock, TimeZoneInfo userTimeZone)
        {
            if (userWallClock.Kind == DateTimeKind.Utc)
                return userWallClock;

            // Local Kind: convert via system zone first is wrong — treat as user-zone wall clock.
            var unspecified = DateTime.SpecifyKind(userWallClock, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, userTimeZone);
        }

        public static DateTime? ToUtc(DateTime? userWallClock, TimeZoneInfo userTimeZone) =>
            userWallClock.HasValue ? ToUtc(userWallClock.Value, userTimeZone) : null;

        /// <summary>
        /// Browser-local conversion (fallback when settings TZ is unavailable).
        /// Prefer <see cref="ToUserTime(DateTime, TimeZoneInfo)"/>.
        /// </summary>
        public static DateTime ToLocal(DateTime value) =>
            value.Kind switch
            {
                DateTimeKind.Local => value,
                DateTimeKind.Utc => value.ToLocalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime(),
            };

        public static DateTime? ToLocal(DateTime? value) =>
            value.HasValue ? ToLocal(value.Value) : null;

        /// <summary>
        /// Browser-local → UTC fallback. Prefer <see cref="ToUtc(DateTime, TimeZoneInfo)"/>.
        /// </summary>
        public static DateTime ToUtc(DateTime value) =>
            value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime(),
            };

        public static DateTime? ToUtc(DateTime? value) =>
            value.HasValue ? ToUtc(value.Value) : null;
    }
}
