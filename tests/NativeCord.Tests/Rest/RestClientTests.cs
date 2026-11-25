using System.Net;
using System.Text;

using NativeCord.Rest;
using NativeCord.Rest.RateLimiting;
using NativeCord.Tests.Helpers;

namespace NativeCord.Tests.Rest;

public sealed class RestClientTests
{
    private static readonly RestRoute s_currentUserRoute = new(
        HttpMethod.Get,
        Path: "/users/@me", // 刻意带前导 '/'，验证 RestClient 会正确裁剪而不会吞掉 /v10/
        BucketTemplate: "GET:/users/@me");

    private static readonly RestRoute s_createMessageRoute = new(
        HttpMethod.Post,
        Path: "channels/123456/messages",
        BucketTemplate: "POST:/channels/{channel_id}/messages",
        MajorResourceId: 123456);

    [Fact]
    public async Task SendAsync_GetJsonModel_DeserializesAndSetsHeadersAndUri()
    {
        MockHttpMessageHandler handler = new();
        RecordingRateLimiter limiter = new();
        handler.EnqueueJson(HttpStatusCode.OK, """{"id":"42","username":"nativecord"}""", res =>
        {
            res.Headers.TryAddWithoutValidation("X-RateLimit-Bucket", "bucket-users");
            res.Headers.TryAddWithoutValidation("X-RateLimit-Limit", "5");
            res.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "4");
            res.Headers.TryAddWithoutValidation("X-RateLimit-Reset-After", "1.25");
        });

        using RestClient client = new(
            defaultToken: new BotToken("secret-token"),
            rateLimiter: limiter,
            handler: handler);

        TestUser? user = await client.SendAsync<TestUser>(new RestRequest { Route = s_currentUserRoute });

        Assert.NotNull(user);
        Assert.Equal("42", user.Id);
        Assert.Equal("nativecord", user.Username);

        CapturedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal(new Uri("https://discord.com/api/v10/users/@me"), sent.RequestUri);
        Assert.Equal("Bot secret-token", sent.GetSingleHeader("Authorization"));
        // HttpRequestHeaders 会把 User-Agent 按产品段和注释段拆成多个值，拼回完整头值进行断言
        Assert.StartsWith("DiscordBot (https://github.com/Cyanflower/NativeCord, ", sent.GetJoinedHeader("User-Agent"));

        RateLimitRequest acquired = Assert.Single(limiter.Acquired);
        Assert.True(acquired.HasAuth);
        Assert.False(acquired.ExemptFromGlobal);
        Assert.Equal("Bot secret-token", acquired.AuthScopeKey);

