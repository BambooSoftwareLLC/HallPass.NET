using HallPass.Api;
using HallPass.Helpers;
using Microsoft.Extensions.Logging;
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
        private int _failSafeLock = 0;

        private readonly ILogger _logger;

        public RemoteLeakyBucket(IHallPassApi hallPass, int rate, TimeSpan frequency, int capacity, string key = null, string instanceId = null, ILogger logger = null)
        {
            _hallPass = hallPass;
            _rate = rate;
            _frequency = frequency;
            _capacity = capacity;
            _key = key ?? Guid.NewGuid().ToString();
            _instanceId = instanceId ?? Guid.NewGuid().ToString();
            _logger = logger;
        }

        public async Task<Ticket> GetTicketAsync(CancellationToken cancellationToken = default)
        {
            Ticket ticket;

            while (true)
            {
                // if there aren't any tickets to take, then wait for a refill
                while (!_tickets.TryPop(out ticket))
                {
                    _logger.LogDebug("No tickets, waiting for refill");

                    await RefillAsync(cancellationToken);

                    _logger.LogDebug("Refilled");
                }

                using (_logger.BeginScope("Pending_HallPass_Ticket: {Pending_HallPass_Ticket}", ticket))
                {
                    // if the ticket isn't yet valid, then wait for it to become valid
                    if (ticket.IsNotYetValid())
                    {
                        _logger.LogDebug("Waiting until valid - {ValidDetails}",
                            new
                            {
                                ValidFrom = ticket.ValidFrom,
                                SecondsUntilValid = (DateTimeOffset.UtcNow - ticket.ValidFrom).TotalSeconds
                            });

                        await ticket.WaitUntilValidAsync(cancellationToken: cancellationToken);

                        _logger.LogDebug("Done waiting for initial validity");
                    }

                    // wait for shift outside of WaitUntilValidAsync to cover cases where a ticket is waiting in that method while the shift was updated
                    if (_shift + _shiftDelta > TimeSpan.Zero)
                    {
                        _logger.LogDebug("Waiting for extra {Shift} adjustments",
                            new
                            {
                                ShiftDelta = _shiftDelta.TotalSeconds,
                                Shift = _shift.TotalSeconds
                            });

                        await Task.Delay(_shift + _shiftDelta, cancellationToken);

                        _logger.LogDebug("Done waiting for shift adjustments");
                    }

                    // if the ticket has already expired, then we need to try to get another ticket
                    // we use the more conservative window of [_shift + _shiftDelta, _shift] instead of
                    // [_shift + _shiftDelta, _shift + _shiftDelta] in order to, well... be more conservative
                    if (!ticket.IsExpired(_shift))
                    {
                        _logger.LogDebug("Ticket is ready");

                        break;
                    }

                    _logger.LogDebug("Ticket is expired, trying again");
                }

            }

            return ticket;
        }

        private async Task RefillAsync(CancellationToken cancellationToken)
        {
            IReadOnlyCollection<Ticket> tickets;
            try
            {
                _logger.LogDebug("Refilling tickets from API");

                var response = await _hallPass.GetTicketsAsync(_key, _instanceId, _rate, _frequency, _capacity, cancellationToken).ConfigureAwait(false);
                tickets = response.HallPasses;

                _logger.LogDebug("{Tickets} retrieved", new { Tickets = tickets });

                _logger.LogDebug("Preparing to adjust shift based on new tickets {ShiftVersion}", new { NewVersion = response.ShiftInfo.Version, OldVersion = _shiftVersion });

                // update shift atomically, but only if the new shift is actually a later version
                if (response.ShiftInfo.Version > _shiftVersion)
                {
                    while (true)
                    {
                        _logger.LogDebug("Trying to take the shift lock");

                        // try to take the lock until successful
                        if (0 != Interlocked.Exchange(ref _shifting, 1))
                        {
                            _logger.LogDebug("Shift lock is already taken, trying again");

                            continue;
                        }

                        _logger.LogDebug("Shift lock acquired");

                        if (response.ShiftInfo.Version <= _shiftVersion)
                        {
                            _logger.LogDebug("Shift versions have changed, new version is out of date {ShiftVersion}", new { NewVersion = response.ShiftInfo.Version, OldVersion = _shiftVersion });

                            // release lock
                            Interlocked.Exchange(ref _shifting, 0);
                            
                            _logger.LogDebug("Lock released");
                            _logger.LogDebug("Exiting loop");

                            break;
                        }

                        try
                        {
                            _logger.LogDebug("Updating shift info");

                            _shift = response.ShiftInfo.Shift;
                            _shiftDelta = TimeSpan.Zero;
                            _shiftVersion = response.ShiftInfo.Version;
                        }
                        finally
                        {
                            _logger.LogDebug("Releasing the lock");

                            // release lock
                            Interlocked.Exchange(ref _shifting, 0);
                            
                            _logger.LogDebug("Lock released");
                        }

                        _logger.LogDebug("Exiting loop");

                        break;
                    }
                }
            }
            catch (HallPassAuthenticationException ex)
            {
                _logger.LogError("HallPassAuthenticationException: {Exception}", ex);

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("{Exception} when retrieving tickets", ex);

                tickets = GetFailSafeTickets().ToList();
            }

            _logger.LogDebug("Adding {Tickets} to collection", tickets);

            _tickets.Add(tickets);

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(() => RefreshMovingAverage(tickets), cancellationToken);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }

        private void RefreshMovingAverage(IReadOnlyCollection<Ticket> tickets)
        {
            for (var i = 0; i < 5; i++)
            {
                using (_logger.BeginScope("Attempt: {Attempt}", i + 1))
                {
                    _logger.LogDebug("Acquiring failsafe lock");

                    // take the lock
                    if (0 != Interlocked.Exchange(ref _failSafeLock, 1))
                    {
                        _logger.LogDebug("Unable to acquire failsafe lock, trying again...");

                        continue;
                    }

                    _logger.LogDebug("failsafe lock acquired");

                    try
                    {
                        _logger.LogDebug("Updating failsafe info");

                        // if remote calls < max, use actual calls
                        // otherwise, use max
                        _remoteCalls = Math.Min(MAX_REMOTE_CALLS, _remoteCalls + 1);
                        _movingAverage = ((_movingAverage * (_remoteCalls - 1)) + tickets.Count) / _remoteCalls;

                        var newValidFrom = tickets.Select(t => t.ValidFrom).Max();
                        _lastValidFrom = newValidFrom > _lastValidFrom ? newValidFrom : _lastValidFrom;

                        _logger.LogDebug("Finished updating failsafe info");
                    }
                    finally
                    {
                        _logger.LogDebug("Releasing failsafe lock");

                        // release the lock
                        Interlocked.Exchange(ref _failSafeLock, 0);

                        _logger.LogDebug("Failsafe lock released");
                    }

                    return;
                }
            }
        }

        private IEnumerable<Ticket> GetFailSafeTickets()
        {
            var tickets = new List<Ticket>();

            using (_logger.BeginScope("GetFailSafeTickets: {GetFailSafeTickets}", true))
            {
                while (true)
                {
                    _logger.LogDebug("Acquiring failsafe lock");

                    // take the lock
                    if (0 != Interlocked.Exchange(ref _failSafeLock, 1))
                    {
                        _logger.LogDebug("Could not acquire lock, trying again");

                        continue;
                    }

                    try
                    {
                        // if average is < 1, return 1
                        // otherwise generate new tickets with count equal to average
                        var countToGenerate = _movingAverage < 1
                            ? 1
                            : (int)Math.Floor(Math.Max(_movingAverage, 1));

                        var now = DateTimeOffset.UtcNow;
                        var validFrom = _lastValidFrom + _frequency > now ? _lastValidFrom + _frequency : now;

                        var windowSize = _frequency * (_capacity / _rate);

                        _logger.LogDebug("Generating new tickets {Details}",
                            new
                            {
                                CountToGenerate = countToGenerate,
                                ValidFrom = validFrom,
                                WindowSizeSeconds = windowSize.TotalSeconds
                            });

                        while (tickets.Count < countToGenerate)
                        {
                            var windowId = Guid.NewGuid().ToString()[..6];
                            for (int i = 0; i < _rate; i++)
                            {
                                tickets.Add(Ticket.New(validFrom, validFrom + windowSize, windowId));
                            }

                            validFrom += _frequency;
                            _lastValidFrom = validFrom;

                            _logger.LogDebug("Tickets generated: {Tickets}", tickets);
                        }
                    }
                    finally
                    {
                        _logger.LogDebug("Releasing lock");

                        // release the lock
                        Interlocked.Exchange(ref _failSafeLock, 0);

                        _logger.LogDebug("Lock released");
                    }

                    _logger.LogDebug("Exiting loop");

                    break;
                }

                return tickets;
            }
        }

        public async Task ShiftWindowAsync(Ticket ticket, CancellationToken cancellationToken = default)
        {
            using (_logger.BeginScope("ShiftWindow: {ShiftWindow}", ticket.WindowId))
            {
                if (_windowIds.TryAdd(ticket.WindowId))
                {
                    _logger.LogDebug("Attempting to adjust window");

                    // immediately update local shiftDelta (atomically)
                    while (true)
                    {
                        _logger.LogDebug("Acquiring lock");

                        // try to take the lock until successful
                        if (0 != Interlocked.Exchange(ref _shifting, 1))
                        {
                            _logger.LogDebug("Could not acquire lock... trying again");

                            continue;
                        }

                        _logger.LogDebug("Lock acquired");

                        try
                        {
                            var shiftDelta = DateTimeOffset.UtcNow - (ticket.ValidFrom + _shift);
                            _shiftDelta += shiftDelta;

                            _logger.LogDebug("Updating shift to API {ShiftInfo}",
                                new
                                {
                                    ShiftDelta = shiftDelta,
                                    WindowId = ticket.WindowId,
                                    ShiftVersion = _shiftVersion,
                                    Key = _key,
                                    Rate = _rate,
                                    Frequency = _frequency,
                                    Capacity = _capacity
                                });

                            // while locked, update remote server
                            UpdateShiftResult updateShiftResult = await _hallPass
                                .UpdateShiftAsync(shiftDelta, ticket.WindowId, _shiftVersion, _key, _rate, _frequency, _capacity, cancellationToken)
                                .ConfigureAwait(false);

                            _logger.LogDebug("Shift updated at API with {UpdateShiftResult}", updateShiftResult);

                            _shift = updateShiftResult.Shift;
                            _shiftDelta = TimeSpan.Zero;
                            _shiftVersion = updateShiftResult.Version;

                            _logger.LogDebug("Finished updated shift info");
                        }
                        finally
                        {
                            _logger.LogDebug("Releasing lock");

                            // release lock
                            Interlocked.Exchange(ref _shifting, 0);

                            _logger.LogDebug("Lock released");
                        }

                        _logger.LogDebug("breaking loop");
                    
                        break;
                    }
                }
                else
                {
                    _logger.LogDebug("{Window} already adjusted", ticket.WindowId);
                }

                _logger.LogDebug("Finished updating shift");
            }


            // IDEA: check remote server again after halfway through a given window's tickets?
        }
    }
}
