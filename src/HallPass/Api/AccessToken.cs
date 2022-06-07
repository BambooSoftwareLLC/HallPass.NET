using System;

namespace HallPass.Api
{
    internal sealed class AccessToken
    {
        public AccessToken(string token, string scope, DateTimeOffset expiration, string tokenType)
        {
            Token = token;
            Scope = scope;
            Expiration = expiration;
            TokenType = tokenType;
        }

        public string Token { get; }

        /// <summary>
        /// Comma-separated list of scopes
        /// </summary>
        public string Scope { get; }

        public DateTimeOffset Expiration { get; }
        public string TokenType { get; }
    }
}