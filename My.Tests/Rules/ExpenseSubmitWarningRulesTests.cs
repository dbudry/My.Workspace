using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpenseSubmitWarningRulesTests
{
    [Fact]
    public void Meal_without_a_head_count_is_named_and_does_not_block_save()
    {
        var message = ExpenseMealRules.IncompleteMessage(
            ExpenseCategoryRules.Meals, ExpenseMealRules.Breakfast, null, "GitHub");
        Assert.Equal("Meal \"GitHub\" needs a head count.", message);

        var saved = new My.Shared.Validation.UpdateExpenseReportDtoValidator().Validate(
            new My.Shared.Dtos.Expenses.UpdateExpenseReportDto
            {
                CoverStart = new DateTime(2026, 8, 1),
                CoverEnd = new DateTime(2026, 8, 31),
                Lines =
                [
                    new My.Shared.Dtos.Expenses.ExpenseLineDto
                    {
                        Date = new DateTime(2026, 8, 10),
                        Description = "GitHub",
                        Category = ExpenseCategoryRules.Meals,
                        MealType = ExpenseMealRules.Breakfast,
                        Amount = 0m
                    }
                ]
            });
        Assert.True(saved.IsValid);
    }

    [Fact]
    public void Submit_warnings_list_the_meal_and_the_missing_receipt()
    {
        var warnings = ExpenseSubmitWarningRules.Messages(
        [
            new ExpenseSubmitWarningRules.Line(
                new DateTime(2026, 8, 10), "GitHub", 0m,
                ExpenseCategoryRules.Meals, ExpenseMealRules.Breakfast, null, 3),
            new ExpenseSubmitWarningRules.Line(
                new DateTime(2026, 8, 1), "asdfasdfaf", 0m,
                ExpenseCategoryRules.Software, null, null, 0)
        ]);

        Assert.Equal(2, warnings.Count);
        Assert.Contains("GitHub", warnings[0]);
        Assert.Contains("head count", warnings[0]);
        Assert.Contains("asdfasdfaf", warnings[1]);
        Assert.Contains("No receipt", warnings[1]);

        var issues = ExpenseSubmitWarningRules.Issues(
        [
            new ExpenseSubmitWarningRules.Line(
                new DateTime(2026, 8, 10), "GitHub", 0m,
                ExpenseCategoryRules.Meals, ExpenseMealRules.Breakfast, null, 3),
            new ExpenseSubmitWarningRules.Line(
                new DateTime(2026, 8, 1), "asdfasdfaf", 0m,
                ExpenseCategoryRules.Software, null, null, 0)
        ]);
        Assert.Equal("No receipt", issues[1].Text);
        Assert.Single(issues[1].Lines);
        Assert.Contains("asdfasdfaf", issues[1].Lines[0]);
        Assert.DoesNotContain("asdfasdfaf", issues[1].Text);
    }

    [Fact]
    public void Submit_confirm_splits_the_filing_sentence()
    {
        var coverStart = new DateTime(2026, 9, 1);
        var coverEnd = new DateTime(2026, 9, 30);
        var today = new DateTime(2026, 10, 21);
        var copy = ExpenseReportRules.SubmitConfirm(coverStart, coverEnd, today);

        Assert.Equal("Submit the report covering 09/01/26 – 09/30/26?", copy.Question);
        Assert.Equal("It will be filed as October 2026.", copy.FiledAs);
        Assert.Equal("This locks the report and files the statement PDF on Drive.", copy.Effect);
        Assert.Equal(
            $"{copy.Question} {copy.FiledAs} {copy.Effect}",
            ExpenseReportRules.SubmitConfirmMessage(coverStart, coverEnd, today));
    }
}
