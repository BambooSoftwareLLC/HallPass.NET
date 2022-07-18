using HallPass.Api;
using HallPass.Buckets;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;

namespace HallPass.Configuration
{
    internal class LeakyBucketConfigurationBuilder : IBucketConfigurationBuilder
    {
        private readonly int _requests;
        private readonly TimeSpan _duration;
        private readonly int _initialBurst;

        private Func<IServiceProvider, IBucket> _factory;
        private readonly Func<HttpRequestMessage, bool> _isTriggeredBy;

        public LeakyBucketConfigurationBuilder(int requests, TimeSpan duration, int initialBurst, Func<IServiceProvider, IBucket> factory, Func<HttpRequestMessage, bool> isTriggeredBy)
        {
            _requests = requests;
            _duration = duration;
            _initialBurst = initialBurst;
            _factory = factory;
            _isTriggeredBy = isTriggeredBy;
        }

        public IBucketConfigurationBuilder ForMultipleInstances(string clientId, string clientSecret, string key = null, string instanceId = null)
        {
            _factory = services =>
            {
                var apiFactory = services.GetService<HallPassApiFactory>();
                var bucket = new RemoteLeakyBucket(
                    apiFactory.GetOrCreate(clientId, clientSecret),
                    _requests,
                    _duration,
                    _initialBurst,
                    key,
                    instanceId);

                return bucket;
            };

            return this;
        }

        BucketConfiguration IBucketConfigurationBuilder.Build(IServiceProvider services) => new BucketConfiguration(_factory(services), _isTriggeredBy);
    }
}
