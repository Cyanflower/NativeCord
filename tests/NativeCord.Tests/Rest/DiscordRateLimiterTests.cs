using System.Net;

using NativeCord.Rest;
using NativeCord.Rest.RateLimiting;
using NativeCord.Tests.Helpers;

namespace NativeCord.Tests.Rest;

public sealed class DiscordRateLimiterTests
{
    private static DiscordRateLimiter CreateLimiter(
        FakeTimeProvider clock,
        int globalLimit = 50,
        TimeSpan? globalWindow = null,
        TimeSpan? bucketBuffer = null,
        TimeSpan? idleTimeout = null,
        TimeSpan? cleanupInterval = null)
    {
        return new DiscordRateLimiter(new DiscordRateLimiterOptions
        {
            GlobalRequestsPerWindow = globalLimit,
            GlobalWindow = globalWindow ?? TimeSpan.FromSeconds(1),
            BucketResetBuffer = bucketBuffer ?? TimeSpan.Zero,
            BucketIdleTimeout = idleTimeout ?? TimeSpan.FromMinutes(10),
            CleanupInterval = cleanupInterval ?? TimeSpan.FromMinutes(2)
        }, clock);
    }

    private static RateLimitRequest MakeRequest(
        string template,
        ulong majorId = 0,
        string? auth = "Bot test-token",
        bool exemptFromGlobal = false)
    {
        return new RateLimitRequest(
            new RateLimitKey(template, majorId),
            HasAuth: auth is not null,
            ExemptFromGlobal: exemptFromGlobal,
            AuthScopeKey: auth);
    }

    [Fact]
    public async Task UnknownRoute_ProbesSeriallyThenMigratesAndReleasesWaitingRequests()
    {
        FakeTimeProvider clock = new(autoCompleteDelays: false);
        using DiscordRateLimiter limiter = CreateLimiter(clock);
        RateLimitRequest req = MakeRequest("POST:/channels/{id}/messages", majorId: 100);

        // 首次访问未知路由：第 1 个立刻放行探测，第 2、3 个在临时桶排队
        ValueTask<RateLimitTicket> firstTask = limiter.AcquireAsync(in req);
        Assert.True(firstTask.IsCompletedSuccessfully);
        RateLimitTicket t1 = await firstTask;

        Task<RateLimitTicket> secondTask = limiter.AcquireAsync(in req).AsTask();
        Task<RateLimitTicket> thirdTask = limiter.AcquireAsync(in req).AsTask();
        Assert.False(secondTask.IsCompleted);
        Assert.False(thirdTask.IsCompleted);

        // 第 1 个返回服务端桶哈希 + 剩余 2 次额度 -> 临时桶迁移并唤醒排队请求汇流到正式桶
        RateLimitInfo probeInfo = new(Limit: 3, Remaining: 2, Reset: null, ResetAfterSeconds: 5.0, RetryAfterSeconds: null, Bucket: "hash-msg", IsGlobal: false, Scope: RateLimitScope.User);
        t1.Complete(HttpStatusCode.OK, in probeInfo);

        RateLimitTicket t2 = await secondTask.WaitAsync(TimeSpan.FromSeconds(1));
        RateLimitTicket t3 = await thirdTask.WaitAsync(TimeSpan.FromSeconds(1));

        // 此时 2 个剩余额度已被 t2、t3 拿走，第 4 个必须排队等 5 秒重置
        Task<RateLimitTicket> fourthTask = limiter.AcquireAsync(in req).AsTask();
        Assert.False(fourthTask.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(5));
        RateLimitTicket t4 = await fourthTask.WaitAsync(TimeSpan.FromSeconds(1));

        t2.Dispose();
        t3.Dispose();
        t4.Dispose();
    }

