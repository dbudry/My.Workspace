using System.Net.Http.Json;
using My.Shared.Constants;
using My.Shared.Dtos.Paging;
using My.Shared.Dtos.StopwatchItem;
using My.Shared.Dtos.TrackedTask;
using My.Shared.Helpers;

namespace My.Client.Services
{
    public class StopwatchItemsClient
    {
        private readonly IHttpClientFactory _clientFactory;

        public StopwatchItemsClient(IHttpClientFactory clientFactory)
        {
            _clientFactory = clientFactory;
        }

        public async Task<PagedResponse<StopwatchItemDto>> LoadPageAsync(
            ListQueryParameters query,
            CancellationToken cancellationToken = default)
        {
            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            var url = ListQueryUrlBuilder.Build(Constants.API.StopwatchItem.Get, query);
            return await client.GetFromJsonAsync<PagedResponse<StopwatchItemDto>>(url, cancellationToken)
                ?? new PagedResponse<StopwatchItemDto>();
        }

        public async Task<StopwatchItemDto> UpdateAsync(UpdateStopwatchItemDto dto, CancellationToken cancellationToken = default)
        {
            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            var response = await client.PutAsJsonAsync(Constants.API.StopwatchItem.Update, dto, cancellationToken);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<StopwatchItemDto>(cancellationToken: cancellationToken))!;
        }

        public async Task<StopwatchItemDto> CreateAndStartAsync(CreateStopwatchItemDto dto, CancellationToken cancellationToken = default)
        {
            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            var response = await client.PostAsJsonAsync($"{Constants.API.StopwatchItem.CreateAndStart}/create-and-start", dto, cancellationToken);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<StopwatchItemDto>(cancellationToken: cancellationToken))!;
        }

        public async Task<StopwatchItemDto> StartAsync(string itemId, CancellationToken cancellationToken = default)
        {
            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            var response = await client.PostAsync($"{Constants.API.StopwatchItem.Start}/{itemId}/start", null, cancellationToken);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<StopwatchItemDto>(cancellationToken: cancellationToken))!;
        }

        public async Task<StopwatchItemDto> StopAsync(string itemId, CancellationToken cancellationToken = default)
        {
            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            var response = await client.PostAsync($"{Constants.API.StopwatchItem.Stop}/{itemId}/stop", null, cancellationToken);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<StopwatchItemDto>(cancellationToken: cancellationToken))!;
        }

        public async Task<List<TrackedTaskDto>> LoadSessionsAsync(string itemId, CancellationToken cancellationToken = default)
        {
            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            return await client.GetFromJsonAsync<List<TrackedTaskDto>>($"{Constants.API.StopwatchItem.Sessions}/{itemId}/sessions", cancellationToken)
                ?? new List<TrackedTaskDto>();
        }

        /// <summary>
        /// Deletes an entire work item and all of its sessions. Works even for an item with no
        /// logged time. Surfaces the server's reason (e.g. a session in a submitted month) as the
        /// exception message so the caller can show it verbatim.
        /// </summary>
        public async Task DeleteAsync(string itemId, CancellationToken cancellationToken = default)
        {
            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            var response = await client.DeleteAsync($"{Constants.API.StopwatchItem.Delete}/{itemId}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException(string.IsNullOrWhiteSpace(error)
                    ? "Couldn't delete the work item."
                    : error);
            }
        }
    }
}