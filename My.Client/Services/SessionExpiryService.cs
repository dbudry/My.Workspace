namespace My.Client.Services;

/// <summary>
/// Tracks whether the user's auth session has expired (Google access token can no
/// longer be silently renewed). Set from the HTTP handler chain when an API call
/// fails with AccessTokenNotAvailableException, or from CustomAccountFactory when
/// provisioning fails for the same reason. Reset by the same handler chain whenever
/// any API call succeeds — that's the cleanest "credentials are fresh again" signal,
/// and it covers the case where silent renewal resolved the token in a background
/// iframe without the top window ever seeing a page reload.
///
/// Consumed by the persistent banner in MainLayout — anyone watching the Changed
/// event can react (e.g., flush local state before redirecting).
/// </summary>
public class SessionExpiryService
{
    private bool _isExpired;

    public bool IsExpired => _isExpired;

    public event Action? Changed;

    public void MarkExpired()
    {
        if (_isExpired) return;
        _isExpired = true;
        Changed?.Invoke();
    }

    public void Reset()
    {
        if (!_isExpired) return;
        _isExpired = false;
        Changed?.Invoke();
    }
}
