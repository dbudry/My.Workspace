using My.Shared;
using My.Shared.Dtos.Intranet;
using Xunit;

namespace My.Tests.Rules;

public class IntranetFileHelperTests
{
    [Theory]
    [InlineData("it-helpdesk", "IT Helpdesk", "screenshot.png", null, null, null, "it-helpdesk-screenshot-1.png")]
    [InlineData(null, "VPN Setup Guide", null, "Network Diagram", "https://cdn.example.com/assets/org-chart.jpg",
        "vpn-setup-guide-org-chart-1.jpg|vpn-setup-guide-org-chart-2.jpg", "vpn-setup-guide-org-chart-3.jpg")]
    [InlineData("hr", "HR Home", "pasted-image.png", null, null, null, "hr-image-1.png")]
    public void SuggestPageImageFileName_uses_page_context_and_sequence(
        string? slug,
        string? title,
        string? sourceFileName,
        string? altText,
        string? sourceUrl,
        string? existingNames,
        string expected)
    {
        IEnumerable<string>? taken = string.IsNullOrWhiteSpace(existingNames)
            ? null
            : existingNames.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var suggested = IntranetFileHelper.SuggestPageImageFileName(
            slug, title, sourceFileName, altText, sourceUrl, taken);

        Assert.Equal(expected, suggested);
        Assert.True(IntranetFileHelper.IsValidUploadFileName(suggested));
    }

    [Fact]
    public void SuggestPageImageFileName_skips_names_already_on_page_or_drive()
    {
        var suggested = IntranetFileHelper.SuggestPageImageFileName(
            "bitlocker",
            "BitLocker",
            null,
            "unnamed",
            null,
            new[] { "bitlocker-unnamed-1.png" });

        Assert.Equal("bitlocker-unnamed-2.png", suggested);
    }

    [Fact]
    public void SuggestUploadFileName_strips_guid_and_hash_suffixes()
    {
        var original =
            "Flex-Shot-Adhesive-Sealant-by-Flex-Seal-RV-and-Home-Sealant-White-8-oz_49da39ca-5a7e-4e42-bd9f-b96ea1998b6b.d7f12ec7153548df3a22fc0d4bbfeef3-2675359703.png";

        var suggested = IntranetFileHelper.SuggestUploadFileName(original);

        Assert.Equal("flex-shot-adhesive-sealant-by-flex-seal-rv-and-home-sealant-white-8-oz.png", suggested);
        Assert.True(IntranetFileHelper.IsValidUploadFileName(suggested));
    }

    [Theory]
    [InlineData("My Photo.PNG", "my-photo.png")]
    [InlineData("Annual_Report 2024.pdf", "annual-report-2024.pdf")]
    [InlineData("  Mixed_Case File.JPG  ", "mixed-case-file.jpg")]
    public void NormalizeUploadFileName_converts_to_lowercase_kebab_case(string input, string expected)
    {
        Assert.Equal(expected, IntranetFileHelper.NormalizeUploadFileName(input));
    }

    [Theory]
    [InlineData("good-name.png")]
    [InlineData("bad name.png")]
    [InlineData("UPPER.PNG")]
    public void NormalizeUploadFileName_always_produces_valid_syntax(string input)
    {
        var normalized = IntranetFileHelper.NormalizeUploadFileName(input);
        Assert.True(IntranetFileHelper.IsValidUploadFileName(normalized));
    }

    [Theory]
    [InlineData("silly-dog-sup.gif", "application/octet-stream", "image/gif")]
    [InlineData("photo.png", null, "image/png")]
    [InlineData("clip.mp4", "application/octet-stream", "video/mp4")]
    public void InferMimeType_uses_extension_when_declared_type_is_generic(string fileName, string? declared, string expected)
    {
        Assert.Equal(expected, IntranetFileHelper.InferMimeType(fileName, declared));
    }

    [Fact]
    public void ClassifyMimeType_treats_gif_as_image_even_with_generic_mime()
    {
        Assert.Equal(IntranetFileHelper.FileTypeImage,
            IntranetFileHelper.ClassifyMimeType("application/octet-stream", "animation.gif"));
    }

