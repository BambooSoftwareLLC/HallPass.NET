using HallPass.Buckets;
using HallPass.Helpers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass
{
    internal static class TicketExtensions
    {
        public static bool IsNotYetValid(this Ticket ticket, ITimeService timeService = null)
        {
            return timeService != null
                ? ticket.ValidFrom > timeService.GetNow()
                : ticket.ValidFrom > DateTimeOffset.UtcNow;
        }

        public static bool IsExpired(this Ticket ticket, ITimeService timeService = null)
        {
            return timeService != null
                ? ticket.ValidTo <= timeService.GetNow()
                : ticket.ValidTo <= DateTimeOffset.UtcNow;
        }

        public static async Task WaitUntilValidAsync(this Ticket ticket, ITimeService timeService = null, CancellationToken cancellationToken = default)
        {
            if (timeService is null)
            {
                await Task.Delay(ticket.ValidFrom - DateTimeOffset.UtcNow, cancellationToken);
            }
            else
            {
                await timeService.DelayAsync(ticket.ValidFrom - timeService.GetNow(), cancellationToken);
            }
        }
    }
}
