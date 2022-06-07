namespace HallPass.TestHelpers
{
    public static class TestEndpoints
    {
        private static readonly string[] Endpoints = new[]
        {
            "https://catfact.ninja/fact",
            "https://catfact.ninja/facts",
            "https://catfact.ninja/breeds",
            "https://api.coindesk.com/v1/bpi/currentprice.json",
            "https://api.agify.io/?name=michael",
            "https://api.genderize.io/?name=alex",
            "https://api.nationalize.io/?name=nathaniel",
            "https://datausa.io/api/data?drilldowns=Nation&measures=Population",
            "https://dog.ceo/api/breeds/image/random",
            "https://api.ipify.org/?format=json",
            "https://ipinfo.io/161.185.160.93/geo",
            "https://randomuser.me/api/",
            "http://universities.hipolabs.com/search?country=United+States",
            "https://api.zippopotam.us/us/33162",
        };

        public static string GetRandom() => Endpoints[DateTimeOffset.Now.ToUnixTimeMilliseconds() % Endpoints.Length];
        public static string Get(int index) => Endpoints[index];
        public static IEnumerable<string> GetAll(int start = 0) => Endpoints.Skip(start);
    }
}
