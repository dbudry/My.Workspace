namespace My.Shared.Dtos.Intranet
{
    public class PurgeIntranetDocumentResultDto
    {
        public string DriveFileId { get; set; } = null!;
        public int PagesStripped { get; set; }
        public int AttachmentsRemoved { get; set; }
        public bool DeletedFromDrive { get; set; }
        public List<string> AffectedPageTitles { get; set; } = new();
    }
}