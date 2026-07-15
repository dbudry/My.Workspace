namespace My.Client.Services;

/// <summary>
/// Rebuilds WASM auth claims after the signed-in user's roles change in the database.
/// </summary>
public interface IUserRoleRefreshService
{
    /// <summary>
    /// Re-fetches provision roles and notifies auth subscribers.
    /// </summary>
    /// <returns><c>true</c> when the OIDC principal cache was reset in-process;
    /// <c>false</c> when a full page reload is needed as fallback.</returns>
    Task<bool> RefreshAfterSelfRoleChangeAsync();
}