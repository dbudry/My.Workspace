using FluentValidation;
using My.Shared.Rules;

namespace My.Shared.Validation
{
    /// <summary>Value object for FluentValidation of free-text start times on Tasks inline edit.</summary>
    public sealed class TaskStartTimeText
    {
        public string? Value { get; set; }
        public bool Use24HourTime { get; set; }
    }

    /// <summary>Value object for FluentValidation of H:MM duration text on Tasks inline edit.</summary>
    public sealed class TaskDurationText
    {
        public string? Value { get; set; }
    }

    /// <summary>Value object for FluentValidation of task name on Tasks inline edit.</summary>
    public sealed class TaskNameText
    {
        public string? Value { get; set; }
    }

    public class TaskStartTimeTextValidator : AbstractValidator<TaskStartTimeText>
    {
        public TaskStartTimeTextValidator()
        {
            RuleFor(x => x.Value)
                .Custom((value, context) =>
                {
                    var use24 = context.InstanceToValidate.Use24HourTime;
                    var err = TimeOfDayTextRules.Validate(value, use24, out _);
                    if (err != null)
                        context.AddFailure(err);
                });
        }
    }

    public class TaskDurationTextValidator : AbstractValidator<TaskDurationText>
    {
        public TaskDurationTextValidator()
        {
            RuleFor(x => x.Value)
                .Custom((value, context) =>
                {
                    // Digits + colon only; commit accepts "4" as 4 hours.
                    var filtered = WeekEntryGridRules.FilterDurationInputChars(value);
                    if (string.IsNullOrWhiteSpace(filtered))
                    {
                        context.AddFailure("Duration is required.");
                        return;
                    }

                    if (!WeekEntryGridRules.TryCommitDayDurationText(filtered, out var duration))
                    {
                        filtered = WeekEntryGridRules.NormalizeDayDurationText(filtered);
                        if (!WeekEntryGridRules.TryCommitDayDurationText(filtered, out duration)
                            || duration <= TimeSpan.Zero)
                        {
                            context.AddFailure("Enter HH:MM (e.g. 02:30), max 24:00.");
                            return;
                        }
                    }

                    if (duration <= TimeSpan.Zero)
                        context.AddFailure("Duration is required.");
                });
        }
    }

    public class TaskNameTextValidator : AbstractValidator<TaskNameText>
    {
        public TaskNameTextValidator()
        {
            RuleFor(x => x.Value)
                .Custom((value, context) =>
                {
                    var err = WeekEntryGridRules.ValidateTaskName(value);
                    if (err != null)
                        context.AddFailure(err);
                });
        }
    }
}
