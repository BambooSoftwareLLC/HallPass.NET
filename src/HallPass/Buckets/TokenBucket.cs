using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass.Buckets
{
    internal sealed class TokenBucket : IBucket
    {
        private readonly ConcurrentQueue<Ticket> _tickets = new();

        private readonly ConcurrentDictionary<string, TimeSpan> _shiftedWindowIds = new();
        private TimeSpan _shift = TimeSpan.Zero;
        private int _shifting = 0;
        private readonly ConcurrentQueue<(string WindowId, DateTimeOffset ExpireAt)> _shiftExpirations = new();

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

                // wait for shift outside of WaitUntilValidAsync to cover cases where a ticket is waiting in that method while the shift was updated
                if (_shift > TimeSpan.Zero)
                    await Task.Delay(_shift, cancellationToken);

                if (ticket.IsExpired(_shift))
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
            var windowId = Guid.NewGuid().ToString()[..6];
            for (int i = 0; i < _requestsPerPeriod; i++)
            {
                _tickets.Enqueue(Ticket.New(validFrom, validTo, windowId));
            }

            // update the time of last refill
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
            var shift = DateTimeOffset.UtcNow - (ticket.ValidFrom + _shift);
            if (_shiftedWindowIds.TryAdd(ticket.WindowId, shift))
            {
                // update global shift atomically
                while (true)
                {
                    if (0 != Interlocked.Exchange(ref _shifting, 1))
                        continue;

                    _shift += shift;
                    Interlocked.Exchange(ref _shifting, 0);
                    break;
                }

                // set this shift to expire to avoid infinite memory needs
                _shiftExpirations.Enqueue((ticket.WindowId, DateTimeOffset.UtcNow + _periodDuration * 2));
            }

            return Task.CompletedTask;
        }
    }
}
