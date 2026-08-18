using FluentValidation;
using My.Shared.Dtos.Contact;
using My.Shared.Rules;

namespace My.Shared.Validation
{
    public class CreateContactDtoValidator : AbstractValidator<CreateContactDto>
    {
        public CreateContactDtoValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Contact name is required.")
                .MaximumLength(100);

            RuleFor(x => x.Title).MaximumLength(100);
            RuleFor(x => x.PhoneNumber).MaximumLength(30);
            RuleFor(x => x.Email)
                .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
                .MaximumLength(256);

            RuleFor(x => x.ContactType).Custom(ContactTypeValidation.AddFailures);
        }
    }

    public class UpdateContactDtoValidator : AbstractValidator<UpdateContactDto>
    {
        public UpdateContactDtoValidator()
        {
            RuleFor(x => x.ContactId).NotEmpty().WithMessage("Contact id is required.");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Contact name is required.")
                .MaximumLength(100);

            RuleFor(x => x.Title).MaximumLength(100);
            RuleFor(x => x.PhoneNumber).MaximumLength(30);
            RuleFor(x => x.Email)
                .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
                .MaximumLength(256);

            RuleFor(x => x.ContactType).Custom(ContactTypeValidation.AddFailures);
        }
    }

    internal static class ContactTypeValidation
    {
        internal static void AddFailures<T>(string? contactType, ValidationContext<T> context)
        {
            var required = context.RootContextData.TryGetValue(
                ValidationContextKeys.ContactTypeRequired, out var reqObj)
                && reqObj is true;

            if (string.IsNullOrWhiteSpace(contactType))
            {
                if (required)
                    context.AddFailure("Contact type is required.");
                return;
            }

            if (!context.RootContextData.TryGetValue(
                    ValidationContextKeys.AllowedContactTypes,
                    out var allowedObj)
                || allowedObj is not IReadOnlyList<string> allowed)
            {
                context.AddFailure("Contact type is required.");
                return;
            }

            if (!ContactTypeRules.IsAllowed(contactType, allowed))
                context.AddFailure($"Contact type must be one of: {string.Join(", ", allowed)}.");
        }
    }
}