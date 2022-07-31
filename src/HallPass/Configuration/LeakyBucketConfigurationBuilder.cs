using HallPass.Api;
using HallPass.Buckets;
using LazyCache;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;
using System.Text;

namespace HallPass.Configuration
{
    internal class LeakyBucketConfigurationBuilder : IBucketConfigurationBuilder
    {
        private readonly int _leakAmount;
        private readonly TimeSpan _leakRate;
        private readonly int _capacity;

        private Func<IServiceProvider, Func<HttpRequestMessage, IBucket>> _factory;
        private readonly Func<HttpRequestMessage, bool> _isTriggeredBy;
        
        private readonly Func<HttpRequestMessage, string> _keySelector;
        private readonly Func<HttpRequestMessage, string> _instanceIdSelector;

        public LeakyBucketConfigurationBuilder(
            int leakAmount,
            TimeSpan leakRate,
            int capacity,
            Func<IServiceProvider, Func<HttpRequestMessage, IBucket>> factory,
            Func<HttpRequestMessage, bool> isTriggeredBy,
            Func<HttpRequestMessage, string> keySelector,
            Func<HttpRequestMessage, string> instanceIdSelector)
        {
            _leakAmount = leakAmount;
            _leakRate = leakRate;
            _capacity = capacity;
            _factory = factory;
            _isTriggeredBy = isTriggeredBy;

            var backupKey = Guid.NewGuid().ToString();
            _keySelector = EscapeString(keySelector ?? (request => backupKey));

            var backupId = Guid.NewGuid().ToString();
            _instanceIdSelector = EscapeString(instanceIdSelector ?? (request => backupId));
        }

        public IBucketConfigurationBuilder ForMultipleInstances(string clientId, string clientSecret)
        {
            _factory = services =>
            {
                var apiFactory = services.GetService<HallPassApiFactory>();

                return httpRequestMessage =>
                {
                    var bucket = new RemoteLeakyBucket(
                        apiFactory.GetOrCreate(clientId, clientSecret),
                        _leakAmount,
                        _leakRate,
                        _capacity,
                        _keySelector(httpRequestMessage),
                        _instanceIdSelector(httpRequestMessage));

                    return bucket;
                };
            };

            return this;
        }

        BucketConfiguration IBucketConfigurationBuilder.Build(IServiceProvider services)
        {
            var cache = services.GetRequiredService<IAppCache>();
            return new(_factory(services), _isTriggeredBy, _keySelector, _instanceIdSelector, cache);
        }

        private Func<HttpRequestMessage, string> EscapeString(Func<HttpRequestMessage, string> innerFunc)
        {
            return request =>
            {
                var initialString = innerFunc(request);
                var bytes = Encoding.UTF8.GetBytes(initialString);
                var base64 = Convert.ToBase64String(bytes);
                return Uri.EscapeDataString(base64);
            };
        }
    }
}
