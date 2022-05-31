using System;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass
{
    public interface IHallMonitor
    {
        Task<T> RequestAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default);
    }
}