    [Fact]
    public async Task DifferentRoutesWithSameServerBucketHash_ShareQuotaPerMajorResource()
    {
        FakeTimeProvider clock = new(autoCompleteDelays: false);
        using DiscordRateLimiter limiter = CreateLimiter(clock);

        RateLimitRequest routeA = MakeRequest("POST:/channels/{id}/messages", majorId: 555);
        RateLimitRequest routeB = MakeRequest("PATCH:/channels/{id}/messages/{msg}", majorId: 555);

        // 路由 A 先探测学到服务端哈希 "shared-hash"，Limit=2, Remaining=1
        RateLimitTicket a1 = await limiter.AcquireAsync(in routeA);
        RateLimitInfo infoA = new(2, 1, null, 4.0, null, "shared-hash", false, RateLimitScope.User);
        a1.Complete(HttpStatusCode.OK, in infoA);

        // 路由 B 首次探测并同样返回 "shared-hash"，Remaining=0 -> 与路由 A 汇流到同一个正式桶
        RateLimitTicket b1 = await limiter.AcquireAsync(in routeB);
        RateLimitInfo infoB = new(2, 0, null, 4.0, null, "shared-hash", false, RateLimitScope.User);
        b1.Complete(HttpStatusCode.OK, in infoB);

        // 现在路由 A 和路由 B 再次请求都应被同一个已空桶挡住
        Task<RateLimitTicket> a2 = limiter.AcquireAsync(in routeA).AsTask();
        Task<RateLimitTicket> b2 = limiter.AcquireAsync(in routeB).AsTask();
        Assert.False(a2.IsCompleted);
        Assert.False(b2.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(4));
        (await a2.WaitAsync(TimeSpan.FromSeconds(1))).Dispose();
        (await b2.WaitAsync(TimeSpan.FromSeconds(1))).Dispose();
    }

    [Fact]
    public async Task SameRouteDifferentMajorResources_DoNotBlockEachOther()
    {
        FakeTimeProvider clock = new(autoCompleteDelays: false);
        using DiscordRateLimiter limiter = CreateLimiter(clock);

        RateLimitRequest chan1 = MakeRequest("POST:/channels/{id}/messages", majorId: 1);
        RateLimitRequest chan2 = MakeRequest("POST:/channels/{id}/messages", majorId: 2);

        // 频道 1 打空桶（Limit=1, Remaining=0, ResetAfter=10s）
        RateLimitTicket t1 = await limiter.AcquireAsync(in chan1);
        RateLimitInfo emptyInfo = new(1, 0, null, 10.0, null, "hash-msg", false, RateLimitScope.User);
        t1.Complete(HttpStatusCode.OK, in emptyInfo);

        Task<RateLimitTicket> chan1Blocked = limiter.AcquireAsync(in chan1).AsTask();
        Assert.False(chan1Blocked.IsCompleted);

        // 频道 2 应立即同步放行，不受频道 1 阻塞
        ValueTask<RateLimitTicket> chan2Task = limiter.AcquireAsync(in chan2);
        Assert.True(chan2Task.IsCompletedSuccessfully);
        (await chan2Task).Dispose();

        clock.Advance(TimeSpan.FromSeconds(10));
        (await chan1Blocked.WaitAsync(TimeSpan.FromSeconds(1))).Dispose();
    }

    [Fact]
    public async Task BucketQueue_ReleasesWaitersInStrictFifoOrderAcrossResets()
    {
        FakeTimeProvider clock = new(autoCompleteDelays: false);
        using DiscordRateLimiter limiter = CreateLimiter(clock);
        RateLimitRequest req = MakeRequest("POST:/channels/{id}/messages", majorId: 42);

        // 建立 Limit=1 的桶并耗尽
        RateLimitTicket init = await limiter.AcquireAsync(in req);
        RateLimitInfo info = new(1, 0, null, 2.0, null, "hash-fifo", false, RateLimitScope.User);
        init.Complete(HttpStatusCode.OK, in info);

        List<int> grantOrder = [];
        Task w1 = WaitAndRecordAsync(1);
        Task w2 = WaitAndRecordAsync(2);
        Task w3 = WaitAndRecordAsync(3);

        async Task WaitAndRecordAsync(int id)
        {
            RateLimitTicket ticket = await limiter.AcquireAsync(in req);
            grantOrder.Add(id);
            RateLimitInfo nextInfo = new(1, 0, null, 2.0, null, "hash-fifo", false, RateLimitScope.User);
            ticket.Complete(HttpStatusCode.OK, in nextInfo);
        }

        clock.Advance(TimeSpan.FromSeconds(2));
        await w1.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(w2.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(2));
        await w2.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(w3.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(2));
        await w3.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal([1, 2, 3], grantOrder);
    }

