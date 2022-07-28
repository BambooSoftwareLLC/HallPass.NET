using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass.Buckets
{
    internal class LeakyBucket : IBucket
    {
        private readonly ConcurrentQueue<Ticket> _tickets = new ConcurrentQueue<Ticket>();

        private TimeSpan _shift = TimeSpan.Zero;
        
        private readonly int _leakAmount;
        private readonly TimeSpan _leakPeriod;
        private readonly int _capacity;

        private DateTimeOffset _lastRefill = DateTimeOffset.UtcNow;
        private int _refilling = 0;

        public LeakyBucket(int leakAmount, TimeSpan leakPeriod, int capacity)
        {
            _leakAmount = leakAmount;
            _leakPeriod = leakPeriod;
            _capacity = capacity;

            // fill initial burst
            Refill(validFrom: DateTimeOffset.UtcNow, burst: true);
        }

        public async Task<Ticket> GetTicketAsync(CancellationToken cancellationToken = default)
        {
            while (true)
            {
                Ticket ticket;
                while (!_tickets.TryDequeue(out ticket))
                {
                    Refill(_lastRefill);
                }

                if (ticket.IsNotYetValid())
                    await ticket.WaitUntilValidAsync(cancellationToken: cancellationToken);

                // wait for shift outside of WaitUntilValidAsync to cover cases where a ticket is waiting in that method while the shift was updated
                if (_shift > TimeSpan.Zero)
                    await Task.Delay(_shift, cancellationToken);

                if (ticket.IsExpired(_shift))
                    continue;

                return ticket;
            }
        }

        private void Refill(DateTimeOffset validFrom, bool burst = false)
        {
            // if somebody else is already refilling, then exit early
            if (0 != Interlocked.Exchange(ref _refilling, 1))
                return;

            // refill up to full capacity, staggered by the leakPeriod
            var leakScale = _capacity / _leakAmount;
            while (_tickets.Count < _capacity)
            {
                for (int i = 0; i < _leakAmount; i++)
                {
                    _tickets.Enqueue(Ticket.New(validFrom, validFrom + _leakPeriod * leakScale, windowId: "TODO"));
                }

                if (!burst)
                    validFrom += _leakPeriod;
            }

            // update the time of last refill
            if (burst)
                validFrom += _leakPeriod;

            _lastRefill = validFrom;

            // release the lock
            Interlocked.Exchange(ref _refilling, 0);
        }

        public Task ShiftWindowAsync(Ticket ticket, CancellationToken cancellationToken = default)
        {
            // todo
            return Task.CompletedTask;
        }
    }
}
