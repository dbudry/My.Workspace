using System.Net.Http.Json;
using My.Shared;
using My.Shared.Constants;
using My.Shared.Dtos.Intranet;

namespace My.Client.Services;

public class IntranetSearchService
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly ILogger<IntranetSearchService> _logger;

    public IntranetSearchService(IHttpClientFactory clientFactory, ILogger<IntranetSearchService> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<IntranetPageSearchResultDto>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < IntranetSearchHelper.MinQueryLength)
            return [];

        try
        {
            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            var url = $"{Constants.API.Intranet.Pages.Search}?q={Uri.EscapeDataString(query.Trim())}";
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Intranet search failed: {StatusCode} for {Url}",
                    (int)response.StatusCode,
                    url);
                return [];
            }

            var results = await response.Content.ReadFromJsonAsync<List<IntranetPageSearchResultDto>>(cancellationToken);
            return results ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Intranet search request failed.");
            return [];
        }
    }
}