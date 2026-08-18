namespace My.Shared.Dtos.Setup;

/// <summary>Anonymous, secret-free snapshot of first-run setup progress.</summary>
public class SetupStatusDto
{
    public bool SetupCompleted { get; set; }
    public bool DatabaseReady { get; set; }
    public bool GoogleClientIdConfiguredOnServer { get; set; }
    public bool GoogleClientSecretConfiguredOnServer { get; set; }
    public bool AllowedDomainsConfigured { get; set; }
    public bool UsersExist { get; set; }
    public string DisplayName { get; set; } = "My Workspace";
    public string? AllowedEmailDomains { get; set; }
    public SetupClientHintsDto ClientHints { get; set; } = new();
}

public class SetupClientHintsDto
{
    /// <summary>Relative paths the operator must register on the Google OAuth client.</summary>
    public string[] ExpectedRedirectPathSuffixes { get; set; } =
    [
        "/authentication/login-callback",
        "/settings"
    ];
}

/// <summary>Body for POST /api/setup/configure (only while setup is open).</summary>
public class SetupConfigureRequest
{
    public string? DisplayName { get; set; }
    /// <summary>Comma-separated domains, or * for any verified Google email.</summary>
    public string? AllowedEmailDomains { get; set; }
}
