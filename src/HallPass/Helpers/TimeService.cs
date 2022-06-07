using System;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass.Helpers
{
    internal sealed class TimeService : ITimeService
    {
        private readonly Func<TimeSpan, TimeSpan> _durationBuffer;

        public TimeService(Func<TimeSpan, TimeSpan> durationBuffer = null)
        {
            _durationBuffer = durationBuffer ?? DurationBuffers.Moderate;
        }

        public DateTimeOffset GetNow() => DateTimeOffset.Now;
        public async Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default) => await Task.Delay(milliseconds, cancellationToken);
        public async Task DelayAsync(TimeSpan timeSpan, CancellationToken cancellationToken = default) => await Task.Delay(timeSpan, cancellationToken);

        public TimeSpan BufferDuration(TimeSpan timeSpan) => _durationBuffer(timeSpan);

        public TimeSpan GetDuration(TimeSpan timeSpan) => timeSpan;
    }

    public static class DurationBuffers
    {
        public static Func<TimeSpan, TimeSpan> None => input => input;
        
        public static Func<TimeSpan, TimeSpan> Moderate => input =>
        {
            var inputMilliseconds = input.TotalMilliseconds;
            var bufferedMilliseconds = Math.Min(inputMilliseconds * 0.10, TimeSpan.FromSeconds(10).TotalMilliseconds);
            return TimeSpan.FromMilliseconds(inputMilliseconds + bufferedMilliseconds);
        };

        public static Func<TimeSpan, TimeSpan> Aggressive => input =>
        {
            var inputMilliseconds = input.TotalMilliseconds;
            var bufferedMilliseconds = Math.Min(inputMilliseconds * 0.05, TimeSpan.FromSeconds(5).TotalMilliseconds);
            return TimeSpan.FromMilliseconds(inputMilliseconds + bufferedMilliseconds);
        };
    }
}
