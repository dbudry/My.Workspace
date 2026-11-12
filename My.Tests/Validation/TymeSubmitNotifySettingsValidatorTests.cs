using My.Shared.Dtos.TimeSubmission;
using My.Shared.Validation;
using Xunit;

namespace My.Tests.Validation;

public class TymeSubmitNotifySettingsValidatorTests
{
    private readonly UpdateTymeSubmitNotifySettingsDtoValidator _validator = new();

    [Fact]
    public void Defaults_are_valid()
    {
        Assert.True(_validator.Validate(new UpdateTymeSubmitNotifySettingsDto()).IsValid);
    }

    [Fact]
    public void Window_over_twelve_months_is_rejected()
    {
        var result = _validator.Validate(new UpdateTymeSubmitNotifySettingsDto { MonthsBefore = 6, MonthsAfter = 6 });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Twelve_month_window_is_allowed()
    {
        var result = _validator.Validate(new UpdateTymeSubmitNotifySettingsDto { MonthsBefore = 6, MonthsAfter = 5 });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Negative_offsets_are_rejected()
    {
        Assert.False(_validator.Validate(new UpdateTymeSubmitNotifySettingsDto { MonthsBefore = -1 }).IsValid);
    }

    [Fact]
    public void Blank_recipient_id_is_rejected()
    {
        var result = _validator.Validate(new UpdateTymeSubmitNotifySettingsDto { RecipientIds = [""] });

        Assert.False(result.IsValid);
    }
}
