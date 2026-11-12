namespace My.Shared.Rules;

public static class CrmAttachmentRules
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public const int FileNameMaxLength = 200;

    public static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".gif", ".heic",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv"
    };

    public static bool TryValidate(string? fileName, int sizeBytes, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            error = "File name is required.";
            return false;
        }

        var name = Path.GetFileName(fileName.Trim());
        if (name.Length == 0 || name.Length > FileNameMaxLength)
        {
            error = "File name is too long.";
            return false;
        }

        var ext = Path.GetExtension(name);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
        {
            error = "Use a PDF, image, text, or Office file.";
            return false;
        }

        if (sizeBytes <= 0)
        {
            error = "That file is empty.";
            return false;
        }

        if (sizeBytes > MaxBytes)
        {
            error = "Attachments can be at most 10 MB.";
            return false;
        }

        return true;
    }

    public static string SafeFileName(string? fileName)
    {
        var name = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "attachment" : fileName.Trim());
        return name.Length <= FileNameMaxLength ? name : name[..FileNameMaxLength];
    }
}
