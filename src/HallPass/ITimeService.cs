using System;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass
{
    internal interface ITimeService
    {
        DateTimeOffset GetNow();
        Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default);
        Task DelayAsync(TimeSpan timeSpan, CancellationToken cancellationToken = default);
    }
}