    [Fact]
    public void IsDriveFolder_recognizes_google_folder_mime_type()
    {
        Assert.True(IntranetFileHelper.IsDriveFolder(IntranetFileHelper.DriveFolderMimeType));
        Assert.False(IntranetFileHelper.IsDriveFolder("image/png"));
    }

    [Fact]
    public void ClassifyMimeType_treats_drive_folders_as_folder_not_document()
    {
        Assert.Equal(IntranetFileHelper.FileTypeFolder,
            IntranetFileHelper.ClassifyMimeType(IntranetFileHelper.DriveFolderMimeType, "Videos"));
        Assert.Equal("Folder", IntranetFileHelper.GetFileTypeLabel(IntranetFileHelper.DriveFolderMimeType, "Videos"));
    }

    [Fact]
    public void MergePageUsage_marks_content_references_and_preserves_attachment_only_pages()
    {
        var merged = IntranetFileHelper.MergePageUsage(
            new List<IntranetDocumentPageUsageDto>
            {
                new() { PageId = "a", Title = "Attached Only" },
                new() { PageId = "c", Title = "Both Ways" }
            },
            new List<IntranetDocumentPageUsageDto>
            {
                new() { PageId = "b", Title = "Embedded Only", IsReferencedInContent = true },
                new() { PageId = "c", Title = "Both Ways", IsReferencedInContent = true }
            });

        Assert.Equal(3, merged.Count);
        Assert.False(merged.Single(p => p.PageId == "a").IsReferencedInContent);
        Assert.True(merged.Single(p => p.PageId == "b").IsReferencedInContent);
        Assert.True(merged.Single(p => p.PageId == "c").IsReferencedInContent);
    }

    [Fact]
    public void FormatUsedOnTableLabel_distinguishes_embedded_and_attached_only_usage()
    {
        var label = IntranetFileHelper.FormatUsedOnTableLabel(new List<IntranetDocumentPageUsageDto>
        {
            new() { PageId = "a", Title = "IT Page", IsReferencedInContent = true },
            new() { PageId = "b", Title = "HR Page", IsReferencedInContent = false }
        });

        Assert.Equal("1 embedded · 1 attached only", label);
    }

    [Fact]
    public void StripDriveFileReferencesFromHtml_removes_editor_image_and_link_inserts()
    {
        const string driveId = "abc123drive";
        var html =
            $"<p>Intro</p>{IntranetFileHelper.BuildInsertHtml("Photo", "image/png", driveId, null, null)}" +
            $"<p>After</p>{IntranetFileHelper.BuildInsertHtml("Doc", "application/pdf", driveId, $"https://drive.google.com/file/d/{driveId}/view", null, forceAsLink: true)}";

        var stripped = IntranetFileHelper.StripDriveFileReferencesFromHtml(html, driveId);

        Assert.DoesNotContain(driveId, stripped, StringComparison.Ordinal);
        Assert.Contains("<p>Intro</p>", stripped);
        Assert.Contains("<p>After</p>", stripped);
    }

    [Fact]
    public void EnrichDriveImageHtmlFromAttachments_adds_drive_id_from_matching_alt()
    {
        const string driveId = "abc123drive";
        var html = "<p><img src=\"blob:https://your-app.example.com/dead\" alt=\"it-bitlocker-ss.png\" /></p>";
        var attachments = new List<IntranetPageDocumentDto>
        {
            new()
            {
                DriveFileId = driveId,
                Name = "it-bitlocker-ss.png",
                MimeType = "image/png"
            }
        };

        var enriched = IntranetFileHelper.EnrichDriveImageHtmlFromAttachments(html, attachments);

        Assert.Contains($"data-drive-file-id=\"{driveId}\"", enriched, StringComparison.Ordinal);
        Assert.Contains("data-intranet-media=\"true\"", enriched, StringComparison.Ordinal);
        Assert.Contains(IntranetFileHelper.ImagePlaceholderSrc, enriched, StringComparison.Ordinal);
    }

