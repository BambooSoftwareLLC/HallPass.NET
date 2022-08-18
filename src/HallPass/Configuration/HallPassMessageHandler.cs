using HallPass.Buckets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass.Configuration
{
    internal sealed class HallPassMessageHandler : DelegatingHandler
    {
        private readonly Lazy<BucketConfiguration[]> _bucketConfigurations;
        private readonly ILogger _logger;

        public HallPassMessageHandler(IServiceProvider services, HallPassOptions options)
        {
            var loggerFactory = services.GetService<ILoggerFactory>() ?? new NullLoggerFactory();
            _logger = loggerFactory.CreateLogger("HallPass");

            _bucketConfigurations = new Lazy<BucketConfiguration[]>(() =>
            {
                return options
                    .BucketConfigurationBuilders
                    .Select(builder => builder.Build(services, _logger))
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
            var requestId = Guid.NewGuid().ToString();

            using (_logger.BeginScope("{HallPass_RequestId}", requestId))
            {
                _logger.LogDebug("Starting HallPass request");

                var buckets = _bucketConfigurations.Value
                    .Where(config => config.IsTriggeredBy(request))
                    .Select(config => config.GetOrBuild(request))
                    .ToList();

                _logger.LogDebug("Found {BucketCount} buckets", buckets.Count);

                // exit early if no buckets are found (request does not need to be throttled by HallPass)
                if (!buckets.Any())
                    return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);



                var tasks = buckets.Select(async bucket =>
                {
                    using (_logger.BeginScope("{HallPass_Bucket}", bucket.Dump()))
                    {
                        _logger.LogDebug("Acquiring ticket");

                        var ticket = await bucket.GetTicketAsync(cancellationToken).ConfigureAwait(false);

                        _logger.LogDebug("Acquired {Ticket}", ticket.Dump());

                        return (ticket, bucket);
                    }
                });

                _logger.LogDebug("Waiting for required tickets");

                (Ticket ticket, IBucket bucket)[] ticketResults = await Task.WhenAll(tasks).ConfigureAwait(false);

                _logger.LogDebug("Required tickets acquired");
                _logger.LogDebug("Sending request");

                var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

                _logger.LogDebug("Response received");

                var shiftWindowTasks = ticketResults.Select(async tr =>
                {
                    var scopeProps = new Dictionary<string, object>
                    {
                        { "HallPass_Bucket", tr.bucket.Dump() },
                        { "HallPass_Ticket", tr.ticket.Dump() }
                    };
                    using (_logger.BeginScope(scopeProps))
                    {
                        _logger.LogDebug("Shifting time window");

                        await tr.bucket.ShiftWindowAsync(tr.ticket, cancellationToken);

                        _logger.LogDebug("Time window shifted");
                    }
                });

                _logger.LogDebug("Waiting for shift windows");

                await Task.WhenAll(shiftWindowTasks).ConfigureAwait(false);

                _logger.LogDebug("Windows all shifted");
                _logger.LogDebug("Finished HallPass request");

                return response;
            }
        }
    }
}