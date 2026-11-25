namespace NativeCord.Tests.Helpers;

/// <summary>
/// Deterministic <see cref="TimeProvider"/> for unit tests with optional auto-completing delays
/// and manual clock advancement.
/// </summary>
// 既支持 RestClient 429 重试测试的“自动快进并记录延迟”模式，也支持后续限流器测试的“手动 Advance 步进”模式
internal sealed class FakeTimeProvider : TimeProvider
{
    private readonly Lock _sync = new();
    private readonly List<TimeSpan> _recordedDelays = [];
    private readonly List<ManualTimer> _timers = [];
    private readonly bool _autoCompleteDelays;
    private DateTimeOffset _utcNow;

    public FakeTimeProvider(DateTimeOffset? startTime = null, bool autoCompleteDelays = true)
    {
        _utcNow = startTime ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _autoCompleteDelays = autoCompleteDelays;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public IReadOnlyList<TimeSpan> RecordedDelays
    {
        get
        {
            lock (_sync)
                return [.. _recordedDelays];
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
            return _utcNow;
    }

    public override long GetTimestamp()
    {
        lock (_sync)
            return _utcNow.UtcTicks;
    }

    /// <summary>
    /// Advances the virtual clock and fires all pending timers whose due time has elapsed.
    /// </summary>
    public void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);

        List<ManualTimer> ready = [];
        lock (_sync)
        {
            _utcNow += delta;
            long nowTicks = _utcNow.UtcTicks;

            for (int i = _timers.Count - 1; i >= 0; i--)
            {
                ManualTimer timer = _timers[i];
                if (!timer.IsDisposed && timer.DueAtTicks <= nowTicks)
                {
                    ready.Add(timer);
                    _timers.RemoveAt(i);
                }
            }
        }

        // 按到期时间升序触发，保持严格时序确定性
        ready.Sort(static (a, b) => a.DueAtTicks.CompareTo(b.DueAtTicks));
        foreach (ManualTimer timer in ready)
        {
            if (!timer.IsDisposed)
                timer.Callback(timer.State);
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime == Timeout.InfiniteTimeSpan)
            return NoOpTimer.Instance;

        bool fireImmediately = false;
        ManualTimer timer;

        lock (_sync)
        {
            _recordedDelays.Add(dueTime);
            if (_autoCompleteDelays)
            {
                // 自动快进虚拟时间并在线程池异步触发回调，确保 Task.Delay 在 CreateTimer 返回后立刻完成且不阻塞
                _utcNow += dueTime;
                fireImmediately = true;
                timer = new ManualTimer(this, callback, state, _utcNow.UtcTicks);
            }
            else if (dueTime <= TimeSpan.Zero)
            {
                fireImmediately = true;
                timer = new ManualTimer(this, callback, state, _utcNow.UtcTicks);
            }
            else
            {
                long dueAt = _utcNow.UtcTicks + dueTime.Ticks;
                timer = new ManualTimer(this, callback, state, dueAt);
                _timers.Add(timer);
            }
        }

        if (fireImmediately)
            ThreadPool.QueueUserWorkItem(static pair => pair.Callback(pair.State), (Callback: callback, State: state), preferLocal: false);

        return timer;
    }

    private void RemoveTimer(ManualTimer timer)
    {
        lock (_sync)
            _timers.Remove(timer);
    }

    private sealed class ManualTimer(FakeTimeProvider owner, TimerCallback callback, object? state, long dueAtTicks) : ITimer
    {
        public TimerCallback Callback { get; } = callback;
        public object? State { get; } = state;
        public long DueAtTicks { get; private set; } = dueAtTicks;
        public bool IsDisposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (IsDisposed)
                return false;

            lock (owner._sync)
            {
                if (dueTime == Timeout.InfiniteTimeSpan)
                {
                    owner._timers.Remove(this);
                    return true;
                }

                DueAtTicks = owner._utcNow.UtcTicks + dueTime.Ticks;
                if (!owner._timers.Contains(this))
                    owner._timers.Add(this);
            }

            return true;
        }

        public void Dispose()
        {
            IsDisposed = true;
            owner.RemoveTimer(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoOpTimer : ITimer
    {
        public static NoOpTimer Instance { get; } = new();
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}