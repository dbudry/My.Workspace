using System.Text.Json;
using ConstantsClass = My.Shared.Constants.Constants;
using My.Shared.Dtos.Intranet;

namespace My.Shared.Rules;

/// <summary>
/// Allowed file types and per-extension max upload sizes for intranet editor uploads (images, documents, etc.).
/// Configured as JSON in App Settings (extension + max MB per type).
/// </summary>
public sealed class IntranetMediaPolicy
{
    public IReadOnlyList<string> AllowedExtensions { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, long> MaxUploadBytesByExtension { get; init; }
        = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

    public bool IsConfigured =>
        AllowedExtensions.Count > 0
        && AllowedExtensions.All(ext =>
            MaxUploadBytesByExtension.TryGetValue(ext, out var bytes) && bytes > 0);
}

public static class IntranetMediaPolicyRules
{
    public const long AbsoluteMinUploadBytes = 1024;
    public const long AbsoluteMaxUploadBytes = 52_428_800; // 50 MB
    public const int AbsoluteMinUploadMegabytes = 1;
    public const int AbsoluteMaxUploadMegabytes = 50;
    public const int AbsoluteMaxBase64Length = 69_905_076; // base64 for AbsoluteMaxUploadBytes

    /// <summary>
    /// Executables and script installers — always blocked regardless of App Settings.
    /// Document/media types (html, svg, pdf, etc.) are controlled by admin extension:MB pairs.
    /// </summary>
    private static readonly HashSet<string> DeniedUploadExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "exe", "bat", "cmd", "com", "scr", "pif", "msi", "dll",
        "vbs", "vbe", "js", "jse", "ws", "wsf", "wsh", "ps1", "ps2",
        "sh", "bash"
    };

