using System.Text.RegularExpressions;
using My.Shared.Dtos.Intranet;

namespace My.Shared
{
    /// <summary>
    /// Shared helpers for intranet file browsing, filtering, and WYSIWYG insertion.
    /// </summary>
    public static class IntranetFileHelper
    {
        public const string FileTypeAll = "all";
        public const string FileTypeImage = "image";
        public const string FileTypeDocument = "document";
        public const string FileTypeVideo = "video";
        public const string FileTypeFolder = "folder";

        public const string DriveFolderMimeType = "application/vnd.google-apps.folder";

        public static bool IsDriveFolder(string? mimeType) =>
            string.Equals(mimeType?.Trim(), DriveFolderMimeType, StringComparison.OrdinalIgnoreCase);

        /// <summary>Shown on upload rename fields in the intranet file UI.</summary>
        public const string UploadFileNameHelperText =
            "Lowercase letters, numbers, and dashes between words (e.g. flex-seal-white-8-oz.png). Invalid characters are removed when you leave the field.";

        /// <summary>1×1 transparent GIF — keeps Quill from stripping Drive images before hydration.</summary>
        public const string ImagePlaceholderSrc =
            "data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";

        public const string UploadFileNameValidationError =
            "Use lowercase letters, numbers, and dashes only (e.g. my-photo.png).";

        private static readonly Regex UploadFileNameRegex = new(
            @"^[a-z0-9]+(-[a-z0-9]+)*(\.[a-z0-9]+)?$",
            RegexOptions.Compiled);

