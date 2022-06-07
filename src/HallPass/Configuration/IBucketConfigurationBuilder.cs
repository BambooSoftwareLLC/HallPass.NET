using HallPass.Configuration;
using System;

namespace HallPass
{
    public interface IBucketConfigurationBuilder
    {
        IBucketConfigurationBuilder ForMultipleInstances(string clientId, string clientSecret, string key = null, string instanceId = null);
        internal BucketConfiguration Build(IServiceProvider services);
    }
}
