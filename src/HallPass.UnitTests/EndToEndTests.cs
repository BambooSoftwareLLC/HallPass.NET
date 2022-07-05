using HallPass.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace HallPass.UnitTests
{
    public class EndToEndTests
    {
        [Fact]
        public async Task Can_make_a_single_request()
        {
            var uri = TestEndpoints.GetRandom();

            // configure dependency injection that uses HallPass configuration extensions
            var services = new ServiceCollection();

            services.AddHallPass(hallPass =>
            {
                // use HallPass locally
                hallPass.UseTokenBucket(uri, 10, TimeSpan.FromSeconds(5));
            });


            // make a single API call to the throttled endpoint
            var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateHallPassClient();
            var response = await httpClient.GetAsync(uri);

            // make sure nothing blows up
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"URI: {uri}");
            }
        }

        [Fact]
        public async Task Can_make_looped_requests_that_are_properly_throttled()
        {
            var uri = TestEndpoints.GetRandom();

            // configure dependency injection that uses HallPass configuration extensions
            var services = new ServiceCollection();

            services.AddHallPass(hallPass =>
            {
                // use HallPass locally
                hallPass.UseTokenBucket(uri, 10, TimeSpan.FromSeconds(5));
            });

            // make a loop of API calls to the throttled endpoint
            var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateHallPassClient();

            var spy = new List<DateTimeOffset>();

            var fourteenSecondsLater = DateTimeOffset.Now.AddSeconds(14);
            while (DateTimeOffset.Now < fourteenSecondsLater)
            {
                var response = await httpClient.GetAsync(uri);

                // make sure nothing blows up
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"URI: {uri}");
                }

                spy.Add(DateTimeOffset.Now);
            }

            // make sure calls are throttled as expected
            var server14SecondsLater = spy.Min().AddSeconds(14);
            var requestsInTime = spy.Where(s => s <= server14SecondsLater).ToList();

            // 0: 10, 5: 20, 10: 30
            requestsInTime.Count.ShouldBe(30);
        }

        [Fact]
        public async Task Can_make_concurrent_requests_that_are_properly_throttled()
        {
            var uri = TestEndpoints.GetRandom();

            // configure dependency injection that uses HallPass configuration extensions
            var services = new ServiceCollection();

            services.AddHallPass(hallPass =>
            {
                // use HallPass locally
                hallPass.UseTokenBucket(uri, 10, TimeSpan.FromSeconds(5));
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
                    var response = await httpClient.GetAsync(uri);

                    // make sure nothing blows up
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"URI: {uri}");
                    }

                    spy.Add(DateTimeOffset.Now);

                    return response;
                }))
                .ToList();

            var responses = (await Task.WhenAll(tasks)).ToList();
            responses.Count.ShouldBe(40);
            responses.Count(r => r.IsSuccessStatusCode).ShouldBe(40);

            // make sure calls are throttled as expected
            var fourteenSecondsLater = spy.Min().AddSeconds(14);
            var requestsInTime = spy.Where(s => s <= fourteenSecondsLater).ToList();

            spy.Count.ShouldBe(40);

            // 0: 10, 5: 20, 10: 30
            requestsInTime.Count.ShouldBe(30);
        }

        [Fact]
        public async Task Can_compose_multiple_rate_limits()
        {
            var catEndpoints = new[]
            {
                "https://catfact.ninja/fact",
                "https://catfact.ninja/facts",
                "https://catfact.ninja/breeds",
            };

            //var uri = TestEndpoints.GetRandom();

            // configure dependency injection that uses HallPass configuration extensions
            var services = new ServiceCollection();

            services.AddHallPass(hallPass =>
            {
                // use HallPass locally
                hallPass.UseTokenBucket(request => request.RequestUri.ToString().Equals("https://catfact.ninja/fact"), 10, TimeSpan.FromSeconds(3));
                hallPass.UseTokenBucket("https://catfact.ninja/facts", 10, TimeSpan.FromSeconds(3));
                hallPass.UseTokenBucket("https://catfact.ninja/breeds", 10, TimeSpan.FromSeconds(3));
                
                // global limit for the catfact.ninja API
                hallPass.UseTokenBucket("https://catfact.ninja", 20, TimeSpan.FromSeconds(3));
            });

            // make a concurrent bunch of API calls to the throttled endpoint
            var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            var spy = new ConcurrentBag<DateTimeOffset>();

            var factTasks = Enumerable
                .Range(1, 15)
                .Select(_ => Task.Run(async () =>
                {
                    var uri = "https://catfact.ninja/fact";
                    var httpClient = httpClientFactory.CreateHallPassClient();
                    var response = await httpClient.GetAsync(uri);

                    // make sure nothing blows up
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"URI: {uri}");
                    }

                    spy.Add(DateTimeOffset.Now);

                    return response;
                }));

            var factsTasks = Enumerable
                .Range(1, 15)
                .Select(_ => Task.Run(async () =>
                {
                    var uri = "https://catfact.ninja/facts";
                    var httpClient = httpClientFactory.CreateHallPassClient();
                    var response = await httpClient.GetAsync(uri);

                    // make sure nothing blows up
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"URI: {uri}");
                    }

                    spy.Add(DateTimeOffset.Now);

                    return response;
                }));

            var breedsTasks = Enumerable
                .Range(1, 15)
                .Select(_ => Task.Run(async () =>
                {
                    var uri = "https://catfact.ninja/breeds";
                    var httpClient = httpClientFactory.CreateHallPassClient();
                    var response = await httpClient.GetAsync(uri);

                    // make sure nothing blows up
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"URI: {uri}");
                    }

                    spy.Add(DateTimeOffset.Now);

                    return response;
                }));

            var allTasks = factTasks.Concat(factsTasks).Concat(breedsTasks);

            var responses = (await Task.WhenAll(allTasks)).ToList();
            responses.Count.ShouldBe(45);
            responses.Count(r => r.IsSuccessStatusCode).ShouldBe(45);

            // make sure calls are throttled as expected
            var fiveSecondsLater = spy.Min().AddSeconds(5);
            var requestsInTime = spy.Where(s => s <= fiveSecondsLater).ToList();

            spy.Count.ShouldBe(45);

            // 0: 20, 3: 40
            requestsInTime.Count.ShouldBe(40);
        }

        [Fact]
        public async Task Can_use_default_HttpClient()
        {
            var uri = TestEndpoints.GetRandom();

            // configure dependency injection that uses HallPass configuration extensions
            var services = new ServiceCollection();

            services.AddHallPass(hallPass =>
            {
                hallPass.UseDefaultHttpClient = true;

                // use HallPass locally
                hallPass.UseTokenBucket(uri, 10, TimeSpan.FromSeconds(5));
            });

            // make a loop of API calls to the throttled endpoint
            var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient();

            var spy = new List<DateTimeOffset>();

            var fourteenSecondsLater = DateTimeOffset.Now.AddSeconds(14);
            while (DateTimeOffset.Now < fourteenSecondsLater)
            {
                var response = await httpClient.GetAsync(uri);

                // make sure nothing blows up
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"URI: {uri}");
                }

                spy.Add(DateTimeOffset.Now);
            }

            // make sure calls are throttled as expected
            var server14SecondsLater = spy.Min().AddSeconds(14);
            var requestsInTime = spy.Where(s => s <= server14SecondsLater).ToList();

            // 0: 10, 5: 20, 10: 30
            requestsInTime.Count.ShouldBe(30);
        }
    }
}
