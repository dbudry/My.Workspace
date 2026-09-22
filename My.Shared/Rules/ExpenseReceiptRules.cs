namespace My.Shared.Rules;

public static class ExpenseReceiptRules
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public const int FileNameMaxLength = 200;
    public const int MaxFilesPerLine = 10;

    public static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".heic"
    };

    public static bool TryValidate(string? fileName, int sizeBytes, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            error = "File name is required.";
            return false;
        }

        var name = fileName.Trim();
        if (name.Length > FileNameMaxLength)
        {
            error = "File name is too long.";
            return false;
        }

        var ext = Path.GetExtension(name);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
        {
            error = "Use a PDF or image (png, jpg, webp, heic).";
            return false;
        }

        if (sizeBytes <= 0)
        {
            error = "That file is empty.";
            return false;
        }

        if (sizeBytes > MaxBytes)
        {
            error = "Receipts can be at most 10 MB.";
            return false;
        }

        return true;
    }

    public static bool TryValidateCount(int existingCount, int addingCount, out string? error)
    {
        error = null;
        if (addingCount <= 0)
        {
            error = "Choose at least one file.";
            return false;
        }

        if (existingCount + addingCount > MaxFilesPerLine)
        {
            error = existingCount >= MaxFilesPerLine
                ? $"A line can have at most {MaxFilesPerLine} receipts."
                : $"A line can have at most {MaxFilesPerLine} receipts. This line already has {existingCount}.";
            return false;
        }

        return true;
    }

    public static bool IsHeic(string? fileName, string? mimeType = null)
    {
        if (string.Equals(mimeType, "image/heic", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mimeType, "image/heif", StringComparison.OrdinalIgnoreCase))
            return true;
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        return ext is ".heic" or ".heif";
    }
}
