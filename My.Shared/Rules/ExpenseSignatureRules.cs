namespace My.Shared.Rules;

public static class ExpenseSignatureRules
{
    public const int MaxBytes = 200 * 1024;
    public const int MaxWidthPx = 1200;
    public const int MaxHeightPx = 400;

    public static readonly HashSet<string> AllowedMime = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/jpg",
        "image/webp"
    };

    public static bool TryValidate(string? fileName, string? mimeType, int byteLength, out string? error)
    {
        error = null;
        if (byteLength <= 0)
        {
            error = "Signature image is empty.";
            return false;
        }

        if (byteLength > MaxBytes)
        {
            error = "Signature image must be 200 KB or smaller.";
            return false;
        }

        var mime = string.IsNullOrWhiteSpace(mimeType) ? GuessMime(fileName) : mimeType.Trim();
        if (!AllowedMime.Contains(mime))
        {
            error = "Signature must be a PNG, JPEG, or WebP image.";
            return false;
        }

        return true;
    }

    public static string NormalizeMime(string? mimeType, string? fileName)
    {
        var mime = string.IsNullOrWhiteSpace(mimeType) ? GuessMime(fileName) : mimeType.Trim();
        if (string.Equals(mime, "image/jpg", StringComparison.OrdinalIgnoreCase))
            return "image/jpeg";
        return mime;
    }

    private static string GuessMime(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }
}