    [Fact]
    public void EnrichDriveImageHtmlFromAttachments_adds_drive_id_from_drive_src_url()
    {
        const string driveId = "abc123drive";
        var html = $"<p><img src=\"https://drive.google.com/file/d/{driveId}/view\" alt=\"Photo\" /></p>";

        var enriched = IntranetFileHelper.EnrichDriveImageHtmlFromAttachments(html, attachments: null);

        Assert.Contains($"data-drive-file-id=\"{driveId}\"", enriched, StringComparison.Ordinal);
        Assert.Contains("data-intranet-media=\"true\"", enriched, StringComparison.Ordinal);
        Assert.Contains(IntranetFileHelper.ImagePlaceholderSrc, enriched, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeDriveImageHtmlForStorage_replaces_blob_src_with_placeholder()
    {
        const string driveId = "abc123drive";
        var html =
            $"<p><img src=\"blob:https://your-app.example.com/dead\" alt=\"Photo\" data-drive-file-id=\"{driveId}\" /></p>";

        var normalized = IntranetFileHelper.NormalizeDriveImageHtmlForStorage(html);

        Assert.Contains(IntranetFileHelper.ImagePlaceholderSrc, normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("blob:", normalized, StringComparison.Ordinal);
        Assert.Contains($"data-drive-file-id=\"{driveId}\"", normalized, StringComparison.Ordinal);
        Assert.Contains("data-intranet-media=\"true\"", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildExternalImageLinkHtml_includes_hotlink_attrs()
    {
        const string url = "https://lh3.googleusercontent.com/site/photo=w1280";
        var html = IntranetFileHelper.BuildExternalImageLinkHtml(url, "BitLocker screenshot");

        Assert.Contains($"src=\"{url}\"", html, StringComparison.Ordinal);
        Assert.Contains("data-external-image=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains($"data-external-src=\"{url}\"", html, StringComparison.Ordinal);
        Assert.Contains("referrerpolicy=\"no-referrer\"", html, StringComparison.Ordinal);
        Assert.Contains("alt=\"BitLocker screenshot\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildExternalImageLinkHtml_with_drive_id_uses_placeholder_and_preserves_external_src()
    {
        const string url = "https://lh3.googleusercontent.com/site/photo=w1280";
        const string driveId = "drive123";
        var html = IntranetFileHelper.BuildExternalImageLinkHtml(url, "Shot", driveId);

        Assert.StartsWith("<p><img src=\"data:image/gif;base64,", html, StringComparison.Ordinal);
        Assert.Contains($"data-drive-file-id=\"{driveId}\"", html, StringComparison.Ordinal);
        Assert.Contains($"data-external-src=\"{url}\"", html, StringComparison.Ordinal);
        Assert.Contains("data-intranet-media=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"intranet-pending-media\"", html, StringComparison.Ordinal);
        Assert.Contains("data-intranet-hydrated=\"false\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void CanPurgeDocumentFromSinglePage_true_only_when_all_usage_on_current_page()
    {
        const string pageId = "page-a";
        var sole = new List<IntranetDocumentPageUsageDto>
        {
            new() { PageId = pageId, Title = "A" }
        };
        var multi = new List<IntranetDocumentPageUsageDto>
        {
            new() { PageId = pageId, Title = "A" },
            new() { PageId = "page-b", Title = "B" }
        };

        Assert.True(IntranetFileHelper.CanPurgeDocumentFromSinglePage(sole, pageId));
        Assert.False(IntranetFileHelper.CanPurgeDocumentFromSinglePage(multi, pageId));
        Assert.False(IntranetFileHelper.CanPurgeDocumentFromSinglePage(null, pageId));
    }

    [Fact]
    public void UpdateDriveFileDisplayNameInHtml_updates_image_alt_and_link_text()
    {
        const string driveId = "abc123drive";
        var html =
            $"<p>Intro</p>{IntranetFileHelper.BuildInsertHtml("Old Photo", "image/png", driveId, null, null)}" +
            $"<p>After</p>{IntranetFileHelper.BuildInsertHtml("Old Doc", "application/pdf", driveId, $"https://drive.google.com/file/d/{driveId}/view", null, forceAsLink: true)}";

        var updated = IntranetFileHelper.UpdateDriveFileDisplayNameInHtml(html, driveId, "New Label");

        Assert.Contains("alt=\"New Label\"", updated, StringComparison.Ordinal);
        Assert.Contains(">New Label</a>", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("Old Photo", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("Old Doc", updated, StringComparison.Ordinal);
        Assert.Contains("<p>Intro</p>", updated);
    }
}