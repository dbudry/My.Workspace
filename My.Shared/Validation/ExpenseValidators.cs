using FluentValidation;
using My.Shared.Dtos.Expenses;
using My.Shared.Rules;

namespace My.Shared.Validation
{
    public class CreateExpenseReportDtoValidator : AbstractValidator<CreateExpenseReportDto>
    {
        public CreateExpenseReportDtoValidator()
        {
            RuleFor(x => x.Year)
                .InclusiveBetween(2000, 9999).WithMessage("Invalid year/month.");

            RuleFor(x => x.Month)
                .InclusiveBetween(1, 12).WithMessage("Invalid year/month.");
        }
    }

    public class ExpenseLineDtoValidator : AbstractValidator<ExpenseLineDto>
    {
        public ExpenseLineDtoValidator()
        {
            RuleFor(x => x.Date)
                .Must(d => d != default)
                .WithMessage("Each line needs a date.");

            RuleFor(x => x.Description)
                .NotEmpty().WithMessage("Each line needs a description.")
                .MaximumLength(ExpenseLineRules.DescriptionMaxLength);

            RuleFor(x => x.Category)
                .NotEmpty().WithMessage("Each line needs a category.")
                .Must(ExpenseCategoryRules.IsKnown)
                .When(x => !string.IsNullOrWhiteSpace(x.Category))
                .WithMessage("Unknown expense category.");

            RuleFor(x => x.Amount)
                .GreaterThanOrEqualTo(0).WithMessage("Amount cannot be negative.");

            RuleFor(x => x.Miles)
                .GreaterThanOrEqualTo(0)
                .When(x => x.Miles.HasValue)
                .WithMessage("Miles cannot be negative.");

            RuleFor(x => x.TransportationCode)
                .Must(code => string.IsNullOrWhiteSpace(code) || ExpenseCategoryRules.IsTransportationCode(code))
                .WithMessage("Unknown transportation code.");

            RuleFor(x => x.MiscellaneousCode)
                .Must(code => string.IsNullOrWhiteSpace(code) || ExpenseCategoryRules.IsMiscellaneousCode(code))
                .WithMessage("Unknown miscellaneous code.");
        }
    }

    public class UpdateExpenseSettingsDtoValidator : AbstractValidator<UpdateExpenseSettingsDto>
    {
        public UpdateExpenseSettingsDtoValidator()
        {
            RuleFor(x => x.MileageRatePerMile)
                .InclusiveBetween(ExpenseMileageRateRules.MinPerMile, ExpenseMileageRateRules.MaxPerMile)
                .WithMessage($"Mileage rate must be between {ExpenseMileageRateRules.MinPerMile} and {ExpenseMileageRateRules.MaxPerMile}.");
        }
    }

    public class UpdateExpenseReportDtoValidator : AbstractValidator<UpdateExpenseReportDto>
    {
        public UpdateExpenseReportDtoValidator()
        {
            RuleFor(x => x)
                .Must(x => ExpenseReportRules.IsValidCoverPeriod(x.CoverStart, x.CoverEnd))
                .WithMessage("Cover end date cannot be before cover start date.");

            RuleFor(x => x.Purpose)
                .MaximumLength(ExpenseReportRules.PurposeMaxLength);

            RuleFor(x => x.Lines)
                .NotNull()
                .Must(lines => lines.Count <= ExpenseLineRules.MaxLinesPerReport)
                .WithMessage($"A report can have at most {ExpenseLineRules.MaxLinesPerReport} lines.");

            RuleFor(x => x.Lines)
                .Must(lines => lines.Count == 0
                    || ExpenseReportRules.TryPeriodFromLineDates(lines.Select(l => l.Date), out _, out _))
                .When(x => x.Lines != null)
                .WithMessage(ExpenseReportRules.MixedMonthLinesMessage);

            RuleForEach(x => x.Lines).SetValidator(new ExpenseLineDtoValidator());
        }
    }
}
