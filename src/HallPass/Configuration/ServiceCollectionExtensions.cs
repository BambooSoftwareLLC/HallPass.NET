using HallPass.Api;
using HallPass.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Linq;

namespace HallPass
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddHallPass(this IServiceCollection services, Action<HallPassOptions> config)
        {
            // build a custom HttpClientMessageHandler with inner handlers to handle cases per bucket added
            var options = HallPassOptions.Default;
            config(options);

            var maxTimeout = options.BucketConfigurationBuilders.Select(b => b.Frequency).Max() * 1.1;

            // add that handler to the HallPass client's pipeline
            if (options.UseDefaultHttpClient)
            {
                services
                    .AddHttpClient(Options.DefaultName, c => c.Timeout = maxTimeout)
                    .AddHttpMessageHandler(serviceProvider => new HallPassMessageHandler(serviceProvider, options));
            }

            services
                .AddHttpClient(Constants.DEFAULT_HALLPASS_HTTPCLIENT_NAME, c => c.Timeout = maxTimeout)
                .AddHttpMessageHandler(serviceProvider => new HallPassMessageHandler(serviceProvider, options));

            // add a client for calling the HallPass API
            services
                .AddHttpClient(Constants.HALLPASS_API_HTTPCLIENT_NAME, client => client.BaseAddress = new Uri("https://api.hallpass.dev/"))

                // use local buckets for HallPass API
                .AddHttpMessageHandler(serviceProvider => new HallPassMessageHandler(serviceProvider, HallPassOptions.API));

            // register other dependencies
            services.AddTransient<HallPassApiFactory>();
            services.AddLazyCache();

            return services;
        }
    }
}
