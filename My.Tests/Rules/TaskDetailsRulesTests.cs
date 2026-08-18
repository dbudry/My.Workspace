using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class TaskDetailsRulesTests
{
    [Fact]
    public void TruncateForList_leaves_short_text()
    {
        Assert.Equal("Standup", TaskDetailsRules.TruncateForList("Standup", 80));
    }

    [Fact]
    public void TruncateForList_adds_ellipsis_when_too_long()
    {
        var text = new string('a', 100);
        var shown = TaskDetailsRules.TruncateForList(text, 20);
        Assert.EndsWith("…", shown);
        Assert.True(shown.Length <= 20);
        Assert.StartsWith("aaa", shown);
    }

    [Fact]
    public void TruncateForList_flattens_newlines_for_list_rows()
    {
        Assert.Equal("Line one Line two", TaskDetailsRules.TruncateForList("Line one\nLine two"));
    }

    [Theory]
    [InlineData("Enter", false, true)]
    [InlineData("NumpadEnter", false, true)]
    [InlineData("Enter", true, false)]
    [InlineData("a", false, false)]
    public void Plain_Enter_is_accept_not_a_line_break(string key, bool shift, bool accept)
    {
        Assert.Equal(accept, TaskDetailsRules.IsAcceptKey(key, shift));
        Assert.Equal(!accept && (key is "Enter" or "NumpadEnter"), TaskDetailsRules.IsLineBreakKey(key, shift));
    }
}
