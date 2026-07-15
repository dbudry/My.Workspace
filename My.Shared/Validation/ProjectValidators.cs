using FluentValidation;
using My.Shared.Dtos.Project;

namespace My.Shared.Validation
{
    public class CreateProjectDtoValidator : AbstractValidator<CreateProjectDto>
    {
        public CreateProjectDtoValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Project name is required.")
                .MinimumLength(3).WithMessage("Name can not have less then 3 characters and more then 50.")
                .MaximumLength(50).WithMessage("Name can not have less then 3 characters and more then 50.");

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

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Project name is required.")
                .MinimumLength(3).WithMessage("Name can not have less then 3 characters and more then 50.")
                .MaximumLength(50).WithMessage("Name can not have less then 3 characters and more then 50.");

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