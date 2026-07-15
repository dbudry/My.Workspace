using System.Net.Http.Json;

namespace My.Client.Services;

/// <summary>
/// Loads the Google Material Icons font catalog (ligature names) used by curated
/// intranet nav items. Built-in app UI uses MudBlazor SVG icons instead.
/// </summary>
public sealed class MaterialIconsCatalog
{
    private const string CatalogPath = "data/material-icons.json";

    private readonly HttpClient _httpClient;
    private List<string>? _allIcons;

    public MaterialIconsCatalog(HttpClient httpClient) => _httpClient = httpClient;

    public IReadOnlyList<string> RecommendedIcons { get; } =
    [
        "home", "dashboard", "folder", "folder_open", "menu_book", "description", "article", "assignment",
        "people", "groups", "business", "calendar_month", "event", "schedule", "access_time",
        "settings", "admin_panel_settings", "manage_accounts", "link", "account_tree", "list", "checklist",
        "work", "school", "help", "info", "contact_mail", "policy", "security", "verified",
        "star", "favorite", "bookmark", "lightbulb", "campaign", "announcement", "support",
        "library_books", "collections_bookmark", "topic", "category", "inventory", "corporate_fare",
        "contact_page", "feed", "newspaper", "balance", "gavel", "health_and_safety"
    ];

    public async Task EnsureLoadedAsync()
    {
        if (_allIcons != null) return;

        try
        {
            var icons = await _httpClient.GetFromJsonAsync<List<string>>(CatalogPath);
            _allIcons = icons?.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().OrderBy(i => i).ToList()
                        ?? RecommendedIcons.ToList();
        }
        catch
        {
            _allIcons = RecommendedIcons.ToList();
        }
    }

    public IReadOnlyList<string> Filter(string? search)
    {
        var catalog = _allIcons ?? RecommendedIcons.ToList();

        if (string.IsNullOrWhiteSpace(search))
        {
            var others = catalog.Except(RecommendedIcons, StringComparer.Ordinal);
            return RecommendedIcons.Concat(others).ToList();
        }

        var query = search.Trim();
        var matches = catalog
            .Where(i => i.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var recommended = RecommendedIcons
            .Where(i => i.Contains(query, StringComparison.OrdinalIgnoreCase));

        var rest = matches.Except(recommended, StringComparer.Ordinal);
        return recommended.Concat(rest).ToList();
    }

    public static IReadOnlyList<string> Page(IReadOnlyList<string> icons, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        return icons.Skip((page - 1) * pageSize).Take(pageSize).ToList();
    }

    public static int PageCount(int totalCount, int pageSize) =>
        totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);
}