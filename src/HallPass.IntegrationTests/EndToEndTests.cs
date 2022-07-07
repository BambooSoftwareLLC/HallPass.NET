using HallPass.Api;
using HallPass.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using System.Collections.Concurrent;

namespace HallPass.IntegrationTests
{
    public class EndToEndTests
    {
        [Fact]
        public async Task Can_make_concurrent_requests_from_multiple_instances_that_are_properly_throttled()
        {
            var instances = Enumerable.Range(1, 10);
            var uri = TestEndpoints.GetRandom();
            var sharedKey = uri;

            var spy = new ConcurrentBag<DateTimeOffset>();

            var clientId = TestConfig.GetConfiguration().HallPassClientId();
            var clientSecret = TestConfig.GetConfiguration().HallPassClientSecret();

            var tasks = instances
                .Select(_ => Task.Run(async () =>
                {
                    // configure dependency injection that uses HallPass configuration extensions
                    var services = new ServiceCollection();

                    services.AddHallPass(hallPass =>
                    {
                        // use HallPass remotely
                        hallPass
                            .UseTokenBucket(uri, 10, TimeSpan.FromSeconds(5))
                            .ForMultipleInstances(clientId, clientSecret, key: uri);
                    });

                    // make a loop of API calls to the throttled endpoint
                    var serviceProvider = services.BuildServiceProvider(validateScopes: true);
                    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                    for (int i = 0; i < 5; i++)
                    {
                        var httpClient = httpClientFactory.CreateHallPassClient();
                        var response = await httpClient.GetAsync(uri);

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

            spy.Count.ShouldBe(50);

            // make sure calls are throttled as expected
            var spyQueue = new Queue<DateTimeOffset>(spy);
            var sortedSpy = spy.OrderBy(s => s).ToList();
            var localCounts = new List<int>();
            while (spyQueue.TryDequeue(out var callTime))
            {
                var earlyBound = callTime - TimeSpan.FromMilliseconds(250);
                var lateBound = callTime + TimeSpan.FromMilliseconds(250);
                var localCount = spy.Count(x => x >= earlyBound && x < lateBound);

                // MUST be bound by this
                localCount.ShouldBeLessThanOrEqualTo(10);

                localCounts.Add(localCount);
            }

            // should also be greater than this on average to demonstrate reasonable usage of available capacity
            localCounts.Average().ShouldBeGreaterThan(5);
        }

        [Fact]
        public async Task Respects_HallPass_API_rate_limit_for_hallpasses_from_single_instance()
        {
            // setup
            var uri = TestEndpoints.GetRandom();
            var sharedKey = uri;

            var clientId = TestConfig.GetConfiguration().HallPassClientId();
            var clientSecret = TestConfig.GetConfiguration().HallPassClientSecret();

            // configure dependency injection that uses HallPass configuration extensions
            var services = new ServiceCollection();

            services.AddHallPass(hallPass =>
            {
                // use HallPass remotely
                hallPass
                    .UseTokenBucket(uri, 1, TimeSpan.FromSeconds(5))
                    .ForMultipleInstances(clientId, clientSecret, key: uri);
            });

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            var apiFactory = serviceProvider.GetRequiredService<HallPassApiFactory>();
            var api = apiFactory.GetOrCreate(clientId, clientSecret);

            // check time
            var start = DateTimeOffset.Now;

            // call 100 times (rate limit is 100 per minute)
            for (int i = 0; i < 100; i++)
            {
                _ = await api.GetTicketsAsync(sharedKey, sharedKey, 1, TimeSpan.FromSeconds(5));
            }

            // verify time is < 1 minute
            (DateTimeOffset.Now - start).ShouldBeLessThan(TimeSpan.FromMinutes(1));

            // call one more time
            _ = await api.GetTicketsAsync(sharedKey, sharedKey, 1, TimeSpan.FromSeconds(5));

            // verify time is > 1 minute
            (DateTimeOffset.Now - start).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMinutes(1));
        }
    }
}
