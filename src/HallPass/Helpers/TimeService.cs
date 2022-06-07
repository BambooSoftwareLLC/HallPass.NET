﻿using System;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass.Helpers
{
    internal sealed class TimeService : ITimeService
    {
        public DateTimeOffset GetNow() => DateTimeOffset.Now;
        public async Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default) => await Task.Delay(milliseconds, cancellationToken);
        public async Task DelayAsync(TimeSpan timeSpan, CancellationToken cancellationToken = default) => await Task.Delay(timeSpan, cancellationToken);

        public TimeSpan GetDuration(TimeSpan timeSpan) => timeSpan;
    }
}