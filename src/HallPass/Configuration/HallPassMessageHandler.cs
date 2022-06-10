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
            var tasks = _bucketConfigurations.Value
                .Where(c => c.IsTriggeredBy(request))
                .Select(config => config.Bucket)
                .Select(bucket => bucket.GetTicketAsync(cancellationToken));

            Task.WhenAll(tasks).Wait(cancellationToken);

            return base.Send(request, cancellationToken);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var tasks = _bucketConfigurations.Value
                .Where(c => c.IsTriggeredBy(request))
                .Select(config => config.Bucket)
                .Select(bucket => bucket.GetTicketAsync(cancellationToken));

            await Task.WhenAll(tasks).ConfigureAwait(false);

            return await base.SendAsync(request, cancellationToken);
        }
    }
}