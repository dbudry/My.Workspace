namespace My.Shared.Rules
{
    public static class StopwatchRules
    {
        /// <summary>
        /// TrackedTask.Duration persists to SQL <c>time</c>, whose ceiling is 23:59:59.9999999.
        /// A session left running for days must still clamp to something storable when stopped.
        /// </summary>
        public static readonly TimeSpan MaxStorableDuration = new(0, 23, 59, 0);

        /// <summary>Rounds elapsed time up to the next whole minute (minimum 0).</summary>
        public static TimeSpan RoundUpToMinute(TimeSpan value)
        {
            if (value <= TimeSpan.Zero) return TimeSpan.Zero;
            var minutes = (long)Math.Ceiling(value.TotalMinutes);
            return TimeSpan.FromMinutes(minutes);
        }

        /// <summary>Clamps a duration to what SQL <c>time</c> can store. Use only right before persisting Duration.</summary>
        public static TimeSpan ClampForStorage(TimeSpan duration) =>
            duration > MaxStorableDuration ? MaxStorableDuration : duration;

        public static TimeSpan ElapsedForActiveSession(DateTime startUtc, DateTime? endUtc)
        {
            var end = endUtc ?? DateTime.UtcNow;
            return end > startUtc ? end - startUtc : TimeSpan.Zero;
        }
    }
}