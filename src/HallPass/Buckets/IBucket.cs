using System;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass.Buckets
{
    internal interface IBucket
    {
        Task<Ticket> GetTicketAsync(CancellationToken cancellationToken = default);
        Task ShiftWindowAsync(Ticket ticket, CancellationToken cancellationToken = default);
        public TimeSpan Frequency { get; }
    }
}
