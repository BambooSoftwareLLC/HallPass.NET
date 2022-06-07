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
            response.EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task Can_make_looped_requests_that_are_properly_throttled()
        {
            //var uri = TestEndpoints.GetRandom();
            var uri = TestEndpoints.Get(0);

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
                response.EnsureSuccessStatusCode();

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
                    response.EnsureSuccessStatusCode();

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
    }
}
