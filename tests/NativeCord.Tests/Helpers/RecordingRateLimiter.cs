using System.Net;

using NativeCord.Rest.RateLimiting;

namespace NativeCord.Tests.Helpers;

/// <summary>
/// Test spy implementing <see cref="IRateLimiter"/> and <see cref="IRateLimitTicketCallback"/>
/// to verify acquisition context and ticket completion/cancellation lifecycle.
/// </summary>
internal sealed class RecordingRateLimiter : IRateLimiter, IRateLimitTicketCallback
{
    private readonly Lock _sync = new();
    private readonly List<RateLimitRequest> _acquired = [];
    private readonly List<(long Token, HttpStatusCode StatusCode, RateLimitInfo Info)> _completed = [];
    private readonly List<long> _canceled = [];
    private long _nextToken;

    public bool IsDisposed { get; private set; }

    public IReadOnlyList<RateLimitRequest> Acquired
    {
        get
        {
            lock (_sync)
                return [.. _acquired];
        }
    }

    public IReadOnlyList<(long Token, HttpStatusCode StatusCode, RateLimitInfo Info)> Completed
    {
        get
        {
            lock (_sync)
                return [.. _completed];
        }
    }

    public IReadOnlyList<long> Canceled
    {
        get
        {
            lock (_sync)
                return [.. _canceled];
        }
    }

    public ValueTask<RateLimitTicket> AcquireAsync(in RateLimitRequest request, CancellationToken ct = default)
    {
        long token = Interlocked.Increment(ref _nextToken);
        lock (_sync)
            _acquired.Add(request);

        return ValueTask.FromResult(new RateLimitTicket(this, token));
    }

    public void Complete(long ticketToken, HttpStatusCode statusCode, in RateLimitInfo info)
    {
        lock (_sync)
            _completed.Add((ticketToken, statusCode, info));
    }

    public void Cancel(long ticketToken)
    {
        lock (_sync)
            _canceled.Add(ticketToken);
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}