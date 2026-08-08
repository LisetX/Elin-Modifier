using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;

internal sealed class AiHttpTransportModule : IDisposable
{
    private readonly HttpClient _client;
    private bool _disposed;

    internal AiHttpTransportModule()
    {
        _client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    internal HttpClient Client
    {
        get
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AiHttpTransportModule));
            return _client;
        }
    }

    internal HttpRequestMessage CreateRequest(HttpMethod method, string url, string apiKey)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey ?? "");
        return request;
    }

    internal CancellationTokenSource CreateRequestCancellation(
        CancellationToken cancellationToken,
        int timeoutSeconds,
        int minimumSeconds,
        int maximumSeconds)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var boundedSeconds = Math.Max(minimumSeconds, Math.Min(maximumSeconds, timeoutSeconds));
        linked.CancelAfter(TimeSpan.FromSeconds(boundedSeconds));
        return linked;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _client.Dispose();
    }
}
