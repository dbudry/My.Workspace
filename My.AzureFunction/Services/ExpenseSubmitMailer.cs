using Azure;
using Azure.Communication.Email;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace My.Functions.Services;

public sealed class ExpenseSubmitMailer
{
    public const int MaxAttachmentBytes = 10 * 1024 * 1024;

    private readonly ILogger<ExpenseSubmitMailer> _logger;

    public ExpenseSubmitMailer(ILogger<ExpenseSubmitMailer> logger)
    {
        _logger = logger;
    }

    public async Task<string?> SendAsync(
        IReadOnlyList<string> toAddresses,
        string? ccAddress,
        string subject,
        string plainTextBody,
        IReadOnlyList<(string FileName, byte[] Bytes)> attachments,
        CancellationToken cancellationToken = default)
    {
        var sender = Environment.GetEnvironmentVariable("ACS_SENDER_ADDRESS");
        var connectionString = Environment.GetEnvironmentVariable("ACS_CONNECTION_STRING");
        var endpoint = Environment.GetEnvironmentVariable("ACS_ENDPOINT");
        if (string.IsNullOrWhiteSpace(sender)
            || (string.IsNullOrWhiteSpace(connectionString) && string.IsNullOrWhiteSpace(endpoint)))
        {
            return "Expense email is not configured on this server.";
        }

        var to = toAddresses
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (to.Count == 0)
            return "No submission email recipients are configured.";

        string? cc = string.IsNullOrWhiteSpace(ccAddress) ? null : ccAddress.Trim();
        if (cc != null && to.Contains(cc, StringComparer.OrdinalIgnoreCase))
            cc = null;

        var attach = attachments.Where(file => file.Bytes is { Length: > 0 }).ToList();
        var tooLarge = attach.Sum(file => (long)file.Bytes.Length) > MaxAttachmentBytes;
        if (tooLarge)
            attach = [];

        try
        {
            var client = CreateClient(connectionString, endpoint);
            var content = new EmailContent(subject) { PlainText = plainTextBody };
            var recipients = new EmailRecipients(
                to.Select(address => new EmailAddress(address)).ToList(),
                cc == null ? null : [new EmailAddress(cc)]);
            var message = new EmailMessage(sender, recipients, content);
            foreach (var file in attach)
            {
                message.Attachments.Add(new EmailAttachment(
                    file.FileName,
                    "application/pdf",
                    BinaryData.FromBytes(file.Bytes)));
            }

            var operation = await client.SendAsync(WaitUntil.Completed, message, cancellationToken);
            _logger.LogInformation(
                "Expense submission email sent. MessageId={MessageId} Status={Status}",
                operation.Id, operation.Value.Status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Expense submission email failed.");
            return "The report was submitted, but the email did not send.";
        }

        if (tooLarge)
            return "The report was submitted. The PDF was too large to attach, so the email has the link only.";
        return null;
    }

    private static EmailClient CreateClient(string? connectionString, string? endpoint)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
            return new EmailClient(connectionString);
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("Expense email is not configured on this server.");
        return new EmailClient(new Uri(endpoint), new DefaultAzureCredential());
    }
}
