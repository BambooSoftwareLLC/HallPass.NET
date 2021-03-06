using HallPass.Buckets;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass.Configuration
{
    internal sealed class HallPassMessageHandler : DelegatingHandler
    {
        private readonly Lazy<BucketConfiguration[]> _bucketConfigurations;

        public HallPassMessageHandler(IServiceProvider services, HallPassOptions options)
        {
            _bucketConfigurations = new Lazy<BucketConfiguration[]>(() =>
            {
                return options
                    .BucketConfigurationBuilders
                    .Select(builder => builder.Build(services))
                    .ToArray();
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var buckets = _bucketConfigurations.Value
                .Where(config => config.IsTriggeredBy(request))
                .Select(config => config.GetOrBuild(request))
                .ToList();

            var tasks = buckets.Select(async bucket =>
            {
                var ticket = await bucket.GetTicketAsync(cancellationToken);
                return (ticket, bucket);
            });

            (Ticket ticket, IBucket bucket)[] ticketResults = Task.WhenAll(tasks).GetAwaiter().GetResult();

            var response = base.Send(request, cancellationToken);

            // adjust bucket window
            var shiftWindowTasks = ticketResults.Select(tr => tr.bucket.ShiftWindowAsync(tr.ticket, cancellationToken));
            Task.WhenAll(shiftWindowTasks).GetAwaiter().GetResult();

            return response;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var buckets = _bucketConfigurations.Value
                .Where(config => config.IsTriggeredBy(request))
                .Select(config => config.GetOrBuild(request))
                .ToList();

            var tasks = buckets.Select(async bucket =>
            {
                var ticket = await bucket.GetTicketAsync(cancellationToken);
                return (ticket, bucket);
            });

            (Ticket ticket, IBucket bucket)[] ticketResults = await Task.WhenAll(tasks).ConfigureAwait(false);

            var response = await base.SendAsync(request, cancellationToken);

            // adjust bucket window
            var shiftWindowTasks = ticketResults.Select(tr => tr.bucket.ShiftWindowAsync(tr.ticket, cancellationToken));
            await Task.WhenAll(shiftWindowTasks);

            return response;
        }
    }
}