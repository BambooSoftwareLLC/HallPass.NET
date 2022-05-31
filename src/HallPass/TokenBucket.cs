using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass
{
    internal class TokenBucket : IBucket
    {
        private readonly ConcurrentQueue<Ticket> _tickets = new ConcurrentQueue<Ticket>();

        private static int _refilling = 0;
        
        private DateTimeOffset _lastRefill = DateTimeOffset.MinValue;

        private readonly int _requestsPerPeriod;
        private readonly TimeSpan _periodDuration;
        private readonly ITimeService _timeService;

        public TokenBucket(int requestsPerPeriod, TimeSpan periodDuration, ITimeService timeService)
        {
            _requestsPerPeriod = requestsPerPeriod;
            _periodDuration = periodDuration;
            _timeService = timeService;
        }

        public async Task<Ticket> GetTicketAsync(CancellationToken cancellationToken = default)
        {
            Ticket ticket;
            while (!_tickets.TryDequeue(out ticket))
            {
                Refill();
            }

            if (IsNotYetValid(ticket))
                await _timeService.DelayAsync(TimeUntilValid(ticket), cancellationToken);

            return ticket;
        }

        private TimeSpan TimeUntilValid(Ticket ticket) => ticket.ValidFrom - _timeService.GetNow();

        private bool IsNotYetValid(Ticket ticket) => TimeUntilValid(ticket) > TimeSpan.Zero;

        private void Refill()
        {
            // if somebody else is already refilling, then exit early
            if (0 != Interlocked.Exchange(ref _refilling, 1))
                return;

            // if wait time is negative, then just use zero
            var waitTime = _lastRefill + _periodDuration - _timeService.GetNow();
            waitTime = waitTime.TotalMilliseconds < 0 ? TimeSpan.Zero : waitTime;

            // refill the tickets bucket
            var validFrom = waitTime > TimeSpan.Zero ? _timeService.GetNow() + waitTime : _timeService.GetNow();
            var validTo = validFrom + _periodDuration;
            for (int i = 0; i < _requestsPerPeriod; i++)
            {
                _tickets.Enqueue(Ticket.New(validFrom, validTo));
            }

            // update the time of last refill
            _lastRefill = validFrom;

            // release the lock
            Interlocked.Exchange(ref _refilling, 0);
        }
    }
}
