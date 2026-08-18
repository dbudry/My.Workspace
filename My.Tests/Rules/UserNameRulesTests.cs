using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

/// <summary>
/// Exercises <see cref="UserNameRules"/> — the email-to-name heuristic used when
/// Google's <c>name</c> claim isn't available. The real test is qualitative: does
/// the guess feel reasonable for common @example.com address shapes? These cases
/// codify what "reasonable" means so future tweaks don't regress.
/// </summary>
public class UserNameRulesTests
{
    [Theory]
    [InlineData("john.doe@example.com", "John", "Doe")]
    [InlineData("jane_smith@example.com", "Jane", "Smith")]
    [InlineData("dbudry@example.com", "Dbudry", "")]
    [InlineData("a.b.c@example.com", "A", "B C")]              // multi-dot: tail joins into LastName
    [InlineData("UPPER.case@x.com", "Upper", "Case")]           // case folded then title-cased
    [InlineData("hyphen-name@x.com", "Hyphen-name", "")]        // hyphen isn't a separator on purpose (initial pass keeps it simple)
    [InlineData("a.@x.com", "A", "")]                            // trailing dot doesn't yield an empty LastName
    [InlineData(".b@x.com", "B", "")]                            // leading dot doesn't yield an empty FirstName
    public void ParseFromEmail_returns_expected_guess(string email, string expectedFirst, string expectedLast)
    {
        var (first, last) = UserNameRules.ParseFromEmail(email);
        Assert.Equal(expectedFirst, first);
        Assert.Equal(expectedLast, last);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]   // no '@' is still treated as a local part — but only if non-empty after split
    public void ParseFromEmail_handles_degenerate_inputs(string? email)
    {
        var (first, last) = UserNameRules.ParseFromEmail(email);
        // "no-at-sign" yields ("No-at-sign", "") because '-' isn't a separator;
        // empty/whitespace yields ("", ""). We assert weakly here — the rule is
        // "doesn't throw, returns *something*".
        Assert.NotNull(first);
        Assert.NotNull(last);
    }

    [Theory]
    [InlineData("dbudry@example.com", true)]
    [InlineData("Derek", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeEmail_detects_email_shaped_values(string? value, bool expected)
    {
        Assert.Equal(expected, UserNameRules.LooksLikeEmail(value));
    }
}
