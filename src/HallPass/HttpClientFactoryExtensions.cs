using HallPass.Configuration;
using System.Net.Http;

namespace HallPass
{
    public static class HttpClientFactoryExtensions
    {
        public static HttpClient CreateHallPassClient(this IHttpClientFactory factory) => factory.CreateClient(Constants.DEFAULT_HALLPASS_HTTPCLIENT_NAME);
    }
}
