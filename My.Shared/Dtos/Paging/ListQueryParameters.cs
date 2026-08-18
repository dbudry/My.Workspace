namespace My.Shared.Dtos.Paging
{
    /// <summary>
    /// Standard query-string contract for paged list endpoints (organizations, projects,
    /// tracked tasks, …). One request carries paging, search, sort, and list filters so
    /// the server returns the correct slice — no client-side filtering of partial pages.
    /// </summary>
    public class ListQueryParameters
    {
        public const int DefaultPageSize = 50;
        public const int MaxPageSize = 50;

        public const string PageNumberKey = "PageNumber";
        public const string PageSizeKey = "PageSize";
        public const string SearchKey = "Search";
        public const string SortByKey = "SortBy";
        public const string SortDescendingKey = "SortDescending";
        public const string IncludeArchivedKey = "includeArchived";
        public const string IncludeInactiveKey = "includeInactive";
        public const string GroupByKey = "groupBy";

        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = DefaultPageSize;
        public string? Search { get; set; }
        public string? SortBy { get; set; }
        public bool SortDescending { get; set; }
        public bool IncludeArchived { get; set; }
        public bool IncludeInactive { get; set; }
        public string? GroupBy { get; set; }

        public int EffectivePageSize =>
            PageSize < 1 ? DefaultPageSize : Math.Min(PageSize, MaxPageSize);
    }
}