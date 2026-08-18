using System.Net;
using My.Client.Services;
using Xunit;

namespace My.Tests.Components
{
    /// <summary>
    /// Unit tests for the whole-work-item delete wiring. The Stopwatch UI gained a per-row
    /// delete button; these pin the request it sends and how it surfaces server errors, without
    /// needing a database (a fake HttpMessageHandler stands in for the API).
    /// </summary>
    public class StopwatchItemsClientTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            public HttpRequestMessage? LastRequest;
            public HttpResponseMessage Response = new(HttpStatusCode.NoContent);

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult(Response);
            }
        }

        private sealed class StubFactory : IHttpClientFactory
        {
            private readonly HttpClient _client;
            public StubFactory(HttpClient client) => _client = client;
            public HttpClient CreateClient(string name) => _client;
        }

        private static (StopwatchItemsClient client, StubHandler handler) Build(HttpResponseMessage? response = null)
        {
            var handler = new StubHandler();
            if (response != null) handler.Response = response;
            var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") };
            return (new StopwatchItemsClient(new StubFactory(http)), handler);
        }

        [Fact]
        public async Task DeleteAsync_issues_DELETE_to_the_item_route()
        {
            var (client, handler) = Build();

            await client.DeleteAsync("abc123");

            Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
            Assert.Equal("http://localhost/api/stopwatchitems/abc123", handler.LastRequest.RequestUri!.ToString());
        }

        [Fact]
        public async Task DeleteAsync_surfaces_server_reason_on_failure()
        {
            var (client, _) = Build(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("Cannot delete: a session in June 2026 has been submitted.")
            });

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.DeleteAsync("abc123"));
            Assert.Contains("has been submitted", ex.Message);
        }
    }
}