        private static readonly Regex GuidSuffixRegex = new(
            @"_[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex HexDotSuffixRegex = new(
            @"\.[0-9a-f]{16,}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Builds a page-scoped image file name: {page-prefix}-{hint}-{sequence}.{ext}.
        /// Uses slug, then title, for the prefix; derives hint from file name, alt text, or URL path.
        /// </summary>
        public static string SuggestPageImageFileName(
            string? pageSlug,
            string? pageTitle,
            string? sourceFileName,
            string? altText,
            string? sourceUrl,
            IEnumerable<string>? existingFileNames = null)
        {
            var prefix = NormalizePageNamePrefix(pageSlug, pageTitle);
            var hint = DeriveImageNameHint(sourceFileName, altText, sourceUrl);
            var ext = ResolveImageExtension(sourceFileName, sourceUrl);

            var taken = new HashSet<string>(
                (existingFileNames ?? Array.Empty<string>())
                    .Select(NormalizeUploadFileName)
                    .Where(static s => !string.IsNullOrEmpty(s)),
                StringComparer.OrdinalIgnoreCase);

            for (var sequence = 1; sequence < 10_000; sequence++)
            {
                var candidate = NormalizeUploadFileName($"{prefix}-{hint}-{sequence}{ext}");
                if (!taken.Contains(candidate))
                    return candidate;
            }

            return NormalizeUploadFileName($"{prefix}-{hint}-{Guid.NewGuid():N}{ext}");
        }

        private static string NormalizePageNamePrefix(string? pageSlug, string? pageTitle)
        {
            var raw = !string.IsNullOrWhiteSpace(pageSlug)
                ? pageSlug.Trim()
                : !string.IsNullOrWhiteSpace(pageTitle)
                    ? pageTitle.Trim()
                    : "page";

            var normalized = NormalizeUploadFileName(raw);
            if (string.IsNullOrEmpty(normalized))
                return "page";

            var ext = Path.GetExtension(normalized);
            if (!string.IsNullOrEmpty(ext) && normalized.Length > ext.Length)
                return normalized[..^ext.Length];

            return normalized;
        }

        private static string DeriveImageNameHint(string? sourceFileName, string? altText, string? sourceUrl)
        {
            if (!string.IsNullOrWhiteSpace(sourceFileName))
            {
                var baseName = Path.GetFileNameWithoutExtension(sourceFileName);
                baseName = StripTrailingUploadJunk(baseName);
                var fromFile = NormalizeUploadFileName(baseName);
                if (IsUsableImageHint(fromFile))
                    return fromFile;
            }

            if (!string.IsNullOrWhiteSpace(sourceUrl))
            {
                try
                {
                    var uri = new Uri(sourceUrl.Trim());
                    var segment = Path.GetFileNameWithoutExtension(uri.LocalPath);
                    if (!string.IsNullOrWhiteSpace(segment) && segment != "/")
                    {
                        var fromUrl = NormalizeUploadFileName(segment);
                        if (IsUsableImageHint(fromUrl))
                            return fromUrl;
                    }
                }
                catch (UriFormatException)
                {
                    // ignore malformed paste URLs
                }
            }

            if (!string.IsNullOrWhiteSpace(altText))
            {
                var fromAlt = NormalizeUploadFileName(altText.Trim());
                if (IsUsableImageHint(fromAlt))
                    return fromAlt;
            }

            return "image";
        }

        private static bool IsUsableImageHint(string? normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            return normalized is not ("file" or "pasted-image" or "image");
        }

        private static string ResolveImageExtension(string? sourceFileName, string? sourceUrl)
        {
            var ext = Path.GetExtension(sourceFileName ?? string.Empty).ToLowerInvariant();
            if (!string.IsNullOrEmpty(ext) && ext.Length > 1)
                return ext;

            if (!string.IsNullOrWhiteSpace(sourceUrl))
            {
                try
                {
                    ext = Path.GetExtension(new Uri(sourceUrl.Trim()).LocalPath).ToLowerInvariant();
                    if (!string.IsNullOrEmpty(ext) && ext.Length > 1)
                        return ext;
                }
                catch (UriFormatException)
                {
                    // ignore
                }
            }

            return ".png";
        }

        /// <summary>
        /// Builds a friendly default from a browser-provided file name (strips GUID/hash tails, then normalizes).
        /// </summary>
        public static string SuggestUploadFileName(string? originalFileName)
        {
            if (string.IsNullOrWhiteSpace(originalFileName))
                return "file";

            var ext = Path.GetExtension(originalFileName);
            var baseName = Path.GetFileNameWithoutExtension(originalFileName);
            baseName = StripTrailingUploadJunk(baseName);
            return NormalizeUploadFileName(string.IsNullOrEmpty(ext) ? baseName : baseName + ext);
        }

        /// <summary>
        /// Enforces lowercase kebab-case file names with a simple dotted extension.
        /// </summary>
        public static string NormalizeUploadFileName(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return string.Empty;

            var trimmed = fileName.Trim();
            var ext = Path.GetExtension(trimmed);
            var baseName = Path.GetFileNameWithoutExtension(trimmed);

            baseName = baseName.ToLowerInvariant();
            baseName = Regex.Replace(baseName, @"[^a-z0-9]+", "-");
            baseName = Regex.Replace(baseName, @"-+", "-").Trim('-');

            if (string.IsNullOrEmpty(baseName))
                baseName = "file";

            ext = ext.ToLowerInvariant();
            if (!string.IsNullOrEmpty(ext))
            {
                ext = Regex.Replace(ext, @"[^a-z0-9.]", "");
                if (!ext.StartsWith('.'))
                    ext = "." + ext.TrimStart('.');
                if (ext == ".")
                    ext = string.Empty;
            }

            return baseName + ext;
        }

        public static bool IsValidUploadFileName(string? fileName)
        {
            var normalized = NormalizeUploadFileName(fileName);
            return !string.IsNullOrEmpty(normalized) && UploadFileNameRegex.IsMatch(normalized);
        }

        private static string StripTrailingUploadJunk(string baseName)
        {
            var guid = GuidSuffixRegex.Match(baseName);
            if (guid.Success)
                baseName = baseName[..guid.Index];

            var hexDot = HexDotSuffixRegex.Match(baseName);
            if (hexDot.Success)
                baseName = baseName[..hexDot.Index];

            return baseName.TrimEnd('-', '_', '.');
        }

        public static string GetFileTypeLabel(string? mimeType, string? fileName = null)
        {
            var category = ClassifyMimeType(mimeType, fileName);
            return category switch
            {
                FileTypeImage => "Image",
                FileTypeVideo => "Video",
                FileTypeFolder => "Folder",
                _ => "Document"
            };
        }

        /// <summary>
        /// Prefer a concrete image/video MIME from the file name when the browser or Drive reports a generic type.
        /// </summary>
        public static string InferMimeType(string? fileName, string? declaredMimeType)
        {
            var declared = string.IsNullOrWhiteSpace(declaredMimeType)
                ? null
                : declaredMimeType.Trim().ToLowerInvariant();

            if (!string.IsNullOrEmpty(declared)
                && declared != "application/octet-stream"
                && (declared.StartsWith("image/") || declared.StartsWith("video/")))
            {
                return declared;
            }

            var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
            return ext switch
            {
                ".gif" => "image/gif",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                ".svg" => "image/svg+xml",
                ".mp4" => "video/mp4",
                ".mov" => "video/quicktime",
                ".webm" => "video/webm",
                ".avi" => "video/x-msvideo",
                _ => declared ?? "application/octet-stream"
            };
        }

        public static string ClassifyMimeType(string? mimeType, string? fileName = null)
        {
            if (IsDriveFolder(mimeType))
                return FileTypeFolder;

            var m = InferMimeType(fileName, mimeType).Trim().ToLowerInvariant();
            if (m.StartsWith("image/"))
                return FileTypeImage;
            if (m.StartsWith("video/") || m == "application/vnd.google-apps.video")
                return FileTypeVideo;
            return FileTypeDocument;
        }

        public static bool MatchesFileType(string? mimeType, string? fileTypeFilter, string? fileName = null)
        {
            if (string.IsNullOrWhiteSpace(fileTypeFilter) || fileTypeFilter == FileTypeAll)
                return true;
            return ClassifyMimeType(mimeType, fileName) == fileTypeFilter.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Relative API path for authenticated Drive image streaming (hydrated client-side in the editor/page view).
        /// </summary>
        public static string GetDriveMediaApiPath(string driveFileId)
            => $"intranet/documents/drive/{Uri.EscapeDataString(driveFileId)}/media";

        public static string BuildInsertHtml(IntranetDocumentDto doc, bool forceAsLink = false)
            => BuildInsertHtml(doc.Name, doc.MimeType, doc.DriveFileId, doc.WebViewLink, doc.ThumbnailLink, forceAsLink);

        public static string BuildInsertHtml(IntranetPageDocumentDto doc, bool forceAsLink = false)
            => BuildInsertHtml(doc.Name, doc.MimeType, doc.DriveFileId, doc.WebViewLink, doc.ThumbnailLink, forceAsLink);

        public static bool IsDriveFileReferencedInHtml(string? html, string driveFileId)
        {
            if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(driveFileId))
                return false;

            if (html.Contains(driveFileId, StringComparison.Ordinal))
                return true;

            var encodedId = System.Net.WebUtility.HtmlEncode(driveFileId);
            return html.Contains($"data-drive-file-id=\"{encodedId}\"", StringComparison.OrdinalIgnoreCase)
                || html.Contains($"data-drive-file-id='{encodedId}'", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Removes editor-inserted images/links that reference a Drive file id.</summary>
        public static string StripDriveFileReferencesFromHtml(string? html, string driveFileId)
        {
            if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(driveFileId))
                return html ?? string.Empty;

            var result = html;
            var encodedId = System.Net.WebUtility.HtmlEncode(driveFileId);
            var idPattern = $"(?:{Regex.Escape(driveFileId)}|{Regex.Escape(encodedId)})";
            const RegexOptions opts = RegexOptions.IgnoreCase | RegexOptions.Singleline;

            result = Regex.Replace(result,
                $@"<p>\s*<img[^>]*data-drive-file-id=[""']{idPattern}[""'][^>]*>\s*</p>",
                string.Empty, opts);

            result = Regex.Replace(result,
                $@"<img[^>]*data-drive-file-id=[""']{idPattern}[""'][^>]*>",
                string.Empty, opts);

            result = Regex.Replace(result,
                $@"<p>\s*<a[^>]*href=[""'][^""']*{Regex.Escape(driveFileId)}[^""']*[""'][^>]*>.*?</a>\s*</p>",
                string.Empty, opts);

            result = Regex.Replace(result,
                $@"<a[^>]*href=[""'][^""']*{Regex.Escape(driveFileId)}[^""']*[""'][^>]*>.*?</a>",
                string.Empty, opts);

            result = Regex.Replace(result, @"<p>\s*</p>", string.Empty, RegexOptions.IgnoreCase);
            return result.Trim();
        }

        /// <summary>
        /// Updates embedded image alt text and Drive link labels when the library display name changes.
        /// </summary>
        public static string UpdateDriveFileDisplayNameInHtml(string? html, string driveFileId, string newName)
        {
            if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(driveFileId))
                return html ?? string.Empty;

            var safeName = System.Net.WebUtility.HtmlEncode(newName.Trim());
            var encodedId = System.Net.WebUtility.HtmlEncode(driveFileId);
            var idPattern = $"(?:{Regex.Escape(driveFileId)}|{Regex.Escape(encodedId)})";
            const RegexOptions opts = RegexOptions.IgnoreCase | RegexOptions.Singleline;

            var result = Regex.Replace(html,
                $@"(<img\b)([^>]*\bdata-drive-file-id=[""']{idPattern}[""'][^>]*)(>)",
                m => SetImgAltAttribute(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, safeName),
                opts);

            result = Regex.Replace(result,
                $@"(<a\b[^>]*\bhref=[""'][^""']*{Regex.Escape(driveFileId)}[^""']*[""'][^>]*>)(.*?)(</a>)",
                $"$1{safeName}$3",
                opts);

            return result;
        }

        private static string SetImgAltAttribute(string open, string attrs, string close, string safeName)
        {
            if (Regex.IsMatch(attrs, @"\balt=[""']", RegexOptions.IgnoreCase))
            {
                attrs = Regex.Replace(attrs, @"(\balt=[""'])([^""']*)([""'])", $"$1{safeName}$3", RegexOptions.IgnoreCase);
                return open + attrs + close;
            }

            return $"{open}{attrs} alt=\"{safeName}\"{close}";
        }

        /// <summary>
        /// Prepares stored page HTML for editor or read-only view: recover missing Drive ids, then normalize placeholders.
        /// </summary>
        public static string PrepareDriveImageHtmlForView(string? html, IReadOnlyList<IntranetPageDocumentDto>? attachments)
            => NormalizeDriveImageHtmlForStorage(EnrichDriveImageHtmlFromAttachments(html, attachments));

        /// <summary>
        /// Adds data-drive-file-id to embedded images that only have alt text, using page attachments as the source of truth.
        /// Also recovers ids from legacy Drive URLs in src. Helps recover content saved before Quill preserved custom image attributes.
        /// </summary>
        public static string EnrichDriveImageHtmlFromAttachments(string? html, IReadOnlyList<IntranetPageDocumentDto>? attachments)
        {
            if (string.IsNullOrWhiteSpace(html))
                return html ?? string.Empty;

            var imageAttachments = (attachments ?? Array.Empty<IntranetPageDocumentDto>())
                .Where(a => !string.IsNullOrWhiteSpace(a.DriveFileId)
                            && ClassifyMimeType(a.MimeType, a.Name) == FileTypeImage)
                .ToList();

            const RegexOptions opts = RegexOptions.IgnoreCase | RegexOptions.Singleline;
            return Regex.Replace(
                html,
                @"<img\b([^>]*)>",
                m =>
                {
                    var attrs = m.Groups[1].Value;
                    if (Regex.IsMatch(attrs, @"\bdata-drive-file-id=[""']", RegexOptions.IgnoreCase))
                        return m.Value;
                    if (Regex.IsMatch(attrs, @"\bdata-external-image=[""']true[""']", RegexOptions.IgnoreCase))
                        return m.Value;

                    var driveFileId = TryExtractDriveFileIdFromImgAttrs(attrs);
                    if (string.IsNullOrEmpty(driveFileId) && imageAttachments.Count > 0)
                    {
                        var altMatch = Regex.Match(attrs, @"\balt=[""']([^""']*)[""']", RegexOptions.IgnoreCase);
                        if (altMatch.Success)
                        {
                            var alt = System.Net.WebUtility.HtmlDecode(altMatch.Groups[1].Value).Trim();
                            if (!string.IsNullOrEmpty(alt))
                            {
                                driveFileId = imageAttachments
                                    .FirstOrDefault(a => string.Equals(a.Name, alt, StringComparison.OrdinalIgnoreCase))
                                    ?.DriveFileId;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(driveFileId))
                        return m.Value;

                    var safeId = System.Net.WebUtility.HtmlEncode(driveFileId);
                    attrs += $" data-drive-file-id=\"{safeId}\" data-intranet-media=\"true\"";
                    if (Regex.IsMatch(attrs, @"\bsrc=[""']", RegexOptions.IgnoreCase))
                        attrs = Regex.Replace(attrs, @"\bsrc=[""'][^""']*[""']", $"src=\"{ImagePlaceholderSrc}\"", RegexOptions.IgnoreCase);
                    else
                        attrs += $" src=\"{ImagePlaceholderSrc}\"";

                    return $"<img{attrs}>";
                },
                opts);
        }

        private static string? TryExtractDriveFileIdFromImgAttrs(string attrs)
        {
            var srcMatch = Regex.Match(attrs, @"\bsrc=[""']([^""']*)[""']", RegexOptions.IgnoreCase);
            if (!srcMatch.Success)
                return null;

            var src = System.Net.WebUtility.HtmlDecode(srcMatch.Groups[1].Value);
            if (string.IsNullOrWhiteSpace(src) || src.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
                return null;

            var idMatch = Regex.Match(src, @"[?&]id=([^&]+)", RegexOptions.IgnoreCase);
            if (idMatch.Success)
                return Uri.UnescapeDataString(idMatch.Groups[1].Value);

            idMatch = Regex.Match(src, @"/file/d/([^/]+)", RegexOptions.IgnoreCase);
            return idMatch.Success ? idMatch.Groups[1].Value : null;
        }

        /// <summary>
        /// Ensures Drive-backed images store a stable placeholder src (not editor blob previews)
        /// so page view can hydrate via data-drive-file-id + the media API.
        /// </summary>
        public static string NormalizeDriveImageHtmlForStorage(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return html ?? string.Empty;

            const RegexOptions opts = RegexOptions.IgnoreCase | RegexOptions.Singleline;
            return Regex.Replace(
                html,
                @"<img\b([^>]*\bdata-drive-file-id=[""'][^""']+[""'][^>]*)>",
                m =>
                {
                    var attrs = m.Groups[1].Value;
                    if (Regex.IsMatch(attrs, @"\bsrc=[""']", RegexOptions.IgnoreCase))
                        attrs = Regex.Replace(attrs, @"\bsrc=[""'][^""']*[""']", $"src=\"{ImagePlaceholderSrc}\"", RegexOptions.IgnoreCase);
                    else
                        attrs += $" src=\"{ImagePlaceholderSrc}\"";

                    if (!Regex.IsMatch(attrs, @"\bdata-intranet-media=[""']", RegexOptions.IgnoreCase))
                        attrs += " data-intranet-media=\"true\"";

                    return $"<img{attrs}>";
                },
                opts);
        }

        /// <summary>
        /// Hotlinked image HTML for the editor/page view. Uses referrerpolicy so Google CDN URLs can load off-site.
        /// </summary>
        public static string BuildExternalImageLinkHtml(string sourceUrl, string? altText = null, string? driveFileId = null)
        {
            var safeSrc = System.Net.WebUtility.HtmlEncode(sourceUrl.Trim());
            if (string.IsNullOrEmpty(safeSrc))
                return string.Empty;

            var safeAlt = System.Net.WebUtility.HtmlEncode((altText ?? string.Empty).Trim());
            if (!string.IsNullOrWhiteSpace(driveFileId))
            {
                var safeId = System.Net.WebUtility.HtmlEncode(driveFileId.Trim());
                return $"<p><img src=\"{ImagePlaceholderSrc}\" alt=\"{safeAlt}\" data-drive-file-id=\"{safeId}\" data-intranet-media=\"true\" data-external-image=\"true\" data-external-src=\"{safeSrc}\" referrerpolicy=\"no-referrer\" class=\"intranet-pending-media\" data-intranet-hydrated=\"false\" style=\"max-width:100%;height:auto;\" /></p>";
            }

            return $"<p><img src=\"{safeSrc}\" alt=\"{safeAlt}\" data-external-image=\"true\" data-external-src=\"{safeSrc}\" referrerpolicy=\"no-referrer\" style=\"max-width:100%;height:auto;\" /></p>";
        }

        /// <summary>True when a library file is only used on the given page (safe to offer full Drive purge).</summary>
        public static bool CanPurgeDocumentFromSinglePage(
            IReadOnlyList<IntranetDocumentPageUsageDto>? pages,
            string currentPageId)
        {
            if (string.IsNullOrWhiteSpace(currentPageId) || pages == null || pages.Count == 0)
                return false;

            return pages.All(p => string.Equals(p.PageId, currentPageId, StringComparison.Ordinal));
        }

        public static string BuildInsertHtml(string name, string? mimeType, string driveFileId, string? webViewLink, string? thumbnailLink, bool forceAsLink = false)
        {
            var safeName = System.Net.WebUtility.HtmlEncode(name);
            var category = ClassifyMimeType(mimeType, name);

            if (!forceAsLink && category == FileTypeImage)
            {
                var safeId = System.Net.WebUtility.HtmlEncode(driveFileId);
                return $"<p><img src=\"{ImagePlaceholderSrc}\" alt=\"{safeName}\" data-drive-file-id=\"{safeId}\" data-intranet-media=\"true\" class=\"intranet-pending-media\" data-intranet-hydrated=\"false\" style=\"max-width:100%;height:auto;\" /></p>";
            }

            var href = string.IsNullOrWhiteSpace(webViewLink)
                ? $"https://drive.google.com/file/d/{driveFileId}/view"
                : webViewLink;
            return $"<p><a href=\"{href}\" target=\"_blank\" rel=\"noopener noreferrer\">{safeName}</a></p>";
        }

        /// <summary>
        /// Combines attachment links and HTML content references into one per-page usage list.
        /// </summary>
        public static List<IntranetDocumentPageUsageDto> MergePageUsage(
            IReadOnlyList<IntranetDocumentPageUsageDto>? attachmentPages,
            IReadOnlyList<IntranetDocumentPageUsageDto>? contentPages)
        {
            var byPageId = new Dictionary<string, IntranetDocumentPageUsageDto>(StringComparer.Ordinal);

            if (attachmentPages != null)
            {
                foreach (var page in attachmentPages)
                {
                    byPageId[page.PageId] = new IntranetDocumentPageUsageDto
                    {
                        PageId = page.PageId,
                        Title = page.Title,
                        Caption = page.Caption,
                        IsReferencedInContent = false
                    };
                }
            }

            if (contentPages != null)
            {
                foreach (var page in contentPages)
                {
                    if (byPageId.TryGetValue(page.PageId, out var existing))
                    {
                        existing.IsReferencedInContent = true;
                    }
                    else
                    {
                        byPageId[page.PageId] = new IntranetDocumentPageUsageDto
                        {
                            PageId = page.PageId,
                            Title = page.Title,
                            Caption = page.Caption,
                            IsReferencedInContent = true
                        };
                    }
                }
            }

            return byPageId.Values
                .OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string FormatUsedOnTableLabel(IReadOnlyList<IntranetDocumentPageUsageDto>? pages)
        {
            if (pages == null || pages.Count == 0)
                return "—";

            var ordered = pages
                .OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var embeddedCount = ordered.Count(p => p.IsReferencedInContent);
            var attachedOnlyCount = ordered.Count - embeddedCount;

            if (attachedOnlyCount == 0)
            {
                if (ordered.Count <= 2)
                    return string.Join(", ", ordered.Select(p => p.Title));
                return $"{string.Join(", ", ordered.Take(2).Select(p => p.Title))} +{ordered.Count - 2} more";
            }

            if (embeddedCount == 0)
            {
                return attachedOnlyCount == 1
                    ? $"{ordered[0].Title} (attached only)"
                    : $"{attachedOnlyCount} attached only";
            }

            return $"{embeddedCount} embedded · {attachedOnlyCount} attached only";
        }

        public static string? FormatUsedOnTooltip(IReadOnlyList<IntranetDocumentPageUsageDto>? pages)
        {
            if (pages == null || pages.Count == 0)
                return null;

            return string.Join(
                Environment.NewLine,
                pages
                    .OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(p => p.IsReferencedInContent
                        ? $"• {p.Title} — in content"
                        : $"• {p.Title} — attached only"));
        }

        public static string BuildLibraryDeleteMessage(string fileName, IReadOnlyList<IntranetDocumentPageUsageDto>? pages)
        {
            if (pages == null || pages.Count == 0)
            {
                return $"\"{fileName}\" will be permanently deleted from the company Drive folder.\n\n" +
                       "This cannot be undone.";
            }

            var embedded = pages.Where(p => p.IsReferencedInContent)
                .OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var attachedOnly = pages.Where(p => !p.IsReferencedInContent)
                .OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var lines = new List<string>
            {
                $"\"{fileName}\" will be permanently deleted from the company Drive folder.",
                string.Empty
            };

            if (embedded.Count > 0)
            {
                lines.Add("Removed from page content:");
                lines.AddRange(embedded.Select(p => $"• {p.Title}"));
                lines.Add(string.Empty);
            }

            if (attachedOnly.Count > 0)
            {
                lines.Add("Detached from pages (attached but not in content):");
                lines.AddRange(attachedOnly.Select(p => $"• {p.Title}"));
                lines.Add(string.Empty);
            }

            lines.Add("This cannot be undone.");
            return string.Join('\n', lines);
        }

        public static IntranetDocumentDto ToDocumentDto(IntranetDriveLibraryItemDto item) => new()
        {
            DocumentId = item.DocumentId ?? "",
            DriveFileId = item.DriveFileId,
            Name = item.Name,
            MimeType = item.MimeType,
            WebViewLink = item.WebViewLink,
            ThumbnailLink = item.ThumbnailLink,
            SizeBytes = item.SizeBytes,
            DriveLastModified = item.DriveLastModified,
            DriveOwnerEmail = item.DriveOwnerEmail,
            DriveOwnerName = item.DriveOwnerName,
            Description = item.Description,
            Category = item.Category,
            IsFeatured = item.IsFeatured,
            IsActive = true,
            UsageCount = item.UsageCount,
            UsedOnPages = item.UsedOnPages
        };

        public static string FormatFileSize(long? bytes)
        {
            if (!bytes.HasValue || bytes.Value <= 0) return "—";
            double size = bytes.Value;
            string[] units = { "B", "KB", "MB", "GB" };
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return $"{size:0.#} {units[unit]}";
        }
    }
}