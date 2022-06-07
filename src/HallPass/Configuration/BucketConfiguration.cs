using HallPass.Buckets;
using System;
using System.Net.Http;

namespace HallPass.Configuration
{
    internal sealed class BucketConfiguration
    {
        public IBucket Bucket { get; }

        private readonly Func<HttpRequestMessage, bool> _isTriggeredBy;

        public BucketConfiguration(IBucket bucket, Func<HttpRequestMessage, bool> isTriggeredBy)
        {
            Bucket = bucket;
            _isTriggeredBy = isTriggeredBy;
        }

        public bool IsTriggeredBy(HttpRequestMessage httpRequestMessage) => _isTriggeredBy.Invoke(httpRequestMessage);
    }
}
