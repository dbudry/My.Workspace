namespace My.Shared.Rules;

/// <summary>
/// Validation and classification for Tyme:Admin data extraction requests.
/// </summary>
public static class TymeDataExtractionRules
{
    public const string ApplicationUsers = "ApplicationUsers";
    public const string Organizations = "Organizations";
    public const string ProjectGroups = "ProjectGroups";
    public const string Projects = "Projects";
    public const string TrackedTasks = "TrackedTasks";
    public const string TrackedTaskAliases = "TrackedTaskAliases";
    public const string TrackedTaskCorrectionAudits = "TrackedTaskCorrectionAudits";
    public const string TimeSubmissions = "TimeSubmissions";

    public const int MaxDateRangeDays = 731;

    public static readonly IReadOnlyList<string> AllEntities =
    [
        ApplicationUsers,
        Organizations,
        ProjectGroups,
        Projects,
        TrackedTasks,
        TrackedTaskAliases,
        TrackedTaskCorrectionAudits,
        TimeSubmissions
    ];

    public static readonly IReadOnlySet<string> TransactionalEntities =
        new HashSet<string>(StringComparer.Ordinal)
        {
            TrackedTasks,
            TrackedTaskAliases,
            TrackedTaskCorrectionAudits,
            TimeSubmissions
        };

    private static readonly IReadOnlySet<string> EmployeeFilteredEntities =
        new HashSet<string>(StringComparer.Ordinal)
        {
            ApplicationUsers,
            TrackedTasks,
            TrackedTaskAliases,
            TrackedTaskCorrectionAudits,
            TimeSubmissions
        };

    private static readonly IReadOnlySet<string> IncludeArchivedFilteredEntities =
        new HashSet<string>(StringComparer.Ordinal)
        {
            ApplicationUsers,
            Organizations,
            Projects
        };

    private static readonly IReadOnlySet<string> OrganizationFilteredEntities =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Organizations,
            Projects,
            TrackedTasks,
            TrackedTaskAliases
        };

    private static readonly IReadOnlySet<string> ProjectFilteredEntities =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Projects,
            TrackedTasks,
            TrackedTaskAliases
        };

    private static readonly IReadOnlySet<string> ProjectGroupFilteredEntities =
        new HashSet<string>(StringComparer.Ordinal)
        {
            ProjectGroups,
            Projects,
            TrackedTasks,
            TrackedTaskAliases
        };

    public static bool IsTransactional(string entity) =>
        TransactionalEntities.Contains(entity);

    public static bool RequiresDateRange(IEnumerable<string> entities) =>
        entities.Any(IsTransactional);

    public static bool SupportsEmployeesFilter(IEnumerable<string> entities) =>
        entities.Any(e => EmployeeFilteredEntities.Contains(e));

    public static bool SupportsIncludeArchivedFilter(IEnumerable<string> entities) =>
        entities.Any(e => IncludeArchivedFilteredEntities.Contains(e));

    public static bool SupportsOrganizationFilter(IEnumerable<string> entities) =>
        entities.Any(e => OrganizationFilteredEntities.Contains(e))
        || SupportsTaskScopedOrgProjectFilters(entities);

    public static bool SupportsProjectFilter(IEnumerable<string> entities) =>
        entities.Any(e => ProjectFilteredEntities.Contains(e))
        || SupportsTaskScopedOrgProjectFilters(entities);

    public static bool SupportsProjectGroupFilter(IEnumerable<string> entities) =>
        entities.Any(e => ProjectGroupFilteredEntities.Contains(e))
        || SupportsTaskScopedOrgProjectFilters(entities);

    private static bool SupportsTaskScopedOrgProjectFilters(IEnumerable<string> entities)
    {
        var set = entities as IReadOnlySet<string> ?? entities.ToHashSet(StringComparer.Ordinal);
        return set.Contains(TrackedTaskCorrectionAudits) && set.Contains(TrackedTasks);
    }

    public static bool TryParseEntities(string? raw, out HashSet<string> entities, out string? error)
    {
        entities = new HashSet<string>(StringComparer.Ordinal);
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Select at least one dataset.";
            return false;
        }

        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!AllEntities.Contains(part, StringComparer.Ordinal))
            {
                error = $"Unknown dataset: {part}";
                return false;
            }

            entities.Add(part);
        }

        if (entities.Count == 0)
        {
            error = "Select at least one dataset.";
            return false;
        }

        return true;
    }

    public static string? ValidateDateRange(DateTime? from, DateTime? to)
    {
        if (!from.HasValue || !to.HasValue)
            return "A from and to date are required for time-based datasets.";

        if (to.Value.Date < from.Value.Date)
            return "The to date must be on or after the from date.";

        var spanDays = (to.Value.Date - from.Value.Date).TotalDays;
        if (spanDays > MaxDateRangeDays)
            return $"Date range cannot exceed {MaxDateRangeDays} days.";

        return null;
    }

    public static string? ValidateRequest(IReadOnlyCollection<string> entities, DateTime? from, DateTime? to)
    {
        if (entities.Count == 0)
            return "Select at least one dataset.";

        if (!RequiresDateRange(entities))
            return null;

        return ValidateDateRange(from, to);
    }

    public static bool SubmissionMonthOverlapsRange(int year, int month, DateTime fromUtc, DateTime toUtc)
    {
        var monthStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
        return monthStart <= toUtc && monthEnd >= fromUtc;
    }
}