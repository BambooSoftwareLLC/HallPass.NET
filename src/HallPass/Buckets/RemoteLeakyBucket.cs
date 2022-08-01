using HallPass.Api;
using HallPass.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass.Buckets
{
    internal class RemoteLeakyBucket : IBucket
    {
        private readonly ConcurrentSortedStack<Ticket> _tickets = new(Comparer<Ticket>.Create((a, b) => a.ValidFrom.CompareTo(b.ValidFrom)));

        private TimeSpan _shift = TimeSpan.Zero;
        private TimeSpan _shiftDelta = TimeSpan.Zero;
        private long _shiftVersion = 0;
        private int _shifting = 0;
        private readonly ConcurrentFixedSizeQueue<string> _windowIds = new(10);
        
        private readonly IHallPassApi _hallPass;

        private readonly int _rate;
        private readonly TimeSpan _frequency;
        private readonly int _capacity;
        private readonly string _key;
        private readonly string _instanceId;
        
        public string InstanceId => _instanceId;
        public TimeSpan Frequency => _frequency;

        // keep track of tickets returned per remote call for fail-safe if HallPass Remote goes down temporarily
        // todo: generalize this
        private int _remoteCalls = 0;
        private decimal _movingAverage = 0;
        private DateTimeOffset _lastValidFrom;
        private const int MAX_REMOTE_CALLS = 10;
        private readonly object _refreshingMovingAverage = new object();

        public RemoteLeakyBucket(IHallPassApi hallPass, int rate, TimeSpan frequency, int capacity, string key = null, string instanceId = null)
        {
            _hallPass = hallPass;
            _rate = rate;
            _frequency = frequency;
            _capacity = capacity;
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
                if (ticket.IsNotYetValid())
                    await ticket.WaitUntilValidAsync(cancellationToken: cancellationToken);

                // wait for shift outside of WaitUntilValidAsync to cover cases where a ticket is waiting in that method while the shift was updated
                if (_shift + _shiftDelta > TimeSpan.Zero)
                    await Task.Delay(_shift + _shiftDelta, cancellationToken);

                // if the ticket has already expired, then we need to try to get another ticket
                if (!ticket.IsExpired(_shift))
                    break;
            }

            return ticket;
        }

        private async Task RefillAsync(CancellationToken cancellationToken)
        {
            IReadOnlyCollection<Ticket> tickets;
            try
            {
                var response = await _hallPass.GetTicketsAsync(_key, _instanceId, _rate, _frequency, _capacity, cancellationToken).ConfigureAwait(false);
                tickets = response.HallPasses;

                // update shift atomically, but only if the new shift is actually a later version
                if (response.ShiftInfo.Version > _shiftVersion)
                {
                    while (true)
                    {
                        // try to take the lock until successful
                        if (0 != Interlocked.Exchange(ref _shifting, 1))
                            continue;

                        _shift = response.ShiftInfo.Shift;
                        _shiftDelta = TimeSpan.Zero;
                        _shiftVersion = response.ShiftInfo.Version;

                        // release lock
                        Interlocked.Exchange(ref _shifting, 0);
                        break;
                    }
                }
            }
            catch (HallPassAuthenticationException ex)
            {
                // log?
                throw;
            }
            catch (Exception ex)
            {
                // log?
                tickets = GetFailSafeTickets().ToList();
            }

            _tickets.Add(tickets);

            RefreshMovingAverage(tickets);
        }

        private void RefreshMovingAverage(IReadOnlyCollection<Ticket> tickets)
        {
            lock (_refreshingMovingAverage)
            {
                // if remote calls < max, use actual calls
                // otherwise, use max
                _remoteCalls = Math.Min(MAX_REMOTE_CALLS, _remoteCalls + 1);

                _movingAverage = ((_movingAverage * (_remoteCalls - 1)) + tickets.Count) / _remoteCalls;

                _lastValidFrom = tickets.Select(t => t.ValidFrom).Max();
            }
        }

        private IEnumerable<Ticket> GetFailSafeTickets()
        {
            // if average is < 1, return 1
            // otherwise generate new tickets with count equal to average
            var countToGenerate = _movingAverage < 1
                ? 1
                : (int)Math.Floor(Math.Max(_movingAverage, 1));

            var now = DateTimeOffset.UtcNow;
            var validFrom = _lastValidFrom + _frequency > now ? _lastValidFrom + _frequency : now;

            var windowSize = _frequency * (_capacity / _rate);

            var generatedCount = 0;
            while (generatedCount < countToGenerate)
            {
                var windowId = Guid.NewGuid().ToString()[..6];
                for (int i = 0; i < _rate; i++)
                {
                    yield return Ticket.New(validFrom, validFrom + windowSize, windowId);
                }

                validFrom += _frequency;
                _lastValidFrom = validFrom;
            }
        }

        public async Task ShiftWindowAsync(Ticket ticket, CancellationToken cancellationToken = default)
        {
            if (_windowIds.TryAdd(ticket.WindowId))
            {
                // immediately update local shiftDelta (atomically)
                while (true)
                {
                    // try to take the lock until successful
                    if (0 != Interlocked.Exchange(ref _shifting, 1))
                        continue;

                    var shiftDelta = DateTimeOffset.UtcNow - (ticket.ValidFrom + _shift);
                    _shiftDelta += shiftDelta;

                    // while locked, update remote server
                    UpdateShiftResult updateShiftResult = await _hallPass
                        .UpdateShiftAsync(shiftDelta, ticket.WindowId, _shiftVersion, _key, _rate, _frequency, _capacity, cancellationToken)
                        .ConfigureAwait(false);

                    _shift = updateShiftResult.Shift;
                    _shiftDelta = TimeSpan.Zero;
                    _shiftVersion = updateShiftResult.Version;

                    // release lock
                    Interlocked.Exchange(ref _shifting, 0);
                    break;
                }
            }

            // IDEA: check remote server again after halfway through a given window's tickets?
        }
    }
}
