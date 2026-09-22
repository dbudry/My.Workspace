using System.Text.Json;
using My.Shared.Dtos.Expenses;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Serialization;

public class CalendarDateJsonConverterTests
{
    [Fact]
    public void Round_trips_as_yyyy_MM_dd()
    {
        var dto = new ExpenseLineDto { Date = new DateTime(2026, 8, 10, 18, 30, 0, DateTimeKind.Utc) };
        var json = JsonSerializer.Serialize(dto);
        Assert.Contains("\"2026-08-10\"", json, StringComparison.Ordinal);
        var back = JsonSerializer.Deserialize<ExpenseLineDto>(json);
        Assert.Equal(new DateTime(2026, 8, 10), back!.Date.Date);
        Assert.Equal(DateTimeKind.Unspecified, back.Date.Kind);
    }

    [Fact]
    public void CalendarDate_strips_kind_and_time()
    {
        var value = ExpenseLineRules.CalendarDate(new DateTime(2026, 9, 9, 7, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2026, 9, 9), value);
        Assert.Equal(DateTimeKind.Unspecified, value.Kind);
    }

    [Fact]
    public void Cover_dates_round_trip_as_yyyy_MM_dd()
    {
        var dto = new ExpenseReportDto
        {
            CoverStart = new DateTime(2026, 8, 1, 18, 0, 0, DateTimeKind.Utc),
            CoverEnd = new DateTime(2026, 8, 31, 18, 0, 0, DateTimeKind.Utc)
        };
        var json = JsonSerializer.Serialize(dto);
        Assert.Contains("\"2026-08-01\"", json, StringComparison.Ordinal);
        Assert.Contains("\"2026-08-31\"", json, StringComparison.Ordinal);
        var back = JsonSerializer.Deserialize<ExpenseReportDto>(json);
        Assert.Equal(new DateTime(2026, 8, 1), back!.CoverStart.Date);
        Assert.Equal(new DateTime(2026, 8, 31), back.CoverEnd.Date);
    }
}
