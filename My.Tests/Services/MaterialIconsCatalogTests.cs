using System.Net;
using System.Text.Json;
using My.Client.Services;
using Xunit;

namespace My.Tests.Services;

public class MaterialIconsCatalogTests
{
    [Fact]
    public void Filter_EmptySearch_PinsRecommendedFirstThenAlphabetical()
    {
        var catalog = CreateCatalog(["alpha", "beta", "home", "folder"]);

        var result = catalog.Filter(null);

        Assert.StartsWith("home", result[0]);
        Assert.Contains("folder", result);
        Assert.Contains("alpha", result);
        Assert.True(result.ToList().IndexOf("home") < result.ToList().IndexOf("alpha"));
    }

    [Fact]
    public void Page_ReturnsRequestedSlice()
    {
        var icons = new[] { "a", "b", "c", "d", "e" };

        Assert.Equal(["c", "d"], MaterialIconsCatalog.Page(icons, page: 2, pageSize: 2));
    }

    [Fact]
    public void PageCount_RoundsUp()
    {
        Assert.Equal(3, MaterialIconsCatalog.PageCount(totalCount: 5, pageSize: 2));
        Assert.Equal(1, MaterialIconsCatalog.PageCount(totalCount: 0, pageSize: 48));
    }

    [Fact]
    public void Filter_WithSearch_PinsRecommendedMatchesFirst()
    {
        var catalog = CreateCatalog(["folder_shared", "folder", "folder_open", "school", "preschool"]);

        var result = catalog.Filter("folder");

        Assert.Equal(["folder", "folder_open", "folder_shared"], result);
    }

    [Fact]
    public void Filter_WithSearch_IsCaseInsensitive()
    {
        var catalog = CreateCatalog(["home", "home_filled"]);

        var result = catalog.Filter("HOME");

        Assert.Contains("home", result);
        Assert.Contains("home_filled", result);
    }

    private static MaterialIconsCatalog CreateCatalog(IEnumerable<string> allIcons)
    {
        var json = JsonSerializer.Serialize(allIcons.ToList());
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var catalog = new MaterialIconsCatalog(client);
        catalog.EnsureLoadedAsync().GetAwaiter().GetResult();
        return catalog;
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}