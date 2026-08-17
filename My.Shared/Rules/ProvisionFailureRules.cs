namespace My.Shared.Rules;

/// <summary>
/// Stable codes + messages for failed <c>POST /users/provision</c>.
/// Shared so the API and SPA stay aligned.
/// </summary>
public static class ProvisionFailureRules
{
    public const string CodeEmailNotAllowed = "email_not_allowed";
    public const string CodeInactiveOrArchived = "inactive_or_archived";
    public const string CodeNotProvisioned = "not_provisioned";
    public const string CodeUnauthorized = "unauthorized";
    public const string CodeServerError = "server_error";
    public const string CodeTransient = "transient_failure";
    public const string CodeUnknown = "unknown";

    public static string MessageFor(string code) => code switch
    {
        CodeEmailNotAllowed =>
            "Your Google account is not allowed to sign in to this workspace. Ask an administrator, or complete the setup wizard for allowed email domains.",
        CodeInactiveOrArchived =>
            "Your account is inactive or archived. Contact an administrator.",
        CodeNotProvisioned =>
            "Your account has not been set up yet. An administrator must create your user before you can use the app.",
        CodeUnauthorized =>
            "Sign-in could not be verified. Try signing out and back in.",
        CodeServerError =>
            "The server hit an error while loading your profile. Retry, or check API logs if it keeps failing.",
        CodeTransient =>
            "The server may still be starting up. Wait a moment and retry.",
        _ =>
            "Your app profile could not be loaded. Retry, or contact an administrator."
    };
}
