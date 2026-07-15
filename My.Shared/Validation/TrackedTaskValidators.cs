using FluentValidation;
using My.Shared.Dtos.TrackedTask;

namespace My.Shared.Validation
{
    public class CreateTrackedTaskDtoValidator : AbstractValidator<CreateTrackedTaskDto>
    {
        public CreateTrackedTaskDtoValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Name is required.")
                .MinimumLength(2).WithMessage("Name must be at least 2 characters.")
                .MaximumLength(50).WithMessage("Name cannot exceed 50 characters.");
        }
    }

    public class UpdateTrackedTaskDtoValidator : AbstractValidator<UpdateTrackedTaskDto>
    {
        public UpdateTrackedTaskDtoValidator()
        {
            RuleFor(x => x.TaskId).NotEmpty().WithMessage("Task id is required.");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Name is required.")
                .MinimumLength(2).WithMessage("Name must be at least 2 characters.")
                .MaximumLength(50).WithMessage("Name cannot exceed 50 characters.");
        }
    }

    public class DuplicateTrackedTaskDtoValidator : AbstractValidator<DuplicateTrackedTaskDto>
    {
        public DuplicateTrackedTaskDtoValidator()
        {
            // All fields optional — empty body is valid.
        }
    }
}