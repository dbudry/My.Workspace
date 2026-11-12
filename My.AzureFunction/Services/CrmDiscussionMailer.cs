using Azure;
using Azure.Communication.Email;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace My.Functions.Services;

/// <summary>
/// Mails CRM discussion recipients. A failure is a warning string. The comment stays saved.
/// </summary>
public sealed class CrmDiscussionMailer
{
    private readonly ILogger<CrmDiscussionMailer> logger;

    public CrmDiscussionMailer(ILogger<CrmDiscussionMailer> logger)
    {
        this.logger = logger;
    }

    public const string NotSentMessage = "The comment was saved. Email was not sent.";

    public async Task<string?> SendAsync(
        IReadOnlyList<string> toAddresses,
        string subject,
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
            logger.LogInformation("CRM discussion email skipped. No recipients.");
            return NotSentMessage;
        }

        var sender = Environment.GetEnvironmentVariable("ACS_SENDER_ADDRESS");
        var connectionString = Environment.GetEnvironmentVariable("ACS_CONNECTION_STRING");
        var endpoint = Environment.GetEnvironmentVariable("ACS_ENDPOINT");
        if (string.IsNullOrWhiteSpace(sender)
            || (string.IsNullOrWhiteSpace(connectionString) && string.IsNullOrWhiteSpace(endpoint)))
        {
            logger.LogInformation("CRM discussion email skipped. ACS is not configured.");
            return NotSentMessage;
        }

        try
        {
            var client = CreateClient(connectionString, endpoint);
            var content = new EmailContent(subject) { PlainText = plainTextBody };
            var recipients = new EmailRecipients(to.Select(address => new EmailAddress(address)).ToList());
            var message = new EmailMessage(sender, recipients, content);
            var operation = await client.SendAsync(WaitUntil.Completed, message, cancellationToken);
            logger.LogInformation(
                "CRM discussion email sent. MessageId={MessageId} Status={Status}",
                operation.Id, operation.Value.Status);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CRM discussion email failed.");
            return NotSentMessage;
        }
    }

    private static EmailClient CreateClient(string? connectionString, string? endpoint)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
            return new EmailClient(connectionString);
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("CRM email is not configured on this server.");
        return new EmailClient(new Uri(endpoint), new DefaultAzureCredential());
    }
}
