namespace My.Shared.Rules
{
    public static class StopwatchRules
    {
        /// <summary>Rounds elapsed time up to the next whole minute (minimum 0).</summary>
        public static TimeSpan RoundUpToMinute(TimeSpan value)
        {
            if (value <= TimeSpan.Zero) return TimeSpan.Zero;
            var minutes = (long)Math.Ceiling(value.TotalMinutes);
            return TimeSpan.FromMinutes(minutes);
        }

        public static TimeSpan ElapsedForActiveSession(DateTime startUtc, DateTime? endUtc)
        {
            var end = endUtc ?? DateTime.UtcNow;
            return end > startUtc ? end - startUtc : TimeSpan.Zero;
        }
    }
}