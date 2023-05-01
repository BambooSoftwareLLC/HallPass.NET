using HallPass.Api;
using HallPass.Buckets;
using LazyCache;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Text;

namespace HallPass.Configuration
{
    internal class LeakyBucketConfigurationBuilder : IBucketConfigurationBuilder
    {
        private readonly int _rate;
        private readonly TimeSpan _frequency;
        private readonly int _capacity;

        public TimeSpan Frequency => _frequency;

        private Func<IServiceProvider, ILogger, Func<HttpRequestMessage, IBucket>> _factory;
        private readonly Func<HttpRequestMessage, bool> _isTriggeredBy;
        
        private readonly Func<HttpRequestMessage, string> _keySelector;
        private readonly Func<HttpRequestMessage, string> _instanceIdSelector;

        public LeakyBucketConfigurationBuilder(
            int rate,
            TimeSpan frequency,
            int capacity,
            Func<IServiceProvider, ILogger, Func<HttpRequestMessage, IBucket>> factory,
            Func<HttpRequestMessage, bool> isTriggeredBy,
            Func<HttpRequestMessage, string> keySelector,
            Func<HttpRequestMessage, string> instanceIdSelector)
        {
            _rate = rate;
            _frequency = frequency;
            _capacity = capacity;
            _factory = factory;
            _isTriggeredBy = isTriggeredBy;

            var backupKey = Guid.NewGuid().ToString();
            _keySelector = EscapeString(keySelector ?? (request => backupKey));

            var backupId = Guid.NewGuid().ToString();
            _instanceIdSelector = EscapeString(instanceIdSelector ?? (request => backupId));
        }

        BucketConfiguration IBucketConfigurationBuilder.Build(IServiceProvider services, ILogger logger)
        {
            var cache = services.GetRequiredService<IAppCache>();
            return new(_factory(services, logger), _isTriggeredBy, _keySelector, _instanceIdSelector, cache);
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
