namespace NativeCord.Rest.RateLimiting;

/// <summary>
/// Allocation-free sliding-window log rate limiter enforcing Discord's per-scope global limit (50 req/s)
/// and coordinating global 429 pause barriers.
/// </summary>
// 直接内联定长 long[] 环形数组（默认 50 * 8B = 400 字节，常驻 CPU L1 Cache）：
// 相比通用 RingBuffer 省去封装和头尾扩容开销；任意连续 1 秒窗口内数学上严格 <= N，彻底避免固定窗口相位错位打穿，同时突发零延迟。
internal sealed class GlobalRateLimiter
{
    private readonly Lock _sync = new();
    private readonly long[] _timestamps;
    private readonly long _windowTicks;
    private readonly TimeProvider _timeProvider;
    private int _head;
    private int _count;
    private long _pausedUntilTicks;

    public GlobalRateLimiter(int maxRequestsPerWindow, TimeSpan window, TimeProvider timeProvider)
    {
        _timestamps = new long[maxRequestsPerWindow];
        _windowTicks = ToTimestampTicks(window, timeProvider.TimestampFrequency);
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Indicates whether a global 429 pause barrier is currently active.
    /// </summary>
    public bool IsPaused
    {
        get
        {
            lock (_sync)
                return _timeProvider.GetTimestamp() < _pausedUntilTicks;
        }
    }

    /// <summary>
    /// Attempts to acquire a global sliding-window slot synchronously without allocating.
    /// </summary>
    public bool TryAcquireFast() => TryAcquireSync(out _);

    /// <summary>
    /// Pauses all non-exempt requests in this scope for the specified retry duration after a global 429 response.
    /// </summary>
    public void PauseFor(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return;

        long durationTicks = ToTimestampTicks(duration, _timeProvider.TimestampFrequency);
        lock (_sync)
        {
            long target = _timeProvider.GetTimestamp() + durationTicks;
            // 只向后延展暂停截止时间，避免旧响应覆盖掉更晚的暂停屏障
            if (target > _pausedUntilTicks)
                _pausedUntilTicks = target;
        }
    }

    /// <summary>
    /// Waits only if a global 429 pause barrier is currently active, without consuming a sliding-window slot.
    /// </summary>
    // 在进入分桶前调用：若已触发 global 429 全域熔断，直接在最外层拦截，不无谓占用分桶额度
    public ValueTask WaitForGlobalPauseIfNeededAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return ValueTask.FromCanceled(ct);

        TimeSpan delay;
        lock (_sync)
        {
            long now = _timeProvider.GetTimestamp();
            if (now >= _pausedUntilTicks)
                return ValueTask.CompletedTask;

            delay = ToCeilingDelay(_pausedUntilTicks - now, _timeProvider.TimestampFrequency);
        }

        return WaitForGlobalPauseSlowAsync(delay, ct);
    }

    /// <summary>
    /// Acquires a single global sliding-window slot, completing synchronously without allocating when capacity is available.
    /// </summary>
    public ValueTask AcquireAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return ValueTask.FromCanceled(ct);

        // 同步快路径：窗口未满或最早记录已滑出时直接写入时间戳并返回已完成 ValueTask，零堆分配
        if (TryAcquireSync(out TimeSpan delay))
            return ValueTask.CompletedTask;

        return AcquireSlowAsync(delay, ct);
    }

    private bool TryAcquireSync(out TimeSpan delay)
    {
        lock (_sync)
        {
            long now = _timeProvider.GetTimestamp();

            if (now < _pausedUntilTicks)
            {
                delay = ToCeilingDelay(_pausedUntilTicks - now, _timeProvider.TimestampFrequency);
                return false;
            }

            if (_count < _timestamps.Length)
            {
                int tail = (_head + _count) % _timestamps.Length;
                _timestamps[tail] = now;
                _count++;
                delay = TimeSpan.Zero;
                return true;
            }

            // 环形数组已满：检查最早那条记录（第前 N 个请求）是否已经滑出窗口
            long readyAt = _timestamps[_head] + _windowTicks;
            if (now >= readyAt)
            {
                _timestamps[_head] = now;
                _head = (_head + 1) % _timestamps.Length;
                delay = TimeSpan.Zero;
                return true;
            }

            delay = ToCeilingDelay(readyAt - now, _timeProvider.TimestampFrequency);
            return false;
        }
    }

    private async ValueTask AcquireSlowAsync(TimeSpan initialDelay, CancellationToken ct)
    {
        // 等待期间不预占槽位，醒来后再重检：取消时零槽位泄漏，且能立刻感知中途新发生的 global 429 暂停
        TimeSpan delay = initialDelay;
        while (true)
        {
            await Task.Delay(delay, _timeProvider, ct).ConfigureAwait(false);
            if (TryAcquireSync(out delay))
                return;
        }
    }

    private async ValueTask WaitForGlobalPauseSlowAsync(TimeSpan initialDelay, CancellationToken ct)
    {
        TimeSpan delay = initialDelay;
        while (true)
        {
            await Task.Delay(delay, _timeProvider, ct).ConfigureAwait(false);

            lock (_sync)
            {
                long now = _timeProvider.GetTimestamp();
                if (now >= _pausedUntilTicks)
                    return;

                delay = ToCeilingDelay(_pausedUntilTicks - now, _timeProvider.TimestampFrequency);
            }
        }
    }

    internal static long ToTimestampTicks(TimeSpan duration, long timestampFrequency)
    {
        if (duration <= TimeSpan.Zero)
            return 0;

        // 向上取整换算到 TimeProvider 的单调时钟刻度，避免因整数截断导致窗口略短于配置值
        double scaled = duration.TotalSeconds * timestampFrequency;
        return (long)Math.Ceiling(scaled);
    }

    internal static TimeSpan ToCeilingDelay(long remainingTicks, long timestampFrequency)
    {
        if (remainingTicks <= 0)
            return TimeSpan.Zero;

        // 向上取整到整毫秒（至少 1ms）：与 Task.Delay 的毫秒粒度对齐，杜绝提前 1ms 醒来形成忙等循环
        double milliseconds = (double)remainingTicks * 1000.0 / timestampFrequency;
        return TimeSpan.FromMilliseconds(Math.Max(1.0, Math.Ceiling(milliseconds)));
    }
}