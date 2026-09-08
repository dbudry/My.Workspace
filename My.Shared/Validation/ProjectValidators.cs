using FluentValidation;
using My.Shared.Dtos.Project;

namespace My.Shared.Validation
{
    public static class ProjectFieldRules
    {
        public const int NameMinLength = 3;
        public const int NameMaxLength = 100;

        public const string NameLengthMessage =
            "Project name must be between 3 and 100 characters.";

        public static void ApplyName<T>(AbstractValidator<T> validator, System.Linq.Expressions.Expression<Func<T, string>> nameSelector)
            where T : class
        {
            validator.RuleFor(nameSelector)
                .NotEmpty().WithMessage("Project name is required.")
                .MinimumLength(NameMinLength).WithMessage(NameLengthMessage)
                .MaximumLength(NameMaxLength).WithMessage(NameLengthMessage);
        }
    }

    public class CreateProjectDtoValidator : AbstractValidator<CreateProjectDto>
    {
        public CreateProjectDtoValidator()
        {
            ProjectFieldRules.ApplyName(this, x => x.Name);

            RuleFor(x => x.DisplayName).MaximumLength(100)
                .WithMessage("Display name can't exceed 100 characters.");

            RuleFor(x => x.Slug)
                .Must(slug => SlugRules.IsValidShape(SlugRules.Normalize(slug)))
                .When(x => !string.IsNullOrWhiteSpace(x.Slug))
                .WithMessage(SlugRules.ShapeErrorMessage);

            RuleFor(x => x.IsBillable)
                .Equal(false)
                .When(x => x.IsSharedAvailability)
                .WithMessage("Availability projects cannot be marked billable.");
        }
    }

    public class UpdateProjectDtoValidator : AbstractValidator<UpdateProjectDto>
    {
        public UpdateProjectDtoValidator()
        {
            RuleFor(x => x.ProjectId).NotEmpty().WithMessage("Project id is required.");

            ProjectFieldRules.ApplyName(this, x => x.Name);

            RuleFor(x => x.DisplayName).MaximumLength(100)
                .WithMessage("Display name can't exceed 100 characters.");

            RuleFor(x => x.Slug)
                .Must(slug => SlugRules.IsValidShape(SlugRules.Normalize(slug)))
                .When(x => !string.IsNullOrWhiteSpace(x.Slug))
                .WithMessage(SlugRules.ShapeErrorMessage);

            RuleFor(x => x.IsBillable)
                .Equal(false)
                .When(x => x.IsSharedAvailability)
                .WithMessage("Availability projects cannot be marked billable.");
        }
    }
}