using HallPass.Buckets;
using HallPass.Configuration;
using LazyCache;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace HallPass.Api
{
    internal sealed class HallPassApi : IHallPassApi
    {
        private readonly IAppCache _cache;
        private readonly IHttpClientFactory _httpClientFactory;

        private readonly string _clientId;
        private readonly string _clientSecret;

        public HallPassApi(IAppCache cache, IHttpClientFactory httpClientFactory, string clientId, string clientSecret)
        {
            _cache = cache;
            _httpClientFactory = httpClientFactory;
            _clientId = clientId;
            _clientSecret = clientSecret;
        }

        public async Task<HallPassesResponse> GetTicketsAsync(
            string key,
            string instanceId,
            int rate,
            TimeSpan frequency,
            int capacity,
            CancellationToken cancellationToken = default)
        {
            // refresh (and cache) the access token for the given client_id
            var accessToken = await _cache.GetOrAddAsync($"access_token-{_clientId}", async entry =>
            {
                var token = await AuthenticateAsync(cancellationToken);
                entry.AbsoluteExpiration = token.Expiration;
                return token;
            });

            var queryParams = new List<string>
            {
                $"key={key}",
                $"instanceId={instanceId}",
                $"rate={rate}",
                $"frequency={frequency.TotalMilliseconds}",
                $"capacity={capacity}",
            };
            var query = string.Join("&", queryParams);


            var httpClient = _httpClientFactory.CreateClient(Constants.HALLPASS_API_HTTPCLIENT_NAME);

            // retry 429's indefinitely
            while (true)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"v5/hallpasses?{query}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);

                var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retryAfterSeconds = response.Headers.RetryAfter.Delta;
                    if (retryAfterSeconds.HasValue)
                        await Task.Delay(retryAfterSeconds.Value, cancellationToken);
                    else
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                    continue;
                }

                var contentJson = await response.Content.ReadAsStringAsync(cancellationToken);

                var options = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };
                var deserializedResponse = JsonSerializer.Deserialize<HallPassesResponse>(contentJson, options);

                return deserializedResponse;
            }
        }

        public async Task<UpdateShiftResult> UpdateShiftAsync(
            TimeSpan shiftDelta,
            string windowId,
            long shiftVersion,
            string key,
            int rate,
            TimeSpan frequency,
            int capacity,
            CancellationToken cancellationToken)
        {
            // refresh (and cache) the access token for the given client_id
            var accessToken = await _cache.GetOrAddAsync($"access_token-{_clientId}", async entry =>
            {
                var token = await AuthenticateAsync(cancellationToken);
                entry.AbsoluteExpiration = token.Expiration;
                return token;
            });

            var httpClient = _httpClientFactory.CreateClient(Constants.HALLPASS_API_HTTPCLIENT_NAME);

            // retry 429's indefinitely
            while (true)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"v5/shifts/{key}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);
                request.Content = JsonContent.Create(
                    new
                    {
                        delta = shiftDelta,
                        window = windowId,
                        version = shiftVersion,
                        rate,
                        frequency = frequency.TotalMilliseconds,
                        capacity
                    });

                var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retryAfterSeconds = response.Headers.RetryAfter.Delta;
                    if (retryAfterSeconds.HasValue)
                        await Task.Delay(retryAfterSeconds.Value, cancellationToken);
                    else
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                    continue;
                }

                var contentJson = await response.Content.ReadAsStringAsync(cancellationToken);

                var options = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };
                var deserializedResponse = JsonSerializer.Deserialize<UpdateShiftResult>(contentJson, options);

                return deserializedResponse;
            }
        }

        private async Task<AccessToken> AuthenticateAsync(CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"v5/oauth/token");
            var credentials = new { client_id = _clientId, client_secret = _clientSecret };
            request.Content = new StringContent(JsonSerializer.Serialize(credentials), Encoding.UTF8, "application/json");

            var httpClient = _httpClientFactory.CreateClient(Constants.HALLPASS_API_HTTPCLIENT_NAME);

            // retry 429's indefinitely
            while (true)
            {
                var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retryAfterSeconds = response.Headers.RetryAfter.Delta;
                    if (retryAfterSeconds.HasValue)
                        await Task.Delay(retryAfterSeconds.Value, cancellationToken);
                    else
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                    continue;
                }

                else if (!response.IsSuccessStatusCode)
                {
                    throw new HallPassAuthenticationException();
                }

                var contentJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var tokenResponse = JsonSerializer.Deserialize<AccessTokenResponse>(contentJson);
                return new AccessToken(
                    tokenResponse.AccessToken,
                    tokenResponse.Scope,

                    // removing a 5-second buffer from the expiration time to be safe
                    DateTimeOffset.UtcNow + TimeSpan.FromSeconds(tokenResponse.ExpiresIn - 5),

                    tokenResponse.TokenType);
            }
        }
    }

    class HallPassesResponse
    {
        public Ticket[] HallPasses { get; set; }
        public UpdateShiftResult ShiftInfo { get; set; }
    }

    class AccessTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }
        public string Scope { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; }
    }
}
