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

        /// <summary>
        /// One page of active (non-archived, active) projects for time-entry pickers.
        /// Used by ProjectAutocomplete infinite scroll. Does not use projectlookup
        /// (mixed archived/inactive, capped at 25).
        /// </summary>
        public async Task<(IReadOnlyList<Project> Items, bool HasNext)> LookupActivePageAsync(
            string? search = null,
            int pageNumber = 1,
            int pageSize = ListQueryParameters.MaxPageSize)
        {
            var query = new ListQueryParameters
            {
                PageNumber = pageNumber < 1 ? 1 : pageNumber,
                PageSize = pageSize < 1
                    ? ListQueryParameters.MaxPageSize
                    : Math.Min(pageSize, ListQueryParameters.MaxPageSize),
                Search = search,
                SortBy = "Name",
                IncludeArchived = false,
                IncludeInactive = false
            };

            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            var response = await TryGetPagedAsync(client, Constants.API.Project.Get, query);
            if (response?.Items == null)
                return (Array.Empty<Project>(), false);

            var items = response.Items.Select(d => new Project(d)).ToList();
            return (items, response.HasNext);
        }

        /// <summary>
        /// All active projects (pages until complete). Prefer
        /// <see cref="LookupActivePageAsync"/> for UI pickers with infinite scroll.
        /// </summary>
        public async Task<IReadOnlyList<Project>> LookupActiveAsync(string? search = null, int pageSize = ListQueryParameters.MaxPageSize)
        {
            var all = new List<Project>();
            var pageNumber = 1;
            const int maxPages = 200;

            while (pageNumber <= maxPages)
            {
                var (items, hasNext) = await LookupActivePageAsync(search, pageNumber, pageSize);
                if (items.Count == 0)
                    break;

                all.AddRange(items);
                if (!hasNext)
                    break;

                pageNumber++;
            }

            return all;
        }

        /// <summary>
        /// Resolves a project by id when it may fall outside the first lookup page.
        /// Manager correction dialogs must call this for InitialProjectId — otherwise a
        /// save with an empty picker wipes the project to null.
        /// </summary>
        public async Task<Project?> ResolveByIdAsync(string? projectId, string? searchHint = null)
        {
            if (string.IsNullOrEmpty(projectId))
                return null;

            try
            {
                var client = _clientFactory.CreateClient(Constants.API.ClientName);
                var byId = await client.GetFromJsonAsync<ProjectDto>(
                    $"{Constants.API.Project.GetById}{projectId}");
                if (byId != null)
                    return new Project(byId);
            }
            catch
            {
                // Fall through to search-based lookup.
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(searchHint))
                {
                    var byName = await LookupAsync(search: searchHint);
                    var match = byName.FirstOrDefault(p => p.ProjectId == projectId);
                    if (match != null)
                        return match;
                }

                // Last resort: scan the first page (covers small workspaces).
                var page = await LookupAsync();
                return page.FirstOrDefault(p => p.ProjectId == projectId);
            }
            catch
            {
                return null;
            }
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