    [Fact]
    public async Task BucketBeforeGlobal_QueuedBucketRequestsDoNotConsumeGlobalQuota()
    {
        FakeTimeProvider clock = new(autoCompleteDelays: false);
        // 全局仅允许 2 req/s，方便验证拥堵频道是否会误吞全局配额
        using DiscordRateLimiter limiter = CreateLimiter(clock, globalLimit: 2, globalWindow: TimeSpan.FromSeconds(1));

        RateLimitRequest congestedChan = MakeRequest("POST:/channels/{id}/messages", majorId: 1);
        RateLimitRequest healthyChan = MakeRequest("POST:/channels/{id}/messages", majorId: 2);

        // 频道 1 探测打空并推进 1 秒让全局窗口重新满额
        RateLimitTicket probe = await limiter.AcquireAsync(in congestedChan);
        RateLimitInfo congestedInfo = new(1, 0, null, 10.0, null, "hash-msg", false, RateLimitScope.User);
        probe.Complete(HttpStatusCode.OK, in congestedInfo);
        clock.Advance(TimeSpan.FromSeconds(1));

        // 往拥堵频道 1 塞入 5 个排队请求（它们应停在分桶门口，一滴全局配额都不扣）
        Task<RateLimitTicket>[] blockedOnChan1 = Enumerable.Range(0, 5)
            .Select(_ => limiter.AcquireAsync(in congestedChan).AsTask())
            .ToArray();
        Assert.All(blockedOnChan1, t => Assert.False(t.IsCompleted));

        // 健康频道 2 应仍能立刻拿到全局配额放行（如果先全局后分桶，这里早就被卡死了）
        ValueTask<RateLimitTicket> healthy1 = limiter.AcquireAsync(in healthyChan);
        Assert.True(healthy1.IsCompletedSuccessfully);
        (await healthy1).Dispose();

        // 取消这 5 个排队请求清理现场
        limiter.Dispose();
        foreach (Task<RateLimitTicket> t in blockedOnChan1)
            await Assert.ThrowsAsync<ObjectDisposedException>(() => t);
    }

    [Fact]
    public async Task Shared429_DoesNotPenalizeLocalRouteBucket()
    {
        FakeTimeProvider clock = new(autoCompleteDelays: false);
        using DiscordRateLimiter limiter = CreateLimiter(clock);
        RateLimitRequest req = MakeRequest("POST:/channels/{id}/messages", majorId: 77);

        // 初始化 Limit=5, Remaining=4 的桶
        RateLimitTicket t1 = await limiter.AcquireAsync(in req);
        RateLimitInfo initInfo = new(5, 4, null, 5.0, null, "hash-shared", false, RateLimitScope.User);
        t1.Complete(HttpStatusCode.OK, in initInfo);

        // 第 2 次请求收到 scope=shared 的 429（Retry-After=1337s）
        RateLimitTicket t2 = await limiter.AcquireAsync(in req);
        RateLimitInfo shared429 = new(5, 3, null, 5.0, 1337.0, "hash-shared", false, RateLimitScope.Shared);
        t2.Complete(HttpStatusCode.TooManyRequests, in shared429);

        // 本地桶不应被 1337s 锁死，下一个请求仍应立即可发
        ValueTask<RateLimitTicket> t3Task = limiter.AcquireAsync(in req);
        Assert.True(t3Task.IsCompletedSuccessfully);
        (await t3Task).Dispose();
    }

