using HallPass.Configuration;
using System;

namespace HallPass
{
    public interface IBucketConfigurationBuilder
    {
        IBucketConfigurationBuilder ForMultipleInstances(string clientId, string clientSecret);
        internal BucketConfiguration Build(IServiceProvider services);
        internal TimeSpan Frequency { get; }
    }
}
