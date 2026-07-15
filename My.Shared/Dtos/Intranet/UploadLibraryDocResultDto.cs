namespace My.Shared.Dtos.Intranet
{
    public class UploadLibraryDocResultDto
    {
        public IntranetDocumentDto Document { get; set; } = null!;
        public IntranetPageDocumentDto? PageAttachment { get; set; }
    }
}