    [Fact]
    public async Task Global429_PausesNormalEndpointsButExemptsInteractionEndpoints()
    {
        FakeTimeProvider clock = new(autoCompleteDelays: false);
        using DiscordRateLimiter limiter = CreateLimiter(clock, globalLimit: 2);

        RateLimitRequest normalReq = MakeRequest("GET:/users/@me");
        RateLimitRequest interactionReq = MakeRequest("POST:/interactions/{id}/{token}/callback", majorId: 99, exemptFromGlobal: true);

        // 普通请求触发 global=true 429（暂停 3 秒）
        RateLimitTicket t1 = await limiter.AcquireAsync(in normalReq);
        RateLimitInfo global429 = new(null, null, null, null, 3.0, null, IsGlobal: true, Scope: RateLimitScope.Global);
        t1.Complete(HttpStatusCode.TooManyRequests, in global429);

        // 普通端点必须被全局暂停挡住
        Task<RateLimitTicket> blockedNormal = limiter.AcquireAsync(in normalReq).AsTask();
        Assert.False(blockedNormal.IsCompleted);

        // ExemptFromGlobal 的 Interaction 回调端点必须不受全局暂停影响，立即可发
        ValueTask<RateLimitTicket> exemptTask = limiter.AcquireAsync(in interactionReq);
        Assert.True(exemptTask.IsCompletedSuccessfully);
        (await exemptTask).Dispose();

        // 推进 3 秒后全局暂停解除，普通端点放行
        clock.Advance(TimeSpan.FromSeconds(3));
        (await blockedNormal.WaitAsync(TimeSpan.FromSeconds(1))).Dispose();
    }

    [Fact]
    public async Task TicketCancel_RestoresBucketSlotAndWakesNextWaiter()
    {
        FakeTimeProvider clock = new(autoCompleteDelays: false);
        using DiscordRateLimiter limiter = CreateLimiter(clock);
        RateLimitRequest req = MakeRequest("POST:/channels/{id}/messages", majorId: 88);

        // 场景 A：未知桶探测请求未调用 Complete 直接 Dispose（模拟网络异常）-> 必须释放探测锁唤醒下一个
        RateLimitTicket probe = await limiter.AcquireAsync(in req);
        Task<RateLimitTicket> waitingProbe = limiter.AcquireAsync(in req).AsTask();
        Assert.False(waitingProbe.IsCompleted);

        probe.Dispose(); // 触发 Cancel
        RateLimitTicket nextProbe = await waitingProbe.WaitAsync(TimeSpan.FromSeconds(1));

        // 建好 Limit=1, Remaining=1 的正式桶，测试场景 B：正式桶预扣后 Cancel 归还额度
        RateLimitInfo initInfo = new(1, 1, null, 5.0, null, "hash-cancel", false, RateLimitScope.User);
        nextProbe.Complete(HttpStatusCode.OK, in initInfo);

        RateLimitTicket slot = await limiter.AcquireAsync(in req);
        Task<RateLimitTicket> waitingNext = limiter.AcquireAsync(in req).AsTask();
        Assert.False(waitingNext.IsCompleted);

        slot.Dispose(); // 触发 Cancel -> 归还额度并立刻唤醒 waitingNext
        (await waitingNext.WaitAsync(TimeSpan.FromSeconds(1))).Dispose();
    }

    [Fact]
    public async Task StaleResponseFromPreviousWindow_DoesNotCorruptNewWindowQuota()
    {
        FakeTimeProvider clock = new(autoCompleteDelays: false);
        using DiscordRateLimiter limiter = CreateLimiter(clock);
        RateLimitRequest req = MakeRequest("POST:/channels/{id}/messages", majorId: 33);

        // 建立 Limit=2, Remaining=1, ResetAfter=2s 的桶
        RateLimitTicket init = await limiter.AcquireAsync(in req);
        RateLimitInfo initInfo = new(2, 1, null, 2.0, null, "hash-gen", false, RateLimitScope.User);
        init.Complete(HttpStatusCode.OK, in initInfo);

        // 在第 1 窗口借出最后一个额度但先不 Complete（模拟慢响应）
        RateLimitTicket slowTicket = await limiter.AcquireAsync(in req);

        // 推进 3 秒进入第 2 窗口并立即发出 1 个新请求（新窗口满额 2 -> 剩 1）
        clock.Advance(TimeSpan.FromSeconds(3));
        RateLimitTicket newWindowTicket = await limiter.AcquireAsync(in req);

        // 此时第 1 窗口的慢响应终于迟到，带着 Remaining=0 -> 必须被代次隔离忽略，不能把新窗口剩的 1 个清零
        RateLimitInfo staleInfo = new(2, 0, null, 0.1, null, "hash-gen", false, RateLimitScope.User);
        slowTicket.Complete(HttpStatusCode.OK, in staleInfo);

        // 新窗口剩下的第 2 个额度仍应能立刻获取
        ValueTask<RateLimitTicket> secondInNewWindow = limiter.AcquireAsync(in req);
        Assert.True(secondInNewWindow.IsCompletedSuccessfully);

        newWindowTicket.Dispose();
        (await secondInNewWindow).Dispose();
    }

