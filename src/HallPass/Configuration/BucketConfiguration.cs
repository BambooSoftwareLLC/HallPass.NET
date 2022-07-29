using HallPass.Buckets;
using LazyCache;
using System;
using System.Net.Http;

namespace HallPass.Configuration
{
    internal sealed class BucketConfiguration
    {
        private readonly Func<HttpRequestMessage, IBucket> _builder;

        private readonly Func<HttpRequestMessage, bool> _isTriggeredBy;
        private readonly Func<HttpRequestMessage, string> _keySelector;
        private readonly Func<HttpRequestMessage, string> _instanceIdSelector;
        private readonly IAppCache _cache;

        public BucketConfiguration(
            Func<HttpRequestMessage, IBucket> builder,
            Func<HttpRequestMessage, bool> isTriggeredBy,
            Func<HttpRequestMessage, string> keySelector,
            Func<HttpRequestMessage, string> instanceIdSelector,
            IAppCache cache)
        {
            _builder = builder;
            _isTriggeredBy = isTriggeredBy;
            _keySelector = keySelector;
            _instanceIdSelector = instanceIdSelector;
            _cache = cache;
        }

        public bool IsTriggeredBy(HttpRequestMessage httpRequestMessage) => _isTriggeredBy.Invoke(httpRequestMessage);
        public IBucket GetOrBuild(HttpRequestMessage httpRequestMessage)
        {
            var cacheKey = $"bucket-instance-{_keySelector(httpRequestMessage)}";
            return _cache.GetOrAdd(cacheKey, entry =>
            {
                var bucket = _builder(httpRequestMessage);
                entry.SlidingExpiration = bucket.Frequency * 10;
                return bucket;
            });
        }
    }
}
