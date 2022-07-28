using HallPass.Api;
using HallPass.Buckets;
using HallPass.Configuration;
using LazyCache;
using NSubstitute;
using Shouldly;
using System.Collections.Concurrent;

namespace HallPass.IntegrationTests
{
    public class RemoteTokenBucketTests
    {
        [Fact]
        public async Task GetTicketAsync___should_allow_15_requests_in_14_seconds_with_TokenBucket_allowing_5_request_every_5_seconds()
        {
            var cache = new CachingService();
            var clientId = TestConfig.HallPassClientId();
            var clientSecret = TestConfig.HallPassClientSecret();
            using var httpClient = new HttpClient() { BaseAddress = new Uri("https://api.hallpass.dev") };

            var httpClientFactory = Substitute.For<IHttpClientFactory>();
            httpClientFactory
                .CreateClient(Constants.HALLPASS_API_HTTPCLIENT_NAME)
                .Returns(httpClient);

            var hallPass = new HallPassApi(cache, httpClientFactory, clientId, clientSecret);
            var bucket = new RemoteTokenBucket(hallPass, 5, TimeSpan.FromSeconds(5));

            var spy = new List<DateTimeOffset>();

            var fourteenSecondsLater = DateTimeOffset.UtcNow.AddSeconds(14);
            while (DateTimeOffset.UtcNow < fourteenSecondsLater)
            {
                var ticket = await bucket.GetTicketAsync();
                spy.Add(DateTimeOffset.Now);
            }

            var server14SecondsLater = spy.Min().AddSeconds(14);
            var requestsInTime = spy.Where(s => s <= server14SecondsLater).ToList();

            // 0: 5, 5: 10, 10: 15
            requestsInTime.Count.ShouldBe(15);
        }

        [Fact]
        public async Task GetTicketAsync___should_work_for_multiple_threads_with_single_bucket()
        {
            var cache = new CachingService();
            var clientId = TestConfig.HallPassClientId();
            var clientSecret = TestConfig.HallPassClientSecret();

            using var httpClient = new HttpClient() { BaseAddress = new Uri("https://api.hallpass.dev") };
            var httpClientFactory = Substitute.For<IHttpClientFactory>();
            httpClientFactory
                .CreateClient(Constants.HALLPASS_API_HTTPCLIENT_NAME)
                .Returns(httpClient);

            var hallPass = new HallPassApi(cache, httpClientFactory, clientId, clientSecret);
            var bucket = new RemoteTokenBucket(hallPass, 5, TimeSpan.FromSeconds(2));

            var spy = new ConcurrentBag<DateTimeOffset>();

            var tasks = Enumerable
                .Range(1, 25)
                .Select(_ => Task.Run(async () =>
                {
                    var ticket = await bucket.GetTicketAsync();
                    spy.Add(DateTimeOffset.Now);
                }))
                .ToList();

            await Task.WhenAll(tasks);

            var sevenSecondsLater = spy.Min().AddSeconds(7);
            var requestsInTime = spy.Where(s => s <= sevenSecondsLater).ToList();

            spy.Count.ShouldBe(25);
            // 0: 5, 2: 10, 4: 15, 6: 20
            requestsInTime.Count.ShouldBe(20);
        }

        [Fact]
        public async Task GetTicketAsync___should_work_for_multiple_threads_with_multiple_buckets_with_same_key_and_unique_instanceIds()
        {
            var cache = new CachingService();
            var clientId = TestConfig.HallPassClientId();
            var clientSecret = TestConfig.HallPassClientSecret();

            using var httpClient = new HttpClient() { BaseAddress = new Uri("https://api.hallpass.dev") };
            var httpClientFactory = Substitute.For<IHttpClientFactory>();
            httpClientFactory
                .CreateClient(Constants.HALLPASS_API_HTTPCLIENT_NAME)
                .Returns(httpClient);

            var spy = new ConcurrentBag<DateTimeOffset>();

            var sharedKey = Guid.NewGuid().ToString();
            var buckets = Enumerable.Range(1, 5).Select(_ =>
            {
                var localCache = new CachingService();
                var localHallPass = new HallPassApi(localCache, httpClientFactory, clientId, clientSecret);
                var bucket = new RemoteTokenBucket(localHallPass, 5, TimeSpan.FromSeconds(2), sharedKey);
                return bucket;
            });

            // each bucket gets 5 tickets for itself
            var tasks = buckets
                .SelectMany(bucket =>
                {
                    return Enumerable
                        .Range(1, 5)
                        .Select(_ => Task.Run(async () =>
                        {
                            var ticket = await bucket.GetTicketAsync();
                            spy.Add(DateTimeOffset.Now);
                        }));
                })
                .ToList();

            await Task.WhenAll(tasks);

            // we need to get the min time from the actual tickets because the clocks of the server and test runner could be out of sync
            var sevenSecondsLater = spy.Min().AddSeconds(7);
            var requestsInTime = spy.Where(s => s <= sevenSecondsLater).ToList();

            // 0: 5, 2: 10, 4: 15, 6: 20
            spy.Count.ShouldBeGreaterThan(20);
            requestsInTime.Count.ShouldBeLessThanOrEqualTo(20);
        }

        [Fact]
        public async Task GetTicketAsync___should_work_for_multiple_time_windows_for_multiple_threads()
        {
            var cache = new CachingService();
            var clientId = TestConfig.HallPassClientId();
            var clientSecret = TestConfig.HallPassClientSecret();

            using var httpClient = new HttpClient() { BaseAddress = new Uri("https://api.hallpass.dev") };
            var httpClientFactory = Substitute.For<IHttpClientFactory>();
            httpClientFactory
                .CreateClient(Constants.HALLPASS_API_HTTPCLIENT_NAME)
                .Returns(httpClient);

            var spy = new ConcurrentBag<DateTimeOffset>();

            var sharedKey = Guid.NewGuid().ToString();
            var buckets = Enumerable.Range(1, 5).Select(_ =>
            {
                var localCache = new CachingService();
                //var localHttp = new HttpClient() { BaseAddress = new Uri(TestConfig.GetConfiguration().HallPassBaseUrl()) };
                var localHallPass = new HallPassApi(localCache, httpClientFactory, clientId, clientSecret);
                var bucket = new RemoteTokenBucket(localHallPass, 5, TimeSpan.FromSeconds(2), sharedKey);
                return bucket;
            });

            // each bucket gets 5 tickets for itself
            var tasks = buckets
                .SelectMany(bucket =>
                {
                    return Enumerable
                        .Range(1, 5)
                        .Select(_ => Task.Run(async () =>
                        {
                            var ticket = await bucket.GetTicketAsync();
                            spy.Add(DateTimeOffset.Now);
                        }));
                })
                .ToList();

            await Task.WhenAll(tasks);

            // 0: 5, 2: 10, 4: 15, 6: 20
            spy.Count.ShouldBe(25);

            // check all 2-minute time windows
            foreach (var item in spy)
            {
                var twoSecondsLater = item.AddSeconds(2);
                spy.Count(s => s >= item && s < twoSecondsLater).ShouldBeLessThanOrEqualTo(5);
            }
        }
    }
}
