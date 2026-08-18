using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

/// <summary>
/// Pins the rule that no Tyme surface ever renders an email address as someone's
/// employee name. Real data on production has at least one user with
/// <c>FirstName="dbudry@example.com"</c>, which surfaced as the Employee column
/// on Management. The helper has to be defensive against that.
/// </summary>
public class UserDisplayNameRulesTests
{
    // ---------- Happy path ----------

    [Fact]
    public void First_and_last_both_present_returns_space_joined()
    {
        Assert.Equal("Derek Budry",
            UserDisplayNameRules.Resolve("Derek", "Budry", "dbudry@example.com"));
    }

    [Fact]
    public void Whitespace_around_names_is_trimmed()
    {
        Assert.Equal("Derek Budry",
            UserDisplayNameRules.Resolve("  Derek  ", "  Budry  ", null));
    }

    [Fact]
    public void First_only_returns_first()
    {
        Assert.Equal("Derek",
            UserDisplayNameRules.Resolve("Derek", null, "dbudry@example.com"));
    }

    [Fact]
    public void Last_only_returns_last()
    {
        Assert.Equal("Budry",
            UserDisplayNameRules.Resolve(null, "Budry", "dbudry@example.com"));
    }

    // ---------- Email accidentally stored in FirstName ----------
    //
    // This is the bug that motivated the helper: a user with
    // FirstName="dbudry@example.com" should render as "Dbudry", never as the
    // full email. The @suffix gets stripped regardless of LastName.

    [Fact]
    public void First_name_containing_email_strips_after_at()
    {
        Assert.Equal("dbudry",
            UserDisplayNameRules.Resolve("dbudry@example.com", null, "dbudry@example.com"));
    }

    [Fact]
    public void First_name_with_email_plus_separate_last_name_renders_local_part()
    {
        Assert.Equal("dbudry Budry",
            UserDisplayNameRules.Resolve("dbudry@example.com", "Budry", null));
    }

    [Fact]
    public void Last_name_containing_email_strips_after_at()
    {
        Assert.Equal("Derek dbudry",
            UserDisplayNameRules.Resolve("Derek", "dbudry@example.com", null));
    }

    [Fact]
    public void Rendered_output_never_contains_an_at_sign()
    {
        // The cardinal invariant — no Tyme employee column ever displays an email.
        var rendered = UserDisplayNameRules.Resolve(
            "dbudry@example.com", "extra@example.com", "another@example.com");
        Assert.DoesNotContain("@", rendered);
    }

    // ---------- Both name fields empty, fall back to email local part ----------

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", "  ")]
    public void Empty_names_with_email_uses_prettified_local_part(string? first, string? last)
    {
        Assert.Equal("Dbudry",
            UserDisplayNameRules.Resolve(first, last, "dbudry@example.com"));
    }

    [Fact]
    public void Dotted_email_local_part_is_split_and_title_cased()
    {
        Assert.Equal("Derek Budry",
            UserDisplayNameRules.Resolve(null, null, "derek.budry@example.com"));
    }

    [Fact]
    public void Triple_dot_local_part_title_cases_each_segment()
    {
        Assert.Equal("Anna Marie Lee",
            UserDisplayNameRules.Resolve(null, null, "anna.marie.lee@example.com"));
    }

    // ---------- Nothing useful — final fallback ----------

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", "", "")]
    [InlineData("  ", "  ", "   ")]
    public void Everything_empty_returns_unknown(string? first, string? last, string? email)
    {
        Assert.Equal("Unknown", UserDisplayNameRules.Resolve(first, last, email));
    }
}
