namespace My.Shared;

/// <summary>
/// Resolves the Intranet Drive parent folder and optional Images / Videos / Documents subfolders.
/// </summary>
public sealed class IntranetDriveFolderLayout
{
    public const string ImagesFolderName = "Images";
    public const string VideosFolderName = "Videos";
    public const string DocumentsFolderName = "Documents";

    public string ParentFolderId { get; init; } = "";
    public string? ImagesFolderId { get; init; }
    public string? VideosFolderId { get; init; }
    public string? DocumentsFolderId { get; init; }

    public static IntranetDriveFolderLayout FromParent(
        string parentFolderId,
        IReadOnlyList<(string Id, string Name)> childFolders)
    {
        string? Find(string expected) =>
            childFolders.FirstOrDefault(f =>
                string.Equals(f.Name, expected, StringComparison.OrdinalIgnoreCase)).Id;

        return new IntranetDriveFolderLayout
        {
            ParentFolderId = parentFolderId,
            ImagesFolderId = Find(ImagesFolderName),
            VideosFolderId = Find(VideosFolderName),
            DocumentsFolderId = Find(DocumentsFolderName)
        };
    }

    public string ResolveUploadFolderId(string? mimeType, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(ParentFolderId))
            return "";

        var category = IntranetFileHelper.ClassifyMimeType(mimeType, fileName);

        return category switch
        {
            IntranetFileHelper.FileTypeImage => ImagesFolderId ?? ParentFolderId,
            IntranetFileHelper.FileTypeVideo => VideosFolderId ?? ParentFolderId,
            _ => DocumentsFolderId ?? ParentFolderId
        };
    }

    public IReadOnlyList<string> GetBrowseFolderIds()
    {
        if (string.IsNullOrWhiteSpace(ParentFolderId))
            return Array.Empty<string>();

        var ids = new List<string> { ParentFolderId };
        if (!string.IsNullOrEmpty(ImagesFolderId)) ids.Add(ImagesFolderId);
        if (!string.IsNullOrEmpty(VideosFolderId)) ids.Add(VideosFolderId);
        if (!string.IsNullOrEmpty(DocumentsFolderId)) ids.Add(DocumentsFolderId);
        return ids.Distinct(StringComparer.Ordinal).ToList();
    }
}