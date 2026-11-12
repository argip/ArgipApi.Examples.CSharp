namespace ArgipApi.Client;

/// <summary>Connection settings. Credentials come from the Argip sales department.</summary>
public sealed class ArgipApiOptions
{
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }

    private static readonly Uri DefaultApiUrl = new("https://argipapi.argip.com.pl/");
    private static readonly Uri DefaultTokenUrl = new("https://identityserver.argip.com.pl/connect/token");

    // A trailing slash matters: relative paths like "v1/Products" are resolved against this address.
    public Uri ApiUrl { get; init => field = value.AbsoluteUri.EndsWith('/') ? value : new Uri(value.AbsoluteUri + "/"); } = DefaultApiUrl;
    public Uri TokenUrl { get; init; } = DefaultTokenUrl;
    public string Scope { get; init; } = "argipapi";

    /// <summary>Reads ARGIP_CLIENT_ID, ARGIP_CLIENT_SECRET and the optional ARGIP_API_URL / ARGIP_TOKEN_URL (absolute http or https URLs).</summary>
    /// <exception cref="ArgipApiConfigurationException">A variable is missing or invalid.</exception>
    public static ArgipApiOptions FromEnvironment()
    {
        static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new ArgipApiConfigurationException($"Environment variable {name} is not set. Copy .env.example to .env and fill it in.");

        static Uri? OptionalUrl(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } text
            ? Uri.TryCreate(text, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" && url.Host.Length > 0
                ? url
                : throw new ArgipApiConfigurationException($"{name} is not an absolute http(s) URL: '{text}'.")
            : null;

        return new ArgipApiOptions
        {
            ClientId = Required("ARGIP_CLIENT_ID"),
            ClientSecret = Required("ARGIP_CLIENT_SECRET"),
            ApiUrl = OptionalUrl("ARGIP_API_URL") ?? DefaultApiUrl,
            TokenUrl = OptionalUrl("ARGIP_TOKEN_URL") ?? DefaultTokenUrl,
        };
    }
}
