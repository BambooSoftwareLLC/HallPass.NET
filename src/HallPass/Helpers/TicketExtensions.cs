using HallPass.Buckets;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass
{
    internal static class TicketExtensions
    {
        public static bool IsNotYetValid(this Ticket ticket) => ticket.ValidFrom > DateTimeOffset.UtcNow;
        public static bool IsExpired(this Ticket ticket, TimeSpan _shift) => ticket.ValidTo + _shift <= DateTimeOffset.UtcNow;

        public static async Task WaitUntilValidAsync(this Ticket ticket, CancellationToken cancellationToken = default)
        {
            await Task.Delay(ticket.ValidFrom - DateTimeOffset.UtcNow, cancellationToken);
        }
    }
}
