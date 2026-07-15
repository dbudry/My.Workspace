namespace My.Shared.Rules;

public static class GoogleCalendarWebhookRules
{
    /// <summary>
    /// Validates the channel token Google sends on push notifications.
    /// </summary>
    public static bool IsChannelTokenValid(string? incomingToken, string? storedToken) =>
        !string.IsNullOrEmpty(incomingToken)
        && !string.IsNullOrEmpty(storedToken)
        && string.Equals(incomingToken.Trim(), storedToken.Trim(), StringComparison.Ordinal);
}