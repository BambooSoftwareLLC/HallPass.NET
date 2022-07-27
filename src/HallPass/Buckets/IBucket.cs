﻿using System;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass.Buckets
{
    internal interface IBucket
    {
        Task<Ticket> GetTicketAsync(CancellationToken cancellationToken = default);
        Task ShiftWindowAsync(TimeSpan shift, string windowId, CancellationToken cancellationToken = default);
    }
}
