using FluentValidation;
using My.Shared.Dtos.ProjectGroup;

namespace My.Shared.Validation
{
    public class CreateProjectGroupDtoValidator : AbstractValidator<CreateProjectGroupDto>
    {
        public CreateProjectGroupDtoValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Project group name is required.")
                .MinimumLength(2).WithMessage("Name must be between 2 and 100 characters.")
                .MaximumLength(100).WithMessage("Name must be between 2 and 100 characters.");

            RuleFor(x => x.Color).NotEmpty().WithMessage("Color is required.");
        }
    }

    public class UpdateProjectGroupDtoValidator : AbstractValidator<UpdateProjectGroupDto>
    {
        public UpdateProjectGroupDtoValidator()
        {
            RuleFor(x => x.ProjectGroupId).NotEmpty().WithMessage("Project group id is required.");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Project group name is required.")
                .MinimumLength(2).WithMessage("Name must be between 2 and 100 characters.")
                .MaximumLength(100).WithMessage("Name must be between 2 and 100 characters.");

            RuleFor(x => x.Color).NotEmpty().WithMessage("Color is required.");
        }
    }
}