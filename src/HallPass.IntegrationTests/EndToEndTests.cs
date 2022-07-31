using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace HallPass.IntegrationTests
{
    public class EndToEndTests
    {
        [Fact]
        public async Task Can_make_concurrent_requests_from_multiple_instances_that_are_properly_throttled_with_LeakyBucket()
        {
            var instances = Enumerable.Range(1, 3);
            var traceId = Guid.NewGuid().ToString()[..6];
            var uri = $"{TestConfig.HallPassTestApiBaseUrl()}/leaky-bucket/10-rate-5000-milliseconds-10-capacity/{traceId}";

            var clientId = TestConfig.HallPassClientId();
            var clientSecret = TestConfig.HallPassClientSecret();
            var apiKey = TestConfig.HallPassTestApiKey();

            var tasks = instances
                .Select(_ => Task.Run(async () =>
                {
                    // configure dependency injection that uses HallPass configuration extensions
                    var services = new ServiceCollection();

                    services.AddHallPass(hallPass =>
                    {
                        // use HallPass remotely
                        hallPass
                            .UseLeakyBucket(uri, 10, TimeSpan.FromSeconds(5), 10, key: uri)
                            .ForMultipleInstances(clientId, clientSecret);
                    });

                    // make a loop of API calls to the throttled endpoint
                    var serviceProvider = services.BuildServiceProvider(validateScopes: true);
                    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                    for (int i = 0; i < 10; i++)
                    {
                        var httpClient = httpClientFactory.CreateHallPassClient();
                        httpClient.DefaultRequestHeaders.Add("hallpass-api-key", apiKey);

                        var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                    }
                }))
                .ToList();

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        [Fact(Skip = "need to wait until RemoteLeakyBucket window is shifted automatically")]
        public async Task Respects_HallPass_API_rate_limit_for_hallpasses_from_single_instance()
        {
            // setup
            var traceId = Guid.NewGuid().ToString()[..6];
            var uri = $"{TestConfig.HallPassTestApiBaseUrl()}/leaky-bucket/100-rate-60000-milliseconds-100-capacity/{traceId}";

            var clientId = TestConfig.HallPassClientId();
            var clientSecret = TestConfig.HallPassClientSecret();
            var apiKey = TestConfig.HallPassTestApiKey();

            // configure dependency injection that uses HallPass configuration extensions
            var services = new ServiceCollection();

            services.AddHallPass(hallPass =>
            {
                // use HallPass remotely
                hallPass
                    .UseLeakyBucket(uri, 100, TimeSpan.FromSeconds(60), 100, key: uri)
                    .ForMultipleInstances(clientId, clientSecret);
            });

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            // check time
            var start = DateTimeOffset.Now;

            // rate limit is 100 per minute with an initial burst of 100, so we call this 100 times in a burst expecting to be significantly earlier than a minute...
            var tasks = Enumerable.Range(1, 10)
                .Select(async _ =>
                {
                    for (int i = 0; i < 10; i++)
                    {
                        var httpClient = httpClientFactory.CreateHallPassClient();
                        httpClient.DefaultRequestHeaders.Add("hallpass-api-key", apiKey);

                        var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                    }
                })
                .ToList();

            await Task.WhenAll(tasks).ConfigureAwait(false);

            // verify time is < 1 minute
            var first100 = DateTimeOffset.Now - start;
            first100.ShouldBeLessThan(TimeSpan.FromMinutes(1));

            // ... and then 100 more times expecting to be significantly later than a minute
            tasks = Enumerable.Range(1, 10)
                .Select(async _ =>
                {
                    for (int i = 0; i < 10; i++)
                    {
                        var httpClient = httpClientFactory.CreateHallPassClient();
                        httpClient.DefaultRequestHeaders.Add("hallpass-api-key", apiKey);

                        var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                    }
                })
                .ToList();

            await Task.WhenAll(tasks).ConfigureAwait(false);

            // verify time is > 1 minute
            var second100 = DateTimeOffset.Now - start;
            second100.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMinutes(1));
        }
    }
}
