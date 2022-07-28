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
    public class LeakyBucketTests
    {
        [Fact]
        public async Task GetTicketAsync___should_allow_for_bursts_when_configured()
        {
            var bucket = new LeakyBucket(rate: 1, frequency: TimeSpan.FromMilliseconds(500), capacity: 5);

            var spy = new List<DateTimeOffset>();

            for (int i = 0; i < 10; i++)
            {
                await bucket.GetTicketAsync();
                spy.Add(DateTimeOffset.UtcNow);
            }

            // first 5 should have been bursted
            for (int i = 0; i < 4; i++)
            {
                var current = spy[i];
                var next = spy[i + 1];

                (next - current).ShouldBe(TimeSpan.Zero, TimeSpan.FromMilliseconds(30));
            }

            // next 5 should be spread out roughly every 500 milliseconds
            var diffs = new List<TimeSpan>();
            for (int i = 4; i < 9; i++)
            {
                var current = spy[i];
                var next = spy[i + 1];

                (next - current).ShouldBe(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(30));

                diffs.Add(next - current - TimeSpan.FromMilliseconds(500));
            }

            diffs.Select(d => d.TotalMilliseconds).Average().ShouldBeLessThan(10);
        }

        [Fact]
        public async Task GetTicketAsync___should_work_for_multiple_threads_With_bursting()
        {
            var bucket = new LeakyBucket(rate: 1, frequency: TimeSpan.FromMilliseconds(500), capacity: 5);
            var spy = new ConcurrentBag<DateTimeOffset>();

            var tasks = Enumerable.Range(1, 10)
                .Select(async _ =>
                {
                    await bucket.GetTicketAsync();
                    spy.Add(DateTimeOffset.UtcNow);
                })
                .ToList();

            await Task.WhenAll(tasks);

            var sortedSpy = spy.OrderBy(s => s).ToList();

            // first 5 should have been bursted
            for (int i = 0; i < 4; i++)
            {
                var current = sortedSpy[i];
                var next = sortedSpy[i + 1];

                (next - current).ShouldBe(TimeSpan.Zero, TimeSpan.FromMilliseconds(30));
            }

            // next 5 should be spread out roughly every 500 milliseconds
            var diffs = new List<TimeSpan>();
            for (int i = 4; i < 9; i++)
            {
                var current = sortedSpy[i];
                var next = sortedSpy[i + 1];

                (next - current).ShouldBe(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(30));

                diffs.Add(next - current - TimeSpan.FromMilliseconds(500));
            }

            diffs.Select(d => d.TotalMilliseconds).Average().ShouldBeLessThan(10);
        }
    }
}
