using FluentValidation;
using My.Shared.Dtos.UserSettings;
using My.Shared.Rules;

namespace My.Shared.Validation
{
    public class UpdateUserSettingsDtoValidator : AbstractValidator<UpdateUserSettingsDto>
    {
        public UpdateUserSettingsDtoValidator()
        {
            RuleFor(x => x.DefaultStartTimeMinutes)
                .InclusiveBetween(0, 23 * 60 + 59)
                .WithMessage("Default start time must be between 00:00 and 23:59.");

            RuleFor(x => x.TymeEventColorId)
                .Matches("^[1-9]$|^1[01]$").When(x => !string.IsNullOrWhiteSpace(x.TymeEventColorId))
                .WithMessage("Tyme event color id must be between 1 and 11.");

            RuleFor(x => x.TymeUnmatchedEventColorId)
                .Matches("^[1-9]$|^1[01]$").When(x => !string.IsNullOrWhiteSpace(x.TymeUnmatchedEventColorId))
                .WithMessage("Tyme unmatched event color id must be between 1 and 11.");

            RuleFor(x => x.ExpenseHomeAddress)
                .MaximumLength(ExpenseReportRules.AddressSnapshotMaxLength)
                .When(x => !string.IsNullOrWhiteSpace(x.ExpenseHomeAddress));

            RuleFor(x => x.ExpenseDefaultCurrencyCode)
                .Must(code => string.IsNullOrWhiteSpace(code) || ExpenseCurrencyRules.IsCodeShape(code))
                .WithMessage("Default currency must be 3 to 8 letters.");

            RuleFor(x => x.ExpenseEditorView)
                .Must(view => string.IsNullOrWhiteSpace(view)
                    || ExpenseCurrencyRules.NormalizeView(view) == view
                    || string.Equals(view, ExpenseCurrencyRules.EditorForm, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(view, ExpenseCurrencyRules.EditorDetail, StringComparison.OrdinalIgnoreCase))
                .WithMessage("Expense view must be Detail or Form.");

            RuleFor(x => x.ExpenseCurrencyLayout)
                .Must(layout => string.IsNullOrWhiteSpace(layout)
                    || string.Equals(layout, ExpenseCurrencyRules.LayoutMixed, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(layout, ExpenseCurrencyRules.LayoutGrouped, StringComparison.OrdinalIgnoreCase))
                .WithMessage("Currency view must be Mixed or Grouped.");
        }
    }
}