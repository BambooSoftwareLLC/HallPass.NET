using LazyCache;
using System.Collections.Concurrent;
using System.Net.Http;

namespace HallPass.Api
{
    internal sealed class HallPassApiFactory
    {
        private readonly ConcurrentDictionary<string, IHallPassApi> _apis;

        private readonly IAppCache _cache;
        private readonly IHttpClientFactory _httpClientFactory;

        public HallPassApiFactory(IAppCache cache, IHttpClientFactory httpClientFactory)
        {
            _cache = cache;
            _httpClientFactory = httpClientFactory;

            _apis = new();
        }

        public IHallPassApi GetOrCreate(string clientId, string clientSecret)
        {
            return _apis.GetOrAdd(clientId, _ => new HallPassApi(_cache, _httpClientFactory, clientId, clientSecret));
        }
    }
}
