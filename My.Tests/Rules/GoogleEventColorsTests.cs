using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

/// <summary>
/// Exercises <see cref="GoogleEventColors"/>. The reason this rule exists: Google Calendar's
/// Insert endpoint rejects any color id outside "1".."11" *including* the empty string,
/// and a 400 from one event aborts the whole backfill. Centralizing validation means a
/// corrupted UserSettings row or a deprecated id falls back to "calendar default" instead
/// of taking down the sync.
/// </summary>
public class GoogleEventColorsTests
{
    // ---------- All 11 documented Google color ids validate ----------

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("3")]
    [InlineData("4")]
    [InlineData("5")]
    [InlineData("6")]
    [InlineData("7")]
    [InlineData("8")]
    [InlineData("9")]
    [InlineData("10")]
    [InlineData("11")]
    public void Valid_color_ids_pass(string id)
    {
        Assert.True(GoogleEventColors.IsValidColorId(id));
        Assert.Equal(id, GoogleEventColors.NormalizeOrNull(id));
    }

    // ---------- Invalid / corrupted values fall back to null ----------
    //
    // "0" and "12" are out-of-range; the empty string is the specific value that previously
    // broke backfill before we caught it in BuildEvent; whitespace and arbitrary tokens
    // represent corrupted-DB or future-Google-change scenarios.

    [Theory]
    [InlineData("0")]
    [InlineData("12")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("11 ")]
    [InlineData("eleven")]
    [InlineData("#ff0000")]
    public void Invalid_color_ids_fail_and_normalize_to_null(string? id)
    {
        Assert.False(GoogleEventColors.IsValidColorId(id));
        Assert.Null(GoogleEventColors.NormalizeOrNull(id));
    }

    [Fact]
    public void Null_color_id_fails_validation_and_normalizes_to_null()
    {
        Assert.False(GoogleEventColors.IsValidColorId(null));
        Assert.Null(GoogleEventColors.NormalizeOrNull(null));
    }

    // ---------- Palette contract ----------

    [Fact]
    public void Palette_contains_exactly_eleven_colors()
    {
        // Locks down the contract: if Google ever publishes a 12th color we'll need to
        // bump this and the migration. Keeps a silent drift from creeping in.
        Assert.Equal(11, GoogleEventColors.All.Count);
    }

    [Fact]
    public void Palette_ids_match_one_through_eleven_in_order()
    {
        var ids = GoogleEventColors.All.Select(c => c.Id).ToArray();
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11" }, ids);
    }

    [Fact]
    public void Default_unmatched_color_is_tomato()
    {
        // Eleven is Tomato — the "needs a project" flag color. The migration backfills
        // existing rows to this value and the SQL default is "11". Keep them aligned.
        Assert.Equal("11", GoogleEventColors.DefaultUnmatchedColorId);
        Assert.Equal("Tomato", GoogleEventColors.All.Single(c => c.Id == "11").Name);
    }

    [Fact]
    public void All_color_names_are_distinct()
    {
        var names = GoogleEventColors.All.Select(c => c.Name).ToArray();
        Assert.Equal(names.Length, names.Distinct().Count());
    }
}
