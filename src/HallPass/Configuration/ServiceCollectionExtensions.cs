﻿using HallPass.Api;
using HallPass.Configuration;
using HallPass.Helpers;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace HallPass
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddHallPass(this IServiceCollection services, Action<HallPassOptions> config)
        {
            // build a custom HttpClientMessageHandler with inner handlers to handle cases per bucket added
            var options = HallPassOptions.Default;
            config(options);

            // add that handler to the HallPass client's pipeline
            services
                .AddHttpClient(Constants.DEFAULT_HALLPASS_HTTPCLIENT_NAME)
                .AddHttpMessageHandler(serviceProvider => new HallPassMessageHandler(serviceProvider, options));

            services.AddHttpClient(Constants.HALLPASS_API_HTTPCLIENT_NAME, client =>
            {
                client.BaseAddress = new Uri("https://api.hallpass.dev/");
            });

            // register other dependencies
            services.AddSingleton<ITimeService, TimeService>();
            services.AddTransient<HallPassApiFactory>();
            services.AddLazyCache();

            return services;
        }
    }
}