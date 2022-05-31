using System.Threading;
using System.Threading.Tasks;

namespace HallPass
{
    internal interface IBucket
    {
        Task<Ticket> GetTicketAsync(CancellationToken cancellationToken = default);
    }
}
