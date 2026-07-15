using FluentValidation;
using My.Shared.Dtos.Department;

namespace My.Shared.Validation
{
    public class CreateDepartmentDtoValidator : AbstractValidator<CreateDepartmentDto>
    {
        public CreateDepartmentDtoValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Department name is required.")
                .MinimumLength(2).WithMessage("Department name must be between 2 and 100 characters.")
                .MaximumLength(100).WithMessage("Department name must be between 2 and 100 characters.");

            RuleFor(x => x.OrganizationId).NotEmpty().WithMessage("Organization id is required.");
        }
    }

    public class UpdateDepartmentDtoValidator : AbstractValidator<UpdateDepartmentDto>
    {
        public UpdateDepartmentDtoValidator()
        {
            RuleFor(x => x.DepartmentId).NotEmpty().WithMessage("Department id is required.");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Department name is required.")
                .MinimumLength(2).WithMessage("Department name must be between 2 and 100 characters.")
                .MaximumLength(100).WithMessage("Department name must be between 2 and 100 characters.");

            RuleFor(x => x.OrganizationId).NotEmpty().WithMessage("Organization id is required.");
        }
    }
}