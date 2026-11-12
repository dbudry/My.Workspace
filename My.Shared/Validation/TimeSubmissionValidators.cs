using FluentValidation;
using My.Shared.Dtos.TimeSubmission;

namespace My.Shared.Validation
{
    public class CreateTimeSubmissionDtoValidator : AbstractValidator<CreateTimeSubmissionDto>
    {
        public CreateTimeSubmissionDtoValidator()
        {
            RuleFor(x => x.Month)
                .InclusiveBetween(1, 12).WithMessage("Invalid Year/Month.");

            RuleFor(x => x.Year)
                .InclusiveBetween(2000, 9999).WithMessage("Invalid Year/Month.");

            // Deliberately no "must be a past month" rule here. Early submission of the
            // current or a future month is allowed (e.g. pre-entering vacation before
            // going on leave) as long as the user has tracked time in that month — that
            // check needs the DbContext, so it lives in TimeSubmissionFunction.CreateAsync
            // as a business rule, not here in shape validation. See
            // My.Shared/Rules/TimeSubmissionRules.cs for the pure "is this an early
            // submission" classification used to label the month for the user.
        }
    }

    public class UpdateTymeSubmitNotifySettingsDtoValidator : AbstractValidator<UpdateTymeSubmitNotifySettingsDto>
    {
        public UpdateTymeSubmitNotifySettingsDtoValidator()
        {
            RuleFor(x => x.RecipientIds).NotNull();
            RuleForEach(x => x.RecipientIds)
                .NotEmpty().MaximumLength(50);

            RuleFor(x => x.MonthsBefore)
                .InclusiveBetween(0, Rules.SubmissionGridRules.MaxOffset)
                .WithMessage("Months before must be between 0 and 11.");
            RuleFor(x => x.MonthsAfter)
                .InclusiveBetween(0, Rules.SubmissionGridRules.MaxOffset)
                .WithMessage("Months after must be between 0 and 11.");
            RuleFor(x => x)
                .Must(x => x.MonthsBefore + x.MonthsAfter + 1 <= Rules.SubmissionGridRules.MaxMonths)
                .WithMessage($"The grid can show at most {Rules.SubmissionGridRules.MaxMonths} months.");
        }
    }

    public class SendUnsubmittedReminderDtoValidator : AbstractValidator<SendUnsubmittedReminderDto>
    {
        public SendUnsubmittedReminderDtoValidator()
        {
            RuleFor(x => x.UserId)
                .NotEmpty().WithMessage("Employee is required.")
                .MaximumLength(50);
            RuleFor(x => x.Year)
                .InclusiveBetween(2000, 9999).WithMessage("Invalid Year/Month.");
            RuleFor(x => x.Month)
                .InclusiveBetween(1, 12).WithMessage("Invalid Year/Month.");
        }
    }

    public class TymeReminderSettingsDtoValidator : AbstractValidator<TymeReminderSettingsDto>
    {
        public TymeReminderSettingsDtoValidator()
        {
            RuleFor(x => x.Coverage)
                .Must(c => Rules.UnsubmittedReminderRules.IsCoverage(c))
                .WithMessage("Coverage must be month, range or year.");
            RuleFor(x => x.MonthsBefore)
                .InclusiveBetween(0, Rules.SubmissionGridRules.MaxOffset)
                .WithMessage("Months before must be between 0 and 11.");
            RuleFor(x => x.MonthsAfter)
                .InclusiveBetween(0, Rules.SubmissionGridRules.MaxOffset)
                .WithMessage("Months after must be between 0 and 11.");
            RuleFor(x => x)
                .Must(x => x.MonthsBefore + x.MonthsAfter + 1 <= Rules.SubmissionGridRules.MaxMonths)
                .WithMessage($"The grid can show at most {Rules.SubmissionGridRules.MaxMonths} months.");
            RuleFor(x => x.Statuses).NotNull();
            RuleForEach(x => x.Statuses)
                .Must(s => Rules.SubmissionGridRules.StatusOptions.Any(o => o.Key == s))
                .WithMessage("Unknown status.");
        }
    }

    public class CreateManagerTimeSubmissionDtoValidator: AbstractValidator<CreateManagerTimeSubmissionDto>
    {
        public CreateManagerTimeSubmissionDtoValidator()
        {
            RuleFor(x => x.UserId)
                .NotEmpty().WithMessage("Employee is required.");

            RuleFor(x => x.Month)
                .InclusiveBetween(1, 12).WithMessage("Invalid Year/Month.");

            RuleFor(x => x.Year)
                .InclusiveBetween(2000, 9999).WithMessage("Invalid Year/Month.");
        }
    }
}
