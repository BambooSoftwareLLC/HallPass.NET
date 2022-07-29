using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass.Buckets
{
    internal class LeakyBucket : IBucket
    {
        private readonly ConcurrentQueue<Ticket> _tickets = new ConcurrentQueue<Ticket>();

        private readonly ConcurrentDictionary<string, string> _shiftedWindowIds = new();
        private readonly ConcurrentQueue<(string WindowId, DateTimeOffset ExpireAt)> _shiftExpirations = new();
        private int _shifting = 0;
        private TimeSpan _shift = TimeSpan.Zero;

        private readonly int _rate;
        private readonly TimeSpan _frequency;
        private readonly int _capacity;

        public TimeSpan Frequency => _frequency;

        private DateTimeOffset _lastRefill = DateTimeOffset.UtcNow;
        private int _refilling = 0;

        public LeakyBucket(int rate, TimeSpan frequency, int capacity)
        {
            _rate = rate;
            _frequency = frequency;
            _capacity = capacity;

            // fill initial capacity
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
            var windowSize = _frequency * (_capacity / _rate);
            while (_tickets.Count < _capacity)
            {
                var windowId = Guid.NewGuid().ToString()[..6];
                for (int i = 0; i < _rate; i++)
                {
                    _tickets.Enqueue(Ticket.New(validFrom, validFrom + windowSize, windowId));
                }

                if (!burst)
                    validFrom += _frequency;
            }

            // update the time of last refill
            if (burst)
                validFrom += _frequency;

            _lastRefill = validFrom;

            // release the lock
            Interlocked.Exchange(ref _refilling, 0);

            // try to clean up shifts (don't need the lock here)
            while (_shiftExpirations.TryPeek(out var pair) && pair.ExpireAt <= DateTimeOffset.UtcNow)
            {
                _shiftExpirations.TryDequeue(out pair);

                if (pair.ExpireAt <= DateTimeOffset.UtcNow)
                {
                    _shiftedWindowIds.TryRemove(pair.WindowId, out _);
                }
                else
                {
                    _shiftExpirations.Enqueue(pair);
                }
            }
        }

        public Task ShiftWindowAsync(Ticket ticket, CancellationToken cancellationToken = default)
        {
            // if we can add it, that means it hasn't been processed yet
            if (_shiftedWindowIds.TryAdd(ticket.WindowId, null))
            {
                // update global shift atomically
                while (true)
                {
                    // try to take the lock
                    if (0 != Interlocked.Exchange(ref _shifting, 1))
                        continue;

                    var shift = DateTimeOffset.UtcNow - (ticket.ValidFrom + _shift);
                    _shift += shift;

                    // release lock
                    Interlocked.Exchange(ref _shifting, 0);
                    break;
                }

                // set this shift to expire to avoid infinite memory needs
                _shiftExpirations.Enqueue((ticket.WindowId, DateTimeOffset.UtcNow.AddMinutes(5)));
            }

            return Task.CompletedTask;
        }
    }
}
