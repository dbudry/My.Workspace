namespace My.Shared.Rules;

/// <summary>
/// Personal dashboard packing on a 12-column CSS grid. Widgets occupy a
/// (Col, Row, Columns, Rows) rectangle and keep it unless the user moves
/// them into free space (or swaps with one overlapping tile).
/// </summary>
public static class DashboardLayoutRules
{
    public const string ProjectMix = "projectMix";
    public const string TopProjects = "topProjects";
    public const string Favorites = "favorites";
    public const string UnsubmittedTime = "unsubmittedTime";
    public const string LastMonthExpenses = "lastMonthExpenses";

    public const int GridColumns = 12;
    public const int ColThird = 4;
    public const int ColHalf = 6;
    public const int ColTwoThirds = 8;
    public const int ColFull = 12;

    public static readonly IReadOnlyList<DashboardWidgetSpec> Catalog =
    [
        new(ProjectMix, col: 1, row: 1, columns: 6, rows: 3, minColumns: 6, minRows: 3),
        new(TopProjects, col: 7, row: 1, columns: 6, rows: 3, minColumns: 4, minRows: 1,
            compactMinColumns: 2, compactMinRows: 2),
        new(Favorites, col: 1, row: 4, columns: 4, rows: 1, minColumns: 3, minRows: 1),
        new(UnsubmittedTime, col: 5, row: 4, columns: 8, rows: 2, minColumns: 6, minRows: 2),
        new(LastMonthExpenses, col: 1, row: 6, columns: 12, rows: 2, minColumns: 6, minRows: 2)
    ];

