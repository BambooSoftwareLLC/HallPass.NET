using HallPass.Api;
using HallPass.Buckets;
using HallPass.Configuration;
using HallPass.Helpers;
using LazyCache;
using NSubstitute;
using Shouldly;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace HallPass.IntegrationTests
{
    public class RemoteLeakyBucketTests
    {
        [Fact]
        public async Task GetTicketAsync___should_allow_15_requests_in_14_seconds_with_TokenBucket_allowing_5_request_every_5_seconds()
        {
            var cache = new CachingService();
            var clientId = TestConfig.GetConfiguration().HallPassClientId();
            var clientSecret = TestConfig.GetConfiguration().HallPassClientSecret();
            using var httpClient = new HttpClient() { BaseAddress = new Uri("https://api.hallpass.dev") };

            var httpClientFactory = Substitute.For<IHttpClientFactory>();
            httpClientFactory
                .CreateClient(Constants.HALLPASS_API_HTTPCLIENT_NAME)
                .Returns(httpClient);

            var hallPass = new HallPassApi(cache, httpClientFactory, clientId, clientSecret);
            var bucket = new RemoteLeakyBucket(hallPass, 5, TimeSpan.FromSeconds(5), 0);

            var spy = new List<DateTimeOffset>();

            while (spy.Count < 25)
            {
                var ticket = await bucket.GetTicketAsync();
                spy.Add(DateTimeOffset.Now);
            }

            var orderedSpy = new Queue<DateTimeOffset>(spy.OrderBy(s => s));
            var stagger = TimeSpan.FromSeconds(5) / 5;
            var buffer = TimeSpan.FromMilliseconds(50);
            var current = orderedSpy.Dequeue();
            while (orderedSpy.TryDequeue(out var next))
            {
                (next - current).ShouldBeGreaterThanOrEqualTo(stagger - buffer);
                current = next;
            }
        }

        [Fact]
        public async Task GetTicketAsync___should_work_for_multiple_threads_with_single_bucket()
        {
            var cache = new CachingService();
            var clientId = TestConfig.GetConfiguration().HallPassClientId();
            var clientSecret = TestConfig.GetConfiguration().HallPassClientSecret();

            using var httpClient = new HttpClient() { BaseAddress = new Uri("https://api.hallpass.dev") };
            var httpClientFactory = Substitute.For<IHttpClientFactory>();
            httpClientFactory
                .CreateClient(Constants.HALLPASS_API_HTTPCLIENT_NAME)
                .Returns(httpClient);

            var hallPass = new HallPassApi(cache, httpClientFactory, clientId, clientSecret);
            var bucket = new RemoteLeakyBucket(hallPass, 5, TimeSpan.FromSeconds(2), 0);

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

            spy.Count.ShouldBe(25);

            var orderedSpy = new Queue<DateTimeOffset>(spy.OrderBy(s => s));
            var stagger = TimeSpan.FromSeconds(2) / 5;
            var current = orderedSpy.Dequeue();
            while (orderedSpy.TryDequeue(out var next))
            {
                var buffer = TimeSpan.FromMilliseconds(50);
                (next - current).ShouldBeGreaterThanOrEqualTo(stagger - buffer);
                current = next;
            }
        }

        [Fact]
        public async Task GetTicketAsync___should_work_for_multiple_threads_with_multiple_buckets_with_same_key_and_unique_instanceIds()
        {
            var cache = new CachingService();
            var clientId = TestConfig.GetConfiguration().HallPassClientId();
            var clientSecret = TestConfig.GetConfiguration().HallPassClientSecret();

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
                var bucket = new RemoteLeakyBucket(localHallPass, 5, TimeSpan.FromSeconds(2), 0, sharedKey);
                //var bucket = new RemoteLeakyBucket(localHallPass, 5, TimeSpan.FromMinutes(1), 0, sharedKey);
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
            var clientId = TestConfig.GetConfiguration().HallPassClientId();
            var clientSecret = TestConfig.GetConfiguration().HallPassClientSecret();

            using var httpClient = new HttpClient() { BaseAddress = new Uri("https://api.hallpass.dev") };
            //using var httpClient = new HttpClient() { BaseAddress = new Uri("https://localhost:55004") };
            var httpClientFactory = Substitute.For<IHttpClientFactory>();
            httpClientFactory
                .CreateClient(Constants.HALLPASS_API_HTTPCLIENT_NAME)
                .Returns(httpClient);

            var spy = new ConcurrentBag<DateTimeOffset>();
            var tickets = new ConcurrentBag<(Ticket Ticket, string InstanceId)>();

            var sharedKey = Guid.NewGuid().ToString();
            var buckets = Enumerable.Range(1, 5).Select(_ =>
            {
                var localCache = new CachingService();
                var localHallPass = new HallPassApi(localCache, httpClientFactory, clientId, clientSecret);
                var bucket = new RemoteLeakyBucket(localHallPass, 5, TimeSpan.FromSeconds(2), 0, sharedKey);
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
                            tickets.Add((ticket, bucket.InstanceId));
                        }));
                })
                .ToList();

            await Task.WhenAll(tasks);

            // 0: 5, 2: 10, 4: 15, 6: 20
            spy.Count.ShouldBe(25);

            // check all 2-second time windows
            var orderedSpy = new Queue<DateTimeOffset>(spy.OrderBy(x => x));
            var current = orderedSpy.Dequeue();
            var stagger = TimeSpan.FromSeconds(2) / 5;
            while (orderedSpy.TryDequeue(out var next))
            {
                var buffer = TimeSpan.FromMilliseconds(50);
                (next - current).ShouldBeGreaterThanOrEqualTo(stagger - buffer);
                current = next;
            }
        }
    }
}
