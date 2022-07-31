using System;

namespace HallPass.Buckets
{
    internal class UpdateShiftResult
    {
        public UpdateShiftResult(TimeSpan shift, long version)
        {
            Shift = shift;
            Version = version;
        }

        public TimeSpan Shift { get; }
        public long Version { get; }
    }
}
