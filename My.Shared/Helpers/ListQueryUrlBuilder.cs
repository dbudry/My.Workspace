using System.Collections.Specialized;
using My.Shared.Dtos.Paging;

namespace My.Shared.Helpers
{
    public static class ListQueryUrlBuilder
    {
        public static string Build(string basePath, ListQueryParameters query, params (string Key, string? Value)[] extra)
        {
            var parts = new List<string>
            {
                $"{ListQueryParameters.PageNumberKey}={query.PageNumber}",
                $"{ListQueryParameters.PageSizeKey}={query.EffectivePageSize}"
            };

            if (!string.IsNullOrWhiteSpace(query.Search))
                parts.Add($"{ListQueryParameters.SearchKey}={Uri.EscapeDataString(query.Search.Trim())}");

            if (!string.IsNullOrWhiteSpace(query.SortBy))
                parts.Add($"{ListQueryParameters.SortByKey}={Uri.EscapeDataString(query.SortBy)}");

            if (query.SortDescending)
                parts.Add($"{ListQueryParameters.SortDescendingKey}=true");

            if (query.IncludeArchived)
                parts.Add($"{ListQueryParameters.IncludeArchivedKey}=true");

            if (query.IncludeInactive)
                parts.Add($"{ListQueryParameters.IncludeInactiveKey}=true");

            if (!string.IsNullOrWhiteSpace(query.GroupBy))
                parts.Add($"{ListQueryParameters.GroupByKey}={Uri.EscapeDataString(query.GroupBy.Trim())}");

            foreach (var (key, value) in extra)
            {
                if (!string.IsNullOrEmpty(value))
                    parts.Add($"{key}={Uri.EscapeDataString(value)}");
            }

            return $"{basePath}?{string.Join('&', parts)}";
        }

        public static ListQueryParameters FromNameValueCollection(NameValueCollection query)
        {
            var result = new ListQueryParameters
            {
                PageNumber = int.TryParse(query[ListQueryParameters.PageNumberKey], out var page) && page > 0 ? page : 1,
                PageSize = int.TryParse(query[ListQueryParameters.PageSizeKey], out var size) && size > 0 ? size : ListQueryParameters.DefaultPageSize,
                Search = query[ListQueryParameters.SearchKey],
                SortBy = query[ListQueryParameters.SortByKey],
                SortDescending = bool.TryParse(query[ListQueryParameters.SortDescendingKey], out var desc) && desc,
                IncludeArchived = bool.TryParse(query[ListQueryParameters.IncludeArchivedKey], out var archived) && archived,
                IncludeInactive = bool.TryParse(query[ListQueryParameters.IncludeInactiveKey], out var inactive) && inactive,
                GroupBy = query[ListQueryParameters.GroupByKey]
            };
            return result;
        }
    }
}