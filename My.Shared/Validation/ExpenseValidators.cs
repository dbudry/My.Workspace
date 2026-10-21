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

            RuleFor(x => x.ChargeToOrganizationId)
                .NotEmpty()
                .WithMessage("Charge to is required.");
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

            RuleFor(x => x.SpentAmount)
                .GreaterThanOrEqualTo(0).WithMessage("Amount cannot be negative.");

            RuleFor(x => x.CurrencyCode)
                .Must(code => string.IsNullOrWhiteSpace(code) || ExpenseCurrencyRules.IsCodeShape(code))
                .WithMessage("Currency must be 3 to 8 letters.");

            RuleFor(x => x.ExchangeRateToUsd)
                .GreaterThan(0).WithMessage("Exchange rate must be greater than zero.");

            RuleFor(x => x.Miles)
                .GreaterThanOrEqualTo(0)
                .When(x => x.Miles.HasValue)
                .WithMessage("Miles cannot be negative.");

            RuleFor(x => x.TransportationCode)
                .Must(code => string.IsNullOrWhiteSpace(code) || ExpenseCodeFormatRules.IsValid(code))
                .WithMessage("Transportation code must be a letter A–Z, or a subcategory such as A.01.");

            RuleFor(x => x.MiscellaneousCode)
                .Must(code => string.IsNullOrWhiteSpace(code) || ExpenseCodeFormatRules.IsValid(code))
                .WithMessage("Miscellaneous code must be a letter A–Z, or a subcategory such as A.01.");
        }
    }

    public class UpsertExpenseCodeDtoValidator : AbstractValidator<UpsertExpenseCodeDto>
    {
        public UpsertExpenseCodeDtoValidator()
        {
            RuleFor(x => x.Code)
                .Must(ExpenseCodeFormatRules.IsValid)
                .WithMessage("Code must be a letter A–Z, or a subcategory such as A.01.");

            RuleFor(x => x.Label)
                .NotEmpty()
                .MaximumLength(80);
        }
    }

    public class UpdateExpenseNotifyUsersDtoValidator : AbstractValidator<UpdateExpenseNotifyUsersDto>
    {
        public UpdateExpenseNotifyUsersDtoValidator()
        {
            RuleFor(x => x.UserIds).NotNull();
            RuleForEach(x => x.UserIds).NotEmpty().MaximumLength(50);
        }
    }

    public class UpdateExpenseSettingsDtoValidator : AbstractValidator<UpdateExpenseSettingsDto>
    {
        public UpdateExpenseSettingsDtoValidator()
        {
            RuleFor(x => x.MileageRatePerMile)
                .InclusiveBetween(ExpenseMileageRateRules.MinPerMile, ExpenseMileageRateRules.MaxPerMile)
                .WithMessage($"Mileage rate must be between {ExpenseMileageRateRules.MinPerMile} and {ExpenseMileageRateRules.MaxPerMile}.");

            RuleFor(x => x.CompanyCurrencyCode)
                .Must(ExpenseCurrencyRules.IsCodeShape)
                .WithMessage("Choose a default currency.");

            RuleFor(x => x.CreditEntryMode)
                .Must(mode => mode == ExpenseCreditEntryRules.FromLine || mode == ExpenseCreditEntryRules.OnForm)
                .WithMessage("Credit entry must be from a paid line or on the expense form.");
        }
    }

    public class UpsertExpenseCurrencyDtoValidator : AbstractValidator<UpsertExpenseCurrencyDto>
    {
        public UpsertExpenseCurrencyDtoValidator()
        {
            RuleFor(x => x.Code)
                .Must(ExpenseCurrencyRules.IsCodeShape)
                .WithMessage("Currency code must be 3 to 8 letters.");

            RuleFor(x => x.Name)
                .NotEmpty()
                .MaximumLength(ExpenseCurrencyRules.NameMaxLength);

            RuleFor(x => x.RateToUsd)
                .InclusiveBetween(ExpenseCurrencyRules.MinRate, ExpenseCurrencyRules.MaxRate);
        }
    }

    public class ReimburseExpenseReportDtoValidator : AbstractValidator<ReimburseExpenseReportDto>
    {
        public ReimburseExpenseReportDtoValidator()
        {
            RuleFor(x => x.PaymentDate)
                .Must(date => date != default)
                .WithMessage("Payment date is required.");
        }
    }

    public class ReturnExpenseLineDtoValidator : AbstractValidator<ReturnExpenseLineDto>
    {
        public ReturnExpenseLineDtoValidator()
        {
            RuleFor(x => x.ReturnDate)
                .Must(date => date != default)
                .WithMessage("Credit date is required.");

            RuleFor(x => x.Description)
                .NotEmpty()
                .MaximumLength(ExpenseCreditEntryRules.DescriptionMaxLength)
                .WithMessage("Enter a description.");

            RuleFor(x => x.Amount)
                .GreaterThan(0)
                .WithMessage("Amount must be greater than zero.");

            RuleFor(x => x.CurrencyCode)
                .Must(ExpenseCurrencyRules.IsCodeShape)
                .WithMessage("Choose a currency.");
        }
    }

    public class ExpenseFormCreditDtoValidator : AbstractValidator<ExpenseFormCreditDto>
    {
        public ExpenseFormCreditDtoValidator()
        {
            RuleFor(x => x.Date)
                .Must(date => date != default)
                .WithMessage("Each credit needs a date.");

            RuleFor(x => x.Description)
                .NotEmpty()
                .MaximumLength(ExpenseCreditEntryRules.DescriptionMaxLength)
                .WithMessage("Each credit needs a description.");

            RuleFor(x => x.Amount)
                .GreaterThan(0)
                .WithMessage("Credit amount must be greater than zero.");

            RuleFor(x => x.CurrencyCode)
                .Must(ExpenseCurrencyRules.IsCodeShape)
                .WithMessage("Each credit needs a currency.");

            RuleFor(x => x.ExchangeRateToUsd)
                .GreaterThan(0)
                .WithMessage("Exchange rate must be greater than zero.");
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

            RuleFor(x => x.FormCredits).NotNull();
            RuleForEach(x => x.FormCredits).SetValidator(new ExpenseFormCreditDtoValidator());
        }
    }
}
