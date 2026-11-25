namespace NativeCord.Rest.RateLimiting;

/// <summary>
/// Configuration options for <see cref="DiscordRateLimiter"/>.
/// </summary>
public sealed record DiscordRateLimiterOptions
{
    public static DiscordRateLimiterOptions Default { get; } = new();

    /// <summary>
    /// Maximum number of non-exempt requests permitted within <see cref="GlobalWindow"/> per auth scope.
    /// Discord enforces a hard global limit of 50 requests per second.
    /// </summary>
    public int GlobalRequestsPerWindow { get; init; } = 50;

    /// <summary>
    /// Duration of the global sliding window. Slightly exceeding 1 second absorbs network arrival jitter.
    /// </summary>
    public TimeSpan GlobalWindow { get; init; } = TimeSpan.FromMilliseconds(1050);

    /// <summary>
    /// Safety buffer added to per-route <c>X-RateLimit-Reset-After</c> durations to avoid arriving milliseconds early.
    /// </summary>
    public TimeSpan BucketResetBuffer { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Idle duration after which an inactive bucket becomes eligible for eviction during periodic cleanup.
    /// </summary>
    public TimeSpan BucketIdleTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Minimum interval between opportunistic idle-bucket cleanup sweeps.
    /// </summary>
    public TimeSpan CleanupInterval { get; init; } = TimeSpan.FromMinutes(2);

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(GlobalRequestsPerWindow);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(GlobalWindow, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(BucketResetBuffer, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(BucketIdleTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(CleanupInterval, TimeSpan.Zero);
    }
}