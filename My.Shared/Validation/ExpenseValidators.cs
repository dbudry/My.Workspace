using FluentValidation;
using My.Shared.Dtos.Expenses;
using My.Shared.Rules;

namespace My.Shared.Validation
{
    public class CreateExpenseReportDtoValidator : AbstractValidator<CreateExpenseReportDto>
    {
        public CreateExpenseReportDtoValidator()
        {
            RuleFor(x => x.CoverStart)
                .Must(d => d != default)
                .WithMessage("Cover start is required.");

            RuleFor(x => x.CoverEnd)
                .Must(d => d != default)
                .WithMessage("Cover end is required.");

            RuleFor(x => x)
                .Must(x => ExpenseReportRules.IsValidCoverPeriod(x.CoverStart, x.CoverEnd))
                .When(x => x.CoverStart != default && x.CoverEnd != default)
                .WithMessage("Cover end date cannot be before cover start date.");
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

            RuleForEach(x => x.Lines).SetValidator(new ExpenseLineDtoValidator());
        }
    }
}
