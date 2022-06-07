using HallPass.Api;
using HallPass.Helpers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass.Buckets
{
    internal sealed class RemoteTokenBucket : IBucket
    {
        private readonly ConcurrentSortedStack<Ticket> _tickets = new(Comparer<Ticket>.Create((a, b) => a.ValidFrom.CompareTo(b.ValidFrom)));
        private readonly ITimeService _timeService;
        private readonly IHallPassApi _hallPass;

        private readonly int _requestsPerPeriod;
        private readonly TimeSpan _periodDuration;
        private readonly string _key;
        private readonly string _instanceId;

        public RemoteTokenBucket(ITimeService timeService, IHallPassApi hallPass, int requestsPerPeriod, TimeSpan periodDuration, string key = null, string instanceId = null)
        {
            _timeService = timeService;
            _hallPass = hallPass;
            _requestsPerPeriod = requestsPerPeriod;
            _periodDuration = periodDuration * 1.05;
            _key = key ?? Guid.NewGuid().ToString();
            _instanceId = instanceId ?? Guid.NewGuid().ToString();
        }

        public async Task<Ticket> GetTicketAsync(CancellationToken cancellationToken = default)
        {
            Ticket ticket;

            while (true)
            {
                // if there aren't any tickets to take, then wait for a refill
                while (!_tickets.TryPop(out ticket))
                {
                    await RefillAsync(cancellationToken);
                }

                // if the ticket isn't yet valid, then wait for it to become valid
                if (IsNotYetValid(ticket))
                    await _timeService.DelayAsync(TimeUntilValid(ticket), cancellationToken);

                // if the ticket has already expired, then we need to try to get another ticket
                if (!IsExpired(ticket))
                    break;
            }

            return ticket;
        }

        // This is the only method that changes from the in-memory version, from sync to async.
        // Could we lump these all together and just provide custom implementations of RefillAsync?
        // This could even serve as the basis of most bucket implementations, where the fancy logic is all in refill.
        private async Task RefillAsync(CancellationToken cancellationToken)
        {
            var tickets = await _hallPass.GetTicketsAsync(_key, _instanceId, _requestsPerPeriod, _timeService.GetDuration(_periodDuration), cancellationToken);
            _tickets.Add(tickets);
        }

        // these methods are identical to local version and could be shared in base class
        private TimeSpan TimeUntilValid(Ticket ticket) => ticket.ValidFrom - _timeService.GetNow();
        private bool IsNotYetValid(Ticket ticket) => TimeUntilValid(ticket) > TimeSpan.Zero;
        private bool IsExpired(Ticket ticket) => ticket.ValidTo <= _timeService.GetNow();
    }
}
