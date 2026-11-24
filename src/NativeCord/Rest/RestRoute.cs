using NativeCord.Rest.RateLimiting;

namespace NativeCord.Rest;

// 统一承载路径、限流键和鉴权标记：Path 可包含查询参数，BucketTemplate 保持不含具体 ID 的模板形式

/// <summary>
/// Describes a target Discord REST endpoint, its rate-limit identity, and authentication rules.
/// </summary>
/// <param name="Method">HTTP method for the endpoint.</param>
/// <param name="Path">Relative API path without a leading slash, e.g. <c>channels/123/messages</c>.</param>
/// <param name="BucketTemplate">
/// Route template excluding dynamic minor parameters, e.g. <c>POST:/channels/{channel_id}/messages</c>.
/// </param>
/// <param name="MajorResourceId">
/// Top-level resource ID (<c>channel_id</c>, <c>guild_id</c>, or <c>webhook_id</c>), or <c>0</c> if none.
/// </param>
/// <param name="MajorResourceToken">Optional secondary top-level resource identifier (such as a webhook token).</param>
/// <param name="UseAuth">Whether to attach the client's default authorization header when no override token is specified.</param>
/// <param name="ExemptFromGlobal">Whether this endpoint is exempt from the bot's global rate limit (e.g. interaction callbacks).</param>
public readonly record struct RestRoute(
    HttpMethod Method,
    string Path,
    string BucketTemplate,
    ulong MajorResourceId = 0,
    string? MajorResourceToken = null,
    bool UseAuth = true,
    bool ExemptFromGlobal = false)
{
    /// <summary>
    /// Extracts the rate-limit routing key associated with this route.
    /// </summary>
    public RateLimitKey RateLimitKey => new(BucketTemplate, MajorResourceId, MajorResourceToken);
}