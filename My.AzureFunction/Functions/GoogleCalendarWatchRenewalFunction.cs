using Microsoft.Azure.Functions.Worker;
using My.Functions.Services;

namespace My.Functions
{
    /// <summary>
    /// Google Calendar push channels expire (~1 week). Daily re-register of any
    /// channel in the renewal window so inbound sync keeps flowing.
    /// </summary>
    public class GoogleCalendarWatchRenewalFunction(GoogleCalendarWatchRenewer renewer)
    {
        // Runs daily at 06:00 UTC
        [Function("RenewGoogleCalendarWatches")]
        public Task RunAsync([TimerTrigger("0 0 6 * * *")] TimerInfo timer, CancellationToken cancellationToken) =>
            renewer.RenewDueWatchesAsync(cancellationToken);
    }
}
