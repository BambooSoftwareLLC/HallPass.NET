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

        public static string HallPassClientId(this IConfiguration config) => config.GetValue<string>("HallPass_Api_ClientId");
        public static string HallPassClientSecret(this IConfiguration config) => config.GetValue<string>("HallPass_Api_ClientSecret");
    }
}
