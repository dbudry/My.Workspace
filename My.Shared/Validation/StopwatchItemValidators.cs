using FluentValidation;
using My.Shared.Dtos.StopwatchItem;
using My.Shared.Rules;

namespace My.Shared.Validation
{
    public class CreateStopwatchItemDtoValidator : AbstractValidator<CreateStopwatchItemDto>
    {
        public CreateStopwatchItemDtoValidator()
        {
            RuleFor(x => x.Details)
                .NotEmpty().WithMessage(TaskDetailsRules.RequiredMessage)
                .MinimumLength(TaskDetailsRules.MinLength).WithMessage(TaskDetailsRules.MinLengthMessage)
                .MaximumLength(TaskDetailsRules.MaxLength).WithMessage(TaskDetailsRules.MaxLengthMessage);

            RuleFor(x => x.ProjectId)
                .NotEmpty().WithMessage("A project is required to log time.");
        }
    }

    public class UpdateStopwatchItemDtoValidator : AbstractValidator<UpdateStopwatchItemDto>
    {
        public UpdateStopwatchItemDtoValidator()
        {
            RuleFor(x => x.StopwatchItemId)
                .NotEmpty().WithMessage("Stopwatch item id is required.");

            RuleFor(x => x.Details)
                .NotEmpty().WithMessage(TaskDetailsRules.RequiredMessage)
                .MinimumLength(TaskDetailsRules.MinLength).WithMessage(TaskDetailsRules.MinLengthMessage)
                .MaximumLength(TaskDetailsRules.MaxLength).WithMessage(TaskDetailsRules.MaxLengthMessage);

            RuleFor(x => x.ProjectId)
                .NotEmpty().WithMessage("A project is required to log time.");
        }
    }
}