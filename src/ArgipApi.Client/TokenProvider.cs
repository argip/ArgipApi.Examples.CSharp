using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArgipApi.Client;

/// <summary>
/// Gets an access token with the OAuth 2.0 client credentials flow and caches it.
/// Tokens are valid for 8 hours: do NOT request a new token for every API call.
/// </summary>
internal sealed class TokenProvider(HttpClient http, ArgipApiOptions options, TimeProvider time)
{
    // Renew a little before expiry so a request never starts with a token that dies on the way.
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    // Used when the token server does not say how long a token lives (the documented lifetime is 8 hours).
    private const int DefaultExpiresInSeconds = 28800;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    /// <summary>
    /// Returns a valid token. Pass the token the API just rejected with 401 as <paramref name="rejectedToken"/>:
    /// if another caller already replaced it, the new one is reused, so concurrent 401s cause only one token request.
    /// </summary>
    public async Task<string> GetTokenAsync(string? rejectedToken, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_token is not null && _token != rejectedToken && time.GetUtcNow() < _expiresAt - RefreshMargin) return _token;

            // The token endpoint expects form fields (application/x-www-form-urlencoded), not JSON.
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = options.ClientId,
                ["client_secret"] = options.ClientSecret,
                ["scope"] = options.Scope,
            });
            using var response = await http.PostAsync(options.TokenUrl, content, ct);
            if (!response.IsSuccessStatusCode) throw ArgipApiException.From(response.StatusCode, await response.Content.ReadAsStringAsync(ct), fromTokenServer: true);

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
            if (string.IsNullOrEmpty(token?.AccessToken)) throw new ArgipApiProtocolException("The token server response has no access_token.");

            // Read expires_in defensively: a missing or non-numeric value must not make every call fetch a new token.
            var expiresIn = token.ExpiresIn is { ValueKind: JsonValueKind.Number } number && number.TryGetInt32(out var seconds) && seconds > 0
                ? seconds
                : DefaultExpiresInSeconds;
            _token = token.AccessToken;
            _expiresAt = time.GetUtcNow().AddSeconds(expiresIn);
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] JsonElement? ExpiresIn);
}
