using System.Net.Http.Json;
using My.Client.Models;
using My.Shared.Constants;
using My.Shared.Dtos.Paging;
using My.Shared.Dtos.Project;
using My.Shared.Helpers;

namespace My.Client.Services
{
    /// <summary>
    /// Typeahead lookup for project pickers. Management tables use server-paged GET /projects.
    /// </summary>
    public class ProjectsCache
    {
        private readonly IHttpClientFactory _clientFactory;

        public ProjectsCache(IHttpClientFactory clientFactory)
        {
            _clientFactory = clientFactory;
        }

        public event Action? Changed;

        public void Invalidate() => Changed?.Invoke();

        public async Task<IReadOnlyList<Project>> LookupAsync(string? search = null, int pageSize = 25)
        {
            var query = new ListQueryParameters
            {
                PageNumber = 1,
                PageSize = pageSize,
                Search = search,
                SortBy = "Name",
                IncludeArchived = true,
                IncludeInactive = true
            };

            var client = _clientFactory.CreateClient(Constants.API.ClientName);

            var response = await TryGetPagedAsync(client, Constants.API.Project.Lookup, query)
                ?? await TryGetPagedAsync(client, Constants.API.Project.Get, query);

            return response?.Items.Select(d => new Project(d)).ToList() ?? new List<Project>();
        }

        public async Task<IReadOnlyList<Project>> LoadSharedAvailabilityAsync()
        {
            var query = new ListQueryParameters
            {
                PageNumber = 1,
                PageSize = ListQueryParameters.MaxPageSize,
                SortBy = "Name",
                IncludeArchived = true,
                IncludeInactive = true
            };

            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            var url = ListQueryUrlBuilder.Build(
                Constants.API.Project.Get,
                query,
                ("sharedAvailabilityOnly", "true"));
            var response = await client.GetFromJsonAsync<PagedResponse<ProjectDto>>(url);
            return response?.Items.Select(d => new Project(d)).ToList() ?? new List<Project>();
        }

        private static async Task<PagedResponse<ProjectDto>?> TryGetPagedAsync(
            HttpClient client,
            string basePath,
            ListQueryParameters query,
            params (string Key, string? Value)[] extra)
        {
            var url = ListQueryUrlBuilder.Build(basePath, query, extra);
            using var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<PagedResponse<ProjectDto>>();
        }

    }
}