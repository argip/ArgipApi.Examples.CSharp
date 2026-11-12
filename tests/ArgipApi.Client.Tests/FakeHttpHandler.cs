using System.Net;

namespace ArgipApi.Client.Tests;

/// <summary>Replays queued responses and records every request; no network.</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _apiResponses = new();
    private readonly Queue<HttpResponseMessage> _tokenResponses = new();
    private readonly object _gate = new();
    private int _tokenNumber;

    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];
    public int TokenExpiresIn { get; set; } = 28800;

    /// <summary>API requests carrying this bearer token get 401 without consuming a queued response.</summary>
    public string? RejectedBearer { get; set; }

    public IEnumerable<(HttpRequestMessage Request, string Body)> TokenRequests => Requests.Where(x => x.Request.RequestUri!.AbsolutePath == "/connect/token");
    public IEnumerable<(HttpRequestMessage Request, string Body)> ApiRequests => Requests.Where(x => x.Request.RequestUri!.AbsolutePath != "/connect/token");

    public void EnqueueToken(HttpResponseMessage response) => _tokenResponses.Enqueue(response);

    public void Enqueue(HttpStatusCode status, string body = "", Action<HttpResponseMessage>? configure = null) =>
        _apiResponses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
            configure?.Invoke(response);
            return response;
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Complete asynchronously, like a real network call, so concurrent callers really interleave.
        await Task.Yield();
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_gate) Requests.Add((request, body));

        if (request.RequestUri!.AbsolutePath == "/connect/token")
        {
            int number;
            lock (_gate)
            {
                if (_tokenResponses.Count > 0) return _tokenResponses.Dequeue();
                number = ++_tokenNumber;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"access_token":"tok-{{number}}","expires_in":{{TokenExpiresIn}},"token_type":"Bearer"}""")
            };
        }

        if (RejectedBearer is not null && request.Headers.Authorization?.Parameter == RejectedBearer) return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        Func<HttpRequestMessage, HttpResponseMessage> next;
        lock (_gate) next = _apiResponses.Dequeue();
        return next(request);
    }
}
