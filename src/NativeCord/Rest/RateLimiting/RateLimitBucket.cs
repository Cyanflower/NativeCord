using System.Net;

namespace NativeCord.Rest.RateLimiting;

/// <summary>
/// Composite dictionary key uniquely identifying a temporary route bucket or a server-hashed bucket.
/// </summary>
// 使用值类型键避免拼接字符串产生临时堆分配
internal readonly record struct BucketId(
    bool IsServerHash,
    string Name,
    ulong MajorResourceId,
    string? MajorResourceToken);

internal enum BucketAcquireOutcome : byte
{
    Acquired = 0,
    RetryLookup = 1
}

internal readonly record struct BucketLease(
    BucketAcquireOutcome Outcome,
    RateLimitBucket? Bucket,
    long TicketToken);

/// <summary>
/// Per-route rate-limit bucket state machine implementing <see cref="IRateLimitTicketCallback"/> directly
/// so issuing a <see cref="RateLimitTicket"/> requires zero extra heap allocation.
/// </summary>
internal sealed class RateLimitBucket : IRateLimitTicketCallback
{
    private readonly Lock _sync = new();
    private readonly DiscordRateLimiter.AuthScopeState _scope;
    private readonly TimeProvider _timeProvider;
    private readonly long _resetBufferTicks;
    private readonly Queue<Waiter> _waiters = new();

    private ITimer? _resetTimer;
    private long _windowGeneration = 1;
    private long _resetAtTicks;
    private long _lastUsedTicks;
    private int _limit;
    private int _remaining;
    private int _inFlight;

    // 临时桶专属状态：首次探测串行放行 1 个请求，拿到响应头后按结果迁移到正式桶或标记为无分桶端点
    private bool _hasServerLimit;
    private bool _probeInFlight;
    private bool _unbucketed;
    private bool _migratedOrEvicted;
    private bool _disposed;

    public BucketId Id { get; }

    public RateLimitBucket(
        BucketId id,
        DiscordRateLimiter.AuthScopeState scope,
        TimeProvider timeProvider,
        long resetBufferTicks)
    {
        Id = id;
        _scope = scope;
        _timeProvider = timeProvider;
        _resetBufferTicks = resetBufferTicks;
        _lastUsedTicks = timeProvider.GetTimestamp();
    }

    /// <summary>
    /// Attempts to acquire permission synchronously without creating an async state machine when uncontended.
    /// </summary>
    internal bool TryAcquireFast(out long ticketToken)
    {
        lock (_sync)
        {
            if (_disposed || _migratedOrEvicted || _waiters.Count > 0)
            {
                ticketToken = 0;
                return false;
            }

            long now = _timeProvider.GetTimestamp();
            _lastUsedTicks = now;
            return TryGrantLocked(now, out ticketToken);
        }
    }

    /// <summary>
    /// Acquires permission from this bucket or queues in FIFO order until capacity becomes available.
    /// Returns <see cref="BucketAcquireOutcome.RetryLookup"/> if the bucket migrated to a server hash or was evicted.
    /// </summary>
    public ValueTask<BucketLease> AcquireAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return ValueTask.FromCanceled<BucketLease>(ct);

        Waiter waiter;
        lock (_sync)
        {
            if (_disposed)
                return ValueTask.FromException<BucketLease>(new ObjectDisposedException(nameof(DiscordRateLimiter)));

            if (_migratedOrEvicted)
                return ValueTask.FromResult(new BucketLease(BucketAcquireOutcome.RetryLookup, null, 0));

            long now = _timeProvider.GetTimestamp();
            _lastUsedTicks = now;

            // 快路径：仅当无人排队时才允许直接获取，严格保证 FIFO 公平顺序不插队
            if (_waiters.Count == 0 && TryGrantLocked(now, out long ticketToken))
                return ValueTask.FromResult(new BucketLease(BucketAcquireOutcome.Acquired, this, ticketToken));

            waiter = new Waiter(ct);
            _waiters.Enqueue(waiter);
            EnsureResetTimerLocked(now);
        }

