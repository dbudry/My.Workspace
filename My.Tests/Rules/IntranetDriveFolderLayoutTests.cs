using My.Shared;
using Xunit;

namespace My.Tests.Rules;

public class IntranetDriveFolderLayoutTests
{
    [Fact]
    public void ResolveUploadFolderId_routes_png_to_images_subfolder()
    {
        var layout = IntranetDriveFolderLayout.FromParent("intranet-root", new[]
        {
            ("img-id", "Images"),
            ("vid-id", "Videos"),
            ("doc-id", "Documents")
        });

        Assert.Equal("img-id", layout.ResolveUploadFolderId("application/octet-stream", "silly-ostrich.png"));
        Assert.Equal("vid-id", layout.ResolveUploadFolderId("video/mp4", "clip.mp4"));
        Assert.Equal("doc-id", layout.ResolveUploadFolderId("application/pdf", "notes.pdf"));
    }

    [Fact]
    public void ResolveUploadFolderId_falls_back_to_parent_when_subfolder_missing()
    {
        var layout = new IntranetDriveFolderLayout { ParentFolderId = "root-only" };
        Assert.Equal("root-only", layout.ResolveUploadFolderId("image/png", "photo.png"));
    }
}