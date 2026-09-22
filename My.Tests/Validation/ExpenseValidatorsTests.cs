using My.Shared.Dtos.Expenses;
using My.Shared.Rules;
using My.Shared.Validation;
using Xunit;

namespace My.Tests.Validation;

public class ExpenseValidatorsTests
{
    [Fact]
    public void Create_rejects_invalid_month()
    {
        var result = new CreateExpenseReportDtoValidator()
            .Validate(new CreateExpenseReportDto { Year = 2026, Month = 13 });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Create_accepts_valid_year_month()
    {
        var result = new CreateExpenseReportDtoValidator()
            .Validate(new CreateExpenseReportDto { Year = 2026, Month = 8 });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Settings_rejects_mileage_out_of_range()
    {
        var result = new UpdateExpenseSettingsDtoValidator()
            .Validate(new UpdateExpenseSettingsDto { MileageRatePerMile = 11m });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Settings_accepts_default_rate()
    {
        var result = new UpdateExpenseSettingsDtoValidator()
            .Validate(new UpdateExpenseSettingsDto { MileageRatePerMile = ExpenseMileageRateRules.DefaultPerMile });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Update_rejects_unknown_category()
    {
        var result = new UpdateExpenseReportDtoValidator().Validate(new UpdateExpenseReportDto
        {
            Lines =
            [
                new ExpenseLineDto
                {
                    Date = new DateTime(2026, 8, 1),
                    Description = "Widget",
                    Category = "Travel"
                }
            ]
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("category", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Update_accepts_software_line()
    {
        var result = new UpdateExpenseReportDtoValidator().Validate(new UpdateExpenseReportDto
        {
            Lines =
            [
                new ExpenseLineDto
                {
                    Date = new DateTime(2026, 7, 31),
                    Description = "GitHub Source Control",
                    Category = ExpenseCategoryRules.Software,
                    Amount = 24m
                }
            ]
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Update_rejects_cover_end_before_cover_start()
    {
        var result = new UpdateExpenseReportDtoValidator().Validate(new UpdateExpenseReportDto
        {
            CoverStart = new DateTime(2026, 9, 15),
            CoverEnd = new DateTime(2026, 9, 1),
            Lines = []
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Cover end date", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Update_accepts_free_form_cover_period()
    {
        var result = new UpdateExpenseReportDtoValidator().Validate(new UpdateExpenseReportDto
        {
            CoverStart = new DateTime(2026, 9, 25),
            CoverEnd = new DateTime(2026, 10, 5),
            Lines = []
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Update_allows_blank_purpose()
    {
        var result = new UpdateExpenseReportDtoValidator().Validate(new UpdateExpenseReportDto
        {
            CoverStart = new DateTime(2026, 9, 1),
            CoverEnd = new DateTime(2026, 9, 30),
            Purpose = "",
            Lines = []
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Update_rejects_purpose_over_max_length()
    {
        var result = new UpdateExpenseReportDtoValidator().Validate(new UpdateExpenseReportDto
        {
            CoverStart = new DateTime(2026, 9, 1),
            CoverEnd = new DateTime(2026, 9, 30),
            Purpose = new string('a', ExpenseReportRules.PurposeMaxLength + 1),
            Lines = []
        });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Update_accepts_purpose_at_max_length()
    {
        var result = new UpdateExpenseReportDtoValidator().Validate(new UpdateExpenseReportDto
        {
            CoverStart = new DateTime(2026, 9, 1),
            CoverEnd = new DateTime(2026, 9, 30),
            Purpose = new string('a', ExpenseReportRules.PurposeMaxLength),
            Lines = []
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Update_rejects_line_dates_in_different_months()
    {
        var result = new UpdateExpenseReportDtoValidator().Validate(new UpdateExpenseReportDto
        {
            Lines =
            [
                new ExpenseLineDto
                {
                    Date = new DateTime(2026, 9, 9),
                    Description = "Sept",
                    Category = ExpenseCategoryRules.Software,
                    Amount = 1m
                },
                new ExpenseLineDto
                {
                    Date = new DateTime(2026, 8, 10),
                    Description = "Aug",
                    Category = ExpenseCategoryRules.Software,
                    Amount = 1m
                }
            ]
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == ExpenseReportRules.MixedMonthLinesMessage);
    }
}
