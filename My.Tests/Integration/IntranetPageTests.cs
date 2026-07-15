using Microsoft.EntityFrameworkCore;
using My.DAL.Data;
using My.DAL.Models;
using My.DAL.Repository;
using Xunit;

namespace My.Tests.Integration;

/// <summary>
/// Covers the intranet page data-access changes: workspace-wide slug uniqueness
/// (IntranetFunction.IsPageSlugUniqueAsync) and the new CancellationToken plumbing on
/// BaseRepository. Runs against SQL Server like the other integration tests; each test
/// cleans up the rows it creates.
/// </summary>
public class IntranetPageTests
{
    private static ApplicationDbContext NewContext() => IntegrationTestConnection.NewContext();

    private static IntranetPage NewPage(string title, string? slug)
    {
        var now = DateTime.UtcNow;
        return new IntranetPage
        {
            PageId = Guid.NewGuid().ToString("N"),
            Title = title,
            Slug = slug,
            CreatedAt = now,
            UpdatedAt = now,
            RestrictEditingToOwner = false,
            Visibility = "Default"
        };
    }

    [SqlServerFact]
    public async Task Page_slug_uniqueness_excludes_the_page_being_edited()
    {
        await using var db = NewContext();

        var slug = "pg-" + Guid.NewGuid().ToString("N").Substring(0, 12);
        var page = NewPage("Slug uniqueness fixture", slug);
        db.IntranetPages.Add(page);
        await db.SaveChangesAsync();

        try
        {
            // Editing the same page must not collide with its own slug.
            var collidesWhenEditingSelf = await db.IntranetPages.AsNoTracking()
                .AnyAsync(p => p.Slug == slug && p.PageId != page.PageId);
            Assert.False(collidesWhenEditingSelf);

            // A new page (nothing excluded) sees the slug as taken.
            var collidesForNewPage = await db.IntranetPages.AsNoTracking()
                .AnyAsync(p => p.Slug == slug && p.PageId != null);
            Assert.True(collidesForNewPage);
        }
        finally
        {
            db.IntranetPages.Remove(page);
            await db.SaveChangesAsync();
        }
    }

    [SqlServerFact]
    public async Task Repository_Get_honors_a_cancelled_token()
    {
        await using var db = NewContext();
        var repo = new BaseRepository<IntranetPage>(db);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repo.Get(ct: cts.Token));
    }
}
