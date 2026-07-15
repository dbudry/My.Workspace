using FluentValidation;
using My.Shared.Dtos.UserSettings;

namespace My.Shared.Validation
{
    public class UpdateUserSettingsDtoValidator : AbstractValidator<UpdateUserSettingsDto>
    {
        public UpdateUserSettingsDtoValidator()
        {
            RuleFor(x => x.TymeEventColorId)
                .Matches("^[1-9]$|^1[01]$").When(x => !string.IsNullOrWhiteSpace(x.TymeEventColorId))
                .WithMessage("Tyme event color id must be between 1 and 11.");

            RuleFor(x => x.TymeUnmatchedEventColorId)
                .Matches("^[1-9]$|^1[01]$").When(x => !string.IsNullOrWhiteSpace(x.TymeUnmatchedEventColorId))
                .WithMessage("Tyme unmatched event color id must be between 1 and 11.");
        }
    }
}