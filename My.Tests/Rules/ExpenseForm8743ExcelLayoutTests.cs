using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpenseForm8743ExcelLayoutTests
{
    [Fact]
    public void ChunkLines_empty_is_one_blank_page()
    {
        var pages = ExpenseForm8743ExcelLayout.ChunkLines(Array.Empty<int>());
        Assert.Single(pages);
        Assert.Empty(pages[0]);
    }

    [Fact]
    public void ChunkLines_fits_eight_on_one_page()
    {
        var pages = ExpenseForm8743ExcelLayout.ChunkLines(Enumerable.Range(1, 8).ToList());
        Assert.Single(pages);
        Assert.Equal(8, pages[0].Count);
    }

    [Fact]
    public void ChunkLines_ninth_line_starts_second_page()
    {
        var pages = ExpenseForm8743ExcelLayout.ChunkLines(Enumerable.Range(1, 9).ToList());
        Assert.Equal(2, pages.Count);
        Assert.Equal(8, pages[0].Count);
        Assert.Equal([9], pages[1]);
    }
}
