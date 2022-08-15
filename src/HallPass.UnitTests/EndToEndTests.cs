using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace HallPass.UnitTests
{
    public class EndToEndTests
    {
        [Fact]
        public async Task Can_make_a_single_request_with_LeakyBucket()
        {
            var traceId = Guid.NewGuid().ToString()[..6];
            var uri = "https://api.ratelimited.dev/key/1-per-second";

            // configure dependency injection that uses HallPass configuration extensions
            var services = new ServiceCollection();

            services.AddHallPass(hallPass =>
            {
                // use HallPass locally
                hallPass.UseLeakyBucket(uri, 1, TimeSpan.FromSeconds(1), 1, key: traceId);
            });


            // make a single API call to the throttled endpoint
            var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateHallPassClient();
            httpClient.DefaultRequestHeaders.Add("key", traceId);
            var response = await httpClient.GetAsync(uri);

            response.EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task Can_make_looped_requests_that_are_properly_throttled()
        {
            var traceId = Guid.NewGuid().ToString()[..6];
            var uri = "https://api.ratelimited.dev/key/20-per-minute";

            // configure dependency injection that uses HallPass configuration extensions
            var services = new ServiceCollection();

            services.AddHallPass(hallPass =>
            {
                // use HallPass locally
                hallPass.UseLeakyBucket(uri, 20, TimeSpan.FromMinutes(1), 20, key: traceId);
            });

            // make a loop of API calls to the throttled endpoint
            var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateHallPassClient();
            httpClient.DefaultRequestHeaders.Add("key", traceId);

            for (int i = 0; i < 35; i++)
            {
                var response = await httpClient.GetAsync(uri);
                response.EnsureSuccessStatusCode();
            }
        }

        [Fact]
        public async Task Can_make_concurrent_requests_that_are_properly_throttled()
        {
            var traceId = Guid.NewGuid().ToString()[..6];
            var uri = "https://api.ratelimited.dev/key/20-per-second";

            // configure dependency injection that uses HallPass configuration extensions
            var services = new ServiceCollection();

            services.AddHallPass(hallPass =>
            {
                // use HallPass locally
                hallPass.UseLeakyBucket(uri, 20, TimeSpan.FromSeconds(1), 20, key: traceId);
            });

            // make a concurrent bunch of API calls to the throttled endpoint
            var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            var spy = new ConcurrentBag<DateTimeOffset>();

            var tasks = Enumerable
                .Range(1, 40)
                .Select(_ => Task.Run(async () =>
                {
                    var httpClient = httpClientFactory.CreateHallPassClient();
                    httpClient.DefaultRequestHeaders.Add("key", traceId);

                    var response = await httpClient.GetAsync(uri);
                    response.EnsureSuccessStatusCode();
                }))
                .ToList();

            await Task.WhenAll(tasks);
        }

        [Fact]
        public async Task Can_compose_multiple_rate_limits()
        {
            var traceIdA = Guid.NewGuid().ToString()[..6];
            var localUriA = "https://api.ratelimited.dev/key/10-per-second";

            var traceIdB = Guid.NewGuid().ToString()[..6];
            var localUriB = "https://api.ratelimited.dev/key/10-per-second";

            var traceIdC = Guid.NewGuid().ToString()[..6];
            var localUriC = "https://api.ratelimited.dev/key/10-per-second";

            var traceIdD = Guid.NewGuid().ToString()[..6];
            var globalUri = "https://api.ratelimited.dev/";

            // configure dependency injection that uses HallPass configuration extensions
            var services = new ServiceCollection();

            services.AddHallPass(hallPass =>
            {
                // use HallPass locally
                hallPass.UseLeakyBucket(request => request.RequestUri.ToString().Equals(localUriA), 10, TimeSpan.FromSeconds(1), 10, keySelector: r => traceIdA);
                hallPass.UseLeakyBucket(localUriB, 10, TimeSpan.FromSeconds(1), 10, key: traceIdB);
                hallPass.UseLeakyBucket(localUriC, 10, TimeSpan.FromSeconds(1), 10, key: traceIdC);
                
                // global limit for the catfact.ninja API
                hallPass.UseLeakyBucket(globalUri, 20, TimeSpan.FromSeconds(1), 20, key: traceIdD);
            });

            // make a concurrent bunch of API calls to the throttled endpoint
            var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            var tasksA = Enumerable
                .Range(1, 15)
                .Select(_ => Task.Run(async () =>
                {
                    var uri = localUriA;
                    var httpClient = httpClientFactory.CreateHallPassClient();
                    httpClient.DefaultRequestHeaders.Add("key", traceIdA);

                    var response = await httpClient.GetAsync(uri).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                }));

            var tasksB = Enumerable
                .Range(1, 15)
                .Select(_ => Task.Run(async () =>
                {
                    var uri = localUriB;
                    var httpClient = httpClientFactory.CreateHallPassClient();
                    httpClient.DefaultRequestHeaders.Add("key", traceIdB);

                    var response = await httpClient.GetAsync(uri).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                }));

            var tasksC = Enumerable
                .Range(1, 15)
                .Select(_ => Task.Run(async () =>
                {
                    var uri = localUriC;
                    var httpClient = httpClientFactory.CreateHallPassClient();
                    httpClient.DefaultRequestHeaders.Add("key", traceIdC);

                    var response = await httpClient.GetAsync(uri).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                }));

            var allTasks = tasksA.Concat(tasksB).Concat(tasksC);

            await Task.WhenAll(allTasks).ConfigureAwait(false);
        }

        [Fact]
        public async Task Can_use_default_HttpClient()
        {
            var traceId = Guid.NewGuid().ToString()[..6];
            var uri = "https://api.ratelimited.dev/key/10-per-minute";

            // configure dependency injection that uses HallPass configuration extensions
            var services = new ServiceCollection();

            services.AddHallPass(hallPass =>
            {
                hallPass.UseDefaultHttpClient = true;

                // use HallPass locally
                hallPass.UseLeakyBucket(uri, 10, TimeSpan.FromMinutes(1), 10, key: traceId);
            });

            // make a loop of API calls to the throttled endpoint
            var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("key", traceId);

            for (int i = 0; i < 19; i++)
            {
                var response = await httpClient.GetAsync(uri);
                response.EnsureSuccessStatusCode();
            }
        }

        /// <summary>
        /// Dotnet's HttpClient has a default timeout of 100 seconds. What happens if we go over that with HallPass?
        /// </summary>
        [Fact(Skip = "Need longer rate limits in test api")]
        public async Task Can_have_wait_times_longer_than_100_seconds()
        {
            var traceId = Guid.NewGuid().ToString()[..6];
            //var uri = $"{TestConfig.HallPassTestApiBaseUrl()}/leaky-bucket/1-rate-110000-milliseconds-1-capacity/{traceId}";
            var uri = string.Empty;

            // configure dependency injection that uses HallPass configuration extensions
            var services = new ServiceCollection();

            services.AddHallPass(hallPass =>
            {
                hallPass.UseDefaultHttpClient = true;

                // use HallPass locally
                hallPass.UseLeakyBucket(uri, 1, TimeSpan.FromSeconds(110), 1, key: traceId);
            });

            // make a loop of API calls to the throttled endpoint
            var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("key", traceId);

            for (int i = 0; i < 2; i++)
            {
                var response = await httpClient.GetAsync(uri);
                response.EnsureSuccessStatusCode();
            }
        }
    }
}
