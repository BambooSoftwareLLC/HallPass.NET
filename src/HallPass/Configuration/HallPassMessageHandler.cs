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
            var bucketConfiguration = _bucketConfigurations.Value.FirstOrDefault(c => c.IsTriggeredBy(request));
            if (bucketConfiguration is not null)
            {
                var bucket = bucketConfiguration.Bucket;
                bucket.GetTicketAsync(cancellationToken).Wait(cancellationToken);
            }

            return base.Send(request, cancellationToken);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var bucketConfiguration = _bucketConfigurations.Value.FirstOrDefault(c => c.IsTriggeredBy(request));
            if (bucketConfiguration is not null)
            {
                var bucket = bucketConfiguration.Bucket;
                await bucket.GetTicketAsync(cancellationToken).ConfigureAwait(false);
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}