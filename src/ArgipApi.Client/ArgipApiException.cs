using System.Net;
using System.Text.Json;

namespace ArgipApi.Client;

/// <summary>
/// Any non-success response. Business errors carry <see cref="ApiMessage"/> (the API's {"message": ...} body,
/// or the token server's "error" code). An empty 404 can also mean an unexpected server error: for a read, log it and retry later; after a write the outcome is unknown, so never resend automatically.
/// </summary>
public sealed class ArgipApiException(HttpStatusCode statusCode, string? apiMessage, string body, bool fromTokenServer = false)
    : Exception(Describe(statusCode, apiMessage, fromTokenServer))
{
    /// <summary>True when the token server (not the API) answered, e.g. wrong credentials or a mistyped token URL.</summary>
    public bool FromTokenServer { get; } = fromTokenServer;

    public HttpStatusCode StatusCode { get; } = statusCode;
    public string? ApiMessage { get; } = apiMessage;
    public string Body { get; } = body;

    private static string Describe(HttpStatusCode statusCode, string? apiMessage, bool fromTokenServer)
    {
        var source = fromTokenServer ? "Token server" : "Argip API";
        return apiMessage is null ? $"{source} returned {(int)statusCode} {statusCode}." : $"{source} returned {(int)statusCode}: {apiMessage}";
    }

    internal static ArgipApiException From(HttpStatusCode statusCode, string body, bool fromTokenServer = false)
    {
        string? message = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && (document.RootElement.TryGetProperty("message", out var value) || document.RootElement.TryGetProperty("error", out value)))
            {
                message = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
            }
        }
        catch (JsonException)
        {
            // Empty or non-JSON body: keep message null, the raw body is still available.
        }

        return new ArgipApiException(statusCode, message, body, fromTokenServer);
    }
}

/// <summary>A setting is missing or invalid (credentials, ARGIP_API_URL, ARGIP_TOKEN_URL). Nothing was sent.</summary>
public sealed class ArgipApiConfigurationException(string message) : Exception(message);

/// <summary>A 200 answer that is not what the API documents (not JSON, JSON null, a token response without access_token).</summary>
public sealed class ArgipApiProtocolException(string message) : Exception(message);
