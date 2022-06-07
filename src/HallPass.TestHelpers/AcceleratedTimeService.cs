﻿using HallPass.Helpers;

namespace HallPass
{
    public class AcceleratedTimeService : ITimeService
    {
        private readonly DateTimeOffset _initialTime;
        private readonly int _scaleFactor;

        public AcceleratedTimeService(int scaleFactor, DateTimeOffset? initialTime = null)
        {
            _scaleFactor = scaleFactor;
            _initialTime = initialTime ?? DateTimeOffset.Now;
        }

        public async Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default) => await Task.Delay((int)(milliseconds / (decimal)_scaleFactor), cancellationToken);
        public async Task DelayAsync(TimeSpan timeSpan, CancellationToken cancellationToken = default) => await Task.Delay(timeSpan / _scaleFactor, cancellationToken);

        public TimeSpan GetDuration(TimeSpan timeSpan) => timeSpan / _scaleFactor;

        public DateTimeOffset GetNow() => _initialTime.Add((DateTimeOffset.Now - _initialTime) * _scaleFactor);
    }
}
