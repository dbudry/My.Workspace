using System.Net;
using System.Threading;
using My.Client.Services;
using Xunit;

namespace My.Tests.Components
{
    /// <summary>
    /// LoadRangeAsync coalesces identical in-flight requests. On a fresh sign-in the authorized
    /// page initializes twice (auth state settles in two beats), firing two overlapping identical
    /// range loads; they should collapse to a single HTTP call. A later, separate load must still
    /// hit the API so edits are never served stale.
    /// </summary>
    public class TrackedTasksClientTests
    {
        private sealed class CountingHandler : HttpMessageHandler
        {
            private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int CallCount;

            /// <summary>Release all in-flight responses.</summary>
            public void Release() => _gate.TrySetResult();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref CallCount);
                await _gate.Task; // hold the response open so callers overlap
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
                };
            }
        }

        private sealed class StubFactory : IHttpClientFactory
        {
            private readonly HttpClient _client;
            public StubFactory(HttpClient client) => _client = client;
            public HttpClient CreateClient(string name) => _client;
        }

        private static (TrackedTasksClient client, CountingHandler handler) Build()
        {
            var handler = new CountingHandler();
            var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") };
            return (new TrackedTasksClient(new StubFactory(http)), handler);
        }

        [Fact]
        public async Task Overlapping_identical_range_loads_make_one_http_call()
        {
            var (client, handler) = Build();
            var from = new DateTime(2025, 7, 1);

            // Start two identical loads while the response is still held open — they overlap.
            var first = client.LoadRangeAsync(from, null);
            var second = client.LoadRangeAsync(from, null);

            handler.Release();
            await Task.WhenAll(first, second);

            Assert.Equal(1, handler.CallCount);
        }

        [Fact]
        public async Task A_later_load_fetches_fresh_after_the_first_completes()
        {
            var (client, handler) = Build();
            var from = new DateTime(2025, 7, 1);

            handler.Release(); // don't hold responses — each call completes before the next starts
            await client.LoadRangeAsync(from, null);
            await client.LoadRangeAsync(from, null);

            Assert.Equal(2, handler.CallCount);
        }
    }
}
