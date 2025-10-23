using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using System.Configuration;
using System.Web.Http;
using PostProcessingServer.Utilities;

namespace PostProcessingServer.Filters
{
    /// <summary>
    /// Authentication filter for server-to-server requests
    /// </summary>
    public class ServerAuthenticationAttribute : Attribute, IAuthenticationFilter
    {
        private static readonly ConcurrentDictionary<string, DateTimeOffset> _usedNonces = new ConcurrentDictionary<string, DateTimeOffset>();
        private static readonly TimeSpan NonceExpirationTime = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan RequestTimeWindow = TimeSpan.FromMinutes(5);

        public bool AllowMultiple => false;

        public async Task AuthenticateAsync(HttpAuthenticationContext context, CancellationToken cancellationToken)
        {
            var request = context.Request;

            // Extract authentication headers using ConfigurationUtility constants
            var apiKeyHeader = request.Headers.FirstOrDefault(h => h.Key == ConfigurationUtility.ApiKeyHeader).Value?.FirstOrDefault();
            var signatureHeader = request.Headers.FirstOrDefault(h => h.Key == ConfigurationUtility.SignatureHeader).Value?.FirstOrDefault();
            var timestampHeader = request.Headers.FirstOrDefault(h => h.Key == ConfigurationUtility.TimestampHeader).Value?.FirstOrDefault();
            var nonceHeader = request.Headers.FirstOrDefault(h => h.Key == ConfigurationUtility.NonceHeader).Value?.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(apiKeyHeader) || 
                string.IsNullOrWhiteSpace(signatureHeader) || 
                string.IsNullOrWhiteSpace(timestampHeader) || 
                string.IsNullOrWhiteSpace(nonceHeader))
            {
                context.ErrorResult = new AuthenticationFailureResult("Missing authentication headers", request);
                return;
            }

            // Read request body
            string requestBody = string.Empty;
            if (request.Content != null)
            {
                requestBody = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
            }

            // Validate signature
            if (!ValidateSignature(apiKeyHeader, signatureHeader, timestampHeader, nonceHeader, requestBody))
            {
                context.ErrorResult = new AuthenticationFailureResult("Invalid authentication", request);
                return;
            }

            // Authentication successful
        }

        public Task ChallengeAsync(HttpAuthenticationChallengeContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        private bool ValidateSignature(string apiKey, string signature, string timestampStr, string nonce, string requestBody)
        {
            try
            {
                // Get configured API keys using ConfigurationUtility
                string allowedApiKeys = ConfigurationUtility.GetString(ConfigurationUtility.AllowedApiKeysKey);
                if (string.IsNullOrWhiteSpace(allowedApiKeys))
                    return false;

                // Parse API key pairs using ConfigurationUtility delimiters
                var apiKeyPairs = allowedApiKeys.Split(new[] { ConfigurationUtility.ApiKeyPairSeparator }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(pair => pair.Split(new[] { ConfigurationUtility.ApiKeySecretDelimiter }, StringSplitOptions.None))
                    .Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());

                if (!apiKeyPairs.ContainsKey(apiKey))
                    return false;

                string apiSecret = apiKeyPairs[apiKey];

                // Validate timestamp with configurable timeout
                if (!long.TryParse(timestampStr, out long timestamp))
                    return false;

                DateTimeOffset requestTime = DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
                DateTimeOffset now = DateTimeOffset.UtcNow;

                // Get configurable request timeout
                int requestTimeoutSeconds = ConfigurationUtility.GetInt(
                    ConfigurationUtility.RequestTimeoutSecondsKey, 
                    ConfigurationUtility.DefaultRequestTimeoutSeconds);
                
                if (Math.Abs((now - requestTime).TotalSeconds) > requestTimeoutSeconds)
                    return false;

                // Validate nonce
                if (_usedNonces.ContainsKey(nonce))
                    return false;

                // Generate expected signature
                string message = $"{apiKey}|{timestamp}|{nonce}|{requestBody}";
                string expectedSignature;

                using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret)))
                {
                    byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
                    expectedSignature = Convert.ToBase64String(hashBytes);
                }

                if (signature != expectedSignature)
                    return false;

                // Mark nonce as used
                _usedNonces.TryAdd(nonce, now);

                // Cleanup old nonces
                CleanupOldNonces();

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void CleanupOldNonces()
        {
            var expiredNonces = _usedNonces
                .Where(kvp => DateTimeOffset.UtcNow - kvp.Value > NonceExpirationTime)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var nonce in expiredNonces)
            {
                _usedNonces.TryRemove(nonce, out _);
            }
        }
    }

    /// <summary>
    /// Authentication failure result
    /// </summary>
    public class AuthenticationFailureResult : IHttpActionResult
    {
        private readonly string _reasonPhrase;
        private readonly HttpRequestMessage _request;

        public AuthenticationFailureResult(string reasonPhrase, HttpRequestMessage request)
        {
            _reasonPhrase = reasonPhrase;
            _request = request;
        }

        public Task<HttpResponseMessage> ExecuteAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Execute());
        }

        private HttpResponseMessage Execute()
        {
            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                RequestMessage = _request,
                ReasonPhrase = _reasonPhrase
            };
            return response;
        }
    }
}
