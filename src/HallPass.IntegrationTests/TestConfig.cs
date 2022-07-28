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

        public static string HallPassClientId() => GetConfiguration().GetValue<string>("HallPass_Api_ClientId");
        public static string HallPassClientSecret() => GetConfiguration().GetValue<string>("HallPass_Api_ClientSecret");
        public static string HallPassTestApiBaseUrl() => GetConfiguration().GetValue<string>("HallPass_Test_Api_BaseUrl");
        public static string HallPassTestApiKey() => GetConfiguration().GetValue<string>("HallPass_Test_Api_ApiKey");
    }
}
