using HallPass.Buckets;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass.Api
{
    internal interface IHallPassApi
    {
        Task<HallPassesResponse> GetTicketsAsync(
            string key,
            string instanceId,
            int amount,
            TimeSpan frequency,
            int capacity,
            CancellationToken cancellationToken = default);

        Task<UpdateShiftResult> UpdateShiftAsync(
            TimeSpan shiftDelta,
            string windowId,
            long shiftVersion,
            string key,
            int rate,
            TimeSpan frequency,
            int capacity,
            CancellationToken cancellationToken);
    }
}
