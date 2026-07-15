namespace My.Shared.Dtos.Intranet
{
    public record CreateGoogleDocRequest(string Name, string? Kind, string? MimeType, string? Caption);

    public record UploadDocRequest(string FileName, string MimeType, string ContentBase64, string? Caption);

    public record AttachExistingRequest(
        string DriveFileId,
        string? Caption,
        string? Name = null,
        string? MimeType = null,
        string? WebViewLink = null,
        string? ThumbnailLink = null,
        long? SizeBytes = null,
        DateTime? DriveLastModified = null);

    public record UploadLibraryDocRequest(
        string FileName,
        string MimeType,
        string ContentBase64,
        string? Category,
        string? Description,
        string? PageId,
        string? Caption);

    public record ReorderRequest(string[] OrderedIds);

    public record MovePageRequest(string PageId, string? NewParentPageId, int? NewSortOrder);

    public record FetchExternalImageRequest(string Url);
}