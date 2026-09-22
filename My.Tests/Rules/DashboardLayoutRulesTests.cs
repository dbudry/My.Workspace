using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class DashboardLayoutRulesTests
{
    [Fact]
    public void Resolve_uses_catalog_slots_when_nothing_saved()
    {
        var available = new HashSet<string>(StringComparer.Ordinal)
        {
            DashboardLayoutRules.ProjectMix,
            DashboardLayoutRules.TopProjects,
            DashboardLayoutRules.Favorites
        };

        var layout = DashboardLayoutRules.Resolve(null, available);

        Assert.Contains(layout, w => w.Id == DashboardLayoutRules.Favorites);
        Assert.Equal(3, layout.Count);
        var mix = layout.Single(w => w.Id == DashboardLayoutRules.ProjectMix);
        Assert.Equal(1, mix.Col);
        Assert.Equal(6, mix.Columns);
        Assert.Equal(3, mix.Rows);
        var top = layout.Single(w => w.Id == DashboardLayoutRules.TopProjects);
        Assert.Equal(7, top.Col);
        Assert.False(DashboardLayoutRules.Overlaps(mix, top));
    }

    [Fact]
    public void Resolve_keeps_saved_rect_and_drops_unavailable()
    {
        var saved = new List<DashboardLayoutItem>
        {
            new() { Id = DashboardLayoutRules.UnsubmittedTime, Col = 1, Row = 1, Columns = 8, Rows = 2 },
            new() { Id = DashboardLayoutRules.Favorites, Col = 9, Row = 1, Columns = 4, Rows = 1 },
            new() { Id = DashboardLayoutRules.ProjectMix, Col = 1, Row = 4, Columns = 6, Rows = 2 }
        };
        var available = new HashSet<string>(StringComparer.Ordinal)
        {
            DashboardLayoutRules.Favorites,
            DashboardLayoutRules.ProjectMix
        };

        var layout = DashboardLayoutRules.Resolve(saved, available);

        Assert.Equal(2, layout.Count);
        Assert.Equal(9, layout.Single(w => w.Id == DashboardLayoutRules.Favorites).Col);
        Assert.Equal(1, layout.Single(w => w.Id == DashboardLayoutRules.ProjectMix).Col);
    }

    [Fact]
    public void Resolve_packs_legacy_saves_without_col_row()
    {
        var saved = new List<DashboardLayoutItem>
        {
            new() { Id = DashboardLayoutRules.Favorites, Columns = 6 }
        };
        var available = new HashSet<string>(StringComparer.Ordinal)
        {
            DashboardLayoutRules.Favorites,
            DashboardLayoutRules.LastMonthExpenses
        };

        var layout = DashboardLayoutRules.Resolve(saved, available);

        Assert.Equal(2, layout.Count);
        Assert.All(layout, w => Assert.True(w.Col >= 1 && w.Row >= 1));
        Assert.False(DashboardLayoutRules.Overlaps(
            layout.Single(w => w.Id == DashboardLayoutRules.Favorites),
            layout.Single(w => w.Id == DashboardLayoutRules.LastMonthExpenses)));
    }

    [Fact]
    public void TryMove_into_free_space_does_not_shift_neighbors()
    {
        var layout = DashboardLayoutRules.Resolve(null, new HashSet<string>(StringComparer.Ordinal)
        {
            DashboardLayoutRules.ProjectMix,
            DashboardLayoutRules.TopProjects
        });
        var topBefore = layout.Single(w => w.Id == DashboardLayoutRules.TopProjects);

        var moved = DashboardLayoutRules.TryMove(layout, DashboardLayoutRules.ProjectMix, 1, 4);

        var mix = moved.Single(w => w.Id == DashboardLayoutRules.ProjectMix);
        var top = moved.Single(w => w.Id == DashboardLayoutRules.TopProjects);
        Assert.Equal(1, mix.Col);
        Assert.Equal(4, mix.Row);
        Assert.Equal(topBefore.Col, top.Col);
        Assert.Equal(topBefore.Row, top.Row);
    }

    [Fact]
    public void TryMove_swaps_when_dropping_on_one_tile()
    {
        var layout = DashboardLayoutRules.Resolve(null, new HashSet<string>(StringComparer.Ordinal)
        {
            DashboardLayoutRules.ProjectMix,
            DashboardLayoutRules.TopProjects
        });

        var swapped = DashboardLayoutRules.TryMove(layout, DashboardLayoutRules.ProjectMix, 7, 1);

        var mix = swapped.Single(w => w.Id == DashboardLayoutRules.ProjectMix);
        var top = swapped.Single(w => w.Id == DashboardLayoutRules.TopProjects);
        Assert.Equal(7, mix.Col);
        Assert.Equal(1, top.Col);
    }

    [Fact]
    public void TryMove_stays_put_when_target_overlaps_two_tiles()
    {
        var layout = new List<DashboardLayoutItem>
        {
            new() { Id = DashboardLayoutRules.ProjectMix, Col = 1, Row = 1, Columns = 6, Rows = 2 },
            new() { Id = DashboardLayoutRules.Favorites, Col = 1, Row = 3, Columns = 4, Rows = 1 },
            new() { Id = DashboardLayoutRules.UnsubmittedTime, Col = 5, Row = 3, Columns = 8, Rows = 2 }
        };

        var result = DashboardLayoutRules.TryMove(layout, DashboardLayoutRules.ProjectMix, 1, 3);

        var mix = result.Single(w => w.Id == DashboardLayoutRules.ProjectMix);
        Assert.Equal(1, mix.Col);
        Assert.Equal(1, mix.Row);
        Assert.Equal(1, result.Single(w => w.Id == DashboardLayoutRules.Favorites).Col);
        Assert.Equal(5, result.Single(w => w.Id == DashboardLayoutRules.UnsubmittedTime).Col);
    }

    [Fact]
    public void Sanitize_lifts_tiles_below_minimum()
    {
        var layout = new List<DashboardLayoutItem>
        {
            new() { Id = DashboardLayoutRules.ProjectMix, Col = 1, Row = 1, Columns = 1, Rows = 1 }
        };

        var result = DashboardLayoutRules.Sanitize(layout);
        var mix = result.Single();
        Assert.Equal(6, mix.Columns);
        Assert.Equal(3, mix.Rows);
    }

    [Fact]
    public void Resolve_lifts_undersized_saved_tiles_to_catalog_minimum()
    {
        var saved = new List<DashboardLayoutItem>
        {
            new() { Id = DashboardLayoutRules.Favorites, Col = 1, Row = 3, Columns = 1, Rows = 1 },
            new() { Id = DashboardLayoutRules.UnsubmittedTime, Col = 2, Row = 3, Columns = 1, Rows = 1 }
        };
        var available = new HashSet<string>(StringComparer.Ordinal)
        {
            DashboardLayoutRules.Favorites,
            DashboardLayoutRules.UnsubmittedTime
        };

        var layout = DashboardLayoutRules.Resolve(saved, available);

        var favorites = layout.Single(w => w.Id == DashboardLayoutRules.Favorites);
        var unsubmitted = layout.Single(w => w.Id == DashboardLayoutRules.UnsubmittedTime);
        Assert.True(favorites.Columns >= 3);
        Assert.True(favorites.Rows >= 1);
        Assert.True(unsubmitted.Columns >= 6);
        Assert.True(unsubmitted.Rows >= 2);
        Assert.False(DashboardLayoutRules.Overlaps(favorites, unsubmitted));
    }

    [Fact]
    public void Resolve_empty_saved_uses_catalog_sizes()
    {
        var available = new HashSet<string>(StringComparer.Ordinal)
        {
            DashboardLayoutRules.ProjectMix,
            DashboardLayoutRules.TopProjects,
            DashboardLayoutRules.Favorites,
            DashboardLayoutRules.UnsubmittedTime
        };

        var layout = DashboardLayoutRules.Resolve([], available);

        Assert.Equal(4, layout.Single(w => w.Id == DashboardLayoutRules.Favorites).Columns);
        Assert.Equal(1, layout.Single(w => w.Id == DashboardLayoutRules.Favorites).Rows);
        Assert.Equal(8, layout.Single(w => w.Id == DashboardLayoutRules.UnsubmittedTime).Columns);
        Assert.Equal(2, layout.Single(w => w.Id == DashboardLayoutRules.UnsubmittedTime).Rows);
        Assert.Equal(5, layout.Single(w => w.Id == DashboardLayoutRules.UnsubmittedTime).Col);
    }

    [Fact]
    public void TryResize_clamps_to_widget_minimum()
    {
        var layout = new List<DashboardLayoutItem>
        {
            new() { Id = DashboardLayoutRules.ProjectMix, Col = 1, Row = 1, Columns = 6, Rows = 2 }
        };

        var result = DashboardLayoutRules.TryResize(layout, DashboardLayoutRules.ProjectMix, 3, 1);
        var mix = result.Single();
        Assert.Equal(6, mix.Columns);
        Assert.Equal(3, mix.Rows);
    }

    [Fact]
    public void TryResize_grows_until_it_would_overlap()
    {
        var layout = new List<DashboardLayoutItem>
        {
            new() { Id = DashboardLayoutRules.ProjectMix, Col = 1, Row = 1, Columns = 6, Rows = 2 },
            new() { Id = DashboardLayoutRules.TopProjects, Col = 9, Row = 1, Columns = 4, Rows = 2 }
        };

        var result = DashboardLayoutRules.TryResize(layout, DashboardLayoutRules.ProjectMix, 12, 2);
        var mix = result.Single(w => w.Id == DashboardLayoutRules.ProjectMix);
        Assert.Equal(8, mix.Columns);
        Assert.Equal(9, result.Single(w => w.Id == DashboardLayoutRules.TopProjects).Col);
    }

    [Fact]
    public void TryResize_west_keeps_right_edge()
    {
        var layout = new List<DashboardLayoutItem>
        {
            new() { Id = DashboardLayoutRules.TopProjects, Col = 7, Row = 1, Columns = 6, Rows = 3 }
        };

        var result = DashboardLayoutRules.TryResize(
            layout, DashboardLayoutRules.TopProjects, col: 4, row: 1, columns: 9, rows: 3);
        var top = result.Single();
        Assert.Equal(4, top.Col);
        Assert.Equal(9, top.Columns);
        Assert.Equal(13, top.Col + top.Columns);
    }

    [Fact]
    public void TryResize_west_stops_at_neighbor()
    {
        var layout = new List<DashboardLayoutItem>
        {
            new() { Id = DashboardLayoutRules.ProjectMix, Col = 1, Row = 1, Columns = 6, Rows = 3 },
            new() { Id = DashboardLayoutRules.TopProjects, Col = 7, Row = 1, Columns = 6, Rows = 3 }
        };

        var result = DashboardLayoutRules.TryResize(
            layout, DashboardLayoutRules.TopProjects, col: 1, row: 1, columns: 12, rows: 3);
        var top = result.Single(w => w.Id == DashboardLayoutRules.TopProjects);
        Assert.Equal(7, top.Col);
        Assert.Equal(6, top.Columns);
        Assert.Equal(1, result.Single(w => w.Id == DashboardLayoutRules.ProjectMix).Col);
    }

    [Fact]
    public void TopProjects_one_row_needs_four_columns()
    {
        var spec = DashboardLayoutRules.Spec(DashboardLayoutRules.TopProjects);
        Assert.Equal(4, spec.EffectiveMinColumns(1));
        Assert.Equal(1, spec.EffectiveMinRows(4));
    }

    [Fact]
    public void TopProjects_two_rows_can_be_two_columns()
    {
        var spec = DashboardLayoutRules.Spec(DashboardLayoutRules.TopProjects);
        Assert.Equal(2, spec.EffectiveMinColumns(2));
        Assert.Equal(2, spec.EffectiveMinRows(2));
    }

    [Fact]
    public void Normalize_narrow_top_projects_gains_a_second_row()
    {
        var layout = DashboardLayoutRules.Sanitize(
        [
            new() { Id = DashboardLayoutRules.TopProjects, Col = 10, Row = 1, Columns = 2, Rows = 1 }
        ]);
        var top = layout.Single();
        Assert.Equal(2, top.Columns);
        Assert.Equal(2, top.Rows);
    }

    [Fact]
    public void MergeForSave_keeps_hidden_widgets()
    {
        var visible = new List<DashboardLayoutItem>
        {
            new() { Id = DashboardLayoutRules.ProjectMix, Col = 1, Row = 1, Columns = 12, Rows = 2 }
        };
        var previous = new List<DashboardLayoutItem>
        {
            new() { Id = DashboardLayoutRules.Favorites, Col = 1, Row = 4, Columns = 4, Rows = 1 },
            new() { Id = DashboardLayoutRules.ProjectMix, Col = 1, Row = 1, Columns = 6, Rows = 2 }
        };

        var saved = DashboardLayoutRules.MergeForSave(visible, previous);

        Assert.Equal(2, saved.Count);
        Assert.Equal(12, saved.Single(w => w.Id == DashboardLayoutRules.ProjectMix).Columns);
        Assert.Equal(4, saved.Single(w => w.Id == DashboardLayoutRules.Favorites).Row);
    }
}
