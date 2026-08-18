using System.Net.Http.Json;
using My.Shared.Constants;
using My.Shared.Dtos.TimeSubmission;

namespace My.Client.Services;

/// <summary>
/// Session-scoped overdue submission state shared by the nav badge, dashboard banner,
/// and Submit page — so we do not each re-hit GET /timesubmissions/overdue.
///
/// <list type="bullet">
///   <item><see cref="RefreshAsync"/> — one coalesced HTTP fetch; publishes count to subscribers.</item>
///   <item><see cref="Publish"/> — apply a list already loaded elsewhere (zero extra bandwidth).</item>
///   <item><see cref="NotifyChanged"/> — after submit/unsubmit; invalidates cache and asks
///     subscribers to refresh (one shared fetch).</item>
/// </list>
/// </summary>
public class TimeSubmissionEvents
{
    private readonly IHttpClientFactory _clientFactory;
    private List<OverdueMonthDto>? _months;
    private Task<IReadOnlyList<OverdueMonthDto>>? _inflight;
    private bool _loaded;

    public TimeSubmissionEvents(IHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    /// <summary>Raised when the cached overdue set changes or is invalidated.</summary>
    public event Action? Changed;

    public bool HasLoaded => _loaded;

    public int OverdueCount => _months?.Count ?? 0;

    public IReadOnlyList<OverdueMonthDto> OverdueMonths =>
        _months ?? (IReadOnlyList<OverdueMonthDto>)Array.Empty<OverdueMonthDto>();

    /// <summary>
    /// Apply a server list already obtained (e.g. dashboard just fetched it).
    /// Updates the badge without a second HTTP call.
    /// </summary>
    public void Publish(IReadOnlyList<OverdueMonthDto>? months)
    {
        _months = months?.ToList() ?? new List<OverdueMonthDto>();
        _loaded = true;
        Changed?.Invoke();
    }

    /// <summary>
    /// After submit/unsubmit: drop cache so the next <see cref="RefreshAsync"/> hits the server.
    /// Subscribers re-fetch via a single coalesced request.
    /// </summary>
    public void NotifyChanged()
    {
        _loaded = false;
        _months = null;
        Changed?.Invoke();
    }

    /// <summary>Return cached months if loaded; otherwise fetch once.</summary>
    public Task<IReadOnlyList<OverdueMonthDto>> GetOrLoadAsync() =>
        _loaded && _months != null
            ? Task.FromResult((IReadOnlyList<OverdueMonthDto>)_months)
            : RefreshAsync();

    /// <summary>
    /// Always GET /timesubmissions/overdue (coalesced if several callers race).
    /// Publishes the result to all subscribers.
    /// </summary>
    public Task<IReadOnlyList<OverdueMonthDto>> RefreshAsync()
    {
        if (_inflight != null)
            return _inflight;

        _inflight = LoadCoreAsync();
        return AwaitAndClearAsync(_inflight);
    }

    private async Task<IReadOnlyList<OverdueMonthDto>> AwaitAndClearAsync(
        Task<IReadOnlyList<OverdueMonthDto>> task)
    {
        try
        {
            return await task;
        }
        finally
        {
            if (ReferenceEquals(_inflight, task))
                _inflight = null;
        }
    }

    private async Task<IReadOnlyList<OverdueMonthDto>> LoadCoreAsync()
    {
        var client = _clientFactory.CreateClient(Constants.API.ClientName);
        var list = await client.GetFromJsonAsync<List<OverdueMonthDto>>(Constants.API.TimeSubmission.GetOverdue)
                   ?? new List<OverdueMonthDto>();

        // Publish without re-entering Refresh via Changed handlers that only read cache.
        _months = list;
        _loaded = true;
        Changed?.Invoke();
        return list;
    }
}
