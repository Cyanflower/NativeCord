using System.Collections.Concurrent;
using System.Net;

namespace NativeCord.Rest.RateLimiting;

/// <summary>
/// Full Discord REST rate limiter coordinating per-route server-hashed buckets,
/// unknown-route serial probing, and per-auth-scope sliding-window global rate limits.
/// </summary>
public sealed class DiscordRateLimiter : IRateLimiter
{
    private readonly DiscordRateLimiterOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly long _resetBufferTicks;
    private readonly long _idleTimeoutTicks;
    private readonly long _cleanupIntervalTicks;

    // 按认证域（不同 Bot/Bearer Token 与无鉴权匿名 IP 域）完全隔离全局限流与分桶状态
    private readonly ConcurrentDictionary<string, AuthScopeState> _authScopes = new(StringComparer.Ordinal);
    private readonly AuthScopeState _anonymousScope;
    private long _lastCleanupTicks;
    private int _disposed;

    /// <summary>
    /// Initializes a new <see cref="DiscordRateLimiter"/> instance.
    /// </summary>
    /// <param name="options">Optional rate-limiting configuration; defaults to <see cref="DiscordRateLimiterOptions.Default"/>.</param>
    /// <param name="timeProvider">Optional time provider for deterministic testing; defaults to <see cref="TimeProvider.System"/>.</param>
    public DiscordRateLimiter(DiscordRateLimiterOptions? options = null, TimeProvider? timeProvider = null)
    {
        _options = options ?? DiscordRateLimiterOptions.Default;
        _options.Validate();

        _timeProvider = timeProvider ?? TimeProvider.System;
        _resetBufferTicks = GlobalRateLimiter.ToTimestampTicks(_options.BucketResetBuffer, _timeProvider.TimestampFrequency);
        _idleTimeoutTicks = GlobalRateLimiter.ToTimestampTicks(_options.BucketIdleTimeout, _timeProvider.TimestampFrequency);
        _cleanupIntervalTicks = GlobalRateLimiter.ToTimestampTicks(_options.CleanupInterval, _timeProvider.TimestampFrequency);
        _lastCleanupTicks = _timeProvider.GetTimestamp();

        _anonymousScope = new AuthScopeState(this);
    }

    /// <inheritdoc />
    public ValueTask<RateLimitTicket> AcquireAsync(in RateLimitRequest request, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (ct.IsCancellationRequested)
            return ValueTask.FromCanceled<RateLimitTicket>(ct);

        TrySweepIdleBuckets();

        AuthScopeState scope = ResolveScope(in request);

        // 同步零分配快路径：未触发全局暂停、分桶有余量且全局窗口未满时直接同步放行，不创建 async 状态机
        if (request.ExemptFromGlobal || !scope.GlobalLimiter.IsPaused)
        {
            RateLimitBucket bucket = scope.GetOrCreateBucket(request.Key);
            if (bucket.TryAcquireFast(out long ticketToken))
            {
                if (request.ExemptFromGlobal || scope.GlobalLimiter.TryAcquireFast())
                    return ValueTask.FromResult(new RateLimitTicket(bucket, ticketToken));

                // 已拿到分桶额度但全局窗口已满：进入慢路径等待全局窗口，取消时自动退还分桶额度
                return AcquireGlobalSlowAsync(scope.GlobalLimiter, bucket, ticketToken, ct);
            }
        }

        return AcquireSlowAsync(scope, request.Key, request.ExemptFromGlobal, ct);
    }

    private static async ValueTask<RateLimitTicket> AcquireGlobalSlowAsync(
        GlobalRateLimiter globalLimiter,
        RateLimitBucket bucket,
        long ticketToken,
        CancellationToken ct)
    {
        try
        {
            await globalLimiter.AcquireAsync(ct).ConfigureAwait(false);
            return new RateLimitTicket(bucket, ticketToken);
        }
        catch
        {
            // 等待全局额度期间被取消时立刻退还分桶额度，防止槽位泄漏
            bucket.Cancel(ticketToken);
            throw;
        }
    }

    private static async ValueTask<RateLimitTicket> AcquireSlowAsync(
        AuthScopeState scope,
        RateLimitKey key,
        bool exemptFromGlobal,
        CancellationToken ct)
    {
        // 1. 若所属认证域正处于 global 429 暂停期（且本端点不豁免），先在分桶前等待暂停结束
        if (!exemptFromGlobal)
            await scope.GlobalLimiter.WaitForGlobalPauseIfNeededAsync(ct).ConfigureAwait(false);

        // 2. 先抢分桶额度再抢全局额度：避免被单个拥堵频道排队的请求白白耗尽全局 50/s 配额
        BucketLease lease = await AcquireBucketLeaseAsync(scope, key, ct).ConfigureAwait(false);

        // 3. 拿到分桶许可准备发 HTTP 前，再申请全局滑动窗口额度
        if (!exemptFromGlobal)
            return await AcquireGlobalSlowAsync(scope.GlobalLimiter, lease.Bucket!, lease.TicketToken, ct).ConfigureAwait(false);

        return new RateLimitTicket(lease.Bucket, lease.TicketToken);
    }

