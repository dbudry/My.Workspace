using FluentValidation;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Rules;

namespace My.Shared.Validation
{
    public class CreateTrackedTaskDtoValidator : AbstractValidator<CreateTrackedTaskDto>
    {
        public CreateTrackedTaskDtoValidator()
        {
            // Length rules use the trimmed name so leading/trailing spaces cannot pad past min length.
            RuleFor(x => x.Name)
                .Must(n => WeekEntryGridRules.SanitizeTaskName(n).Length > 0)
                    .WithMessage("Name is required.")
                .Must(n => WeekEntryGridRules.SanitizeTaskName(n).Length >= 2)
                    .WithMessage("Name must be at least 2 characters.")
                .Must(n => WeekEntryGridRules.SanitizeTaskName(n).Length <= 50)
                    .WithMessage("Name cannot exceed 50 characters.");
        }
    }

    public class UpdateTrackedTaskDtoValidator : AbstractValidator<UpdateTrackedTaskDto>
    {
        public UpdateTrackedTaskDtoValidator()
        {
            RuleFor(x => x.TaskId).NotEmpty().WithMessage("Task id is required.");

            RuleFor(x => x.Name)
                .Must(n => WeekEntryGridRules.SanitizeTaskName(n).Length > 0)
                    .WithMessage("Name is required.")
                .Must(n => WeekEntryGridRules.SanitizeTaskName(n).Length >= 2)
                    .WithMessage("Name must be at least 2 characters.")
                .Must(n => WeekEntryGridRules.SanitizeTaskName(n).Length <= 50)
                    .WithMessage("Name cannot exceed 50 characters.");
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