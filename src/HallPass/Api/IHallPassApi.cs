using HallPass.Buckets;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass.Api
{
    internal interface IHallPassApi
    {
        Task<IReadOnlyList<Ticket>> GetTicketsAsync(
            string key,
            string instanceId,
            int amount,
            TimeSpan frequency,
            int capacity,
            CancellationToken cancellationToken = default);
    }
}
