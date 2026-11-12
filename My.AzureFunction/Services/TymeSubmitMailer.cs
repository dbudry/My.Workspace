using Azure;
using Azure.Communication.Email;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace My.Functions.Services;

/// <summary>
/// Sends the Tyme submit notification (HTML + plain text). A failure is a warning string.
/// The submission itself stays saved.
/// </summary>
public sealed class TymeSubmitMailer
{
    public const string NotSentMessage = "Time was submitted. The notification email did not send.";

    private readonly ILogger<TymeSubmitMailer> _logger;

    public TymeSubmitMailer(ILogger<TymeSubmitMailer> logger)
    {
        _logger = logger;
    }

    public async Task<string?> SendAsync(
        IReadOnlyList<string> toAddresses,
        string subject,
        string htmlBody,
        string plainTextBody,
        CancellationToken cancellationToken = default)
    {
        var to = toAddresses
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (to.Count == 0)
        {
            _logger.LogInformation("Tyme submit email skipped. No recipients.");
            return null;
        }

        var sender = Environment.GetEnvironmentVariable("ACS_SENDER_ADDRESS");
        var connectionString = Environment.GetEnvironmentVariable("ACS_CONNECTION_STRING");
        var endpoint = Environment.GetEnvironmentVariable("ACS_ENDPOINT");
        if (string.IsNullOrWhiteSpace(sender)
            || (string.IsNullOrWhiteSpace(connectionString) && string.IsNullOrWhiteSpace(endpoint)))
        {
            _logger.LogInformation("Tyme submit email skipped. ACS is not configured.");
            return NotSentMessage;
        }

        try
        {
            var client = !string.IsNullOrWhiteSpace(connectionString)
                ? new EmailClient(connectionString)
                : new EmailClient(new Uri(endpoint!), new DefaultAzureCredential());
            var content = new EmailContent(subject) { Html = htmlBody, PlainText = plainTextBody };
            var recipients = new EmailRecipients(to.Select(address => new EmailAddress(address)).ToList());
            var message = new EmailMessage(sender, recipients, content);
            var operation = await client.SendAsync(WaitUntil.Completed, message, cancellationToken);
            _logger.LogInformation(
                "Tyme submit email sent. MessageId={MessageId} Status={Status}",
                operation.Id, operation.Value.Status);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tyme submit email failed.");
            return NotSentMessage;
        }
    }
}
