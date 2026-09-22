using My.Functions.Services;
using Xunit;

namespace My.Tests.Services;

public class SharedDriveInfoTests
{
    [Fact]
    public void Parse_reads_writer_download_restriction()
    {
        const string json = """
            {
              "id": "0AExfgQkj51blUk9PVA",
              "name": "App Shared Drive",
              "restrictions": {
                "domainUsersOnly": true,
                "driveMembersOnly": true,
                "sharingFoldersRequiresOrganizerPermission": true,
                "copyRequiresWriterPermission": true,
                "downloadRestriction": {
                  "restrictedForReaders": true,
                  "restrictedForWriters": true
                }
              }
            }
            """;

        var info = SharedDriveInfo.Parse(json);

        Assert.Equal("0AExfgQkj51blUk9PVA", info.Id);
        Assert.True(info.DomainUsersOnly);
        Assert.True(info.DriveMembersOnly);
        Assert.True(info.SharingFoldersRequiresOrganizerPermission);
        Assert.True(info.CopyRequiresWriterPermission);
        Assert.True(info.RestrictedForWriters);
    }
}