    public static DashboardWidgetSpec Spec(string id) =>
        Catalog.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal))
        ?? new DashboardWidgetSpec(id, 1, 1, ColFull, 1, 3, 1);

    public static bool IsKnown(string? id) =>
        Catalog.Any(c => string.Equals(c.Id, id, StringComparison.Ordinal));

    public static List<DashboardLayoutItem> Resolve(
        IReadOnlyList<DashboardLayoutItem>? saved,
        IReadOnlySet<string> available)
    {
        var placed = new List<DashboardLayoutItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (saved != null)
        {
            foreach (var item in saved)
            {
                if (string.IsNullOrWhiteSpace(item.Id) || !IsKnown(item.Id) || !available.Contains(item.Id))
                    continue;
                if (!seen.Add(item.Id))
                    continue;
                var spec = Spec(item.Id);
                var normalized = Normalize(item, spec);
                PlaceWithoutMovingOthers(placed, normalized, spec);
            }
        }

        foreach (var spec in Catalog)
        {
            if (!available.Contains(spec.Id) || !seen.Add(spec.Id))
                continue;
            PlaceWithoutMovingOthers(placed, FromSpec(spec), spec);
        }

        return Sanitize(placed);
    }

    /// <summary>Force every tile up to its min size, relocating if the current slot is too small.</summary>
    public static List<DashboardLayoutItem> Sanitize(IReadOnlyList<DashboardLayoutItem> layout)
    {
        var result = new List<DashboardLayoutItem>();
        foreach (var item in layout)
        {
            if (!IsKnown(item.Id))
                continue;
            PlaceWithoutMovingOthers(result, item, Spec(item.Id));
        }
        return result;
    }

    public static List<DashboardLayoutItem> TryMove(
        IReadOnlyList<DashboardLayoutItem> layout, string id, int col, int row)
    {
        var next = Clone(layout);
        var item = Find(next, id);
        if (item is null)
            return next;

        var spec = Spec(id);
        var proposed = CloneItem(item);
        proposed.Col = col;
        proposed.Row = row;
        proposed = Normalize(proposed, spec);

        if (!OverlapsAny(next, proposed, id))
        {
            Replace(next, proposed);
            return next;
        }

        var hits = Overlapping(next, proposed, id).ToList();
        if (hits.Count == 1)
        {
            var swapped = TrySwap(next, original: item, moving: proposed, other: hits[0]);
            if (swapped != null)
                return swapped;
        }

        return next;
    }

    public static List<DashboardLayoutItem> TryResize(
        IReadOnlyList<DashboardLayoutItem> layout, string id, int columns, int rows) =>
        TryResize(layout, id, col: null, row: null, columns, rows);

    public static List<DashboardLayoutItem> TryResize(
        IReadOnlyList<DashboardLayoutItem> layout, string id, int? col, int? row, int columns, int rows)
    {
        var next = Clone(layout);
        var item = Find(next, id);
        if (item is null)
            return next;

        var spec = Spec(id);
        var right = item.Col + item.Columns;
        var proposed = CloneItem(item);
        if (col.HasValue)
            proposed.Col = col.Value;
        if (row.HasValue)
            proposed.Row = row.Value;
        proposed.Columns = columns;
        proposed.Rows = rows;
        proposed = Normalize(proposed, spec);

        var fromWest = col.HasValue && col.Value != item.Col;
        while (OverlapsAny(next, proposed, id) && proposed.Columns > spec.EffectiveMinColumns(proposed.Rows))
        {
            proposed.Columns--;
            if (fromWest)
                proposed.Col = right - proposed.Columns;
            proposed = Normalize(proposed, spec);
        }
        proposed = Normalize(proposed, spec);
        while (OverlapsAny(next, proposed, id) && proposed.Rows > spec.EffectiveMinRows(proposed.Columns))
            proposed.Rows--;
        proposed = Normalize(proposed, spec);

        if (OverlapsAny(next, proposed, id))
        {
            var packed = FirstFit(
                next.Where(w => !string.Equals(w.Id, id, StringComparison.Ordinal)).ToList(),
                spec,
                Math.Max(item.Columns, spec.MinColumns),
                Math.Max(item.Rows, spec.MinRows));
            Replace(next, packed);
            return next;
        }

        Replace(next, proposed);
        return next;
    }

    public static List<DashboardLayoutItem> MergeForSave(
        IReadOnlyList<DashboardLayoutItem> visible,
        IReadOnlyList<DashboardLayoutItem>? previousSaved)
    {
        var result = Clone(visible);
        var seen = result.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
        if (previousSaved == null)
            return result;
        foreach (var item in previousSaved)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !IsKnown(item.Id) || !seen.Add(item.Id))
                continue;
            result.Add(Normalize(item, Spec(item.Id)));
        }
        return result;
    }

    public static bool Overlaps(DashboardLayoutItem a, DashboardLayoutItem b) =>
        a.Col < b.Col + b.Columns
        && b.Col < a.Col + a.Columns
        && a.Row < b.Row + b.Rows
        && b.Row < a.Row + a.Rows;

    private static void PlaceWithoutMovingOthers(
        List<DashboardLayoutItem> placed, DashboardLayoutItem item, DashboardWidgetSpec spec)
    {
        var candidate = Normalize(item, spec);
        if (candidate.Col >= 1 && candidate.Row >= 1 && !OverlapsAny(placed, candidate, candidate.Id))
        {
            placed.Add(candidate);
            return;
        }

        var packed = FirstFit(placed, spec, candidate.Columns, candidate.Rows);
        placed.Add(packed);
    }

    private static DashboardLayoutItem FirstFit(
        IReadOnlyList<DashboardLayoutItem> placed, DashboardWidgetSpec spec, int columns, int rows)
    {
        FitSize(spec, ref columns, ref rows);
        for (var row = 1; row <= 40; row++)
        {
            for (var col = 1; col <= GridColumns - columns + 1; col++)
            {
                var trial = new DashboardLayoutItem
                {
                    Id = spec.Id,
                    Col = col,
                    Row = row,
                    Columns = columns,
                    Rows = rows
                };
                if (!OverlapsAny(placed, trial, spec.Id))
                    return trial;
            }
        }

        return FromSpec(spec);
    }

    private static List<DashboardLayoutItem>? TrySwap(
        List<DashboardLayoutItem> layout,
        DashboardLayoutItem original,
        DashboardLayoutItem moving,
        DashboardLayoutItem other)
    {
        var movingSpec = Spec(moving.Id);
        var otherSpec = Spec(other.Id);
        var moved = CloneItem(moving);
        var displaced = CloneItem(other);
        displaced.Col = original.Col;
        displaced.Row = original.Row;
        moved = Normalize(moved, movingSpec);
        displaced = Normalize(displaced, otherSpec);
        var next = layout.Select(w =>
        {
            if (string.Equals(w.Id, moved.Id, StringComparison.Ordinal)) return moved;
            if (string.Equals(w.Id, displaced.Id, StringComparison.Ordinal)) return displaced;
            return CloneItem(w);
        }).ToList();
        if (OverlapsAny(next, moved, moved.Id) || OverlapsAny(next, displaced, displaced.Id))
            return null;
        return next;
    }

    internal static void FitSize(DashboardWidgetSpec spec, ref int columns, ref int rows)
    {
        if (columns <= 0) columns = spec.Columns;
        if (rows <= 0) rows = spec.Rows;
        if (spec.CompactMinColumns > 0 && columns < spec.MinColumns)
            rows = Math.Max(rows, spec.CompactMinRows);
        columns = Math.Clamp(columns, spec.EffectiveMinColumns(rows), spec.MaxColumns);
        rows = Math.Clamp(rows, spec.EffectiveMinRows(columns), spec.MaxRows);
        columns = Math.Clamp(columns, spec.EffectiveMinColumns(rows), spec.MaxColumns);
    }

    private static DashboardLayoutItem Normalize(DashboardLayoutItem item, DashboardWidgetSpec spec)
    {
        var columns = item.Columns <= 0 ? spec.Columns : item.Columns;
        var rows = item.Rows <= 0 ? spec.Rows : item.Rows;
        FitSize(spec, ref columns, ref rows);
        var col = item.Col < 1 ? spec.Col : item.Col;
        var row = item.Row < 1 ? spec.Row : item.Row;
        col = Math.Clamp(col, 1, GridColumns - columns + 1);
        row = Math.Max(1, row);
        return new DashboardLayoutItem
        {
            Id = item.Id,
            Col = col,
            Row = row,
            Columns = columns,
            Rows = rows
        };
    }

    private static DashboardLayoutItem FromSpec(DashboardWidgetSpec spec) => new()
    {
        Id = spec.Id,
        Col = spec.Col,
        Row = spec.Row,
        Columns = spec.Columns,
        Rows = spec.Rows
    };

    private static bool OverlapsAny(
        IReadOnlyList<DashboardLayoutItem> layout, DashboardLayoutItem item, string exceptId) =>
        Overlapping(layout, item, exceptId).Any();

    private static IEnumerable<DashboardLayoutItem> Overlapping(
        IReadOnlyList<DashboardLayoutItem> layout, DashboardLayoutItem item, string exceptId) =>
        layout.Where(w =>
            !string.Equals(w.Id, exceptId, StringComparison.Ordinal) && Overlaps(w, item));

    private static DashboardLayoutItem? Find(List<DashboardLayoutItem> layout, string id) =>
        layout.FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.Ordinal));

    private static void Replace(List<DashboardLayoutItem> layout, DashboardLayoutItem item)
    {
        var index = layout.FindIndex(w => string.Equals(w.Id, item.Id, StringComparison.Ordinal));
        if (index >= 0)
            layout[index] = item;
    }

    private static List<DashboardLayoutItem> Clone(IReadOnlyList<DashboardLayoutItem> source) =>
        source.Select(CloneItem).ToList();

    private static DashboardLayoutItem CloneItem(DashboardLayoutItem item) => new()
    {
        Id = item.Id,
        Col = item.Col,
        Row = item.Row,
        Columns = item.Columns,
        Rows = item.Rows
    };
}

