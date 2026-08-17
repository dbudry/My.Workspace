using FluentValidation;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Rules;

namespace My.Shared.Validation
{
    public class CreateTrackedTaskDtoValidator : AbstractValidator<CreateTrackedTaskDto>
    {
        public CreateTrackedTaskDtoValidator()
        {
            // Details (Name) are optional. Length is measured after trim so spaces
            // cannot pad past the max. Empty / whitespace becomes "" on save.
            RuleFor(x => x.Details)
                .Must(n => WeekEntryGridRules.SanitizeTaskDetails(n).Length <= TaskDetailsRules.MaxLength)
                    .WithMessage(TaskDetailsRules.MaxLengthMessage);

            // Timed entries only — all-day duration is derived server-side after this gate.
            // SQL time columns reject 24h+; never let that hit SaveChanges as a 500.
            RuleFor(x => x.Duration)
                .Must(DurationStorageRules.IsWithinStorageLimit)
                .When(x => !x.IsAllDay)
                .WithMessage(DurationStorageRules.ExceedsStorageLimitMessage);
        }
    }

    public class UpdateTrackedTaskDtoValidator : AbstractValidator<UpdateTrackedTaskDto>
    {
        public UpdateTrackedTaskDtoValidator()
        {
            RuleFor(x => x.TaskId).NotEmpty().WithMessage("Task id is required.");

            RuleFor(x => x.Details)
                .Must(n => WeekEntryGridRules.SanitizeTaskDetails(n).Length <= TaskDetailsRules.MaxLength)
                    .WithMessage(TaskDetailsRules.MaxLengthMessage);

            RuleFor(x => x.Duration)
                .Must(d => d is null || DurationStorageRules.IsWithinStorageLimit(d.Value))
                .When(x => !x.IsAllDay)
                .WithMessage(DurationStorageRules.ExceedsStorageLimitMessage);
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