        (long _, HttpStatusCode status, RateLimitInfo info) = Assert.Single(limiter.Completed);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("bucket-users", info.Bucket);
        Assert.Equal(4, info.Remaining);
        Assert.Empty(limiter.Canceled);
    }

    [Fact]
    public async Task SendAsync_NoResult_SucceedsOn204()
    {
        MockHttpMessageHandler handler = new();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.NoContent));

        using RestClient client = new(defaultToken: new BotToken("tok"), handler: handler);

        RestRoute deleteRoute = new(
            HttpMethod.Delete,
            "channels/123456/messages/999",
            "DELETE:/channels/{channel_id}/messages/{message_id}",
            MajorResourceId: 123456);

        await client.SendAsync(new RestRequest { Route = deleteRoute });

        CapturedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, sent.Method);
        Assert.Equal(new Uri("https://discord.com/api/v10/channels/123456/messages/999"), sent.RequestUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.OK)]
    public async Task SendAsync_Generic_ReturnsDefaultOn204OrEmptyBody(HttpStatusCode statusCode)
    {
        MockHttpMessageHandler handler = new();
        handler.Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent([])
        });

        using RestClient client = new(handler: handler);
        TestUser? result = await client.SendAsync<TestUser>(new RestRequest { Route = s_currentUserRoute });

        Assert.Null(result);
    }

    [Fact]
    public async Task SendAsync_JsonBody_SendsUtf8JsonAndReusesBytesAcrossRetry()
    {
        MockHttpMessageHandler handler = new();
        FakeTimeProvider clock = new(autoCompleteDelays: true);

        handler.EnqueueJson(HttpStatusCode.TooManyRequests, """{"message":"Rate limited","retry_after":0.5,"global":false}""");
        handler.EnqueueJson(HttpStatusCode.OK, """{"id":"1","username":"ok"}""");

        using RestClient client = new(
            defaultToken: new BotToken("tok"),
            rateLimiter: NullRateLimiter.Instance,
            handler: handler,
            timeProvider: clock);

        RestRequest request = new()
        {
            Route = s_createMessageRoute,
            Body = RestBody.FromJson(new TestMessagePayload("hello nativecord", Tts: true))
        };

        TestUser? user = await client.SendAsync<TestUser>(request);

        Assert.NotNull(user);
        Assert.Equal(2, handler.CallCount);
        foreach (CapturedRequest req in handler.Requests)
        {
            Assert.Equal("application/json; charset=utf-8", req.ContentType);
            Assert.Equal("""{"content":"hello nativecord","tts":true}""", req.BodyText);
        }
    }

    [Fact]
    public async Task SendAsync_FormBody_SendsFormUrlEncodedContent()
    {
        MockHttpMessageHandler handler = new();
        handler.EnqueueJson(HttpStatusCode.OK, """{"id":"1","username":"oauth"}""");

        using RestClient client = new(handler: handler);
        byte[] formBytes = Encoding.UTF8.GetBytes("grant_type=client_credentials&scope=identify");

        RestRoute oauthRoute = new(HttpMethod.Post, "oauth2/token", "POST:/oauth2/token", UseAuth: false);
        await client.SendAsync<TestUser>(new RestRequest
        {
            Route = oauthRoute,
            Body = RestBody.FromForm(formBytes),
            OverrideToken = new BotToken("dXNlcjpwYXNz", BotTokenType.Basic)
        });

        CapturedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal("application/x-www-form-urlencoded", sent.ContentType);
        Assert.Equal("grant_type=client_credentials&scope=identify", sent.BodyText);
        Assert.Equal("Basic dXNlcjpwYXNz", sent.GetSingleHeader("Authorization"));
    }

    [Fact]
    public async Task SendAsync_MultipartBody_RebuildsContentOnRetryWhenRetryable()
    {
        MockHttpMessageHandler handler = new();
        FakeTimeProvider clock = new(autoCompleteDelays: true);
        handler.EnqueueJson(HttpStatusCode.TooManyRequests, """{"retry_after":0.25,"global":false}""");
        handler.EnqueueJson(HttpStatusCode.OK, """{"id":"7","username":"uploaded"}""");

        int factoryInvocations = 0;
        RestBody body = RestBody.FromMultipart(() =>
        {
            factoryInvocations++;
            MultipartFormDataContent form = new();
            form.Add(new StringContent("""{"content":"hi"}""", Encoding.UTF8, "application/json"), "payload_json");
            return form;
        }, canRetry: true);

        using RestClient client = new(rateLimiter: NullRateLimiter.Instance, handler: handler, timeProvider: clock);
        TestUser? user = await client.SendAsync<TestUser>(new RestRequest
        {
            Route = s_createMessageRoute,
            Body = body
        });

        Assert.NotNull(user);
        Assert.Equal(2, factoryInvocations);
        Assert.All(handler.Requests, req => Assert.StartsWith("multipart/form-data; boundary=", req.ContentType));
    }

    [Fact]
    public async Task SendAsync_MultipartBody_DoesNotRetryWhenNonRetryable()
    {
        MockHttpMessageHandler handler = new();
        FakeTimeProvider clock = new(autoCompleteDelays: true);
        handler.EnqueueJson(HttpStatusCode.TooManyRequests, """{"message":"Rate limited","retry_after":1.5,"global":false}""");

        RestBody body = RestBody.FromMultipart(() => new MultipartFormDataContent(), canRetry: false);

        using RestClient client = new(handler: handler, timeProvider: clock);
        DiscordRestException ex = await Assert.ThrowsAsync<DiscordRestException>(async () =>
            await client.SendAsync<TestUser>(new RestRequest { Route = s_createMessageRoute, Body = body }));

        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
        Assert.Equal(1, handler.CallCount);
        Assert.Empty(clock.RecordedDelays);
    }

    [Fact]
    public async Task SendAsync_AuthPrecedence_RespectsOverrideTokenAndUseAuthFlag()
    {
        MockHttpMessageHandler handler = new();
        RecordingRateLimiter limiter = new();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.NoContent));
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.NoContent));
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.NoContent));

        using RestClient client = new(
            defaultToken: new BotToken("bot-default"),
            rateLimiter: limiter,
            handler: handler);

        // 1. 路由禁用 UseAuth 且无 OverrideToken -> 不带 Authorization
        RestRoute webhookRoute = new(HttpMethod.Post, "webhooks/1/tok", "POST:/webhooks/{id}/{token}", MajorResourceId: 1, MajorResourceToken: "tok", UseAuth: false, ExemptFromGlobal: true);
        await client.SendAsync(new RestRequest { Route = webhookRoute });

        // 2. 路由禁用 UseAuth 但显式指定 OverrideToken -> 仍优先使用 OverrideToken
        await client.SendAsync(new RestRequest
        {
            Route = webhookRoute,
            OverrideToken = new BotToken("user-bearer", BotTokenType.Bearer)
        });

        // 3. 普通路由指定 OverrideToken -> 覆盖默认 Bot Token
        await client.SendAsync(new RestRequest
        {
            Route = s_currentUserRoute,
            OverrideToken = new BotToken("override-bearer", BotTokenType.Bearer)
        });

        Assert.Null(handler.Requests[0].GetSingleHeader("Authorization"));
        Assert.False(limiter.Acquired[0].HasAuth);
        Assert.True(limiter.Acquired[0].ExemptFromGlobal);

        Assert.Equal("Bearer user-bearer", handler.Requests[1].GetSingleHeader("Authorization"));
        Assert.True(limiter.Acquired[1].HasAuth);

        Assert.Equal("Bearer override-bearer", handler.Requests[2].GetSingleHeader("Authorization"));
        Assert.Equal("Bearer override-bearer", limiter.Acquired[2].AuthScopeKey);
    }

    [Fact]
    public async Task SendAsync_AuditLogReason_UrlEncodesUnicodeHeader()
    {
        MockHttpMessageHandler handler = new();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.NoContent));

        using RestClient client = new(handler: handler);
        await client.SendAsync(new RestRequest
        {
            Route = s_currentUserRoute,
            AuditLogReason = "刷屏 封禁"
        });

        CapturedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal(Uri.EscapeDataString("刷屏 封禁"), sent.GetSingleHeader("X-Audit-Log-Reason"));
    }

    [Fact]
    public async Task SendAsync_429WithJsonBody_UsesExactRetryAfterAndEnrichesTicketInfo()
    {
        MockHttpMessageHandler handler = new();
        RecordingRateLimiter limiter = new();
        FakeTimeProvider clock = new(autoCompleteDelays: true);

        handler.EnqueueJson(HttpStatusCode.TooManyRequests, """{"message":"You are being rate limited.","retry_after":64.57,"global":true}""", res =>
        {
            res.Headers.TryAddWithoutValidation("Retry-After", "65");
            res.Headers.TryAddWithoutValidation("X-RateLimit-Reset-After", "10.0");
        });
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.NoContent));

        using RestClient client = new(rateLimiter: limiter, handler: handler, timeProvider: clock);
        await client.SendAsync(new RestRequest { Route = s_currentUserRoute });

        Assert.Equal(2, handler.CallCount);
        // 应使用 JSON 体中的 64.57s（向上取整为 64570ms）而非整数 Retry-After (65s) 或 Reset-After (10s)
        TimeSpan delay = Assert.Single(clock.RecordedDelays);
        Assert.Equal(TimeSpan.FromMilliseconds(64570), delay);

        Assert.Equal(2, limiter.Completed.Count);
        Assert.True(limiter.Completed[0].Info.IsGlobal);
        Assert.Equal(64.57, limiter.Completed[0].Info.RetryAfterSeconds);
    }

    [Fact]
    public async Task SendAsync_429SharedWithoutJsonBody_PrefersRetryAfterOverResetAfter()
    {
        MockHttpMessageHandler handler = new();
        FakeTimeProvider clock = new(autoCompleteDelays: true);

        HttpResponseMessage rateLimited = new(HttpStatusCode.TooManyRequests)
        {
            Content = new ByteArrayContent([])
        };
        rateLimited.Headers.TryAddWithoutValidation("Retry-After", "1337");
        rateLimited.Headers.TryAddWithoutValidation("X-RateLimit-Reset-After", "64.57");
        rateLimited.Headers.TryAddWithoutValidation("X-RateLimit-Scope", "shared");

        handler.Enqueue(rateLimited);
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.NoContent));

        using RestClient client = new(rateLimiter: NullRateLimiter.Instance, handler: handler, timeProvider: clock);
        await client.SendAsync(new RestRequest { Route = s_currentUserRoute });

        Assert.Equal(TimeSpan.FromSeconds(1337), Assert.Single(clock.RecordedDelays));
    }

    [Fact]
    public async Task SendAsync_429UserWithoutJsonBody_PrefersResetAfterOverRetryAfter()
    {
        MockHttpMessageHandler handler = new();
        FakeTimeProvider clock = new(autoCompleteDelays: true);

        HttpResponseMessage rateLimited = new(HttpStatusCode.TooManyRequests)
        {
            Content = new ByteArrayContent([])
        };
        rateLimited.Headers.TryAddWithoutValidation("Retry-After", "5");
        rateLimited.Headers.TryAddWithoutValidation("X-RateLimit-Reset-After", "4.25");
        rateLimited.Headers.TryAddWithoutValidation("X-RateLimit-Scope", "user");

        handler.Enqueue(rateLimited);
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.NoContent));

        using RestClient client = new(rateLimiter: NullRateLimiter.Instance, handler: handler, timeProvider: clock);
        await client.SendAsync(new RestRequest { Route = s_currentUserRoute });

        Assert.Equal(TimeSpan.FromSeconds(4.25), Assert.Single(clock.RecordedDelays));
    }

    [Fact]
    public async Task SendAsync_429CloudflareHtml_FallsBackGracefully()
    {
        MockHttpMessageHandler handler = new();
        FakeTimeProvider clock = new(autoCompleteDelays: true);

        HttpResponseMessage cf429 = new(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("<html><body>Error 1015</body></html>", Encoding.UTF8, "text/html")
        };
        cf429.Headers.TryAddWithoutValidation("Retry-After", "3");

        handler.Enqueue(cf429);
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.NoContent));

        using RestClient client = new(rateLimiter: NullRateLimiter.Instance, handler: handler, timeProvider: clock);
        await client.SendAsync(new RestRequest { Route = s_currentUserRoute });

        Assert.Equal(2, handler.CallCount);
        Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(clock.RecordedDelays));
    }

    [Fact]
    public async Task SendAsync_429ExceedingMaxRetries_ThrowsDiscordRestException()
    {
        MockHttpMessageHandler handler = new();
        FakeTimeProvider clock = new(autoCompleteDelays: true);

        for (int i = 0; i < 3; i++)
            handler.EnqueueJson(HttpStatusCode.TooManyRequests, """{"message":"Slow down","retry_after":0.5,"global":false}""");

        using RestClient client = new(rateLimiter: NullRateLimiter.Instance, handler: handler, maxRateLimitRetries: 2, timeProvider: clock);
        DiscordRestException ex = await Assert.ThrowsAsync<DiscordRestException>(async () =>
            await client.SendAsync(new RestRequest { Route = s_currentUserRoute }));

        // 1 次首次请求 + 2 次重试 = 共 3 次尝试，等待 2 次
        Assert.Equal(3, handler.CallCount);
        Assert.Equal(2, clock.RecordedDelays.Count);
        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
        Assert.Equal("Slow down", ex.DiscordMessage);
    }

    [Fact]
    public async Task SendAsync_DiscordErrorJson_ThrowsStructuredExceptionAndCompletesTicket()
    {
        MockHttpMessageHandler handler = new();
        RecordingRateLimiter limiter = new();

        handler.EnqueueJson(HttpStatusCode.Forbidden, """{"message":"Missing Permissions","code":50013}""");

        using RestClient client = new(rateLimiter: limiter, handler: handler);
        DiscordRestException ex = await Assert.ThrowsAsync<DiscordRestException>(async () =>
            await client.SendAsync<TestUser>(new RestRequest { Route = s_currentUserRoute }));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.Equal(50013, ex.DiscordErrorCode);
        Assert.Equal("Missing Permissions", ex.DiscordMessage);
        Assert.Contains("50013", ex.Message);

        (long _, HttpStatusCode status, RateLimitInfo _) = Assert.Single(limiter.Completed);
        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Empty(limiter.Canceled);
    }

    [Fact]
    public async Task SendAsync_NetworkException_CancelsTicketWithoutCompleting()
    {
        MockHttpMessageHandler handler = new();
        RecordingRateLimiter limiter = new();
        handler.EnqueueException(new HttpRequestException("Simulated socket reset"));

        using RestClient client = new(rateLimiter: limiter, handler: handler);
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.SendAsync(new RestRequest { Route = s_currentUserRoute }));

        // 发出请求时抛异常未拿到响应，ticket.Dispose 必须触发 Cancel 归还槽位，避免限流器死锁
        Assert.Empty(limiter.Completed);
        long canceledToken = Assert.Single(limiter.Canceled);
        Assert.Equal(1, canceledToken);
    }

    [Fact]
    public async Task Dispose_DisposesOwnedLimiterAndRejectsSubsequentRequests()
    {
        MockHttpMessageHandler handler = new();
        RecordingRateLimiter ownedLimiter = new();
        RestClient client = new(rateLimiter: ownedLimiter, handler: handler, disposeRateLimiter: true);

        client.Dispose();
        client.Dispose(); // 幂等

        Assert.True(ownedLimiter.IsDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await client.SendAsync(new RestRequest { Route = s_currentUserRoute }));
    }

    [Fact]
    public async Task DefaultRateLimiter_EnforcesBucketResetOutOfTheBox()
    {
        FakeTimeProvider clock = new(autoCompleteDelays: false);
        MockHttpMessageHandler handler = new();

        handler.EnqueueJson(HttpStatusCode.OK, """{"id":"1","username":"first"}""", res =>
        {
            res.Headers.TryAddWithoutValidation("X-RateLimit-Bucket", "default-bucket");
            res.Headers.TryAddWithoutValidation("X-RateLimit-Limit", "1");
            res.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
            res.Headers.TryAddWithoutValidation("X-RateLimit-Reset-After", "1.0");
        });
        handler.EnqueueJson(HttpStatusCode.OK, """{"id":"2","username":"second"}""");

        // 不显式传入 rateLimiter，验证默认内置的 DiscordRateLimiter 开箱生效
        using RestClient client = new(defaultToken: new BotToken("tok"), handler: handler, timeProvider: clock);

        TestUser? first = await client.SendAsync<TestUser>(new RestRequest { Route = s_createMessageRoute });
        Assert.Equal("first", first?.Username);

        Task<TestUser?> secondTask = client.SendAsync<TestUser>(new RestRequest { Route = s_createMessageRoute }).AsTask();
        Assert.False(secondTask.IsCompleted);
        Assert.Equal(1, handler.CallCount);

        // 1.0s 重置时间 + 默认 50ms BucketResetBuffer
        clock.Advance(TimeSpan.FromMilliseconds(1050));
        TestUser? second = await secondTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal("second", second?.Username);
        Assert.Equal(2, handler.CallCount);
    }
}