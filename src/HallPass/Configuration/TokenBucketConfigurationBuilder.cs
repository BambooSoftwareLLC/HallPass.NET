using HallPass.Api;
using HallPass.Buckets;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;

namespace HallPass.Configuration
{
    internal sealed class TokenBucketConfigurationBuilder : IBucketConfigurationBuilder
    {
        private readonly int _requests;
        private readonly TimeSpan _duration;

        private Func<IServiceProvider, IBucket> _factory;
        private readonly Func<HttpRequestMessage, bool> _isTriggeredBy;

        internal TokenBucketConfigurationBuilder(
            int requests,
            TimeSpan duration,
            Func<IServiceProvider, IBucket> factory,
            Func<HttpRequestMessage, bool> isTriggeredBy)
        {
            _requests = requests;
            _duration = duration;
            _factory = factory;
            _isTriggeredBy = isTriggeredBy;
        }

        public IBucketConfigurationBuilder ForMultipleInstances(string clientId, string clientSecret, string key = null, string instanceId = null)
        {
            _factory = services =>
            {
                var apiFactory = services.GetService<HallPassApiFactory>();
                var bucket = new RemoteTokenBucket(
                    apiFactory.GetOrCreate(clientId, clientSecret),
                    _requests,
                    _duration,
                    key,
                    instanceId);

                return bucket;
            };

            return this;
        }

        BucketConfiguration IBucketConfigurationBuilder.Build(IServiceProvider services) => new BucketConfiguration(_factory(services), _isTriggeredBy);
    }
}
