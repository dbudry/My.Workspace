using FluentValidation;
using My.Shared.Dtos.TrackedTaskAlias;
using My.Shared.Rules;

namespace My.Shared.Validation
{
    public class UpsertTrackedTaskAliasDtoValidator : AbstractValidator<UpsertTrackedTaskAliasDto>
    {
        public UpsertTrackedTaskAliasDtoValidator()
        {
            RuleFor(x => x.Details)
                .NotEmpty().WithMessage(TaskDetailsRules.RequiredMessage)
                .MinimumLength(TaskDetailsRules.MinLength).WithMessage(TaskDetailsRules.MinLengthMessage)
                .MaximumLength(TaskDetailsRules.MaxLength).WithMessage(TaskDetailsRules.MaxLengthMessage);
        }
    }
}