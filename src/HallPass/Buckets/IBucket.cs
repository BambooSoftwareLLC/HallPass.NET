using System.Threading;
using System.Threading.Tasks;

namespace HallPass.Buckets
{
    internal interface IBucket
    {
        Task<Ticket> GetTicketAsync(CancellationToken cancellationToken = default);
    }
}