    private static readonly JsonSerializerOptions UploadLimitsJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static IntranetMediaPolicy Parse(IEnumerable<(string Key, string? Value)>? settings)
    {
        string? maxByExtensionRaw = null;

        if (settings != null)
        {
            foreach (var (key, value) in settings)
            {
                if (string.Equals(key, ConstantsClass.SettingKeys.IntranetImageMaxMegabytesByExtension, StringComparison.Ordinal))
                    maxByExtensionRaw = value;
            }
        }

        var maxUploadBytesByExtension = ParseMaxMegabytesByExtension(maxByExtensionRaw);

        var allowedExtensions = maxUploadBytesByExtension.Keys
            .OrderBy(static e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new IntranetMediaPolicy
        {
            AllowedExtensions = allowedExtensions,
            MaxUploadBytesByExtension = maxUploadBytesByExtension
        };
    }

    public static List<IntranetUploadLimitDto> ParseUploadLimits(string? raw)
    {
        var bytesByExtension = ParseMaxMegabytesByExtension(raw);
        return bytesByExtension
            .OrderBy(static kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static kv => new IntranetUploadLimitDto
            {
                Extension = kv.Key,
                MaxMegabytes = BytesToMegabytes(kv.Value)
            })
            .ToList();
    }

    public static string SerializeUploadLimits(IEnumerable<IntranetUploadLimitDto>? entries)
    {
        var normalized = NormalizeUploadLimitEntries(entries);
        if (normalized.Count == 0)
            return string.Empty;

        return JsonSerializer.Serialize(normalized, UploadLimitsJsonOptions);
    }

    public static string SerializeMaxMegabytesByExtension(string? raw) =>
        SerializeUploadLimits(ParseUploadLimits(raw));

    public static string? ValidateAdminUploadLimits(IReadOnlyList<IntranetUploadLimitDto>? entries)
    {
        if (entries == null || entries.Count == 0)
            return null;

        var normalized = NormalizeUploadLimitEntries(entries);
        if (normalized.Count == 0)
            return "Add at least one file type with a max upload size (1–50 MB).";

        var denied = normalized
            .Select(static e => e.Extension)
            .Where(IsDeniedExtension)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (denied.Count > 0)
            return $"These file types cannot be uploaded: {string.Join(", ", denied.Select(static e => "." + e))}.";

        return null;
    }

    public static string? ValidateAdminSettings(string? extensionMegabytePairsRaw)
    {
        if (string.IsNullOrWhiteSpace(extensionMegabytePairsRaw))
            return null;

        var trimmed = extensionMegabytePairsRaw.Trim();
        if (trimmed.StartsWith('['))
            return ValidateAdminUploadLimits(ParseUploadLimits(trimmed));

        if (int.TryParse(trimmed, out _))
            return "Enter extension and size together, e.g. jpg:1 — not just the number.";

        var denied = ExtractDeniedExtensionsFromRaw(extensionMegabytePairsRaw);
        if (denied.Count > 0)
            return $"These file types cannot be uploaded: {string.Join(", ", denied.Select(static e => "." + e))}.";

        var limits = ParseMaxMegabytesByExtension(extensionMegabytePairsRaw);
        if (limits.Count == 0)
            return "Could not read any extension:MB pairs. Use format png:5,jpg:10 or jpg 1 (1–50 MB per type).";

        return null;
    }

    public static bool IsDeniedExtension(string? extension)
    {
        var normalized = NormalizeExtension(extension);
        return !string.IsNullOrEmpty(normalized) && DeniedUploadExtensions.Contains(normalized);
    }

    public static bool TryValidateUpload(
        string? fileName,
        string? mimeType,
        long byteLength,
        IntranetMediaPolicy policy,
        out string? error)
        => TryValidateUpload(fileName, mimeType, byteLength, policy, out error, ReadOnlySpan<byte>.Empty);

    public static bool TryValidateUpload(
        string? fileName,
        string? mimeType,
        long byteLength,
        IntranetMediaPolicy policy,
        out string? error,
        ReadOnlySpan<byte> content)
    {
        error = null;
        if (!policy.IsConfigured)
        {
            error = NotConfiguredMessage;
            return false;
        }

        var ext = ResolveUploadExtension(fileName, mimeType);
        if (!string.IsNullOrEmpty(ext) && IsDeniedExtension(ext))
        {
            error = $"File type .{ext} is not allowed for security reasons.";
            return false;
        }

        if (!IsExtensionAllowed(fileName, policy))
        {
            error = $"File type is not allowed. Allowed extensions: {FormatAllowedExtensions(policy)}.";
            return false;
        }

        if (byteLength <= 0)
        {
            error = "File is empty.";
            return false;
        }

        if (string.IsNullOrEmpty(ext))
        {
            error = "Could not determine the file type. Use a file name with an extension, e.g. report.pdf.";
            return false;
        }

        if (!policy.MaxUploadBytesByExtension.TryGetValue(ext, out var maxBytes) || maxBytes <= 0)
        {
            error = $"No upload size limit is configured for .{ext}.";
            return false;
        }

        if (byteLength > maxBytes)
        {
            error = $"File exceeds the maximum allowed size for .{ext} ({FormatBytes(maxBytes)}).";
            return false;
        }

        if (content.Length > 0 && IsImageExtension(ext)
            && !TryValidateImageContent(content, ext, out error))
        {
            return false;
        }

        return true;
    }

    public static bool TryValidateLink(string? urlOrFileName, IntranetMediaPolicy policy, out string? error)
    {
        error = null;
        if (policy.AllowedExtensions.Count == 0)
        {
            error = NotConfiguredMessage;
            return false;
        }

        if (IsExtensionAllowed(urlOrFileName, policy))
            return true;

        if (LooksLikeKnownImageCdn(urlOrFileName))
        {
            if (PolicyAllowsAnyImageExtension(policy))
                return true;

            error = $"External image links are not allowed. Allowed extensions: {FormatAllowedExtensions(policy)}.";
            return false;
        }

        error = $"File type is not allowed. Allowed extensions: {FormatAllowedExtensions(policy)}.";
        return false;
    }

    public static string NotConfiguredMessage =>
        "Intranet editor file settings are not configured. Ask an admin to set extension:MB limits in App Settings → Intranet.";

    public static string FormatAllowedExtensions(IntranetMediaPolicy policy) =>
        policy.AllowedExtensions.Count == 0
            ? "(none)"
            : string.Join(", ", policy.AllowedExtensions.Select(static e => "." + e));

    public static string FormatMaxUploadSizes(IntranetMediaPolicy policy)
    {
        if (policy.AllowedExtensions.Count == 0)
            return "—";

        var parts = policy.AllowedExtensions
            .Select(ext =>
            {
                if (policy.MaxUploadBytesByExtension.TryGetValue(ext, out var bytes) && bytes > 0)
                    return $".{ext} {FormatBytes(bytes)}";
                return $".{ext} —";
            })
            .ToList();

        return parts.Count == 0 ? "—" : string.Join(", ", parts);
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double size = bytes;
        string[] units = { "KB", "MB", "GB" };
        var unit = 0;
        size /= 1024;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.#} {units[unit]}";
    }

    public static Dictionary<string, long> ParseMaxMegabytesByExtension(string? raw)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('['))
        {
            try
            {
                var entries = JsonSerializer.Deserialize<List<IntranetUploadLimitDto>>(trimmed, UploadLimitsJsonOptions);
                foreach (var entry in NormalizeUploadLimitEntries(entries))
                {
                    if (IsDeniedExtension(entry.Extension))
                        continue;

                    result[entry.Extension] = entry.MaxMegabytes * 1024L * 1024L;
                }

                return result;
            }
            catch
            {
                return result;
            }
        }

