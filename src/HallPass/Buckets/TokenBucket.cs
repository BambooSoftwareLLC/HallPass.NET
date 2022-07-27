using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass.Buckets
{
    internal sealed class TokenBucket : IBucket
    {
        private readonly ConcurrentQueue<Ticket> _tickets = new ConcurrentQueue<Ticket>();

        private int _refilling = 0;

        private DateTimeOffset _lastRefill = DateTimeOffset.MinValue;

        private readonly int _requestsPerPeriod;
        private readonly TimeSpan _periodDuration;

        public TokenBucket(int requestsPerPeriod, TimeSpan periodDuration)
        {
            _requestsPerPeriod = requestsPerPeriod;
            _periodDuration = periodDuration;
        }

        public async Task<Ticket> GetTicketAsync(CancellationToken cancellationToken = default)
        {
            while (true)
            {
                Ticket ticket;
                while (!_tickets.TryDequeue(out ticket))
                {
                    Refill();
                }

                if (ticket.IsNotYetValid())
                    await ticket.WaitUntilValidAsync(cancellationToken);

                if (ticket.IsExpired())
                    continue;

                return ticket;
            }
        }

        private void Refill()
        {
            // if somebody else is already refilling, then exit early
            if (0 != Interlocked.Exchange(ref _refilling, 1))
                return;

            // if wait time is negative, then just use zero
            var waitTime = _lastRefill + _periodDuration - DateTimeOffset.UtcNow;
            waitTime = waitTime.TotalMilliseconds < 0 ? TimeSpan.Zero : waitTime;

            // refill the tickets bucket
            var validFrom = waitTime > TimeSpan.Zero ? DateTimeOffset.UtcNow + waitTime : DateTimeOffset.UtcNow;
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
