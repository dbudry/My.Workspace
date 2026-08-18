using System.Text.Json;
using My.Shared.Constants;
using My.Shared.Dtos.Intranet;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class IntranetMediaPolicyRulesTests
{
    [Fact]
    public void Parse_returns_unconfigured_when_settings_missing()
    {
        var policy = IntranetMediaPolicyRules.Parse(null);
        Assert.False(policy.IsConfigured);
        Assert.Empty(policy.AllowedExtensions);
        Assert.Empty(policy.MaxUploadBytesByExtension);
    }

    [Fact]
    public void Parse_normalizes_extension_mb_pairs()
    {
        var policy = IntranetMediaPolicyRules.Parse(new (string Key, string? Value)[]
        {
            (Constants.SettingKeys.IntranetImageMaxMegabytesByExtension, "png:5, JPG:10, jpeg:10\ngif:2")
        });

        Assert.True(policy.IsConfigured);
        Assert.Equal(new[] { "gif", "jpeg", "jpg", "png" }, policy.AllowedExtensions);
        Assert.Equal(5 * 1024 * 1024, policy.MaxUploadBytesByExtension["png"]);
        Assert.Equal(10 * 1024 * 1024, policy.MaxUploadBytesByExtension["jpg"]);
        Assert.Equal(10 * 1024 * 1024, policy.MaxUploadBytesByExtension["jpeg"]);
        Assert.Equal(2 * 1024 * 1024, policy.MaxUploadBytesByExtension["gif"]);
    }

    [Fact]
    public void ValidateAdminSettings_rejects_invalid_pairs()
    {
        var error = IntranetMediaPolicyRules.ValidateAdminSettings("not-valid");
        Assert.Contains("extension:MB", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateAdminSettings_allows_empty_list()
    {
        Assert.Null(IntranetMediaPolicyRules.ValidateAdminSettings(null));
        Assert.Null(IntranetMediaPolicyRules.ValidateAdminSettings(""));
    }

    [Fact]
    public void TryValidateUpload_rejects_when_policy_not_configured()
    {
        var policy = new IntranetMediaPolicy();
        Assert.False(IntranetMediaPolicyRules.TryValidateUpload("photo.png", "image/png", 100, policy, out var error));
        Assert.Contains("not configured", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryValidateUpload_rejects_disallowed_extension_and_per_type_oversize()
    {
        var policy = new IntranetMediaPolicy
        {
            AllowedExtensions = new[] { "png", "jpg" },
            MaxUploadBytesByExtension = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["png"] = 1024,
                ["jpg"] = 5 * 1024 * 1024
            }
        };

        Assert.False(IntranetMediaPolicyRules.TryValidateUpload("doc.pdf", "application/pdf", 100, policy, out _));
        Assert.False(IntranetMediaPolicyRules.TryValidateUpload("big.png", "image/png", 2048, policy, out var sizeError));
        Assert.Contains(".png", sizeError, StringComparison.OrdinalIgnoreCase);
        Assert.True(IntranetMediaPolicyRules.TryValidateUpload("ok.png", "image/png", 512, policy, out _));
        Assert.True(IntranetMediaPolicyRules.TryValidateUpload("photo.jpg", "image/jpeg", 2 * 1024 * 1024, policy, out _));
    }

    [Fact]
    public void TryValidateLink_requires_configured_extensions()
    {
        var policy = new IntranetMediaPolicy
        {
            AllowedExtensions = new[] { "png" },
            MaxUploadBytesByExtension = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["png"] = 1024
            }
        };

        Assert.True(IntranetMediaPolicyRules.TryValidateLink("https://cdn.example.com/a.png", policy, out _));
        Assert.True(IntranetMediaPolicyRules.TryValidateLink("https://lh3.googleusercontent.com/abc", policy, out _));
    }

    [Fact]
    public void TryValidateLink_allows_duckduckgo_proxy_without_path_extension()
    {
        var policy = new IntranetMediaPolicy
        {
            AllowedExtensions = new[] { "jpg", "png" },
            MaxUploadBytesByExtension = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["jpg"] = 1024 * 1024,
                ["png"] = 1024 * 1024
            }
        };

        Assert.True(IntranetMediaPolicyRules.TryValidateLink(
            "https://external-content.duckduckgo.com/iu/?u=https%3A%2F%2Fexample.com%2Fphoto.jpg",
            policy,
            out _));
    }

    [Fact]
    public void TryValidateLink_blocks_cdn_when_only_documents_allowed()
    {
        var policy = new IntranetMediaPolicy
        {
            AllowedExtensions = new[] { "pdf" },
            MaxUploadBytesByExtension = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["pdf"] = 5 * 1024 * 1024
            }
        };

        Assert.False(IntranetMediaPolicyRules.TryValidateLink("https://lh3.googleusercontent.com/abc", policy, out var cdnError));
        Assert.Contains("image", cdnError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SerializeUploadLimits_writes_json_array()
    {
        var serialized = IntranetMediaPolicyRules.SerializeUploadLimits(new[]
        {
            new IntranetUploadLimitDto { Extension = "jpg", MaxMegabytes = 10 },
            new IntranetUploadLimitDto { Extension = "png", MaxMegabytes = 5 }
        });

        var parsed = JsonSerializer.Deserialize<List<IntranetUploadLimitDto>>(serialized);
        Assert.NotNull(parsed);
        Assert.Equal(2, parsed!.Count);
        Assert.Contains(parsed, e => e.Extension == "jpg" && e.MaxMegabytes == 10);
        Assert.Contains(parsed, e => e.Extension == "png" && e.MaxMegabytes == 5);
    }

    [Fact]
    public void ParseUploadLimits_reads_json_and_legacy_pairs()
    {
        var json = IntranetMediaPolicyRules.SerializeUploadLimits(new[]
        {
            new IntranetUploadLimitDto { Extension = "pdf", MaxMegabytes = 25 }
        });
        var fromJson = IntranetMediaPolicyRules.ParseUploadLimits(json);
        Assert.Single(fromJson);
        Assert.Equal("pdf", fromJson[0].Extension);
        Assert.Equal(25, fromJson[0].MaxMegabytes);

        var fromLegacy = IntranetMediaPolicyRules.ParseUploadLimits(" JPG:10 ,png:5 ");
        Assert.Equal(2, fromLegacy.Count);
        Assert.Contains(fromLegacy, e => e.Extension == "jpg" && e.MaxMegabytes == 10);
        Assert.Contains(fromLegacy, e => e.Extension == "png" && e.MaxMegabytes == 5);
    }

    [Fact]
    public void SerializeMaxMegabytesByExtension_migrates_legacy_pairs_to_json()
    {
        var serialized = IntranetMediaPolicyRules.SerializeMaxMegabytesByExtension(" JPG:10 ,png:5 ");
        Assert.StartsWith("[", serialized, StringComparison.Ordinal);
        var limits = IntranetMediaPolicyRules.ParseUploadLimits(serialized);
        Assert.Equal(2, limits.Count);
    }

    [Fact]
    public void Parse_accepts_jpg_colon_one()
    {
        var policy = IntranetMediaPolicyRules.Parse(new (string Key, string? Value)[]
        {
            (Constants.SettingKeys.IntranetImageMaxMegabytesByExtension, "jpg:1")
        });

        Assert.True(policy.IsConfigured);
        Assert.Equal(new[] { "jpg" }, policy.AllowedExtensions);
        Assert.Equal(1024 * 1024, policy.MaxUploadBytesByExtension["jpg"]);
    }

    [Fact]
    public void Parse_accepts_space_separated_pairs()
    {
        var limits = IntranetMediaPolicyRules.ParseMaxMegabytesByExtension("jpg 1, png 5");
        Assert.Equal(1024 * 1024, limits["jpg"]);
        Assert.Equal(5 * 1024 * 1024, limits["png"]);
    }

    [Fact]
    public void ValidateAdminSettings_rejects_bare_number()
    {
        var error = IntranetMediaPolicyRules.ValidateAdminSettings("1");
        Assert.Contains("jpg:1", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryValidateUpload_allows_configured_documents()
    {
        var policy = new IntranetMediaPolicy
        {
            AllowedExtensions = new[] { "pdf", "docx" },
            MaxUploadBytesByExtension = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["pdf"] = 25 * 1024 * 1024,
                ["docx"] = 15 * 1024 * 1024
            }
        };

        Assert.True(IntranetMediaPolicyRules.TryValidateUpload(
            "report.pdf", "application/pdf", 1024, policy, out _));
        Assert.True(IntranetMediaPolicyRules.TryValidateUpload(
            "memo.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 1024, policy, out _));
        Assert.False(IntranetMediaPolicyRules.TryValidateUpload(
            "script.bat", "application/octet-stream", 1024, policy, out _));
    }

    [Fact]
    public void TryValidateUpload_rejects_executable_extensions_even_when_configured()
    {
        var policy = new IntranetMediaPolicy
        {
            AllowedExtensions = new[] { "exe", "msi", "png" },
            MaxUploadBytesByExtension = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["exe"] = 10 * 1024 * 1024,
                ["msi"] = 10 * 1024 * 1024,
                ["png"] = 5 * 1024 * 1024
            }
        };

        Assert.False(IntranetMediaPolicyRules.TryValidateUpload(
            "setup.exe", "application/octet-stream", 1024, policy, out var exeError));
        Assert.Contains("security", exeError, StringComparison.OrdinalIgnoreCase);

        Assert.False(IntranetMediaPolicyRules.TryValidateUpload(
            "installer.msi", "application/octet-stream", 1024, policy, out _));
    }

    [Fact]
    public void TryValidateUpload_allows_admin_configured_document_extensions()
    {
        var policy = new IntranetMediaPolicy
        {
            AllowedExtensions = new[] { "html", "svg", "pdf" },
            MaxUploadBytesByExtension = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["html"] = 5 * 1024 * 1024,
                ["svg"] = 2 * 1024 * 1024,
                ["pdf"] = 25 * 1024 * 1024
            }
        };

        Assert.True(IntranetMediaPolicyRules.TryValidateUpload(
            "guide.html", "text/html", 1024, policy, out _));
        Assert.True(IntranetMediaPolicyRules.TryValidateUpload(
            "logo.svg", "image/svg+xml", 512, policy, out _));
        Assert.True(IntranetMediaPolicyRules.TryValidateUpload(
            "report.pdf", "application/pdf", 1024, policy, out _));
    }

    [Fact]
    public void ValidateAdminSettings_rejects_denied_extensions()
    {
        var error = IntranetMediaPolicyRules.ValidateAdminSettings("png:5,exe:10");
        Assert.Contains("cannot be uploaded", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".exe", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateAdminUploadLimits_rejects_denied_extensions_in_list()
    {
        var error = IntranetMediaPolicyRules.ValidateAdminUploadLimits(new[]
        {
            new IntranetUploadLimitDto { Extension = "png", MaxMegabytes = 5 },
            new IntranetUploadLimitDto { Extension = "exe", MaxMegabytes = 10 }
        });
        Assert.Contains("cannot be uploaded", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".exe", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_ignores_executable_extensions()
    {
        var policy = IntranetMediaPolicyRules.Parse(new (string Key, string? Value)[]
        {
            (Constants.SettingKeys.IntranetImageMaxMegabytesByExtension, "png:5,exe:10,html:2")
        });

        Assert.True(policy.IsConfigured);
        Assert.Equal(new[] { "html", "png" }, policy.AllowedExtensions);
    }

    [Fact]
    public void TryValidateUpload_rejects_mismatched_image_magic_bytes()
    {
        var policy = new IntranetMediaPolicy
        {
            AllowedExtensions = new[] { "png" },
            MaxUploadBytesByExtension = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["png"] = 5 * 1024 * 1024
            }
        };

        var fakePng = new byte[] { 0x4D, 0x5A, 0x90, 0x00 }; // PE/MZ header, not PNG
        Assert.False(IntranetMediaPolicyRules.TryValidateUpload(
            "photo.png", "image/png", fakePng.Length, policy, out var error, fakePng));
        Assert.Contains("does not match", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryValidateUpload_accepts_matching_image_magic_bytes()
    {
        var policy = new IntranetMediaPolicy
        {
            AllowedExtensions = new[] { "png", "jpg" },
            MaxUploadBytesByExtension = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["png"] = 5 * 1024 * 1024,
                ["jpg"] = 5 * 1024 * 1024
            }
        };

        var pngHeader = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00 };
        var jpegHeader = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };

        Assert.True(IntranetMediaPolicyRules.TryValidateUpload(
            "photo.png", "image/png", pngHeader.Length, policy, out _, pngHeader));
        Assert.True(IntranetMediaPolicyRules.TryValidateUpload(
            "photo.jpg", "image/jpeg", jpegHeader.Length, policy, out _, jpegHeader));
    }

    [Fact]
    public void TryValidateUpload_skips_magic_byte_check_without_content()
    {
        var policy = new IntranetMediaPolicy
        {
            AllowedExtensions = new[] { "png" },
            MaxUploadBytesByExtension = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["png"] = 5 * 1024 * 1024
            }
        };

        Assert.True(IntranetMediaPolicyRules.TryValidateUpload(
            "photo.png", "image/png", 512, policy, out _));
    }

    [Fact]
    public void TryValidateLink_allows_document_urls()
    {
        var policy = new IntranetMediaPolicy
        {
            AllowedExtensions = new[] { "pdf" },
            MaxUploadBytesByExtension = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["pdf"] = 1024
            }
        };

        Assert.True(IntranetMediaPolicyRules.TryValidateLink("https://cdn.example.com/guide.pdf", policy, out _));
    }
}