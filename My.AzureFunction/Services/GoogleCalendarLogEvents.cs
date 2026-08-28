using Microsoft.Extensions.Logging;

namespace My.Functions.Services;

/// <summary>
/// EventIds for Google Calendar import so App Insights queries stay stable.
/// Failures: 3104 enqueue, 3105 parse, 3106 unknown channel, 3107 bad token,
/// 3112 event import, 3113 approaching poison, 3118 watch renewal.
/// </summary>
public static class GoogleCalendarLogEvents
{
    public static readonly EventId WebhookReceived = new(3101, nameof(WebhookReceived));
    public static readonly EventId WebhookHandshake = new(3102, nameof(WebhookHandshake));
    public static readonly EventId Enqueued = new(3103, nameof(Enqueued));
    public static readonly EventId EnqueueFailed = new(3104, nameof(EnqueueFailed));
    public static readonly EventId QueueParseFailed = new(3105, nameof(QueueParseFailed));
    public static readonly EventId UnknownChannel = new(3106, nameof(UnknownChannel));
    public static readonly EventId InvalidChannelToken = new(3107, nameof(InvalidChannelToken));
    public static readonly EventId ImportSkipped = new(3108, nameof(ImportSkipped));
    public static readonly EventId ImportStarted = new(3109, nameof(ImportStarted));
    public static readonly EventId ImportFinished = new(3110, nameof(ImportFinished));
    public static readonly EventId SyncTokenInvalidated = new(3111, nameof(SyncTokenInvalidated));
    public static readonly EventId EventImportFailed = new(3112, nameof(EventImportFailed));
    public static readonly EventId ApproachingPoison = new(3113, nameof(ApproachingPoison));
    public static readonly EventId ImportLockWait = new(3114, nameof(ImportLockWait));
    public static readonly EventId ProbeOk = new(3115, nameof(ProbeOk));
    public static readonly EventId WatchRenewalSkipped = new(3116, nameof(WatchRenewalSkipped));
    public static readonly EventId WatchRenewed = new(3117, nameof(WatchRenewed));
    public static readonly EventId WatchRenewalFailed = new(3118, nameof(WatchRenewalFailed));
}
