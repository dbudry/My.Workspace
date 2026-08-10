using FluentValidation;
using My.Shared.Dtos.UserSettings;

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
        }
    }
}