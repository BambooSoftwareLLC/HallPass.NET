using System;
using System.Collections.Generic;
using System.Net.Http;
using HallPass.Buckets;
using HallPass.Configuration;
using HallPass.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace HallPass
{
    public sealed class HallPassOptions
    {
        public static HallPassOptions Default => new();

        private readonly List<IBucketConfigurationBuilder> _bucketConfigurationBuilders = new();
        internal IEnumerable<IBucketConfigurationBuilder> BucketConfigurationBuilders => _bucketConfigurationBuilders;

        public IBucketConfigurationBuilder UseTokenBucket(string uriPattern, int requests, TimeSpan duration)
        {
            var builder = new TokenBucketConfigurationBuilder(
                requests,
                duration,
                factory: services => new TokenBucket(requests, duration, services.GetService<ITimeService>()),
                isTriggeredBy: httpRequestMessage => httpRequestMessage.RequestUri.ToString().Contains(uriPattern));

            _bucketConfigurationBuilders.Add(builder);

            return builder;
        }

        public IBucketConfigurationBuilder UseTokenBucket(Func<HttpRequestMessage, bool> isTriggeredBy, int requests, TimeSpan duration)
        {
            var builder = new TokenBucketConfigurationBuilder(
                requests,
                duration,
                factory: services => new TokenBucket(requests, duration, services.GetService<ITimeService>()),
                isTriggeredBy: isTriggeredBy);

            _bucketConfigurationBuilders.Add(builder);

            return builder;
        }
    }
}