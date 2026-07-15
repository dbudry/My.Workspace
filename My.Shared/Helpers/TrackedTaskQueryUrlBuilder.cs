namespace My.Shared.Helpers
{
    public static class TrackedTaskQueryUrlBuilder
    {
        public const string FromKey = "From";
        public const string ToKey = "To";
        public const string ExcludeStopwatchSessionsKey = "excludeStopwatchSessions";

        public static string Build(
            string basePath,
            Dtos.Paging.ListQueryParameters query,
            DateTime? from = null,
            DateTime? to = null,
            bool excludeStopwatchSessions = false)
        {
            var extras = new List<(string Key, string? Value)>();
            if (from.HasValue)
                extras.Add((FromKey, from.Value.ToString("yyyy-MM-dd")));
            if (to.HasValue)
                extras.Add((ToKey, to.Value.ToString("yyyy-MM-dd")));
            if (excludeStopwatchSessions)
                extras.Add((ExcludeStopwatchSessionsKey, "true"));
            return ListQueryUrlBuilder.Build(basePath, query, extras.ToArray());
        }

        public static string BuildRange(
            string basePath,
            DateTime? from = null,
            DateTime? to = null,
            string? search = null,
            bool excludeStopwatchSessions = false)
        {
            var parts = new List<string>();
            if (from.HasValue)
                parts.Add($"{FromKey}={Uri.EscapeDataString(from.Value.ToString("yyyy-MM-dd"))}");
            if (to.HasValue)
                parts.Add($"{ToKey}={Uri.EscapeDataString(to.Value.ToString("yyyy-MM-dd"))}");
            if (!string.IsNullOrWhiteSpace(search))
                parts.Add($"{Dtos.Paging.ListQueryParameters.SearchKey}={Uri.EscapeDataString(search.Trim())}");
            if (excludeStopwatchSessions)
                parts.Add($"{ExcludeStopwatchSessionsKey}=true");
            return parts.Count == 0 ? basePath : $"{basePath}?{string.Join('&', parts)}";
        }
    }
}