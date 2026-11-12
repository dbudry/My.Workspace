using FluentValidation;
using My.Shared.Dtos.Crm;
using My.Shared.Rules;

namespace My.Shared.Validation;

public class CreatePotentialDtoValidator : AbstractValidator<CreatePotentialDto>
{
    public const int DescriptionMaxLength = 500;

    public CreatePotentialDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Potential name is required.")
            .MaximumLength(200).WithMessage("Potential name can't exceed 200 characters.");

        RuleFor(x => x.Description).MaximumLength(DescriptionMaxLength);

        RuleFor(x => x.CrmStageId)
            .NotEmpty().WithMessage("Stage is required.")
            .MaximumLength(450);

        RuleFor(x => x.Probability)
            .InclusiveBetween(CrmStageRules.ProbabilityMin, CrmStageRules.ProbabilityMax)
            .WithMessage("Probability must be a whole number from 0 to 100.");

        RuleFor(x => x.Amount)
            .GreaterThanOrEqualTo(0).When(x => x.Amount.HasValue)
            .WithMessage("Forecast revenue can't be negative.");

        RuleFor(x => x.OrganizationId).MaximumLength(450);
        RuleFor(x => x.ContactId).MaximumLength(450);
        RuleFor(x => x.RelationshipType).Custom(RelationshipTypeValidation.AddOptionalFailure);
    }
}

public class UpdatePotentialDtoValidator : AbstractValidator<UpdatePotentialDto>
{
    public UpdatePotentialDtoValidator()
    {
        RuleFor(x => x.PotentialId).NotEmpty().WithMessage("Potential id is required.");
        Include(new CreatePotentialDtoValidator());
    }
}

public class SetPotentialArchivedDtoValidator : AbstractValidator<SetPotentialArchivedDto>
{
    public SetPotentialArchivedDtoValidator()
    {
    }
}

public class CreatePotentialStatusDtoValidator : AbstractValidator<CreatePotentialStatusDto>
{
    public CreatePotentialStatusDtoValidator()
    {
        RuleFor(x => x.CrmStageId).NotEmpty().WithMessage("Stage is required.").MaximumLength(450);
        RuleFor(x => x.StatusDate).NotEmpty().WithMessage("Date is required.");
        RuleFor(x => x.Body)
            .NotEmpty().WithMessage("Status is required.")
            .MaximumLength(CreatePotentialDtoValidator.DescriptionMaxLength);
    }
}

public class CreateCrmDiscussionDtoValidator : AbstractValidator<CreateCrmDiscussionDto>
{
    public CreateCrmDiscussionDtoValidator()
    {
        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Discussion is required.")
            .MaximumLength(CreatePotentialDtoValidator.DescriptionMaxLength);
    }
}

public class CreateCrmStageDtoValidator : AbstractValidator<CreateCrmStageDto>
{
    public CreateCrmStageDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Stage name is required.")
            .MaximumLength(CrmStageRules.NameMaxLength);
        RuleFor(x => x.DefaultProbability)
            .InclusiveBetween(CrmStageRules.ProbabilityMin, CrmStageRules.ProbabilityMax)
            .WithMessage("Default percent must be a whole number from 0 to 100.");
        RuleFor(x => x.Outcome)
            .Must(outcome => CrmStageRules.TryCanonicalOutcome(outcome, out _))
            .WithMessage("Outcome must be Open, Closed Won, or Closed Lost.");
    }
}

public class UpdateCrmStageDtoValidator : AbstractValidator<UpdateCrmStageDto>
{
    public UpdateCrmStageDtoValidator()
    {
        RuleFor(x => x.CrmStageId).NotEmpty().WithMessage("Stage id is required.");
        Include(new CreateCrmStageDtoValidator());
    }
}

public class ReorderCrmStagesDtoValidator : AbstractValidator<ReorderCrmStagesDto>
{
    public ReorderCrmStagesDtoValidator()
    {
        RuleFor(x => x.StageIds)
            .NotNull()
            .Must(ids => ids.Count > 0).WithMessage("Stage order is required.");
    }
}

public class SaveCrmRelationshipTypesDtoValidator : AbstractValidator<SaveCrmRelationshipTypesDto>
{
    public SaveCrmRelationshipTypesDtoValidator()
    {
        RuleFor(x => x.Types).NotNull();
    }
}

public class SaveCrmNotifyUsersDtoValidator : AbstractValidator<SaveCrmNotifyUsersDto>
{
    public SaveCrmNotifyUsersDtoValidator()
    {
        RuleFor(x => x.UserIds).NotNull();
        RuleForEach(x => x.UserIds).NotEmpty().MaximumLength(CrmNotifyRecipientRules.UserIdMaxLength);
    }
}

public class UploadCrmAttachmentDtoValidator : AbstractValidator<UploadCrmAttachmentDto>
{
    public UploadCrmAttachmentDtoValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(CrmAttachmentRules.FileNameMaxLength);
        RuleFor(x => x.MimeType).MaximumLength(100);
        RuleFor(x => x.ContentBase64).NotEmpty().WithMessage("File content is required.");
        RuleFor(x => x.CrmStageId).MaximumLength(450);
    }
}

public class MoveCrmAttachmentStageDtoValidator : AbstractValidator<MoveCrmAttachmentStageDto>
{
    public MoveCrmAttachmentStageDtoValidator()
    {
        RuleFor(x => x.CrmStageId).NotEmpty().WithMessage("Stage is required.").MaximumLength(450);
    }
}

internal static class RelationshipTypeValidation
{
    internal static void AddOptionalFailure<T>(string? relationshipType, ValidationContext<T> context)
    {
        if (string.IsNullOrWhiteSpace(relationshipType))
            return;

        if (!context.RootContextData.TryGetValue(
                ValidationContextKeys.AllowedRelationshipTypes,
                out var allowedObj)
            || allowedObj is not IReadOnlyList<CrmRelationshipType> allowed)
        {
            context.AddFailure("Relationship type is not available.");
            return;
        }

        if (!CrmRelationshipTypeRules.IsAllowed(relationshipType, allowed))
            context.AddFailure($"Type must be one of: {string.Join(", ", allowed.Select(type => type.Name))}.");
    }
}
