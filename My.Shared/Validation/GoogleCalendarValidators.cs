using FluentValidation;
using My.Shared.Dtos.GoogleCalendar;
using My.Shared.Rules;

namespace My.Shared.Validation
{
    public class GoogleCalendarCallbackDtoValidator : AbstractValidator<GoogleCalendarCallbackDto>
    {
        public GoogleCalendarCallbackDtoValidator()
        {
            RuleFor(x => x.Code)
                .NotEmpty().WithMessage("code and redirectUri are required.");

            RuleFor(x => x.RedirectUri)
                .Custom((uri, context) =>
                {
                    if (!GoogleOAuthRedirectRules.IsAllowedRedirectUri(uri, out var error))
                        context.AddFailure(nameof(GoogleCalendarCallbackDto.RedirectUri), error!);
                });
        }
    }
}