        // 仅在真正需要排队时才挂取消回调，且取消只操作 TCS 不抢桶锁，彻底消除死锁风险
        waiter.RegisterCancellation();
        return new ValueTask<BucketLease>(waiter.Task);
    }

    /// <inheritdoc />
    public void Complete(long ticketToken, HttpStatusCode statusCode, in RateLimitInfo info)
    {
        // 若响应包含 global 429，立即触发所属 AuthScope 的全局暂停屏障
        if (statusCode == HttpStatusCode.TooManyRequests && info.IsGlobal)
        {
            TimeSpan pause = ResolvePauseDuration(in info);
            if (pause > TimeSpan.Zero)
                _scope.GlobalLimiter.PauseFor(pause);
        }

        // 若本桶是临时路由桶且服务端下发了 X-RateLimit-Bucket 哈希，升级迁移到共享的正式桶
        if (!Id.IsServerHash && !string.IsNullOrEmpty(info.Bucket))
        {
            _scope.PromoteTempBucket(this, info.Bucket, statusCode, in info);
            return;
        }

        List<(Waiter Waiter, BucketLease Lease)>? toRelease = null;
        lock (_sync)
        {
            long now = _timeProvider.GetTimestamp();
            _lastUsedTicks = now;

            if (_inFlight > 0)
                _inFlight--;

            if (_probeInFlight)
                _probeInFlight = false;

            if (_disposed || _migratedOrEvicted)
                return;

            // 仅处理未过期的同代响应，防止慢响应乱序回来污染已经重置的新窗口
            bool isCurrentWindow = ticketToken == _windowGeneration;

            if (statusCode == HttpStatusCode.TooManyRequests)
            {
                if (info.IsGlobal || info.Scope == RateLimitScope.Shared)
                {
                    // global / shared 429 不惩罚本地路由桶（shared 不算作你的限流消耗）
                }
                else if (isCurrentWindow)
                {
                    // user 级 429：清空本窗口余量并锁定到服务端要求重置时刻
                    ApplyUser429Locked(now, in info);
                }
            }
            else if (info.HasBucketHeaders)
            {
                if (isCurrentWindow)
                    ApplyHeadersLocked(now, in info);
            }
            else if (!Id.IsServerHash && !_hasServerLimit)
            {
                // 首次探测返回成功且无任何限流头：说明该端点不受分桶限流约束，解除串行探测锁
                _unbucketed = true;
            }

            toRelease = DrainWaitersLocked(now);
        }

        ReleaseWaiters(toRelease);
    }

    /// <inheritdoc />
    public void Cancel(long ticketToken)
    {
        List<(Waiter Waiter, BucketLease Lease)>? toRelease = null;
        lock (_sync)
        {
            long now = _timeProvider.GetTimestamp();
            _lastUsedTicks = now;

            if (_inFlight > 0)
                _inFlight--;

            if (_probeInFlight)
                _probeInFlight = false;

            if (_disposed || _migratedOrEvicted)
                return;

            // 同一窗口内请求尚未发到服务端就取消或失败，把预扣的 1 个本地额度归还并唤醒排队者
            if (_hasServerLimit && ticketToken == _windowGeneration && _remaining < _limit)
                _remaining++;

            toRelease = DrainWaitersLocked(now);
        }

        ReleaseWaiters(toRelease);
    }

    /// <summary>
    /// Applies headers learned from a promoted temporary bucket's probe response onto this canonical server-hash bucket.
    /// </summary>
    internal void ApplyPromotedProbeResponse(HttpStatusCode statusCode, in RateLimitInfo info)
    {
        List<(Waiter Waiter, BucketLease Lease)>? toRelease = null;
        lock (_sync)
        {
            if (_disposed || _migratedOrEvicted)
                return;

            long now = _timeProvider.GetTimestamp();
            _lastUsedTicks = now;

            if (statusCode == HttpStatusCode.TooManyRequests)
            {
                if (!info.IsGlobal && info.Scope != RateLimitScope.Shared)
                    ApplyUser429Locked(now, in info);
            }
            else if (info.HasBucketHeaders)
            {
                ApplyHeadersLocked(now, in info);
            }

            toRelease = DrainWaitersLocked(now);
        }

        ReleaseWaiters(toRelease);
    }

    /// <summary>
    /// Marks a temporary bucket as migrated to a canonical server-hash bucket and wakes all waiters to re-route.
    /// </summary>
    internal void MarkMigratedAndWakeWaiters()
    {
        List<(Waiter Waiter, BucketLease Lease)>? toRelease = null;
        ITimer? timerToDispose;

        lock (_sync)
        {
            if (_inFlight > 0)
                _inFlight--;

            _probeInFlight = false;
            _migratedOrEvicted = true;
            timerToDispose = _resetTimer;
            _resetTimer = null;

            // 唤醒所有仍在临时桶排队的等待者，让他们重新查表并无缝汇流到正式桶
            if (_waiters.Count > 0)
            {
                toRelease = new List<(Waiter, BucketLease)>(_waiters.Count);
                while (_waiters.Count > 0)
                    toRelease.Add((_waiters.Dequeue(), new BucketLease(BucketAcquireOutcome.RetryLookup, null, 0)));
            }
        }

        timerToDispose?.Dispose();
        ReleaseWaiters(toRelease);
    }

    /// <summary>
    /// Attempts to mark this bucket evicted if it has been completely idle past <paramref name="idleTimeoutTicks"/>.
    /// </summary>
    internal bool TryEvictIfIdle(long nowTicks, long idleTimeoutTicks)
    {
        ITimer? timerToDispose;
        lock (_sync)
        {
            if (_migratedOrEvicted || _disposed)
                return false;

            // 有在飞请求、有人排队、尚未到重置时刻或空闲时长未达阈值时绝不驱逐
            if (_inFlight > 0 || _waiters.Count > 0 || nowTicks < _resetAtTicks || (nowTicks - _lastUsedTicks) < idleTimeoutTicks)
                return false;

            _migratedOrEvicted = true;
            timerToDispose = _resetTimer;
            _resetTimer = null;
        }

        timerToDispose?.Dispose();
        return true;
    }

    internal void Dispose()
    {
        List<Waiter>? toCancel = null;
        ITimer? timerToDispose;

        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            timerToDispose = _resetTimer;
            _resetTimer = null;

            if (_waiters.Count > 0)
            {
                toCancel = new List<Waiter>(_waiters.Count);
                while (_waiters.Count > 0)
                    toCancel.Add(_waiters.Dequeue());
            }
        }

        timerToDispose?.Dispose();
        if (toCancel is not null)
        {
            foreach (Waiter waiter in toCancel)
                waiter.SetDisposed();
        }
    }

    private bool TryGrantLocked(long now, out long ticketToken)
    {
        // 情况 1：已确认无分桶限流的端点，直接放行
        if (_unbucketed)
        {
            _inFlight++;
            ticketToken = _windowGeneration;
            return true;
        }

        // 情况 2：尚未从服务端学到任何桶上限（临时桶或刚创建的正式桶），只允许 1 个探测请求在飞
        if (!_hasServerLimit)
        {
            if (_probeInFlight)
            {
                ticketToken = 0;
                return false;
            }

            _probeInFlight = true;
            _inFlight++;
            ticketToken = _windowGeneration;
            return true;
        }

        // 情况 3：正式桶上一个窗口已经过期（无论余量是否耗尽），刷新满额并递增窗口代数以隔离旧窗口迟到响应
        if (_resetAtTicks > 0 && now >= _resetAtTicks)
        {
            _windowGeneration++;
            _remaining = _limit;
            _resetAtTicks = 0;
        }

        if (_remaining > 0)
        {
            _remaining--;
            _inFlight++;
            ticketToken = _windowGeneration;
            return true;
        }

        ticketToken = 0;
        return false;
    }

    private void ApplyHeadersLocked(long now, in RateLimitInfo info)
    {
        if (info.Limit is > 0)
            _limit = info.Limit.Value;

        if (info.ResetAfterSeconds is > 0)
        {
            long resetTicks = GlobalRateLimiter.ToTimestampTicks(
                TimeSpan.FromSeconds(info.ResetAfterSeconds.Value),
                _timeProvider.TimestampFrequency);
            long candidateResetAt = now + resetTicks + _resetBufferTicks;
            if (candidateResetAt > _resetAtTicks)
                _resetAtTicks = candidateResetAt;
        }

        if (info.Remaining.HasValue)
        {
            int serverRemaining = Math.Max(0, info.Remaining.Value);
            if (!_hasServerLimit)
            {
                // 首次学到服务端桶上限：扣除当前其余在飞请求数（通常为 0）作为安全余量
                if (_limit <= 0)
                    _limit = Math.Max(1, serverRemaining + 1);

                _remaining = Math.Max(0, serverRemaining - _inFlight);
                _hasServerLimit = true;
            }
            else if (_inFlight == 0)
            {
                // 无在飞请求时服务端 Remaining 是当前窗口最准确的权威值
                _remaining = serverRemaining;
            }
            else
            {
                // 仍有并发请求在飞时只收紧、不凭空放大，防止并发乱序导致超发
                _remaining = Math.Min(_remaining, serverRemaining);
            }
        }
        else if (_limit > 0 && !_hasServerLimit)
        {
            _remaining = Math.Max(0, _limit - 1 - _inFlight);
            _hasServerLimit = true;
        }
    }

    private void ApplyUser429Locked(long now, in RateLimitInfo info)
    {
        if (info.Limit is > 0)
            _limit = info.Limit.Value;
        else if (_limit <= 0)
            _limit = 1;

        _hasServerLimit = true;
        _remaining = 0;

        TimeSpan pause = ResolvePauseDuration(in info);
        long pauseTicks = GlobalRateLimiter.ToTimestampTicks(pause, _timeProvider.TimestampFrequency);
        long candidateResetAt = now + pauseTicks + _resetBufferTicks;
        if (candidateResetAt > _resetAtTicks)
            _resetAtTicks = candidateResetAt;
    }

    private List<(Waiter Waiter, BucketLease Lease)>? DrainWaitersLocked(long now)
    {
        List<(Waiter Waiter, BucketLease Lease)>? toRelease = null;

        while (_waiters.Count > 0)
        {
            // 先剔除已取消的队头，避免把宝贵额度分给已经放弃等待的调用方
            Waiter head = _waiters.Peek();
            if (head.IsCompleted)
            {
                _waiters.Dequeue();
                continue;
            }

            if (!TryGrantLocked(now, out long ticketToken))
                break;

            _waiters.Dequeue();
            toRelease ??= [];
            toRelease.Add((head, new BucketLease(BucketAcquireOutcome.Acquired, this, ticketToken)));
        }

        if (_waiters.Count > 0)
            EnsureResetTimerLocked(now);
        else
            DisableResetTimerLocked();

        return toRelease;
    }

    private void EnsureResetTimerLocked(long now)
    {
        // 若正在等探测请求返回且尚未有重置时刻，无需创建定时器（探测 Complete/Cancel 会直接唤醒）
        if (!_hasServerLimit || _resetAtTicks <= now)
            return;

        TimeSpan delay = GlobalRateLimiter.ToCeilingDelay(_resetAtTicks - now, _timeProvider.TimestampFrequency);
        if (_resetTimer is null)
            _resetTimer = _timeProvider.CreateTimer(static state => ((RateLimitBucket)state!).OnResetTimerFired(), this, delay, Timeout.InfiniteTimeSpan);
        else
            _resetTimer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    private void DisableResetTimerLocked()
    {
        _resetTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void OnResetTimerFired()
    {
        List<(Waiter Waiter, BucketLease Lease)>? toRelease = null;
        lock (_sync)
        {
            if (_disposed || _migratedOrEvicted)
                return;

            long now = _timeProvider.GetTimestamp();
            toRelease = DrainWaitersLocked(now);
        }

        ReleaseWaiters(toRelease);
    }

    private void ReleaseWaiters(List<(Waiter Waiter, BucketLease Lease)>? toRelease)
    {
        if (toRelease is null)
            return;

        foreach ((Waiter waiter, BucketLease lease) in toRelease)
        {
            // 若 waiter 在移出队列与设置结果的极短窗口内恰好被取消，立即归还刚刚分配的额度
            if (!waiter.TrySetResult(lease) && lease.Outcome == BucketAcquireOutcome.Acquired)
                Cancel(lease.TicketToken);
        }
    }

    private static TimeSpan ResolvePauseDuration(in RateLimitInfo info)
    {
        double seconds = info.IsGlobal || info.Scope == RateLimitScope.Shared
            ? info.RetryAfterSeconds ?? info.ResetAfterSeconds ?? 1.0
            : info.ResetAfterSeconds ?? info.RetryAfterSeconds ?? 1.0;

        if (seconds <= 0)
            return TimeSpan.Zero;

        return TimeSpan.FromMilliseconds(Math.Ceiling(seconds * 1000.0));
    }

    private sealed class Waiter(CancellationToken ct)
    {
        private readonly TaskCompletionSource<BucketLease> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _registration;

        public Task<BucketLease> Task => _tcs.Task;

        public bool IsCompleted => _tcs.Task.IsCompleted;

        public void RegisterCancellation()
        {
            if (!ct.CanBeCanceled)
                return;

            _registration = ct.UnsafeRegister(
                static (state, token) => ((Waiter)state!)._tcs.TrySetCanceled(token),
                this);
        }

        public bool TrySetResult(BucketLease lease)
        {
            _registration.Dispose();
            return _tcs.TrySetResult(lease);
        }

        public void SetDisposed()
        {
            _registration.Dispose();
            _tcs.TrySetException(new ObjectDisposedException(nameof(DiscordRateLimiter)));
        }
    }
}