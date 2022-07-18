﻿using HallPass.Api;
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
        
        private readonly IHallPassApi _hallPass;

        private readonly int _requestsPerPeriod;
        private readonly TimeSpan _periodDuration;
        private readonly int _initialBurst;
        private readonly string _key;
        private readonly string _instanceId;
        public string InstanceId => _instanceId;

        // keep track of tickets returned per remote call for fail-safe if HallPass Remote goes down temporarily
        // todo: generalize this
        private int _remoteCalls = 0;
        private decimal _movingAverage = 0;
        private DateTimeOffset _lastValidFrom;
        private const int MAX_REMOTE_CALLS = 10;
        private readonly object _refreshingMovingAverage = new object();

        public RemoteLeakyBucket(IHallPassApi hallPass, int requestsPerPeriod, TimeSpan periodDuration, int initialBurst, string key = null, string instanceId = null)
        {
            _hallPass = hallPass;
            _requestsPerPeriod = requestsPerPeriod;
            _periodDuration = periodDuration;
            _initialBurst = initialBurst;
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

                // if the ticket has already expired, then we need to try to get another ticket
                if (!ticket.IsExpired())
                    break;
            }

            return ticket;
        }

        private async Task RefillAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<Ticket> tickets;
            try
            {
                tickets = await _hallPass.GetTicketsAsync(_key, _instanceId, "leakybucket", _requestsPerPeriod, _periodDuration, _initialBurst, cancellationToken);
            }
            catch (Exception ex)
            {
                // log?
                tickets = GetFailSafeTickets().ToList();
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

        private IEnumerable<Ticket> GetFailSafeTickets()
        {
            // if average is < 1, return 1
            // otherwise generate new tickets with count equal to average
            var countToGenerate = _movingAverage < 1
                ? 1
                : (int)Math.Floor(_movingAverage);

            var now = DateTimeOffset.UtcNow;
            var validFrom = _lastValidFrom > now ? _lastValidFrom : now;
            var stagger = _periodDuration / _requestsPerPeriod;

            for (int i = 0; i < countToGenerate; i++)
            {
                validFrom += stagger;
                yield return Ticket.New(validFrom, validFrom + _periodDuration);
            }
        }
    }
}