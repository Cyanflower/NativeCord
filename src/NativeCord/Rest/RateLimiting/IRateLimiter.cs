namespace NativeCord.Rest.RateLimiting;

// AuthScopeKey 用于区分不同 Token（以及无鉴权请求按 IP 共享全局限流）的限流隔离域

/// <summary>
/// Rate-limit acquisition context describing the target route and its global/auth scope.
/// </summary>
public readonly record struct RateLimitRequest(
    RateLimitKey Key,
    bool HasAuth,
    bool ExemptFromGlobal,
    string? AuthScopeKey = null);

/// <summary>
/// Coordinates Discord REST rate limiting across global and per-route buckets.
/// </summary>
public interface IRateLimiter : IDisposable
{
    /// <summary>
    /// Acquires permission to send a single HTTP request, waiting if the relevant bucket is exhausted.
    /// </summary>
    ValueTask<RateLimitTicket> AcquireAsync(in RateLimitRequest request, CancellationToken ct = default);
}