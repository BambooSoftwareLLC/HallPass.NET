using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass.Buckets
{
    internal class LeakyBucket : IBucket
    {
        private readonly ConcurrentQueue<Ticket> _tickets = new ConcurrentQueue<Ticket>();
        
        private readonly int _requestsPerPeriod;
        private readonly TimeSpan _periodDuration;

        private DateTimeOffset _lastRefill = DateTimeOffset.UtcNow;
        private int _refilling = 0;

        private const int MINIMUM_REFILL_QUANTITY = 10;

        public LeakyBucket(int requestsPerPeriod, TimeSpan periodDuration, int initialBurst = 0)
        {
            _requestsPerPeriod = requestsPerPeriod;
            _periodDuration = periodDuration;

            // fill initial burst
            var validFrom = DateTimeOffset.UtcNow;
            for (int i = 0; i < initialBurst; i++)
            {
                _tickets.Enqueue(Ticket.New(validFrom, validFrom + _periodDuration));
            }

            // update the time of last refill
            _lastRefill = validFrom;
        }

        public async Task<Ticket> GetTicketAsync(CancellationToken cancellationToken = default)
        {
            Ticket ticket;
            while (true)
            {
                while (!_tickets.TryDequeue(out ticket))
                {
                    Refill();
                }

                if (ticket.IsNotYetValid())
                    await ticket.WaitUntilValidAsync(cancellationToken: cancellationToken);

                if (!ticket.IsExpired())
                    break;
            }

            return ticket;
        }

        private void Refill()
        {
            // if somebody else is already refilling, then exit early
            if (0 != Interlocked.Exchange(ref _refilling, 1))
                return;

            DateTimeOffset validFrom = _lastRefill;
            var perTicketStagger = _periodDuration / _requestsPerPeriod;

            var refillQuantity = Math.Max(MINIMUM_REFILL_QUANTITY, _requestsPerPeriod);
            for (int i = 0; i < refillQuantity; i++)
            {
                validFrom += perTicketStagger;
                _tickets.Enqueue(Ticket.New(validFrom, validFrom + _periodDuration));
            }

            // update the time of last refill
            _lastRefill = validFrom;

            // release the lock
            Interlocked.Exchange(ref _refilling, 0);
        }
    }
}
