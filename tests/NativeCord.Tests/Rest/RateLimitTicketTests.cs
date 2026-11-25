using System.Net;

using NativeCord.Rest.RateLimiting;
using NativeCord.Tests.Helpers;

namespace NativeCord.Tests.Rest;

public sealed class RateLimitTicketTests
{
    [Fact]
    public void Complete_InvokesCallbackOnceAndPreventsCancelOnDispose()
    {
        RecordingRateLimiter callback = new();
        RateLimitInfo info = new(10, 9, null, 1.0, null, "b1", false, RateLimitScope.User);

        RateLimitTicket ticket = new(callback, token: 77);
        ticket.Complete(HttpStatusCode.OK, in info);
        ticket.Complete(HttpStatusCode.OK, in info); // 二次 Complete 必须幂等
        ticket.Dispose(); // 已 Complete 后 Dispose 不应触发 Cancel

        (long token, HttpStatusCode status, RateLimitInfo reported) = Assert.Single(callback.Completed);
        Assert.Equal(77, token);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("b1", reported.Bucket);
        Assert.Empty(callback.Canceled);
    }

    [Fact]
    public void DisposeWithoutComplete_InvokesCancelOnce()
    {
        RecordingRateLimiter callback = new();

        RateLimitTicket ticket = new(callback, token: 88);
        ticket.Dispose();
        ticket.Dispose(); // 二次 Dispose 必须幂等

        Assert.Empty(callback.Completed);
        Assert.Equal(88, Assert.Single(callback.Canceled));
    }

    [Fact]
    public async Task NullRateLimiter_CompletesSynchronouslyWithNoOpTicket()
    {
        RateLimitRequest request = new(new RateLimitKey("GET:/users/@me"), HasAuth: true, ExemptFromGlobal: false);
        ValueTask<RateLimitTicket> task = NullRateLimiter.Instance.AcquireAsync(in request);

        Assert.True(task.IsCompletedSuccessfully);

        using RateLimitTicket ticket = await task;
        RateLimitInfo empty = default;
        ticket.Complete(HttpStatusCode.OK, in empty);
    }
}