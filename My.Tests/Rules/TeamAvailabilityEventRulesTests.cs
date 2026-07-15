using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

/// <summary>
/// Pins the sanitized team-calendar event format. The team calendar must never expose
/// task names, raw emails, or other Tyme details — Tyme entries are private "except
/// to the manager or admin"; only an availability echo is allowed.
///
/// Signature is <c>BuildTitle(displayName, projectName)</c>. The caller is expected
/// to have already resolved <paramref name="displayName"/> via
/// <see cref="UserDisplayNameRules.Resolve"/> so an email field never reaches this
/// helper. The DisplayName-style override on the project flows in via projectName.
/// </summary>
public class TeamAvailabilityEventRulesTests
{
    // ---------- Title format: "[DisplayName] Project" ----------

    [Fact]
    public void Builds_bracketed_name_then_project_title()
    {
        Assert.Equal("[Derek Budry] Vacation",
            TeamAvailabilityEventRules.BuildTitle("Derek Budry", "Vacation"));
    }

    [Theory]
    [InlineData("Vacation")]
    [InlineData("Wedding")]
    [InlineData("Sick")]
    [InlineData("OOO")]
    [InlineData("Out of Office")]
    [InlineData("Jury Duty")]
    public void Project_name_flows_through_as_typed_by_manager(string projectName)
    {
        var title = TeamAvailabilityEventRules.BuildTitle("Derek Budry", projectName);
        Assert.Equal($"[Derek Budry] {projectName}", title);
    }

    [Fact]
    public void Name_is_wrapped_in_brackets()
    {
        var title = TeamAvailabilityEventRules.BuildTitle("Derek Budry", "Vacation");
        Assert.StartsWith("[", title);
        Assert.Contains("] ", title);
    }

    // ---------- Whitespace + missing pieces ----------

    [Theory]
    [InlineData(" Derek Budry ", " Vacation ")]
    [InlineData("Derek Budry", "Vacation")]
    public void Surrounding_whitespace_is_trimmed(string name, string project)
    {
        Assert.Equal("[Derek Budry] Vacation",
            TeamAvailabilityEventRules.BuildTitle(name, project));
    }

    [Fact]
    public void Missing_project_returns_just_the_name()
    {
        Assert.Equal("Derek Budry",
            TeamAvailabilityEventRules.BuildTitle("Derek Budry", null));
    }

    [Fact]
    public void Missing_name_returns_just_the_project()
    {
        Assert.Equal("Vacation",
            TeamAvailabilityEventRules.BuildTitle(null, "Vacation"));
    }

    [Fact]
    public void Missing_everything_returns_empty()
    {
        Assert.Equal(string.Empty,
            TeamAvailabilityEventRules.BuildTitle(null, null));

        Assert.Equal(string.Empty,
            TeamAvailabilityEventRules.BuildTitle("  ", "   "));
    }

    // ---------- Fixed color ----------

    [Fact]
    public void Fixed_color_is_sage_id_two()
    {
        Assert.Equal("2", TeamAvailabilityEventRules.FixedColorId);
    }

    // ---------- Email never leaks (UserDisplayNameRules contract) ----------
    //
    // The cardinal rule: a user whose DB record accidentally holds an email in the
    // FirstName field must not have that email leak onto the public team calendar.
    // BuildTitle itself doesn't strip emails — that's UserDisplayNameRules.Resolve's
    // job. These tests verify the helper renders verbatim what's passed in, so the
    // bug class is contained to "caller forgot to resolve" rather than the helper
    // silently double-stripping or accepting raw user records.

    [Fact]
    public void Builds_pretty_local_part_when_caller_uses_email_fallback()
    {
        // Simulates UserDisplayNameRules.Resolve(null, null, "dbudry@example.com") → "Dbudry".
        Assert.Equal("[Dbudry] Vacation",
            TeamAvailabilityEventRules.BuildTitle("Dbudry", "Vacation"));
    }

    [Fact]
    public void Rendered_output_never_contains_at_sign_when_caller_resolves_correctly()
    {
        // If the caller pre-resolves via UserDisplayNameRules, no @ is ever in the input.
        // The helper just renders what it gets; this test pins the absence in a typical
        // pipeline output rather than the helper's behavior on raw email input (which
        // the helper would dutifully render — that's the caller's job to prevent).
        var rendered = TeamAvailabilityEventRules.BuildTitle(
            UserDisplayNameRules.Resolve("dbudry@example.com", null, "dbudry@example.com"),
            "Out of Office");
        Assert.DoesNotContain("@", rendered);
        Assert.Equal("[dbudry] Out of Office", rendered);
    }
}
