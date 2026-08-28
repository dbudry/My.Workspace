using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using My.Shared.Constants;
using My.Shared.Rules;

namespace My.Functions.Services;

/// <summary>
/// Cross-instance lock so two Consumption workers cannot run incremental
/// Google sync for the same user at once (sync token race). 60s lease, renewed
/// until dispose. A crashed worker drops the lease within 60s.
/// </summary>
public sealed class GoogleCalendarImportUserLock : IAsyncDisposable
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RenewEvery = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RetryWait = TimeSpan.FromMilliseconds(500);

    private readonly BlobLeaseClient _lease;
    private readonly CancellationTokenSource _renewCts;
    private readonly Task _renewTask;

    private GoogleCalendarImportUserLock(BlobLeaseClient lease, CancellationTokenSource renewCts, Task renewTask)
    {
        _lease = lease;
        _renewCts = renewCts;
        _renewTask = renewTask;
    }

    public static async Task<GoogleCalendarImportUserLock> AcquireAsync(
        BlobServiceClient blobs,
        string userId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var container = blobs.GetBlobContainerClient(Constants.API.GoogleCalendar.ImportLockContainer);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var blob = container.GetBlobClient(SanitizeBlobName(userId));
        try
        {
            await blob.UploadAsync(BinaryData.FromString(userId), overwrite: false, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (
            GoogleCalendarStorageErrorRules.IsLockBlobAlreadyPresent(ex.Status, ex.ErrorCode))
        {
            // Blob already exists, including when another worker holds the lease
            // (Azure returns 412 LeaseIdMissing instead of 409).
        }

        var leaseClient = blob.GetBlobLeaseClient();
        var waited = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await leaseClient.AcquireAsync(LeaseDuration, cancellationToken: cancellationToken);
                if (waited)
                {
                    logger.LogInformation(
                        GoogleCalendarLogEvents.ImportLockWait,
                        "Acquired Google calendar import lock for user {UserId} after waiting.",
                        userId);
                }
                break;
            }
            catch (RequestFailedException ex) when (
                GoogleCalendarStorageErrorRules.IsLeaseHeld(ex.Status, ex.ErrorCode))
            {
                if (!waited)
                {
                    waited = true;
                    logger.LogInformation(
                        GoogleCalendarLogEvents.ImportLockWait,
                        "Waiting for Google calendar import lock for user {UserId}.",
                        userId);
                }
                await Task.Delay(RetryWait, cancellationToken);
            }
        }

        var renewCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var renewTask = RenewLoopAsync(leaseClient, logger, userId, renewCts.Token);
        return new GoogleCalendarImportUserLock(leaseClient, renewCts, renewTask);
    }

    public async ValueTask DisposeAsync()
    {
        await _renewCts.CancelAsync();
        try
        {
            await _renewTask;
        }
        catch (OperationCanceledException)
        {
            // expected
        }
        catch
        {
            // renew already logged
        }

        try
        {
            await _lease.ReleaseAsync();
        }
        catch
        {
            // Lease expires in 60s if release fails (crashed worker).
        }

        _renewCts.Dispose();
    }

    private static async Task RenewLoopAsync(
        BlobLeaseClient lease, ILogger logger, string userId, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(RenewEvery, cancellationToken);
                await lease.RenewAsync(cancellationToken: cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // disposing
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to renew Google calendar import lock for user {UserId}; another worker may take over.",
                userId);
        }
    }

    internal static string SanitizeBlobName(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return "_unknown";
        Span<char> buffer = stackalloc char[userId.Length];
        var n = 0;
        foreach (var ch in userId)
        {
            buffer[n++] = char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-';
        }
        return new string(buffer[..n]);
    }
}
