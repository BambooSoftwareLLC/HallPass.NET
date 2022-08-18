using System.Text.Json;

namespace HallPass
{
    internal static class JsonExtensions
    {
        public static string Dump(this object obj) => JsonSerializer.Serialize(obj);
    }
}
