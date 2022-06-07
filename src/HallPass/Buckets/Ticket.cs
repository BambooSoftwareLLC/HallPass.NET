using System;

namespace HallPass.Buckets
{
    internal sealed class Ticket
    {
        public Ticket(DateTimeOffset validFrom, DateTimeOffset validTo)
        {
            ValidFrom = validFrom;
            ValidTo = validTo;
        }

        public DateTimeOffset ValidFrom { get; }
        public DateTimeOffset ValidTo { get; }

        internal static Ticket New(DateTimeOffset validFrom, DateTimeOffset validTo) => new Ticket(validFrom, validTo);
    }
}