using System;

namespace HallPass.Buckets
{
    internal sealed class Ticket
    {
        public Ticket(DateTimeOffset validFrom, DateTimeOffset validTo, string windowId)
        {
            ValidFrom = validFrom;
            ValidTo = validTo;
            WindowId = windowId;
        }

        public DateTimeOffset ValidFrom { get; }
        public DateTimeOffset ValidTo { get; }
        public string WindowId { get; set; }

        internal static Ticket New(DateTimeOffset validFrom, DateTimeOffset validTo, string windowId) => new Ticket(validFrom, validTo, windowId);
    }
}