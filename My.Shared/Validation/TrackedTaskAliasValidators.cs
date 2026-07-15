using FluentValidation;
using My.Shared.Dtos.TrackedTaskAlias;

namespace My.Shared.Validation
{
    public class UpsertTrackedTaskAliasDtoValidator : AbstractValidator<UpsertTrackedTaskAliasDto>
    {
        public UpsertTrackedTaskAliasDtoValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Name is required.")
                .MinimumLength(2).WithMessage("Name must be at least 2 characters.")
                .MaximumLength(50).WithMessage("Name cannot exceed 50 characters.");
        }
    }
}