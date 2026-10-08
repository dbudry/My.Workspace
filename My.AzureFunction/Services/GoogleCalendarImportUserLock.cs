using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using My.Shared.Constants;
using My.Shared.Rules;

namespace My.Functions.Services;

/// <summary>
/// Cross-instance lock so two workers cannot run incremental Google sync for
/// the same user at once (sync token race). 60s lease, renewed until dispose.
/// A crashed worker drops the lease within 60s.
///
/// Acquire waits at most <see cref="AcquireWait"/>. If another import still
/// holds the lease, <see cref="TryAcquireAsync"/> returns null so the queue
/// message can complete instead of sitting until the function timeout.
/// </summary>
public sealed class GoogleCalendarImportUserLock : IAsyncDisposable
{
    /// <summary>How long a queue worker waits for another import to finish.</summary>
    public static readonly TimeSpan AcquireWait = TimeSpan.FromSeconds(5);

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

    /// <summary>
    /// Returns null when another worker still holds this user's lease after
    /// <see cref="AcquireWait"/>. Does not throw for a busy lock.
    /// </summary>
    public static async Task<GoogleCalendarImportUserLock?> TryAcquireAsync(
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
        var acquired = await TryWaitForLeaseAsync(
            async ct =>
            {
                try
                {
                    await leaseClient.AcquireAsync(LeaseDuration, cancellationToken: ct);
                    return true;
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
                    return false;
                }
            },
            AcquireWait,
            RetryWait,
            cancellationToken);

        if (!acquired)
            return null;

        if (waited)
        {
            logger.LogInformation(
                GoogleCalendarLogEvents.ImportLockWait,
                "Acquired Google calendar import lock for user {UserId} after waiting.",
                userId);
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

    /// <summary>
    /// Polls <paramref name="tryAcquire"/> until it returns true, the deadline
    /// passes, or <paramref name="cancellationToken"/> is cancelled.
    /// A false result means the lease stayed held — callers must not retry forever.
    /// </summary>
    internal static async Task<bool> TryWaitForLeaseAsync(
        Func<CancellationToken, Task<bool>> tryAcquire,
        TimeSpan maxWait,
        TimeSpan retryWait,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + maxWait;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await tryAcquire(cancellationToken))
                return true;

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return false;

            var delay = remaining < retryWait ? remaining : retryWait;
            await Task.Delay(delay, cancellationToken);
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
