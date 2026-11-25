using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using NativeCord.Rest.RateLimiting;

namespace NativeCord.Rest;

/// <summary>
/// AOT-friendly HTTP client pipeline for sending requests to the Discord REST API.
/// </summary>
public sealed class RestClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IRateLimiter _rateLimiter;
    private readonly TimeProvider _timeProvider;
    private readonly bool _disposeRateLimiter;
    // 预先拼好默认 Authorization 头（如 "Bot xxx"），按请求动态挂载，避免绑死在 HttpClient.DefaultRequestHeaders 上
    private readonly string? _defaultAuthHeader;
    private readonly int _maxRateLimitRetries;
    private int _disposed;

    /// <summary>
    /// Initializes a new <see cref="RestClient"/> instance.
    /// </summary>
    /// <param name="defaultToken">Default authorization token attached to routes with <see cref="RestRoute.UseAuth"/> enabled.</param>
    /// <param name="baseUrl">Optional custom base URL and API version settings.</param>
    /// <param name="rateLimiter">
    /// Optional rate limiter; defaults to a dedicated <see cref="DiscordRateLimiter"/> owned by this client.
    /// Pass <see cref="NullRateLimiter.Instance"/> to disable client-side rate limiting.
    /// </param>
    /// <param name="handler">Optional custom <see cref="HttpMessageHandler"/> (for proxies or unit testing).</param>
    /// <param name="disposeHandler">Whether disposing this client also disposes a caller-supplied <paramref name="handler"/>.</param>
    /// <param name="disposeRateLimiter">Whether disposing this client also disposes a caller-supplied <paramref name="rateLimiter"/>.</param>
    /// <param name="maxRateLimitRetries">Maximum number of automatic retries on HTTP 429 responses.</param>
    /// <param name="timeProvider">Optional time provider controlling retry delays and the default rate limiter; defaults to <see cref="TimeProvider.System"/>.</param>
    public RestClient(
        BotToken? defaultToken = null,
        BaseUrl? baseUrl = null,
        IRateLimiter? rateLimiter = null,
        HttpMessageHandler? handler = null,
        bool disposeHandler = true,
        bool disposeRateLimiter = false,
        int maxRateLimitRetries = 5,
        TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRateLimitRetries);

        _defaultAuthHeader = defaultToken is { } token ? token.ToAuthorizationHeader() : null;
        // 注入 TimeProvider 使 429 退避等待与默认限流器在单元测试中完全可确定、无需真实睡眠
        _timeProvider = timeProvider ?? TimeProvider.System;
        // 默认开箱启用 DiscordRateLimiter 并由本实例托管释放；如需禁用可显式传入 NullRateLimiter.Instance
        _rateLimiter = rateLimiter ?? new DiscordRateLimiter(timeProvider: _timeProvider);
        _disposeRateLimiter = rateLimiter is null || disposeRateLimiter;
        _maxRateLimitRetries = maxRateLimitRetries;

        // 若由内部创建 SocketsHttpHandler，则强制由内部负责释放，防止句柄泄漏
        HttpMessageHandler actualHandler = handler ?? CreateSocketsHttpHandler();
        bool actualDisposeHandler = handler is null || disposeHandler;
        Uri baseAddress = (baseUrl ?? BaseUrl.Default).GetRestApiBaseUri();

        _httpClient = CreateHttpClient(actualHandler, actualDisposeHandler, baseAddress);
        // Discord 强制要求 User-Agent 遵循 "DiscordBot ($url, $versionNumber)" 格式
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            $"{MagicString.Rest.UserAgent} ({MagicString.Rest.RepoUri}, {MagicString.Rest.LibraryVersion})");
    }

    /// <summary>
    /// Sends a request without deserializing a response body (for example <c>204 No Content</c> endpoints).
    /// </summary>
    public async ValueTask SendAsync(RestRequest request, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await SendWithRetryAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a request and deserializes the JSON response body using the provided AOT metadata.
    /// </summary>
    public async ValueTask<TRes?> SendAsync<TRes>(
        RestRequest request,
        JsonTypeInfo<TRes> responseJsonInfo,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(responseJsonInfo);

        using HttpResponseMessage response = await SendWithRetryAsync(request, ct).ConfigureAwait(false);

        // 204 或空响应体直接返回 default，避免 JsonSerializer 读到空流时抛出 JsonException
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            return default;

        // 配合 ResponseHeadersRead，这里直接以流式方式反序列化，无需先把整段响应体缓存在 byte[] 里
        await using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, responseJsonInfo, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a request and deserializes the JSON response body using <see cref="IJsonModel{TSelf}.JsonInfo"/>.
    /// </summary>
    public ValueTask<TRes?> SendAsync<TRes>(RestRequest request, CancellationToken ct = default)
        where TRes : IJsonModel<TRes>
        => SendAsync(request, TRes.JsonInfo, ct);

    /// <inheritdoc />
    public void Dispose()
    {
        // 通过原子交换确保并发多次调用 Dispose 时幂等
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _httpClient.Dispose();
        if (_disposeRateLimiter)
            _rateLimiter.Dispose();
    }

    // 核心发送与 429 重试管道刻意设计为非泛型：
    // 在 NativeAOT 下无论有多少种响应 DTO，整段 HTTP/限流/重试逻辑只编译生成一份机器码。
    private async ValueTask<HttpResponseMessage> SendWithRetryAsync(RestRequest request, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(request.Route.Method);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Route.Path);

        // 去掉可能误传的前导 '/'，防止 Uri 拼接时覆盖掉 BaseAddress 里的 "/api/v10/"；无前导 '/' 时 TrimStart 零分配
        string relativePath = request.Route.Path.TrimStart('/');
        RestBody body = request.Body;
        string? authHeader = ResolveAuthorizationHeader(in request);
        // Discord 要求 X-Audit-Log-Reason 必须经过 URL 编码（特别是包含中文、空格或换行时）
        string? encodedReason = string.IsNullOrEmpty(request.AuditLogReason)
            ? null
            : Uri.EscapeDataString(request.AuditLogReason);

        RateLimitRequest rateLimitRequest = new(
            request.Route.RateLimitKey,
            HasAuth: authHeader is not null,
            ExemptFromGlobal: request.Route.ExemptFromGlobal,
            AuthScopeKey: authHeader);

        for (int attempt = 0; ; attempt++)
        {
            TimeSpan retryDelay;

            // 将 ticket、httpRequest、response 的作用域限定在单次尝试内，
            // 确保进入 429 Task.Delay 等待之前，所有连接、请求对象和限流票据都已完全释放。
            using (RateLimitTicket ticket = await _rateLimiter.AcquireAsync(in rateLimitRequest, ct).ConfigureAwait(false))
            using (HttpRequestMessage httpRequest = CreateHttpRequestMessage(request.Route.Method, relativePath, authHeader, encodedReason, in body))
            {
                HttpResponseMessage? response = null;
                try
                {
                    // 必须使用 ResponseHeadersRead：拿到响应头即返回，把响应体留给流式反序列化或按需读取
                    response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    RateLimitInfo rateLimitInfo = RateLimitInfo.Parse(response.Headers);

                    if (response.IsSuccessStatusCode)
                    {
                        ticket.Complete(response.StatusCode, in rateLimitInfo);

                        // 将 response 的释放所有权转移给外层调用方，防止 finally 提前将其 Dispose
                        HttpResponseMessage successResponse = response;
                        response = null;
                        return successResponse;
                    }

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        byte[] bodyBytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                        bool hasBodyRetryAfter = RateLimitInfo.TryParse429Body(bodyBytes, out double bodyRetryAfter, out bool bodyIsGlobal);

                        // 用 429 JSON 体中精度更高的 retry_after 和 global 标志补全响应头信息后再回传限流器
                        if (hasBodyRetryAfter || bodyIsGlobal)
                        {
                            rateLimitInfo = rateLimitInfo with
                            {
                                IsGlobal = rateLimitInfo.IsGlobal || bodyIsGlobal,
                                RetryAfterSeconds = hasBodyRetryAfter ? bodyRetryAfter : rateLimitInfo.RetryAfterSeconds
                            };
                        }

                        ticket.Complete(response.StatusCode, in rateLimitInfo);

                        // 不可重试的请求体（如不可复位的文件流）或达到最大重试次数时直接抛出异常
                        if (!body.CanRetry || attempt >= _maxRateLimitRetries)
                            throw DiscordRestException.CreateFromBytes(response.StatusCode, bodyBytes, rateLimitInfo);

                        retryDelay = ResolveRetryDelay(in rateLimitInfo, hasBodyRetryAfter, bodyRetryAfter);
                    }
                    else
                    {
                        ticket.Complete(response.StatusCode, in rateLimitInfo);
                        throw await DiscordRestException.CreateAsync(response, rateLimitInfo, ct).ConfigureAwait(false);
                    }
                }
                finally
                {
                    // 异常、取消、429 重试或非 2xx 错误路径都会在此处安全释放 response，避免连接泄漏
                    response?.Dispose();
                }
            }

            if (retryDelay > TimeSpan.Zero)
                await Task.Delay(retryDelay, _timeProvider, ct).ConfigureAwait(false);
        }
    }

    private string? ResolveAuthorizationHeader(in RestRequest request)
    {
        // 鉴权优先级：显式 OverrideToken > 路由允许默认鉴权时的 _defaultAuthHeader > 无鉴权头
        if (request.OverrideToken is { } overrideToken)
            return overrideToken.ToAuthorizationHeader();

        return request.Route.UseAuth ? _defaultAuthHeader : null;
    }

    private static HttpRequestMessage CreateHttpRequestMessage(
        HttpMethod method,
        string relativePath,
        string? authHeader,
        string? encodedReason,
        in RestBody body)
    {
        // HttpRequestMessage 不可跨次复用，每次尝试（含 429 重试）都必须新建实例
        HttpRequestMessage httpRequest = new(method, relativePath)
        {
            Content = body.CreateHttpContent()
        };

        if (authHeader is not null)
            httpRequest.Headers.TryAddWithoutValidation("Authorization", authHeader);

        if (encodedReason is not null)
            httpRequest.Headers.TryAddWithoutValidation("X-Audit-Log-Reason", encodedReason);

        return httpRequest;
    }

    private static TimeSpan ResolveRetryDelay(in RateLimitInfo info, bool hasBodyRetryAfter, double bodyRetryAfter)
    {
        double seconds;
        if (hasBodyRetryAfter)
        {
            // 优先采用 Discord 429 JSON 中的 retry_after（毫秒级浮点精度，统一适用于 user / shared / global）
            seconds = bodyRetryAfter;
        }
        else if (info.IsGlobal || info.Scope == RateLimitScope.Shared)
        {
            // shared 限流时 X-RateLimit-Reset-After 反映的是桶重置时间而非本次资源的实际等待时长，因此优先 Retry-After
            seconds = info.RetryAfterSeconds ?? info.ResetAfterSeconds ?? 1.0;
        }
        else
        {
            // 普通路由限流优先用浮点精度的 X-RateLimit-Reset-After，其次降级到 Retry-After
            seconds = info.ResetAfterSeconds ?? info.RetryAfterSeconds ?? 1.0;
        }

        if (seconds <= 0)
            return TimeSpan.Zero;

        // 向上取整到整毫秒：避免 double 转 TimeSpan 再被 Task.Delay 按毫秒截断，导致提前 1ms 发出重试又触发 429
        return TimeSpan.FromMilliseconds(Math.Ceiling(seconds * 1000.0));
    }

    private static SocketsHttpHandler CreateSocketsHttpHandler()
    {
        return new SocketsHttpHandler
        {
            // 定期轮换底层连接以感知 DNS 变更，避免长生命周期 HttpClient 出现 DNS 陈旧问题
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            // 并发流超过单条 HTTP/2 连接上限时自动扩展连接
            EnableMultipleHttp2Connections = true,
            // 自动透明解压 gzip / deflate / br 响应体
            AutomaticDecompression = DecompressionMethods.All,
            // Discord Bot API 不使用 Cookie，且关闭分布式追踪头注入以减少每次请求的额外开销
            UseCookies = false,
            ActivityHeadersPropagator = null
        };
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler, bool disposeHandler, Uri baseAddress)
    {
        return new HttpClient(handler, disposeHandler)
        {
            BaseAddress = baseAddress,
            // 优先协商 HTTP/2 多路复用，不具备条件时自动降级到 HTTP/1.1
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            // 禁用 HttpClient 全局固定超时，统一交由调用方传入的 CancellationToken 控制超时（含 429 等待期）
            Timeout = Timeout.InfiniteTimeSpan
        };
    }
}