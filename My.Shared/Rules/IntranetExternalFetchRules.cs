using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace My.Shared.Rules;

public static class IntranetExternalFetchRules
{
    public const int MaxUrlLength = 2048;

    private static readonly Regex GoogleWidthSuffixRegex = new(@"=w\d+(?=$|[?#])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    public static bool TryValidateFetchUrl(string? url, out Uri? normalized, out string? error)
    {
        normalized = null;
        error = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            error = "Image URL is required.";
            return false;
        }

        var trimmed = url.Trim();
        if (trimmed.Length > MaxUrlLength)
        {
            error = "Image URL is too long.";
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            error = "Only http and https image URLs can be fetched.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "Image URLs with credentials are not allowed.";
            return false;
        }

        if (IsBlockedHost(uri.Host))
        {
            error = "This image URL points to a blocked host.";
            return false;
        }

        if (IPAddress.TryParse(uri.Host, out var ip) && IsBlockedIp(ip))
        {
            error = "This image URL points to a blocked address.";
            return false;
        }

        normalized = uri;
        return true;
    }

    public static bool LooksLikeImageContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return false;

        var mime = contentType.Split(';')[0].Trim().ToLowerInvariant();
        return mime.StartsWith("image/", StringComparison.Ordinal);
    }

    public static bool IsGoogleHostedImageUrl(Uri uri)
    {
        var host = uri.Host.Trim().ToLowerInvariant();
        return host.Contains("googleusercontent.com", StringComparison.Ordinal)
               || host.Contains("ggpht.com", StringComparison.Ordinal)
               || (host.Contains("gstatic.com", StringComparison.Ordinal)
                   && uri.AbsolutePath.Contains("/images", StringComparison.OrdinalIgnoreCase));
    }

    public static bool ShouldSkipClientFetch(Uri uri) => IsGoogleHostedImageUrl(uri);

    /// <summary>
    /// True when the URL can be used as a bare img src after save (no library copy required).
    /// Google CDN and other blocked hosts return false.
    /// </summary>
    public static bool IsDirectlyLinkable(string? url, out string? blockedReason)
    {
        blockedReason = null;
        if (string.IsNullOrWhiteSpace(url))
        {
            blockedReason = "No image URL to link.";
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            blockedReason = "Only http and https image URLs can be linked.";
            return false;
        }

        if (IsGoogleHostedImageUrl(uri))
        {
            blockedReason = "Google blocks direct image links (403). Use Copy to library — the original URL can be kept as reference.";
            return false;
        }

        return true;
    }

    /// <summary>True when the source cannot be used as a bare img src and must be copied to the library.</summary>
    public static bool RequiresLibraryCopy(string? url) =>
        !string.IsNullOrWhiteSpace(url) && !IsDirectlyLinkable(url, out _);

    /// <summary>Info notice when copy is the only viable path but clipboard bytes are available.</summary>
    public static string? GetCopyRequiredNotice(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            return null;

        if (IsGoogleHostedImageUrl(uri))
        {
            return "This source blocks direct linking. The image will be saved to the intranet library (Images folder) and attached to this page. The original URL is kept as reference.";
        }

        return null;
    }

    /// <summary>Message when paste has a blocked URL and no local image bytes.</summary>
    public static string GetBlockedUrlWithoutBytesMessage(string? url)
    {
        if (IsDirectlyLinkable(url, out _))
            return "Could not download this image. Save the image file and use Copy to library, or try again later.";

        if (!string.IsNullOrWhiteSpace(url)
            && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            && IsGoogleHostedImageUrl(uri))
        {
            return "Google Sites images need image data to display. Drag the image into the editor or paste the image file, then use Copy to library.";
        }

        return "Could not download this image for upload. Drag the image into the editor or paste the image file, then use Copy to library.";
    }

    public static string? GetGoogleFetchBlockedHint(Uri uri) =>
        IsGoogleHostedImageUrl(uri)
            ? "Google blocks server downloads of this image. Drag it into the editor or paste the image file, then use Copy to library."
            : null;

    public static IEnumerable<Uri> GetFetchUrlCandidates(Uri uri, long maxBytesHint)
    {
        if (!IsGoogleHostedImageUrl(uri))
        {
            yield return uri;
            yield break;
        }

        var url = uri.ToString();
        if (!GoogleWidthSuffixRegex.IsMatch(url))
        {
            yield return uri;
            yield break;
        }

        var widths = maxBytesHint <= 1_000_000
            ? new[] { 400, 600, 800, 1280 }
            : new[] { 1280, 800, 600, 400 };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var width in widths)
        {
            var candidateUrl = GoogleWidthSuffixRegex.Replace(url, $"=w{width}");
            if (!seen.Add(candidateUrl))
                continue;

            if (Uri.TryCreate(candidateUrl, UriKind.Absolute, out var candidate))
                yield return candidate;
        }
    }

    public static void ApplyFetchRequestHeaders(HttpRequestMessage request, Uri uri)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");

        if (IsGoogleHostedImageUrl(uri))
            request.Headers.TryAddWithoutValidation("Referer", "https://sites.google.com/");
    }

    public static bool TryDetectImageMimeFromBytes(ReadOnlySpan<byte> bytes, out string mime)
    {
        mime = string.Empty;
        if (bytes.Length < 3)
            return false;

        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            mime = "image/jpeg";
            return true;
        }

        if (bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            mime = "image/png";
            return true;
        }

        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
        {
            mime = "image/gif";
            return true;
        }

        if (bytes.Length >= 12
            && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            mime = "image/webp";
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            mime = "image/bmp";
            return true;
        }

        return false;
    }

    public static bool LooksLikeImageResponse(string? contentType, ReadOnlySpan<byte> bytes)
    {
        if (LooksLikeImageContentType(contentType))
            return true;

        return TryDetectImageMimeFromBytes(bytes, out _);
    }

    public static string? InferFileNameFromUrl(Uri uri, string? contentType)
    {
        var path = uri.AbsolutePath;
        var fileName = string.IsNullOrWhiteSpace(path) ? null : Path.GetFileName(path);
        if (!string.IsNullOrWhiteSpace(fileName) && fileName.Contains('.', StringComparison.Ordinal))
            return fileName;

        var ext = contentType switch
        {
            null or "" => "jpg",
            var ct when ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase) =>
                ct["image/".Length..].Split(';')[0].Trim().TrimStart('.').ToLowerInvariant() switch
                {
                    "jpeg" => "jpg",
                    "svg+xml" => "svg",
                    var e when !string.IsNullOrEmpty(e) => e,
                    _ => "jpg"
                },
            _ => "jpg"
        };

        return $"external-image.{ext}";
    }

    private static bool IsBlockedHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return true;

        var lower = host.Trim().TrimEnd('.').ToLowerInvariant();
        return lower is "localhost"
               or "127.0.0.1"
               or "0.0.0.0"
               or "::1"
               or "[::1]"
               || lower.EndsWith(".localhost", StringComparison.Ordinal)
               || lower.EndsWith(".local", StringComparison.Ordinal);
    }

    private static bool IsBlockedIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return bytes[0] switch
            {
                10 => true,
                127 => true,
                169 when bytes[1] == 254 => true,
                172 when bytes[1] is >= 16 and <= 31 => true,
                192 when bytes[1] == 168 => true,
                _ => false
            };
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                return true;

            var bytes = ip.GetAddressBytes();
            // Unique local (fc00::/7)
            return (bytes[0] & 0xFE) == 0xFC;
        }

        return false;
    }
}