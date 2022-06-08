using HallPass.Buckets;
using HallPass.Configuration;
using HallPass.Helpers;
using LazyCache;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass.Api
{
    internal sealed class HallPassApi : IHallPassApi
    {
        private readonly IAppCache _cache;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ITimeService _timeService;

        private readonly string _clientId;
        private readonly string _clientSecret;

        public HallPassApi(IAppCache cache, IHttpClientFactory httpClientFactory, ITimeService timeService, string clientId, string clientSecret)
        {
            _cache = cache;
            _httpClientFactory = httpClientFactory;
            _timeService = timeService;
            _clientId = clientId;
            _clientSecret = clientSecret;
        }

        public async Task<IReadOnlyList<Ticket>> GetTicketsAsync(string key, string instanceId, int requestsPerPeriod, TimeSpan periodDuration, CancellationToken cancellationToken = default)
        {
            // refresh (and cache) the access token for the given client_id
            var accessToken = await _cache.GetOrAddAsync($"access_token-{_clientId}", async entry =>
            {
                var token = await AuthenticateAsync(cancellationToken);
                entry.AbsoluteExpiration = token.Expiration;
                return token;
            });

            var request = new HttpRequestMessage(HttpMethod.Get, $"hallpasses?key={key}&instanceId={instanceId}&requestsPerPeriod={requestsPerPeriod}&periodDurationMilliseconds={periodDuration.TotalMilliseconds}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);

            var httpClient = _httpClientFactory.CreateClient(Constants.HALLPASS_API_HTTPCLIENT_NAME);
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            var contentJson = await response.Content.ReadAsStringAsync(cancellationToken);

            var options = new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var deserializedResponse = JsonSerializer.Deserialize<HallPassesResponse>(contentJson, options);

            return deserializedResponse.hallPasses;
        }

        private async Task<AccessToken> AuthenticateAsync(CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"oauth/token");
            var credentials = new { client_id = _clientId, client_secret = _clientSecret };
            request.Content = new StringContent(JsonSerializer.Serialize(credentials), Encoding.UTF8, "application/json");

            var httpClient = _httpClientFactory.CreateClient(Constants.HALLPASS_API_HTTPCLIENT_NAME);
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new NotImplementedException("handle auth error");
            }

            var contentJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenResponse = JsonSerializer.Deserialize<AccessTokenResponse>(contentJson);
            return new AccessToken(
                tokenResponse.access_token,
                tokenResponse.scope,

                // removing a 5-second buffer from the expiration time to be safe
                _timeService.GetNow() + _timeService.GetDuration(TimeSpan.FromSeconds(tokenResponse.expires_in - 5)),

                tokenResponse.token_type);
        }
    }

    class HallPassesResponse
    {
        public Ticket[] hallPasses { get; set; }
    }

    class AccessTokenResponse
    {
        public string access_token { get; set; }
        public string scope { get; set; }
        public int expires_in { get; set; }
        public string token_type { get; set; }
    }
}
