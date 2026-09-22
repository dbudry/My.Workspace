namespace My.Shared.Rules;

/// <summary>
/// Shared Drive owned by the app (admin Connect token), not employee accounts.
/// Folders are created per product area.
/// </summary>
public static class AppDriveLayoutRules
{
    public const string CredentialId = "default";
    public const string SharedDriveName = "App Shared Drive";
    /// <summary>
    /// Optional preferred Shared Drive id. Empty = resolve by <see cref="SharedDriveName"/>
    /// after App Drive Connect (greenfield tenants create/name their own drive).
    /// </summary>
    public const string SharedDriveId = "";
    public const string IntranetFolderName = "Intranet";
    public const string ExpensesFolderName = "Expenses";

    public static string SharedDriveUrl =>
        string.IsNullOrWhiteSpace(SharedDriveId)
            ? "https://drive.google.com/drive/shared-drives"
            : $"https://drive.google.com/drive/folders/{SharedDriveId}";

    public const string NotConnectedMessage =
        "App Drive is not connected. A workspace Admin must connect it under App Settings.";

    public static bool IsNotConnectedMessage(string? message) =>
        !string.IsNullOrWhiteSpace(message)
        && message.Contains("App Drive is not connected", StringComparison.OrdinalIgnoreCase);

    public const string NotFoundMessage =
        "App Shared Drive is not visible to the connected Google account. Open the Shared Drive and add that account as a Manager. Do not add employees.";

    public static readonly string[] MembershipRules =
    [
        "The Google account used for Connect must be a Manager on the Shared Drive.",
        "Do not add employees as members.",
        "Workspace Super Admins can still reach the Drive from the Admin console."
    ];

    /// <summary>
    /// Google Shared Drive settings dialog. CheckboxOn is the required state of that
    /// checkbox. ApiProperty/ApiValue are the Drive API restriction fields Apply sets.
    /// </summary>
    public static readonly AppDriveGoogleSetting[] GoogleSettings =
    [
        new(
            "Allow people outside of your organization to access files",
            CheckboxOn: false,
            ApiProperty: "DomainUsersOnly",
            ApiValue: true,
            Why: "Keep files inside the Workspace organization."),
        new(
            "Allow people who aren't shared drive members to access files",
            CheckboxOn: false,
            ApiProperty: "DriveMembersOnly",
            ApiValue: true,
            Why: "Employees use the app, not Drive sharing. Expenses lives on this Drive."),
        new(
            "Allow content managers to share folders",
            CheckboxOn: false,
            ApiProperty: "SharingFoldersRequiresOrganizerPermission",
            ApiValue: true,
            Why: "Do not share Intranet or Expenses folders with employees."),
        new(
            "People who can download, copy, and print — Contributors and content managers",
            CheckboxOn: false,
            ApiProperty: "RestrictedForWriters",
            ApiValue: true,
            Why: "Drive UI copy/download stays off for contributors. The app still uploads and downloads for employees."),
        new(
            "People who can download, copy, and print — Commenters and viewers",
            CheckboxOn: false,
            ApiProperty: "CopyRequiresWriterPermission",
            ApiValue: true,
            Why: "Drive UI copy/download stays off. The app still streams files to signed-in users.")
    ];

    public static string FolderUrl(string? folderId) =>
        string.IsNullOrWhiteSpace(folderId)
            ? SharedDriveUrl
            : $"https://drive.google.com/drive/folders/{folderId.Trim()}";

    /// <summary>Accepts a Drive folder URL or a bare ID.</summary>
    public static string? ParseDriveId(string? urlOrId)
    {
        if (string.IsNullOrWhiteSpace(urlOrId))
            return null;

        var trimmed = urlOrId.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && string.Equals(uri.Host, "drive.google.com", StringComparison.OrdinalIgnoreCase))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i].Equals("folders", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(segments[i + 1]))
                    return segments[i + 1];
            }

            return null;
        }

        return trimmed;
    }
}

public sealed record AppDriveGoogleSetting(
    string Label,
    bool CheckboxOn,
    string? ApiProperty,
    bool ApiValue,
    string Why);
