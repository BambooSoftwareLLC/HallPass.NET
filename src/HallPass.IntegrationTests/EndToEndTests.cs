using HallPass.Api;
using HallPass.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using System.Collections.Concurrent;
using System.Net;

namespace HallPass.IntegrationTests
{
    public class EndToEndTests
    {
        [Fact(Skip = "need to hit real endpoints with known rate limits and confirm no breaches")]
        public async Task Can_make_concurrent_requests_from_multiple_instances_that_are_properly_throttled_with_LeakyBucket()
        {
            var instances = Enumerable.Range(1, 3);
            var uri = TestEndpoints.GetRandom();
            var sharedKey = uri;

            var spy = new ConcurrentBag<DateTimeOffset>();

            var clientId = TestConfig.HallPassClientId();
            var clientSecret = TestConfig.HallPassClientSecret();

            var tasks = instances
                .Select(_ => Task.Run(async () =>
                {
                    // configure dependency injection that uses HallPass configuration extensions
                    var services = new ServiceCollection();

                    services.AddHallPass(hallPass =>
                    {
                        // use HallPass remotely
                        hallPass
                            .UseLeakyBucket(uri, 10, TimeSpan.FromSeconds(15), 10)
                            .ForMultipleInstances(clientId, clientSecret, key: uri);
                    });

                    // make a loop of API calls to the throttled endpoint
                    var serviceProvider = services.BuildServiceProvider(validateScopes: true);
                    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                    for (int i = 0; i < 10; i++)
                    {
                        var httpClient = httpClientFactory.CreateHallPassClient();
                        var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);

                        // make sure nothing blows up
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new HttpRequestException($"URI: {uri}");
                        }

                        spy.Add(DateTimeOffset.Now);
                    }
                }))
                .ToList();

            await Task.WhenAll(tasks);

            spy.Count.ShouldBe(30);

            // make sure calls are throttled as expected
            var spyQueue = new Queue<DateTimeOffset>(spy.OrderBy(s => s));
            var current = spyQueue.Dequeue();
            while (spyQueue.TryDequeue(out var next))
            {
                var earlyBound = next - TimeSpan.FromSeconds(7.5);
                var lateBound = next + TimeSpan.FromSeconds(7.5);
                var localCount = spy.Count(x => x >= earlyBound && x < lateBound);

                // MUST be bound by this
                localCount.ShouldBeLessThanOrEqualTo(10);

                current = next;
            }
        }

        [Fact(Skip = "flaky, need a better test for this")]
        public async Task Respects_HallPass_API_rate_limit_for_hallpasses_from_single_instance()
        {
            // setup
            var uri = TestEndpoints.GetRandom();
            var sharedKey = uri;

            var clientId = TestConfig.HallPassClientId();
            var clientSecret = TestConfig.HallPassClientSecret();

            // configure dependency injection that uses HallPass configuration extensions
            var services = new ServiceCollection();

            services.AddHallPass(hallPass =>
            {
                // use HallPass remotely
                hallPass
                    .UseLeakyBucket(uri, 1, TimeSpan.FromSeconds(5), 1)
                    .ForMultipleInstances(clientId, clientSecret, key: uri);
            });

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            var apiFactory = serviceProvider.GetRequiredService<HallPassApiFactory>();
            var api = apiFactory.GetOrCreate(clientId, clientSecret);

            // check time
            var start = DateTimeOffset.Now;

            // rate limit is 100 per minute with an initial burst of 100, so we call this 100 times in a burst expecting to be significantly earlier than a minute...
            var tasks = Enumerable.Range(1, 10)
                .Select(async _ =>
                {
                    for (int i = 0; i < 10; i++)
                    {
                        await api.GetTicketsAsync(sharedKey, sharedKey, 1, TimeSpan.FromSeconds(5), 1);
                    }
                })
                .ToList();

            await Task.WhenAll(tasks);

            // verify time is < 1 minute
            var first100 = DateTimeOffset.Now - start;
            first100.ShouldBeLessThan(TimeSpan.FromMinutes(1));

            // ... and then 100 more times expecting to be significantly later than a minute
            tasks = Enumerable.Range(1, 10)
                .Select(async _ =>
                {
                    for (int i = 0; i < 10; i++)
                    {
                        await api.GetTicketsAsync(sharedKey, sharedKey, 1, TimeSpan.FromSeconds(5), 1);
                    }
                })
                .ToList();

            await Task.WhenAll(tasks);

            // verify time is > 1 minute
            var second100 = DateTimeOffset.Now - start;
            second100.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMinutes(1));
        }
    }
}
