using HallPass.Configuration;
using Microsoft.Extensions.Logging;
using System;

namespace HallPass
{
    public interface IBucketConfigurationBuilder
    {
        internal BucketConfiguration Build(IServiceProvider services, ILogger _logger);
        internal TimeSpan Frequency { get; }
    }
}
