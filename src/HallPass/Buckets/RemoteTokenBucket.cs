﻿using HallPass.Api;
using HallPass.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
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

        // keep track of tickets returned per remote call for fail-safe if HallPass Remote goes down temporarily
        private int _remoteCalls = 0;
        private decimal _movingAverage = 0;
        private DateTimeOffset _lastValidFrom;
        private const int MAX_REMOTE_CALLS = 10;
        private readonly object _refreshingMovingAverage = new object();

        public RemoteTokenBucket(ITimeService timeService, IHallPassApi hallPass, int requestsPerPeriod, TimeSpan periodDuration, string key = null, string instanceId = null)
        {
            _timeService = timeService;
            _hallPass = hallPass;
            _requestsPerPeriod = requestsPerPeriod;
            _periodDuration = timeService.BufferDuration(periodDuration);
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
            IReadOnlyList<Ticket> tickets;
            try
            {
                tickets = await _hallPass.GetTicketsAsync(_key, _instanceId, "tokenbucket", _requestsPerPeriod, _periodDuration, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                // log?
                tickets = GetFailSafeTickets();
            }

            _tickets.Add(tickets);

            RefreshMovingAverage(tickets);
        }

        private void RefreshMovingAverage(IReadOnlyList<Ticket> tickets)
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

        private IReadOnlyList<Ticket> GetFailSafeTickets()
        {
            // if average is < 1, return 1
            // otherwise generate new tickets with count equal to average
            var countToGenerate = _movingAverage < 1
                ? 1
                : (int)Math.Floor(_movingAverage);

            var validFrom = _lastValidFrom + _periodDuration;

            return Enumerable
                .Range(1, countToGenerate)
                .Select(_ => Ticket.New(validFrom, validFrom + _periodDuration))
                .ToList();
        }

        // these methods are identical to local version and could be shared in base class
        private TimeSpan TimeUntilValid(Ticket ticket) => ticket.ValidFrom - _timeService.GetNow();
        private bool IsNotYetValid(Ticket ticket) => TimeUntilValid(ticket) > TimeSpan.Zero;
        private bool IsExpired(Ticket ticket) => ticket.ValidTo <= _timeService.GetNow();
    }
}
