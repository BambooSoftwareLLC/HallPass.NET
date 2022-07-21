﻿using System;
using System.Collections.Generic;
using System.Net.Http;
using HallPass.Buckets;
using HallPass.Configuration;

namespace HallPass
{
    public sealed class HallPassOptions
    {
        public static HallPassOptions Default
        {
            get
            {
                return new HallPassOptions();
            }
        }

        public static HallPassOptions API
        {
            get
            {
                var options = Default;

                // register hallpass API rate limits
                options.UseTokenBucket("https://api.hallpass.dev/oauth/token", 30, TimeSpan.FromMinutes(1));
                options.UseTokenBucket("https://api.hallpass.dev/hallpasses", 100, TimeSpan.FromMinutes(1));

                return options;
            }
        }

        private readonly List<IBucketConfigurationBuilder> _bucketConfigurationBuilders = new();
        internal IEnumerable<IBucketConfigurationBuilder> BucketConfigurationBuilders => _bucketConfigurationBuilders;

        /// <summary>
        /// Set to TRUE (default is FALSE) to configure the default HttpClient, and use it like this:
        ///     var httpClient = httpClientFactory.CreateClient();
        /// 
        /// ... instead of like this:
        ///     var httpClient = httpClientFactory.CreateHallPassClient();
        /// </summary>
        public bool UseDefaultHttpClient { get; set; } = false;
        
        public IBucketConfigurationBuilder UseTokenBucket(string uriPattern, int requests, TimeSpan duration, StringComparison stringComparison = StringComparison.OrdinalIgnoreCase)
        {
            var builder = new TokenBucketConfigurationBuilder(
                requests,
                duration,
                factory: services => new TokenBucket(requests, duration),
                isTriggeredBy: httpRequestMessage => httpRequestMessage.RequestUri.ToString().Contains(uriPattern, stringComparison));

            _bucketConfigurationBuilders.Add(builder);

            return builder;
        }

        public IBucketConfigurationBuilder UseTokenBucket(Func<HttpRequestMessage, bool> isTriggeredBy, int requests, TimeSpan duration)
        {
            var builder = new TokenBucketConfigurationBuilder(
                requests,
                duration,
                factory: services => new TokenBucket(requests, duration),
                isTriggeredBy: isTriggeredBy);

            _bucketConfigurationBuilders.Add(builder);

            return builder;
        }

        public IBucketConfigurationBuilder UseLeakyBucket(string uriPattern, int requests, TimeSpan duration, int initialBurst = 0, StringComparison stringComparison = StringComparison.OrdinalIgnoreCase)
        {
            var builder = new LeakyBucketConfigurationBuilder(
                requests,
                duration,
                initialBurst,
                factory: services => new LeakyBucket(requests, duration, initialBurst),
                isTriggeredBy: httpRequestMessage => httpRequestMessage.RequestUri.ToString().Contains(uriPattern, stringComparison));

            _bucketConfigurationBuilders.Add(builder);

            return builder;
        }

        public IBucketConfigurationBuilder UseLeakyBucket(Func<HttpRequestMessage, bool> isTriggeredBy, int requests, TimeSpan duration, int initialBurst = 0)
        {
            var builder = new LeakyBucketConfigurationBuilder(
                requests,
                duration,
                initialBurst,
                factory: services => new LeakyBucket(requests, duration, initialBurst),
                isTriggeredBy: isTriggeredBy);

            _bucketConfigurationBuilders.Add(builder);

            return builder;
        }
    }
}