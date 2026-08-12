using My.Shared.Validation;
using Xunit;

namespace My.Tests.Validation;

public class TaskInlineFieldValidatorsTests
{
    private readonly TaskStartTimeTextValidator _time = new();
    private readonly TaskDurationTextValidator _duration = new();
    private readonly TaskNameTextValidator _name = new();

    [Fact]
    public void StartTime_rejects_invalid()
    {
        var result = _time.Validate(new TaskStartTimeText { Value = "nope", Use24HourTime = true });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("14:30", StringComparison.Ordinal));
    }

    [Fact]
    public void StartTime_accepts_valid()
    {
        var result = _time.Validate(new TaskStartTimeText { Value = "9:30 AM", Use24HourTime = false });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Duration_rejects_letters_after_normalize()
    {
        // Letters strip out → empty → required.
        var result = _duration.Validate(new TaskDurationText { Value = "aa:aa" });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Duration_accepts_hh_mm()
    {
        var result = _duration.Validate(new TaskDurationText { Value = "02:30" });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Duration_accepts_h_mm()
    {
        var result = _duration.Validate(new TaskDurationText { Value = "2:30" });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Duration_accepts_bare_hours()
    {
        var result = _duration.Validate(new TaskDurationText { Value = "4" });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Duration_clamps_over_storage_max_to_valid_value()
    {
        var result = _duration.Validate(new TaskDurationText { Value = "25:00" });
        // Normalize clamps to 23:59 which is valid and > 0.
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Name_rejects_whitespace_only()
    {
        var result = _name.Validate(new TaskNameText { Value = "   " });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Name_trims_for_length()
    {
        var result = _name.Validate(new TaskNameText { Value = "  ab  " });
        Assert.True(result.IsValid);
    }
}
