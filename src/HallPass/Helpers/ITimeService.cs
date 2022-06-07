using System;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass.Helpers
{
    internal interface ITimeService
    {
        DateTimeOffset GetNow();
        TimeSpan GetDuration(TimeSpan timeSpan);
        Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default);
        Task DelayAsync(TimeSpan timeSpan, CancellationToken cancellationToken = default);
    }
}