    [Fact]
    public async Task IdleBuckets_AreEvictedOpportunisticallyAfterTimeout()
    {
        FakeTimeProvider clock = new(autoCompleteDelays: false);
        using DiscordRateLimiter limiter = CreateLimiter(
            clock,
            idleTimeout: TimeSpan.FromMinutes(5),
            cleanupInterval: TimeSpan.FromMinutes(1));

        RateLimitRequest req1 = MakeRequest("POST:/channels/{id}/messages", majorId: 1001);
        RateLimitRequest req2 = MakeRequest("POST:/channels/{id}/messages", majorId: 1002);

        RateLimitTicket t1 = await limiter.AcquireAsync(in req1);
        RateLimitInfo info = new(5, 4, null, 1.0, null, "hash-evict", false, RateLimitScope.User);
        t1.Complete(HttpStatusCode.OK, in info);
        Assert.Equal(1, limiter.ActiveBucketCount);

        // 推进 6 分钟超过 idleTimeout 和 cleanupInterval，再访问另一个频道触发惰性清理
        clock.Advance(TimeSpan.FromMinutes(6));
        RateLimitTicket t2 = await limiter.AcquireAsync(in req2);
        t2.Complete(HttpStatusCode.OK, in info);

        // 旧的 1001 桶应已被回收，仅剩活跃的 1002 桶
        Assert.Equal(1, limiter.ActiveBucketCount);
    }

    [Fact]
    public async Task RestClientIntegration_EnforcesBucketResetWithoutHitting429()
    {
        FakeTimeProvider clock = new(autoCompleteDelays: false);
        MockHttpMessageHandler handler = new();
        using DiscordRateLimiter limiter = CreateLimiter(clock);

        handler.EnqueueJson(HttpStatusCode.OK, """{"id":"1","username":"u1"}""", res =>
        {
            res.Headers.TryAddWithoutValidation("X-RateLimit-Bucket", "hash-int");
            res.Headers.TryAddWithoutValidation("X-RateLimit-Limit", "1");
            res.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
            res.Headers.TryAddWithoutValidation("X-RateLimit-Reset-After", "2.0");
        });
        handler.EnqueueJson(HttpStatusCode.OK, """{"id":"2","username":"u2"}""", res =>
        {
            res.Headers.TryAddWithoutValidation("X-RateLimit-Bucket", "hash-int");
            res.Headers.TryAddWithoutValidation("X-RateLimit-Limit", "1");
            res.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
            res.Headers.TryAddWithoutValidation("X-RateLimit-Reset-After", "2.0");
        });

        using RestClient client = new(
            defaultToken: new BotToken("tok"),
            rateLimiter: limiter,
            handler: handler,
            timeProvider: clock);

        RestRoute route = new(HttpMethod.Post, "channels/500/messages", "POST:/channels/{id}/messages", MajorResourceId: 500);

        // 第 1 次发出即耗尽桶
        TestUser? u1 = await client.SendAsync<TestUser>(new RestRequest { Route = route });
        Assert.Equal("u1", u1?.Username);
        Assert.Equal(1, handler.CallCount);

        // 第 2 次在限流器内排队，未发出任何多余 HTTP 请求
        Task<TestUser?> secondCall = client.SendAsync<TestUser>(new RestRequest { Route = route }).AsTask();
        Assert.False(secondCall.IsCompleted);
        Assert.Equal(1, handler.CallCount);

        // 2 秒重置到点后才真正发出第 2 次 HTTP 请求并成功反序列化
        clock.Advance(TimeSpan.FromSeconds(2));
        TestUser? u2 = await secondCall.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal("u2", u2?.Username);
        Assert.Equal(2, handler.CallCount);
    }
}