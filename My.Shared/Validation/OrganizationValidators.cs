using FluentValidation;
using My.Shared.Dtos.Organization;

namespace My.Shared.Validation
{
    public static class OrganizationFieldRules
    {
        public static void Apply<T>(AbstractValidator<T> validator, System.Linq.Expressions.Expression<Func<T, string>> nameSelector)
            where T : class
        {
            validator.RuleFor(nameSelector)
                .NotEmpty().WithMessage("Organization name is required.")
                .MinimumLength(2).WithMessage("Organization name must be between 2 and 100 characters.")
                .MaximumLength(100).WithMessage("Organization name must be between 2 and 100 characters.");
        }
    }

    public class CreateOrganizationDtoValidator : AbstractValidator<CreateOrganizationDto>
    {
        public CreateOrganizationDtoValidator()
        {
            OrganizationFieldRules.Apply(this, x => x.Name);
            RuleFor(x => x.Address).MaximumLength(255);
            RuleFor(x => x.City).MaximumLength(50);
            RuleFor(x => x.State).MaximumLength(50);
            RuleFor(x => x.PostalCode).MaximumLength(20);
            RuleFor(x => x.Country).MaximumLength(50);
            RuleFor(x => x.Color).MaximumLength(9);
        }
    }

    public class UpdateOrganizationDtoValidator : AbstractValidator<UpdateOrganizationDto>
    {
        public UpdateOrganizationDtoValidator()
        {
            RuleFor(x => x.OrganizationId).NotEmpty().WithMessage("Organization id is required.");
            OrganizationFieldRules.Apply(this, x => x.Name);
            RuleFor(x => x.Address).MaximumLength(255);
            RuleFor(x => x.City).MaximumLength(50);
            RuleFor(x => x.State).MaximumLength(50);
            RuleFor(x => x.PostalCode).MaximumLength(20);
            RuleFor(x => x.Country).MaximumLength(50);
            RuleFor(x => x.Color).MaximumLength(9);
        }
    }
}