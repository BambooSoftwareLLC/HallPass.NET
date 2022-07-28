using HallPass.Api;
using HallPass.Buckets;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;

namespace HallPass.Configuration
{
    internal class LeakyBucketConfigurationBuilder : IBucketConfigurationBuilder
    {
        private readonly int _leakAmount;
        private readonly TimeSpan _leakRate;
        private readonly int _capacity;

        private Func<IServiceProvider, IBucket> _factory;
        private readonly Func<HttpRequestMessage, bool> _isTriggeredBy;

        public LeakyBucketConfigurationBuilder(int leakAmount, TimeSpan leakRate, int capacity, Func<IServiceProvider, IBucket> factory, Func<HttpRequestMessage, bool> isTriggeredBy)
        {
            _leakAmount = leakAmount;
            _leakRate = leakRate;
            _capacity = capacity;
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
                    _leakAmount,
                    _leakRate,
                    _capacity,
                    key,
                    instanceId);

                return bucket;
            };

            return this;
        }

        BucketConfiguration IBucketConfigurationBuilder.Build(IServiceProvider services) => new BucketConfiguration(_factory(services), _isTriggeredBy);
    }
}
