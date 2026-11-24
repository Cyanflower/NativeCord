namespace NativeCord.Rest.RateLimiting;

/// <summary>
/// No-op rate limiter that immediately permits every request without allocating.
/// Useful for initial bring-up, testing, or when rate limiting is managed externally (e.g. REST proxy).
/// </summary>
public sealed class NullRateLimiter : IRateLimiter
{
    public static NullRateLimiter Instance { get; } = new();

    private NullRateLimiter()
    {
    }

    /// <inheritdoc />
    public ValueTask<RateLimitTicket> AcquireAsync(in RateLimitRequest request, CancellationToken ct = default)
        // 返回已同步完成的 ValueTask + 空回调的 default struct，整个过程零堆分配
        => ValueTask.FromResult(default(RateLimitTicket));

    /// <inheritdoc />
    public void Dispose()
    {
    }
}