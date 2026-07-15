using System.Net.Http.Json;
using My.Client.Models;
using My.Shared.Constants;
using My.Shared.Dtos.Organization;
using My.Shared.Dtos.Paging;
using My.Shared.Helpers;

namespace My.Client.Services
{
    /// <summary>
    /// Typeahead lookup for organization pickers (project dialog, etc.).
    /// Does not cache the full org list — each debounced search is one API call that
    /// returns at most 25 matches from SQL.
    /// </summary>
    public class OrganizationsCache
    {
        private readonly IHttpClientFactory _clientFactory;

        public OrganizationsCache(IHttpClientFactory clientFactory)
        {
            _clientFactory = clientFactory;
        }

        public event Action? Changed;

        public void Invalidate() => Changed?.Invoke();

        public async Task<IReadOnlyList<Organization>> LookupAsync(string? search = null, int pageSize = 25)
        {
            var query = BuildQuery(search, pageSize);
            var client = _clientFactory.CreateClient(Constants.API.ClientName);

            var response = await TryGetPagedAsync(client, Constants.API.Organization.Lookup, query)
                ?? await TryGetPagedAsync(
                    client,
                    Constants.API.Organization.Get,
                    query,
                    ("summary", "true"));

            return response?.Items.Select(d => new Organization(d)).ToList() ?? new List<Organization>();
        }

        /// <summary>
        /// Org picker data for the project dialog. Loads typeahead matches and, when editing,
        /// hydrates the linked org (with departments) so the department dropdown works.
        /// </summary>
        public async Task<IReadOnlyList<Organization>> LoadForProjectPickerAsync(
            string? search = null,
            string? linkedOrganizationId = null)
        {
            IReadOnlyList<Organization> list;
            try
            {
                list = await LookupAsync(search);
            }
            catch
            {
                list = Array.Empty<Organization>();
            }

            if (string.IsNullOrEmpty(linkedOrganizationId))
                return list;

            var mutable = list.ToList();
            var linked = await TryGetByIdAsync(linkedOrganizationId);
            if (linked == null)
                return mutable;

            var index = mutable.FindIndex(o =>
                string.Equals(o.OrganizationId, linkedOrganizationId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                mutable[index] = linked;
            else
                mutable.Insert(0, linked);

            return mutable;
        }

        public async Task<Organization?> TryGetByIdAsync(string organizationId, bool includeArchived = true)
        {
            var client = _clientFactory.CreateClient(Constants.API.ClientName);
            var url = $"{Constants.API.Organization.GetById}{organizationId}";
            if (includeArchived)
                url += "?includeArchived=true";

            try
            {
                var dto = await client.GetFromJsonAsync<OrganizationDto>(url);
                return dto == null ? null : new Organization(dto);
            }
            catch
            {
                return null;
            }
        }

        private static ListQueryParameters BuildQuery(string? search, int pageSize) => new()
        {
            PageNumber = 1,
            PageSize = pageSize,
            Search = search,
            SortBy = "Name",
            IncludeArchived = true,
            IncludeInactive = true
        };

        private static async Task<PagedResponse<OrganizationDto>?> TryGetPagedAsync(
            HttpClient client,
            string basePath,
            ListQueryParameters query,
            params (string Key, string? Value)[] extra)
        {
            var url = ListQueryUrlBuilder.Build(basePath, query, extra);
            using var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<PagedResponse<OrganizationDto>>();
        }

    }
}