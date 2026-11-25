using NativeCord.Rest.RateLimiting;
using NativeCord.Tests.Helpers;

namespace NativeCord.Tests.Rest;

public sealed class GlobalRateLimiterTests
{
    [Fact]
    public void AcquireAsync_AllowsFullBurstImmediatelyWithoutArtificialDelay()
    {
        FakeTimeProvider clock = new(autoCompleteDelays: false);
        GlobalRateLimiter limiter = new(maxRequestsPerWindow: 5, window: TimeSpan.FromSeconds(1), clock);

        // 突发 5 个请求全部同步零延迟放行
        for (int i = 0; i < 5; i++)
        {
            ValueTask task = limiter.AcquireAsync(CancellationToken.None);
            Assert.True(task.IsCompletedSuccessfully);
        }

        Assert.Empty(clock.RecordedDelays);
    }

    [Fact]
    public async Task AcquireAsync_SlidingWindowPreventsPhaseBoundaryBurst()
    {
        FakeTimeProvider clock = new(autoCompleteDelays: false);
        GlobalRateLimiter limiter = new(maxRequestsPerWindow: 3, window: TimeSpan.FromSeconds(1), clock);

        // t=0ms 放行 1 个，t=400ms 放行 1 个，t=900ms 放行 1 个（窗口内已满 3 个）
        Assert.True(limiter.AcquireAsync(CancellationToken.None).IsCompletedSuccessfully);
        clock.Advance(TimeSpan.FromMilliseconds(400));
        Assert.True(limiter.AcquireAsync(CancellationToken.None).IsCompletedSuccessfully);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        Assert.True(limiter.AcquireAsync(CancellationToken.None).IsCompletedSuccessfully);

        // t=900ms 发第 4 个：必须等到 t=1000ms（最早的 t=0ms 滑出窗口）
        Task fourth = limiter.AcquireAsync(CancellationToken.None).AsTask();
        Assert.False(fourth.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(99)); // t=999ms 仍不可放行
        Assert.False(fourth.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(1)); // t=1000ms 恰好滑出
        await fourth.WaitAsync(TimeSpan.FromSeconds(1));

        // t=1000ms 再发第 5 个：t=400ms 和 t=900ms 仍在最近 1 秒内，必须等到 t=1400ms（杜绝边界双倍突刺）
        Task fifth = limiter.AcquireAsync(CancellationToken.None).AsTask();
        Assert.False(fifth.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(399)); // t=1399ms
        Assert.False(fifth.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(1)); // t=1400ms
        await fifth.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task AcquireAsync_CancelledWhileWaiting_DoesNotLeakWindowSlot()
    {
        FakeTimeProvider clock = new(autoCompleteDelays: false);
        GlobalRateLimiter limiter = new(maxRequestsPerWindow: 1, window: TimeSpan.FromSeconds(1), clock);

        Assert.True(limiter.AcquireAsync(CancellationToken.None).IsCompletedSuccessfully);

        using CancellationTokenSource cts = new();
        Task cancelledWaiter = limiter.AcquireAsync(cts.Token).AsTask();
        Assert.False(cancelledWaiter.IsCompleted);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWaiter);

        // 推进 1 秒后，取消的请求不应占用槽位，正常新请求可立即同步通过
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(limiter.AcquireAsync(CancellationToken.None).IsCompletedSuccessfully);
    }

    [Fact]
    public async Task PauseFor_BlocksAllAcquireAndPauseWaitersUntilExpired()
    {
        FakeTimeProvider clock = new(autoCompleteDelays: false);
        GlobalRateLimiter limiter = new(maxRequestsPerWindow: 50, window: TimeSpan.FromSeconds(1), clock);

        limiter.PauseFor(TimeSpan.FromSeconds(3));
        Assert.True(limiter.IsPaused);

        Task pauseWait = limiter.WaitForGlobalPauseIfNeededAsync(CancellationToken.None).AsTask();
        Task acquireWait = limiter.AcquireAsync(CancellationToken.None).AsTask();
        Assert.False(pauseWait.IsCompleted);
        Assert.False(acquireWait.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(pauseWait.IsCompleted);
        Assert.False(acquireWait.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(1));
        await Task.WhenAll(pauseWait, acquireWait).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(limiter.IsPaused);
    }
}