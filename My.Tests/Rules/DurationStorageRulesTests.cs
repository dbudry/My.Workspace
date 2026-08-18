using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class DurationStorageRulesTests
{
    [Fact]
    public void Accepts_zero_and_typical_day_values()
    {
        Assert.True(DurationStorageRules.IsWithinStorageLimit(TimeSpan.Zero));
        Assert.True(DurationStorageRules.IsWithinStorageLimit(TimeSpan.FromHours(8)));
        Assert.True(DurationStorageRules.IsWithinStorageLimit(new TimeSpan(23, 59, 0)));
        Assert.Null(DurationStorageRules.ValidateForStorage(new TimeSpan(23, 59, 0)));
    }

    [Fact]
    public void Rejects_24_hours_and_above_which_overflow_sql_time()
    {
        // Production failure: Value '1.16:00:00' (40h) and 24:00 both overflow SqlDbType.Time.
        Assert.False(DurationStorageRules.IsWithinStorageLimit(TimeSpan.FromHours(24)));
        Assert.False(DurationStorageRules.IsWithinStorageLimit(TimeSpan.FromHours(40)));
        Assert.Equal(
            DurationStorageRules.ExceedsStorageLimitMessage,
            DurationStorageRules.ValidateForStorage(TimeSpan.FromHours(40)));
    }

    [Fact]
    public void ValidateForStorage_allows_zero_for_in_progress_timers_and_empty_cells()
    {
        // ValidateForStorage is deliberately permissive of zero — an active stopwatch or an
        // empty week-grid cell both persist as TimeSpan.Zero mid-flight. The stricter "must be
        // nonzero to save" rule lives in ValidateForFinalize below.
        Assert.Null(DurationStorageRules.ValidateForStorage(TimeSpan.Zero));
    }

    [Fact]
    public void ValidateForFinalize_rejects_zero_duration_on_a_timed_entry()
    {
        // Regression: new tasks used to default to 30 minutes, so a save with an
        // accidentally-cleared duration field silently went through as zero.
        Assert.Equal(
            DurationStorageRules.ZeroDurationMessage,
            DurationStorageRules.ValidateForFinalize(TimeSpan.Zero, isAllDay: false));
    }

    [Fact]
    public void ValidateForFinalize_accepts_a_positive_timed_duration()
    {
        Assert.Null(DurationStorageRules.ValidateForFinalize(TimeSpan.FromMinutes(1), isAllDay: false));
        Assert.Null(DurationStorageRules.ValidateForFinalize(TimeSpan.FromHours(8), isAllDay: false));
    }

    [Fact]
    public void ValidateForFinalize_still_enforces_the_storage_limit_for_timed_entries()
    {
        Assert.Equal(
            DurationStorageRules.ExceedsStorageLimitMessage,
            DurationStorageRules.ValidateForFinalize(TimeSpan.FromHours(40), isAllDay: false));
    }

    [Fact]
    public void ForSqlTimeColumn_keeps_thirteen_hours_seventeen_minutes_thirty_seconds()
    {
        var span = new TimeSpan(13, 17, 30);
        Assert.Equal(span, DurationStorageRules.ForSqlTimeColumn(span));
    }

    [Fact]
    public void ForSqlTimeColumn_does_not_store_a_24_hour_all_day_total()
    {
        Assert.Equal(TimeSpan.Zero, DurationStorageRules.ForSqlTimeColumn(TimeSpan.FromHours(24)));
    }

    [Fact]
    public void ValidateForFinalize_exempts_all_day_entries_from_the_zero_check()
    {
        // All-day duration is derived from workday hours, not this field — an all-day
        // entry with a zero-valued duration field (irrelevant/unused) must not block save.
        Assert.Null(DurationStorageRules.ValidateForFinalize(TimeSpan.Zero, isAllDay: true));
        // 3 workdays × 8h = 24h. Timed entries still cannot store that; all-day can.
        Assert.Null(DurationStorageRules.ValidateForFinalize(TimeSpan.FromHours(24), isAllDay: true));
    }
}
