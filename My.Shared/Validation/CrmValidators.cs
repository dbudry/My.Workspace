using FluentValidation;
using My.Shared.Dtos.Crm;
using My.Shared.Rules;

namespace My.Shared.Validation;

public class CreateOpportunityDtoValidator : AbstractValidator<CreateOpportunityDto>
{
    public const int NoteMaxLength = 500;

    public CreateOpportunityDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Opportunity name is required.")
            .MaximumLength(200).WithMessage("Opportunity name can't exceed 200 characters.");

        RuleFor(x => x.Stage)
            .Must(stage => CrmStageRules.TryCanonical(stage, out _))
            .WithMessage("Stage must be Lead, Qualified, Proposal, Negotiation, Won, or Lost.");

        RuleFor(x => x.Amount)
            .GreaterThanOrEqualTo(0).When(x => x.Amount.HasValue)
            .WithMessage("Amount can't be negative.");

        RuleFor(x => x.Note).MaximumLength(NoteMaxLength);
        RuleFor(x => x.OrganizationId).MaximumLength(450);
        RuleFor(x => x.ContactId).MaximumLength(450);
    }
}

public class UpdateOpportunityDtoValidator : AbstractValidator<UpdateOpportunityDto>
{
    public UpdateOpportunityDtoValidator()
    {
        RuleFor(x => x.OpportunityId).NotEmpty().WithMessage("Opportunity id is required.");
        Include(new CreateOpportunityDtoValidator());
    }
}

public class SetOpportunityArchivedDtoValidator : AbstractValidator<SetOpportunityArchivedDto>
{
    public SetOpportunityArchivedDtoValidator()
    {
        RuleFor(x => x).NotNull();
    }
}

public class CreateCrmActivityDtoValidator : AbstractValidator<CreateCrmActivityDto>
{
    public CreateCrmActivityDtoValidator()
    {
        RuleFor(x => x.OpportunityId).NotEmpty().WithMessage("Opportunity id is required.");

        RuleFor(x => x.ActivityType)
            .Must(kind => CrmActivityTypeRules.TryCanonical(kind, out _))
            .WithMessage("Activity type must be Note, Call, Meeting, or Task.");

        RuleFor(x => x.Subject)
            .NotEmpty().WithMessage("Subject is required.")
            .MaximumLength(200).WithMessage("Subject can't exceed 200 characters.");

        RuleFor(x => x.Body).MaximumLength(CreateOpportunityDtoValidator.NoteMaxLength);
    }
}

public class UpdateCrmActivityDtoValidator : AbstractValidator<UpdateCrmActivityDto>
{
    public UpdateCrmActivityDtoValidator()
    {
        RuleFor(x => x.CrmActivityId).NotEmpty().WithMessage("Activity id is required.");

        RuleFor(x => x.ActivityType)
            .Must(kind => CrmActivityTypeRules.TryCanonical(kind, out _))
            .WithMessage("Activity type must be Note, Call, Meeting, or Task.");

        RuleFor(x => x.Subject)
            .NotEmpty().WithMessage("Subject is required.")
            .MaximumLength(200).WithMessage("Subject can't exceed 200 characters.");

        RuleFor(x => x.Body).MaximumLength(CreateOpportunityDtoValidator.NoteMaxLength);
    }
}
