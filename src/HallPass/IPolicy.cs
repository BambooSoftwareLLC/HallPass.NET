using System;

namespace HallPass
{
    public interface IPolicy
    {
        int Requests { get; }
        TimeSpan Duration { get; }
    }
}
