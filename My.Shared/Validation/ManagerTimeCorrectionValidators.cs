using FluentValidation;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Rules;

namespace My.Shared.Validation;

public class ManagerTimeCorrectionDtoValidator : AbstractValidator<ManagerTimeCorrectionDto>
{
    public ManagerTimeCorrectionDtoValidator()
    {
        RuleFor(x => x.Details)
            .NotEmpty().WithMessage(TaskDetailsRules.RequiredMessage)
            .MinimumLength(TaskDetailsRules.MinLength).WithMessage(TaskDetailsRules.MinLengthMessage)
            .MaximumLength(TaskDetailsRules.MaxLength).WithMessage(TaskDetailsRules.MaxLengthMessage);
    }
}