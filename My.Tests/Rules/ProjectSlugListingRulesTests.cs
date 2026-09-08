using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ProjectSlugListingRulesTests
{
    [Fact]
    public void ToCsv_writes_name_slug_and_organization()
    {
        var csv = ProjectSlugListingRules.ToCsv(new[]
        {
            new ProjectSlugListingRules.Row("Out of Office", "ooo", "Acme Corp"),
            new ProjectSlugListingRules.Row("Worker", "wrkr", null)
        });

        Assert.StartsWith("Name,Slug,Organization", csv);
        Assert.Contains("\"Out of Office\",\"ooo\",\"Acme Corp\"", csv);
        Assert.Contains("\"Worker\",\"wrkr\",\"\"", csv);
    }

    [Fact]
    public void Quote_doubles_embedded_quotes() =>
        Assert.Equal("\"He said \"\"hi\"\"\"", ProjectSlugListingRules.Quote("He said \"hi\""));
}
