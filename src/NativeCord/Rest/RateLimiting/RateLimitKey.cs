namespace NativeCord.Rest.RateLimiting;

// 首次请求尚未拿到服务端返回的 X-RateLimit-Bucket 哈希，需要用“路由模板 + 顶级资源标识”作为临时桶键；
// 学到服务端哈希后，再把 (BucketTemplate) 映射到服务端 BucketHash，并仍按 MajorResourceId/Token 隔离分桶。

/// <summary>
/// Identifies a rate-limit route bucket before Discord's server-assigned bucket hash is known.
/// </summary>
/// <param name="BucketTemplate">
/// Route template excluding dynamic minor parameters, e.g. <c>POST:/channels/{channel_id}/messages</c>.
/// </param>
/// <param name="MajorResourceId">
/// Top-level resource ID (<c>channel_id</c>, <c>guild_id</c>, or <c>webhook_id</c>), or <c>0</c> if none.
/// </param>
/// <param name="MajorResourceToken">
/// Optional secondary top-level resource identifier (such as a webhook token).
/// </param>
public readonly record struct RateLimitKey(
    string BucketTemplate,
    ulong MajorResourceId = 0,
    string? MajorResourceToken = null);