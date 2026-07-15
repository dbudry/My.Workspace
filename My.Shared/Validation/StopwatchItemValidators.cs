using FluentValidation;
using My.Shared.Dtos.StopwatchItem;

namespace My.Shared.Validation
{
    public class CreateStopwatchItemDtoValidator : AbstractValidator<CreateStopwatchItemDto>
    {
        public CreateStopwatchItemDtoValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Name is required.")
                .MinimumLength(2).WithMessage("Name must be at least 2 characters.")
                .MaximumLength(50).WithMessage("Name cannot exceed 50 characters.");

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

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Name is required.")
                .MinimumLength(2).WithMessage("Name must be at least 2 characters.")
                .MaximumLength(50).WithMessage("Name cannot exceed 50 characters.");

            RuleFor(x => x.ProjectId)
                .NotEmpty().WithMessage("A project is required to log time.");
        }
    }
}