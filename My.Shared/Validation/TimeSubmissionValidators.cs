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
}