using FluentValidation;
using My.Shared.Dtos.Query;
using My.Shared;
using My.Shared.Rules;

namespace My.Shared.Validation
{
    public class DateRangeQueryDtoValidator : AbstractValidator<DateRangeQueryDto>
    {
        public DateRangeQueryDtoValidator()
        {
            RuleFor(x => x.From)
                .NotEmpty().WithMessage("from is required (YYYY-MM-DD).");

            RuleFor(x => x.To)
                .NotEmpty().WithMessage("to is required (YYYY-MM-DD).");

            RuleFor(x => x)
                .Must(q => q.To >= q.From)
                .WithMessage("'to' must be on or after 'from'.");
        }
    }

    public class RedirectUriQueryValidator : AbstractValidator<string>
    {
        public RedirectUriQueryValidator()
        {
            RuleFor(x => x)
                .Custom((uri, context) =>
                {
                    if (!GoogleOAuthRedirectRules.IsAllowedRedirectUri(uri, out var error))
                        context.AddFailure(error!);
                });
        }
    }

    public class DriveFileIdValidator : AbstractValidator<string>
    {
        public DriveFileIdValidator()
        {
            RuleFor(x => x)
                .NotEmpty().WithMessage("driveFileId is required.");
        }
    }

    public class IntranetSearchQueryValidator : AbstractValidator<string>
    {
        public IntranetSearchQueryValidator()
        {
            RuleFor(x => x)
                .MinimumLength(IntranetSearchHelper.MinQueryLength)
                .WithMessage($"Type at least {IntranetSearchHelper.MinQueryLength} characters.");
        }
    }
}