public sealed class DashboardWidgetSpec
{
    public DashboardWidgetSpec(
        string id, int col, int row, int columns, int rows, int minColumns, int minRows,
        int maxColumns = DashboardLayoutRules.GridColumns, int maxRows = 6,
        int compactMinColumns = 0, int compactMinRows = 0)
    {
        Id = id;
        Col = col;
        Row = row;
        Columns = columns;
        Rows = rows;
        MinColumns = minColumns;
        MinRows = minRows;
        MaxColumns = maxColumns;
        MaxRows = maxRows;
        CompactMinColumns = compactMinColumns;
        CompactMinRows = compactMinRows;
    }

    public string Id { get; }
    public int Col { get; }
    public int Row { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int MinColumns { get; }
    public int MinRows { get; }
    public int MaxColumns { get; }
    public int MaxRows { get; }
    public int CompactMinColumns { get; }
    public int CompactMinRows { get; }

    public int EffectiveMinColumns(int rows) =>
        CompactMinColumns > 0 && rows >= CompactMinRows ? CompactMinColumns : MinColumns;

    public int EffectiveMinRows(int columns) =>
        CompactMinColumns > 0 && columns < MinColumns ? CompactMinRows : MinRows;
}

public sealed class DashboardLayoutItem
{
    public string Id { get; set; } = "";
    public int Col { get; set; }
    public int Row { get; set; }
    public int Columns { get; set; } = DashboardLayoutRules.ColFull;
    public int Rows { get; set; } = 1;
}
