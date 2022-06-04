using Microsoft.Extensions.Configuration;

namespace HallPass.IntegrationTests
{
    internal static class TestConfig
    {
        public static IConfiguration GetConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddUserSecrets(typeof(TestConfig).Assembly);

            return builder.Build();
        }

        public static string HallPassClientId(this IConfiguration config) => config.GetValue<string>("HallPass:Api:ClientId");
        public static string HallPassClientSecret(this IConfiguration config) => config.GetValue<string>("HallPass:Api:ClientSecret");
        public static string HallPassBaseUrl(this IConfiguration config) => config.GetValue<string>("HallPass:Api:BaseUrl");
    }
}
