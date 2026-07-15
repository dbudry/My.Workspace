namespace My.Client.Services;

/// <summary>
/// Serializes intranet image-hydration passes and guarantees one more pass after any request
/// that arrives while a hydration is already in flight.
///
/// Blazor re-renders — notably the auth state settling and reloading an intranet page — can
/// recreate the image elements (back on the 1×1 placeholder) after a hydration has already run.
/// A one-shot "hydrate once per page" guard let those freshly-rendered elements slip through and
/// strand on the placeholder. This coordinator coalesces overlapping requests (so redundant
/// renders don't stack passes) while ensuring the last-rendered elements always get a hydration
/// attempt. Combined with the JS hydrate being idempotent (it skips images already showing a
/// blob), repeating passes is cheap and self-healing.
///
/// Blazor WASM is single-threaded, so the in-flight/queued flags need no locking.
/// </summary>
public sealed class MediaHydrationCoordinator
{
    private readonly Func<Task> _hydrate;
    private bool _inFlight;
    private bool _queued;

    public MediaHydrationCoordinator(Func<Task> hydrate) => _hydrate = hydrate;

    /// <summary>
    /// Runs a hydration pass. If one is already running, records that another pass is needed and
    /// returns immediately; the in-flight pass will loop once more when it finishes.
    /// </summary>
    public async Task RequestAsync()
    {
        if (_inFlight)
        {
            _queued = true;
            return;
        }

        _inFlight = true;
        try
        {
            do
            {
                _queued = false;
                await _hydrate();
            }
            while (_queued);
        }
        finally
        {
            _inFlight = false;
        }
    }
}
