using System.Net;
using System.Text;

namespace NativeCord.Tests.Helpers;

/// <summary>
/// Immutable snapshot of an <see cref="HttpRequestMessage"/> captured before disposal.
/// </summary>
internal sealed record CapturedRequest(
    HttpMethod Method,
    Uri? RequestUri,
    IReadOnlyDictionary<string, string[]> Headers,
    string? ContentType,
    byte[] BodyBytes)
{
    public string BodyText => BodyBytes.Length == 0 ? string.Empty : Encoding.UTF8.GetString(BodyBytes);

    public string? GetSingleHeader(string name)
        => Headers.TryGetValue(name, out string[]? values) && values.Length > 0 ? values[0] : null;

    public string? GetJoinedHeader(string name, string separator = " ")
        => Headers.TryGetValue(name, out string[]? values) && values.Length > 0 ? string.Join(separator, values) : null;
}

/// <summary>
/// Programmable <see cref="HttpMessageHandler"/> that records outgoing requests and replays queued responses.
/// </summary>
internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responders = new();
    private readonly List<CapturedRequest> _requests = [];
    private readonly Lock _sync = new();

    public IReadOnlyList<CapturedRequest> Requests
    {
        get
        {
            lock (_sync)
                return [.. _requests];
        }
    }

    public int CallCount
    {
        get
        {
            lock (_sync)
                return _requests.Count;
        }
    }

    public void Enqueue(HttpResponseMessage response)
        => Enqueue((_, _) => Task.FromResult(response));

    public void EnqueueJson(HttpStatusCode statusCode, string json, Action<HttpResponseMessage>? configure = null)
    {
        Enqueue((_, _) =>
        {
            HttpResponseMessage response = new(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            configure?.Invoke(response);
            return Task.FromResult(response);
        });
    }

    public void EnqueueException(Exception exception)
        => Enqueue((_, _) => Task.FromException<HttpResponseMessage>(exception));

    public void Enqueue(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        lock (_sync)
            _responders.Enqueue(responder);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // RestClient 会在单次尝试结束后 Dispose HttpRequestMessage 与 Content，因此必须在返回前拍下完整快照
        Dictionary<string, string[]> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            headers[header.Key] = [.. header.Value];

        string? contentType = null;
        byte[] bodyBytes = [];
        if (request.Content is not null)
        {
            if (request.Content.Headers.TryGetValues("Content-Type", out IEnumerable<string>? ctValues))
                contentType = string.Join(", ", ctValues);

            bodyBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        CapturedRequest captured = new(request.Method, request.RequestUri, headers, contentType, bodyBytes);

        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder;
        lock (_sync)
        {
            _requests.Add(captured);
            if (_responders.Count == 0)
                throw new InvalidOperationException($"No mock HTTP response queued for {request.Method} {request.RequestUri}.");

            responder = _responders.Dequeue();
        }

        return await responder(request, cancellationToken).ConfigureAwait(false);
    }
}