    /// <summary>
    /// Returns the total number of currently tracked buckets across all auth scopes (intended for diagnostics and tests).
    /// </summary>
    internal int ActiveBucketCount
    {
        get
        {
            int total = _anonymousScope.BucketCount;
            foreach (AuthScopeState scope in _authScopes.Values)
                total += scope.BucketCount;

            return total;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _anonymousScope.Dispose();
        foreach (AuthScopeState scope in _authScopes.Values)
            scope.Dispose();

        _authScopes.Clear();
    }

    private AuthScopeState ResolveScope(in RateLimitRequest request)
    {
        if (!request.HasAuth || string.IsNullOrEmpty(request.AuthScopeKey))
            return _anonymousScope;

        return _authScopes.GetOrAdd(request.AuthScopeKey, static (_, limiter) => new AuthScopeState(limiter), this);
    }

    private static async ValueTask<BucketLease> AcquireBucketLeaseAsync(AuthScopeState scope, RateLimitKey key, CancellationToken ct)
    {
        while (true)
        {
            RateLimitBucket bucket = scope.GetOrCreateBucket(key);
            BucketLease lease = await bucket.AcquireAsync(ct).ConfigureAwait(false);

            // 若等待期间临时桶完成迁移或旧桶恰好被惰性驱逐，则重新查表获取最新正式桶
            if (lease.Outcome == BucketAcquireOutcome.Acquired)
                return lease;
        }
    }

    private void TrySweepIdleBuckets()
    {
        long now = _timeProvider.GetTimestamp();
        long last = Volatile.Read(ref _lastCleanupTicks);
        if ((now - last) < _cleanupIntervalTicks)
            return;

        // 仅允许一个线程赢得本次清理窗口，避免高并发时多个线程同时遍历字典
        if (Interlocked.CompareExchange(ref _lastCleanupTicks, now, last) != last)
            return;

        _anonymousScope.SweepIdleBuckets(now, _idleTimeoutTicks);
        foreach (AuthScopeState scope in _authScopes.Values)
            scope.SweepIdleBuckets(now, _idleTimeoutTicks);
    }

    /// <summary>
    /// Holds isolated global limiter, route-to-hash map, and per-route buckets for a single auth scope.
    /// </summary>
    internal sealed class AuthScopeState
    {
        private readonly DiscordRateLimiter _owner;
        private readonly ConcurrentDictionary<string, string> _routeToHash = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<BucketId, RateLimitBucket> _buckets = new();

        public GlobalRateLimiter GlobalLimiter { get; }

        public int BucketCount => _buckets.Count;

        public AuthScopeState(DiscordRateLimiter owner)
        {
            _owner = owner;
            GlobalLimiter = new GlobalRateLimiter(
                owner._options.GlobalRequestsPerWindow,
                owner._options.GlobalWindow,
                owner._timeProvider);
        }

        public RateLimitBucket GetOrCreateBucket(in RateLimitKey key)
        {
            // 若已知该路由模板对应的服务端 BucketHash，直接定位到共享正式桶；否则落入临时路由桶
            BucketId bucketId = _routeToHash.TryGetValue(key.BucketTemplate, out string? serverHash)
                ? new BucketId(IsServerHash: true, serverHash, key.MajorResourceId, key.MajorResourceToken)
                : new BucketId(IsServerHash: false, key.BucketTemplate, key.MajorResourceId, key.MajorResourceToken);

            return _buckets.GetOrAdd(
                bucketId,
                static (id, state) => new RateLimitBucket(id, state, state._owner._timeProvider, state._owner._resetBufferTicks),
                this);
        }

        public void PromoteTempBucket(RateLimitBucket tempBucket, string serverBucketHash, HttpStatusCode statusCode, in RateLimitInfo info)
        {
            // 1. 记录路由模板 -> 服务端 BucketHash 映射，后续所有顶级资源调用此路由都会直接命中正式桶
            _routeToHash[tempBucket.Id.Name] = serverBucketHash;

            // 2. 找到或创建对应的正式桶，并把本次探测响应头同步应用到正式桶上
            BucketId canonicalId = new(
                IsServerHash: true,
                serverBucketHash,
                tempBucket.Id.MajorResourceId,
                tempBucket.Id.MajorResourceToken);

            RateLimitBucket canonicalBucket = _buckets.GetOrAdd(
                canonicalId,
                static (id, state) => new RateLimitBucket(id, state, state._owner._timeProvider, state._owner._resetBufferTicks),
                this);

            canonicalBucket.ApplyPromotedProbeResponse(statusCode, in info);

            // 3. 移除临时桶并唤醒所有在临时桶内排队的请求，使其无缝重路由到正式桶
            _buckets.TryRemove(new KeyValuePair<BucketId, RateLimitBucket>(tempBucket.Id, tempBucket));
            tempBucket.MarkMigratedAndWakeWaiters();
        }

        public void SweepIdleBuckets(long nowTicks, long idleTimeoutTicks)
        {
            foreach (KeyValuePair<BucketId, RateLimitBucket> entry in _buckets)
            {
                if (entry.Value.TryEvictIfIdle(nowTicks, idleTimeoutTicks))
                    _buckets.TryRemove(entry);
            }
        }

        public void Dispose()
        {
            foreach (RateLimitBucket bucket in _buckets.Values)
                bucket.Dispose();

            _buckets.Clear();
            _routeToHash.Clear();
        }
    }
}