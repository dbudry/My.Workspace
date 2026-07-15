using FluentValidation;
using My.Shared.Dtos;

namespace My.Shared.Validation
{
    public class AppSettingDtoValidator : AbstractValidator<AppSettingDto>
    {
        public AppSettingDtoValidator()
        {
            RuleFor(x => x.Key)
                .NotEmpty().WithMessage("Setting key is required.")
                .MaximumLength(100);

            RuleFor(x => x.Value)
                .NotNull().WithMessage("Setting value is required.")
                .MaximumLength(500);

            RuleFor(x => x.Description).MaximumLength(200);
        }
    }

    public class UpdateAppSettingsRequestValidator : AbstractValidator<List<AppSettingDto>>
    {
        public UpdateAppSettingsRequestValidator()
        {
            RuleFor(x => x)
                .NotNull().WithMessage("No settings provided.")
                .Must(list => list.Count > 0).WithMessage("No settings provided.");

            RuleForEach(x => x).SetValidator(new AppSettingDtoValidator());
        }
    }
}