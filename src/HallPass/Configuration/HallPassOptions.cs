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
        public static HallPassOptions Default
        {
            get
            {
                var options = new HallPassOptions();

                options.DurationBuffer = DurationBuffers.Moderate;

                return options;
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

        public Func<TimeSpan, TimeSpan> DurationBuffer { get; set; }

        /// <summary>
        /// Set to TRUE (default is FALSE) to configure the default HttpClient, and use it like this:
        ///     var httpClient = httpClientFactory.CreateClient();
        /// 
        /// ... instead of like this:
        ///     var httpClient = httpClientFactory.CreateHallPassClient();
        /// </summary>
        public bool UseDefaultHttpClient { get; set; } = false;
        
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