using HallPass.Buckets;
using Shouldly;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace HallPass.UnitTests
{
    public class TokenBucketTests
    {
        [Fact]
        public async Task GetTicketAsync___should_allow_3_requests_in_5_seconds_with_TokenBucket_allowing_1_request_every_2_seconds()
        {
            var bucket = new TokenBucket(1, TimeSpan.FromSeconds(2));
            
            var spy = new List<DateTimeOffset>();

            var fiveSecondsLater = DateTimeOffset.UtcNow.AddSeconds(5);
            while (DateTimeOffset.UtcNow < fiveSecondsLater)
            {
                await bucket.GetTicketAsync();
                spy.Add(DateTimeOffset.UtcNow);
            }

            var requestsInTime = spy.Where(s => s <= fiveSecondsLater).ToList();
            requestsInTime.Count.ShouldBe(3);
        }

        [Fact(Skip = "takes forever")]
        public async Task GetTicketAsync___should_allow_10_requests_in_90_seconds_with_TokenBucket_allowing_5_requests_per_minute()
        {
            var bucket = new TokenBucket(5, TimeSpan.FromMinutes(1));

            var spy = new List<DateTimeOffset>();

            var ninetySecondsLater = DateTimeOffset.UtcNow.AddSeconds(90);
            while (DateTimeOffset.UtcNow < ninetySecondsLater)
            {
                await bucket.GetTicketAsync();
                spy.Add(DateTimeOffset.UtcNow);
            }

            var requestsInTime = spy.Where(s => s <= ninetySecondsLater).ToList();
            requestsInTime.Count.ShouldBe(10);
        }

        [Fact(Skip = "takes forever")]
        public async Task GetTicketAsync___should_allow_10_requests_in_90_minutes_with_TokenBucket_allowing_5_requests_per_hour()
        {
            var bucket = new TokenBucket(5, TimeSpan.FromHours(1));

            var spy = new List<DateTimeOffset>();

            var ninetyMinutesLater = DateTimeOffset.UtcNow.AddMinutes(90);
            while (DateTimeOffset.UtcNow < ninetyMinutesLater)
            {
                await bucket.GetTicketAsync();
                spy.Add(DateTimeOffset.UtcNow);
            }

            var requestsInTime = spy.Where(s => s <= ninetyMinutesLater).ToList();
            requestsInTime.Count.ShouldBe(10);
        }

        [Fact]
        public async Task GetTicketAsync___should_work_for_multiple_threads()
        {
            var bucket = new TokenBucket(5, TimeSpan.FromSeconds(2));

            var spy = new ConcurrentBag<DateTimeOffset>();

            var threeSecondsLater = DateTimeOffset.UtcNow.AddSeconds(3);

            var tasks = Enumerable
                .Range(1, 15)
                .Select(_ => Task.Run(async () =>
                {
                    await bucket.GetTicketAsync();
                    spy.Add(DateTimeOffset.UtcNow);
                }))
                .ToList();

            await Task.WhenAll(tasks);

            var requestsInTime = spy.Where(s => s <= threeSecondsLater).ToList();
            requestsInTime.Count.ShouldBe(10);
        }
    }
}
