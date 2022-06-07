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

            // make sure calls are throttled as expected
            var fifteenSecondsLater = spy.Min().AddSeconds(15);
            var requestsInTime = spy.Where(s => s < fifteenSecondsLater).ToList();

            spy.Count.ShouldBe(50);

            // 0: 10, 5: 20, 10: 30
            requestsInTime.Count.ShouldBeInRange(20, 30);
        }
    }
}
