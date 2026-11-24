using System.Net;

namespace NativeCord.Rest.RateLimiting;

// 具体桶或限流器可直接实现此接口并传入 RateLimitTicket，从而免去为每个请求分配独立的 Lease 对象

/// <summary>
/// Callback target notified when a <see cref="RateLimitTicket"/> completes or is canceled.
/// </summary>
public interface IRateLimitTicketCallback
{
    /// <summary>
    /// Reports the HTTP status code and parsed rate-limit headers after receiving a response.
    /// </summary>
    void Complete(long ticketToken, HttpStatusCode statusCode, in RateLimitInfo info);

    /// <summary>
    /// Releases any reserved slot when the request fails or is canceled before receiving a response.
    /// </summary>
    void Cancel(long ticketToken);
}

/// <summary>
/// Allocation-free lease returned by <see cref="IRateLimiter.AcquireAsync"/>.
/// Call <see cref="Complete"/> once the HTTP response headers are available;
/// disposing without calling <see cref="Complete"/> releases any reserved concurrency slot.
/// </summary>
public struct RateLimitTicket : IDisposable
{
    private IRateLimitTicketCallback? _callback;
    private readonly long _token;

    public RateLimitTicket(IRateLimitTicketCallback? callback, long token = 0)
    {
        _callback = callback;
        _token = token;
    }

    /// <summary>
    /// Marks the request as completed and feeds the response headers back to the rate limiter.
    /// </summary>
    public void Complete(HttpStatusCode statusCode, in RateLimitInfo info)
    {
        IRateLimitTicketCallback? callback = _callback;
        if (callback is null)
            return;

        // 先置空回调再通知，保证 Complete 与后续的 Dispose 幂等，不会重复触发 Cancel
        _callback = null;
        callback.Complete(_token, statusCode, in info);
    }

    /// <summary>
    /// Cancels the lease if <see cref="Complete"/> was not called (for example due to network errors or cancellation).
    /// </summary>
    public void Dispose()
    {
        IRateLimitTicketCallback? callback = _callback;
        if (callback is null)
            return;

        // 走到这里说明在收到响应前就因异常或取消跳出了 using 块，必须通知限流器归还占用的槽位以防死锁
        _callback = null;
        callback.Cancel(_token);
    }
}