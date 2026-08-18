using FluentValidation;
using My.Shared.Dtos.Intranet;
using My.Shared.Rules;

namespace My.Shared.Validation
{
    public class CreateIntranetPageDtoValidator : AbstractValidator<CreateIntranetPageDto>
    {
        public CreateIntranetPageDtoValidator()
        {
            RuleFor(x => x.Title)
                .NotEmpty().WithMessage("Title is required.")
                .MaximumLength(200);

            RuleFor(x => x.Slug).MaximumLength(200);
        }
    }

    public class UpdateIntranetPageDtoValidator : AbstractValidator<UpdateIntranetPageDto>
    {
        public UpdateIntranetPageDtoValidator()
        {
            RuleFor(x => x.Title)
                .NotEmpty().WithMessage("Title is required.")
                .MaximumLength(200);

            RuleFor(x => x.Slug).MaximumLength(200);
        }
    }

    public class CreateIntranetNavigationItemDtoValidator : AbstractValidator<CreateIntranetNavigationItemDto>
    {
        public CreateIntranetNavigationItemDtoValidator()
        {
            RuleFor(x => x.Title)
                .NotEmpty().WithMessage("Title is required.")
                .MaximumLength(100);

            RuleFor(x => x.Icon).MaximumLength(50);
            RuleFor(x => x.ExternalUrl).MaximumLength(500);
        }
    }

    public class UpdateIntranetNavigationItemDtoValidator : AbstractValidator<UpdateIntranetNavigationItemDto>
    {
        public UpdateIntranetNavigationItemDtoValidator()
        {
            RuleFor(x => x.Title)
                .NotEmpty().WithMessage("Title is required.")
                .MaximumLength(100);

            RuleFor(x => x.Icon).MaximumLength(50);
            RuleFor(x => x.ExternalUrl).MaximumLength(500);
        }
    }

    public class CreateIntranetDocumentDtoValidator : AbstractValidator<CreateIntranetDocumentDto>
    {
        public CreateIntranetDocumentDtoValidator()
        {
            RuleFor(x => x.DriveFileId)
                .NotEmpty().WithMessage("DriveFileId is required.");

            RuleFor(x => x.Name).MaximumLength(500);
            RuleFor(x => x.Category).MaximumLength(100);
        }
    }

    public class UpdateIntranetDocumentDtoValidator : AbstractValidator<UpdateIntranetDocumentDto>
    {
        public UpdateIntranetDocumentDtoValidator()
        {
            RuleFor(x => x.Name).MaximumLength(500).When(x => x.Name != null);
            RuleFor(x => x.Category).MaximumLength(100).When(x => x.Category != null);
        }
    }

    public class CreateGoogleDocRequestValidator : AbstractValidator<CreateGoogleDocRequest>
    {
        public CreateGoogleDocRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Name is required.");
        }
    }

    public class UploadDocRequestValidator : AbstractValidator<UploadDocRequest>
    {
        public UploadDocRequestValidator()
        {
            RuleFor(x => x.FileName)
                .NotEmpty().WithMessage("fileName and contentBase64 are required.");

            RuleFor(x => x.ContentBase64)
                .NotEmpty().WithMessage("fileName and contentBase64 are required.")
                .MaximumLength(IntranetMediaPolicyRules.AbsoluteMaxBase64Length)
                .MustBeValidBase64();
        }
    }

    public class AttachExistingRequestValidator : AbstractValidator<AttachExistingRequest>
    {
        public AttachExistingRequestValidator()
        {
            RuleFor(x => x.DriveFileId)
                .NotEmpty().WithMessage("driveFileId is required.");
        }
    }

    public class FetchExternalImageRequestValidator : AbstractValidator<FetchExternalImageRequest>
    {
        public FetchExternalImageRequestValidator()
        {
            RuleFor(x => x.Url)
                .NotEmpty().WithMessage("url is required.")
                .MaximumLength(IntranetExternalFetchRules.MaxUrlLength);
        }
    }

    public class UploadLibraryDocRequestValidator : AbstractValidator<UploadLibraryDocRequest>
    {
        public UploadLibraryDocRequestValidator()
        {
            RuleFor(x => x.FileName)
                .NotEmpty().WithMessage("fileName and contentBase64 are required.");

            RuleFor(x => x.ContentBase64)
                .NotEmpty().WithMessage("fileName and contentBase64 are required.")
                .MaximumLength(IntranetMediaPolicyRules.AbsoluteMaxBase64Length)
                .MustBeValidBase64();

            RuleFor(x => x.Category).MaximumLength(100).When(x => x.Category != null);
        }
    }

    public class ReorderRequestValidator : AbstractValidator<ReorderRequest>
    {
        public ReorderRequestValidator()
        {
            RuleFor(x => x.OrderedIds)
                .NotNull().WithMessage("orderedIds required.");
        }
    }

    public class ReorderPagesRequestValidator : AbstractValidator<ReorderRequest>
    {
        public ReorderPagesRequestValidator()
        {
            RuleFor(x => x.OrderedIds)
                .NotNull().WithMessage("orderedIds required.")
                .Must(ids => ids!.Length > 0).WithMessage("orderedIds required.");
        }
    }

    public class MovePageRequestValidator : AbstractValidator<MovePageRequest>
    {
        public MovePageRequestValidator()
        {
            RuleFor(x => x.PageId)
                .NotEmpty().WithMessage("pageId required.");
        }
    }
}