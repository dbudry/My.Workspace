using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class AppDriveLayoutRulesTests
{
    [Fact]
    public void Shared_drive_and_area_folder_names()
    {
        Assert.Equal("App Shared Drive", AppDriveLayoutRules.SharedDriveName);
        Assert.Equal("", AppDriveLayoutRules.SharedDriveId);
        Assert.Equal("Intranet", AppDriveLayoutRules.IntranetFolderName);
        Assert.Equal("Expenses", AppDriveLayoutRules.ExpensesFolderName);
        Assert.Equal("Filed", ExpenseDriveNamingRules.FiledFolderName);
        Assert.DoesNotContain(
            ExpenseDriveNamingRules.FiledFolderName,
            ExpenseDriveNamingRules.UserFolderName("Budry", "Derek", "682a8fdc-5e9b-434d-9a12-15e1b95258de"),
            StringComparison.Ordinal);
        Assert.Equal("default", AppDriveLayoutRules.CredentialId);
        Assert.Equal(
            "https://drive.google.com/drive/shared-drives",
            AppDriveLayoutRules.SharedDriveUrl);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("0AExfgQkj51blUk9PVA", "0AExfgQkj51blUk9PVA")]
    [InlineData("https://drive.google.com/drive/folders/0AExfgQkj51blUk9PVA", "0AExfgQkj51blUk9PVA")]
    [InlineData("https://drive.google.com/drive/u/0/folders/0AExfgQkj51blUk9PVA", "0AExfgQkj51blUk9PVA")]
    public void ParseDriveId_accepts_url_or_bare_id(string? input, string? expected) =>
        Assert.Equal(expected, AppDriveLayoutRules.ParseDriveId(input));

    [Fact]
    public void Not_connected_message_points_at_app_settings()
    {
        Assert.Contains("App Settings", AppDriveLayoutRules.NotConnectedMessage, StringComparison.Ordinal);
        Assert.Contains("App Drive", AppDriveLayoutRules.NotConnectedMessage, StringComparison.Ordinal);
        Assert.True(AppDriveLayoutRules.IsNotConnectedMessage(AppDriveLayoutRules.NotConnectedMessage));
        Assert.False(AppDriveLayoutRules.IsNotConnectedMessage("Google Drive is not connected."));
    }

    [Fact]
    public void Google_settings_are_all_off_in_the_Drive_dialog()
    {
        Assert.Equal(5, AppDriveLayoutRules.GoogleSettings.Length);
        Assert.All(AppDriveLayoutRules.GoogleSettings, s => Assert.False(s.CheckboxOn));
        Assert.Contains(AppDriveLayoutRules.GoogleSettings, s =>
            s.ApiProperty == "DomainUsersOnly" && s.ApiValue);
        Assert.Contains(AppDriveLayoutRules.GoogleSettings, s =>
            s.ApiProperty == "DriveMembersOnly" && s.ApiValue);
        Assert.Contains(AppDriveLayoutRules.GoogleSettings, s =>
            s.ApiProperty == "SharingFoldersRequiresOrganizerPermission" && s.ApiValue);
        Assert.Contains(AppDriveLayoutRules.GoogleSettings, s =>
            s.ApiProperty == "CopyRequiresWriterPermission" && s.ApiValue);
        Assert.Contains(AppDriveLayoutRules.GoogleSettings, s =>
            s.ApiProperty == "RestrictedForWriters" && s.ApiValue);
        Assert.DoesNotContain(AppDriveLayoutRules.GoogleSettings, s => s.ApiProperty == null);
    }

    [Fact]
    public void Membership_keeps_employees_off_the_drive()
    {
        Assert.Contains(AppDriveLayoutRules.MembershipRules, r =>
            r.Contains("Do not add employees", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(AppDriveLayoutRules.MembershipRules, r =>
            r.Contains("Manager", StringComparison.OrdinalIgnoreCase));
    }
}
