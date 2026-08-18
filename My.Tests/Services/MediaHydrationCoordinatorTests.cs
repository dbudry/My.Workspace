using My.Client.Services;
using Xunit;

namespace My.Tests.Services
{
    /// <summary>
    /// Pins the coalescing/re-run guarantee behind intranet image hydration. The original bug:
    /// an image was marked "hydrated" but a later re-render (auth settling) put it back on the
    /// placeholder, and a one-shot guard meant hydration never ran again — the image stayed
    /// invisible. The coordinator guarantees a fresh pass after any request that lands while one
    /// is in flight, so the last-rendered elements always get a hydration attempt.
    /// </summary>
    public class MediaHydrationCoordinatorTests
    {
        [Fact]
        public async Task Runs_a_single_pass_for_a_lone_request()
        {
            var calls = 0;
            var coordinator = new MediaHydrationCoordinator(() => { calls++; return Task.CompletedTask; });

            await coordinator.RequestAsync();

            Assert.Equal(1, calls);
        }

        [Fact]
        public async Task Runs_one_more_pass_when_a_request_arrives_mid_hydration()
        {
            var calls = 0;
            MediaHydrationCoordinator coordinator = null!;

            // The first pass simulates a re-render landing while hydration is in flight by
            // re-requesting from inside the pass. That second request must not be dropped.
            coordinator = new MediaHydrationCoordinator(async () =>
            {
                calls++;
                if (calls == 1)
                {
                    await coordinator.RequestAsync(); // queued, not run re-entrantly
                    await Task.Yield();
                }
            });

            await coordinator.RequestAsync();

            Assert.Equal(2, calls);
        }

        [Fact]
        public async Task Coalesces_many_mid_flight_requests_into_a_single_extra_pass()
        {
            var calls = 0;
            MediaHydrationCoordinator coordinator = null!;

            coordinator = new MediaHydrationCoordinator(async () =>
            {
                calls++;
                if (calls == 1)
                {
                    // Several renders pile up during the first pass; they should collapse to one.
                    await coordinator.RequestAsync();
                    await coordinator.RequestAsync();
                    await coordinator.RequestAsync();
                    await Task.Yield();
                }
            });

            await coordinator.RequestAsync();

            Assert.Equal(2, calls);
        }

        [Fact]
        public async Task Allows_a_fresh_pass_after_the_previous_one_settled()
        {
            var calls = 0;
            var coordinator = new MediaHydrationCoordinator(() => { calls++; return Task.CompletedTask; });

            await coordinator.RequestAsync();
            await coordinator.RequestAsync();

            Assert.Equal(2, calls);
        }
    }
}