        foreach (var segment in trimmed.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryParseExtensionMegabyteSegment(segment, out var ext, out var megabytes)
                || IsDeniedExtension(ext))
                continue;

            result[ext] = megabytes * 1024L * 1024L;
        }

        return result;
    }

    private static List<IntranetUploadLimitDto> NormalizeUploadLimitEntries(IEnumerable<IntranetUploadLimitDto>? entries)
    {
        if (entries == null)
            return new List<IntranetUploadLimitDto>();

        var result = new List<IntranetUploadLimitDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var ext = NormalizeExtension(entry?.Extension);
            if (string.IsNullOrEmpty(ext) || !seen.Add(ext))
                continue;

            var megabytes = Math.Clamp(entry!.MaxMegabytes, AbsoluteMinUploadMegabytes, AbsoluteMaxUploadMegabytes);
            result.Add(new IntranetUploadLimitDto
            {
                Extension = ext,
                MaxMegabytes = megabytes
            });
        }

        return result
            .OrderBy(static e => e.Extension, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryParseExtensionMegabyteSegment(string segment, out string ext, out int megabytes)
    {
        ext = string.Empty;
        megabytes = 0;

        var trimmed = NormalizePolicySegment(segment);
        if (string.IsNullOrEmpty(trimmed))
            return false;

        string? extPart = null;
        string? mbPart = null;

        var colonIndex = trimmed.IndexOf(':');
        var equalsIndex = trimmed.IndexOf('=');
        if (colonIndex > 0)
        {
            extPart = trimmed[..colonIndex];
            mbPart = trimmed[(colonIndex + 1)..];
        }
        else if (equalsIndex > 0)
        {
            extPart = trimmed[..equalsIndex];
            mbPart = trimmed[(equalsIndex + 1)..];
        }
        else
        {
            var lastSpace = trimmed.LastIndexOf(' ');
            if (lastSpace > 0 && lastSpace < trimmed.Length - 1)
            {
                extPart = trimmed[..lastSpace];
                mbPart = trimmed[(lastSpace + 1)..];
            }
        }

        if (string.IsNullOrWhiteSpace(extPart) || string.IsNullOrWhiteSpace(mbPart))
            return false;

        ext = NormalizeExtension(extPart);
        if (string.IsNullOrEmpty(ext))
            return false;

        if (!int.TryParse(mbPart.Trim(), out megabytes))
            return false;

        megabytes = Math.Clamp(megabytes, AbsoluteMinUploadMegabytes, AbsoluteMaxUploadMegabytes);
        return true;
    }

    private static string NormalizePolicySegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return string.Empty;

        return segment
            .Trim()
            .Replace('：', ':')
            .Replace('＝', '=');
    }

    private static int BytesToMegabytes(long bytes) =>
        (int)Math.Clamp(bytes / (1024 * 1024), AbsoluteMinUploadMegabytes, AbsoluteMaxUploadMegabytes);

    private static string NormalizeExtension(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim().TrimStart('.').ToLowerInvariant();
        return trimmed.All(static c => char.IsLetterOrDigit(c)) ? trimmed : string.Empty;
    }

    private static bool IsExtensionAllowed(string? fileNameOrUrl, IntranetMediaPolicy policy)
    {
        var ext = ExtractExtension(fileNameOrUrl);
        return !string.IsNullOrEmpty(ext)
               && policy.AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private static string? ResolveUploadExtension(string? fileName, string? mimeType)
    {
        var ext = ExtractExtension(fileName);
        if (!string.IsNullOrEmpty(ext))
            return ext;

        if (string.IsNullOrWhiteSpace(mimeType))
            return null;

        return MimeToExtension(mimeType.Trim().ToLowerInvariant());
    }

    private static string? MimeToExtension(string mime) => mime switch
    {
        "image/jpeg" => "jpg",
        "image/png" => "png",
        "image/gif" => "gif",
        "image/webp" => "webp",
        "image/bmp" => "bmp",
        "image/svg+xml" => "svg",
        "application/pdf" => "pdf",
        "application/msword" => "doc",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "docx",
        "application/vnd.ms-excel" => "xls",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "xlsx",
        "application/vnd.ms-powerpoint" => "ppt",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation" => "pptx",
        "application/zip" => "zip",
        "application/x-zip-compressed" => "zip",
        "text/plain" => "txt",
        "text/csv" => "csv",
        "application/octet-stream" => null,
        _ when mime.StartsWith("image/", StringComparison.Ordinal) =>
            NormalizeExtension(mime["image/".Length..].TrimEnd('+', 'x')),
        _ => null
    };

    private static string? ExtractExtension(string? fileNameOrUrl)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrUrl))
            return null;

        var value = fileNameOrUrl.Trim();
        try
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
                value = uri.AbsolutePath;
        }
        catch
        {
            // use raw value
        }

        var ext = Path.GetExtension(value);
        if (string.IsNullOrEmpty(ext))
            return null;

        return NormalizeExtension(ext);
    }

    private static bool LooksLikeKnownImageCdn(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var lower = url.ToLowerInvariant();
        return lower.Contains("googleusercontent.com", StringComparison.Ordinal)
               || lower.Contains("ggpht.com", StringComparison.Ordinal)
               || lower.Contains("duckduckgo.com", StringComparison.Ordinal)
               || (lower.Contains("gstatic.com", StringComparison.Ordinal)
                   && lower.Contains("/images", StringComparison.Ordinal));
    }

    private static bool PolicyAllowsAnyImageExtension(IntranetMediaPolicy policy) =>
        policy.AllowedExtensions.Any(IsImageExtension);

    private static List<string> ExtractDeniedExtensionsFromRaw(string? raw)
    {
        var denied = new List<string>();
        if (string.IsNullOrWhiteSpace(raw))
            return denied;

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('['))
        {
            try
            {
                var entries = JsonSerializer.Deserialize<List<IntranetUploadLimitDto>>(trimmed, UploadLimitsJsonOptions);
                if (entries != null)
                {
                    foreach (var entry in entries)
                    {
                        var ext = NormalizeExtension(entry?.Extension);
                        if (!string.IsNullOrEmpty(ext) && IsDeniedExtension(ext))
                            denied.Add(ext);
                    }
                }
            }
            catch
            {
                // fall through
            }
        }
        else
        {
            foreach (var segment in trimmed.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (TryParseExtensionMegabyteSegment(segment, out var ext, out _) && IsDeniedExtension(ext))
                    denied.Add(ext);
            }
        }

        return denied
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsImageExtension(string extension) =>
        extension.Equals("jpg", StringComparison.OrdinalIgnoreCase)
        || extension.Equals("jpeg", StringComparison.OrdinalIgnoreCase)
        || extension.Equals("png", StringComparison.OrdinalIgnoreCase)
        || extension.Equals("gif", StringComparison.OrdinalIgnoreCase)
        || extension.Equals("webp", StringComparison.OrdinalIgnoreCase)
        || extension.Equals("bmp", StringComparison.OrdinalIgnoreCase);

    private static bool TryValidateImageContent(ReadOnlySpan<byte> content, string ext, out string? error)
    {
        error = null;
        if (!IntranetExternalFetchRules.TryDetectImageMimeFromBytes(content, out var detectedMime)
            || !ImageMimeMatchesExtension(detectedMime, ext))
        {
            error = "File content does not match the declared image type.";
            return false;
        }

        return true;
    }

    private static bool ImageMimeMatchesExtension(string detectedMime, string ext) =>
        ext.ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => detectedMime == "image/jpeg",
            "png" => detectedMime == "image/png",
            "gif" => detectedMime == "image/gif",
            "webp" => detectedMime == "image/webp",
            "bmp" => detectedMime == "image/bmp",
            _ => false
        };
}