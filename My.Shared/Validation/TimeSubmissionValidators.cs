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

            RuleFor(x => x)
                .Must(dto =>
                {
                    var nowUtc = DateTime.UtcNow;
                    var currentMonthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    var requested = new DateTime(dto.Year, dto.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    return requested < currentMonthStart;
                })
                .WithMessage("Cannot submit the current or a future month — only completed months.");
        }
    }
}