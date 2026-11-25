# NativeCord.Tests 代码索引

此索引专为 AI 和开发者提供，记录了测试基础设施以及各个测试文件所覆盖的 **目标类 (Target Class)** 和详细的 **测试场景 (Test Cases)**。
修改核心库代码时，可通过搜索此文档快速定位受影响的测试类和方法，避免全局扫描。

> **测试约束**：项目配置了 `<JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>`，所有涉及 JSON 的测试均验证真实 AOT 源生成路径；所有异步时序与重试测试均通过 `FakeTimeProvider` 确定性推进，不依赖真实时钟或外部网络。

---

## Helpers/（测试基础设施）

### MockHttpMessageHandler.cs
- **职责**: 可编程 `HttpMessageHandler`，在 `RestClient` 释放 `HttpRequestMessage` 与 `HttpContent` 之前拍摄完整不可变快照（`CapturedRequest`：方法、URI、全部请求头、`Content-Type`、请求体原始字节与文本），并按队列回放预设响应或模拟网络异常。
- **常用方法**: `Enqueue(...)`、`EnqueueJson(...)`、`EnqueueException(...)`、`CapturedRequest.GetSingleHeader(...)`、`CapturedRequest.GetJoinedHeader(...)`。

### RecordingRateLimiter.cs
- **职责**: 同时实现 `IRateLimiter` 与 `IRateLimitTicketCallback` 的测试探针，记录每一次 `AcquireAsync` 请求上下文（`Acquired`）、`Complete` 回调（`Completed`）、`Cancel` 回调（`Canceled`）以及 `IsDisposed` 状态，用于校验票据生命周期与防泄漏。

### FakeTimeProvider.cs
- **职责**: 零外部依赖的可控 `TimeProvider` 子类，支持两种模式：
  - `autoCompleteDelays: true`：自动记录请求的 `TimeSpan`（`RecordedDelays`）、快进虚拟时钟并在线程池异步完成定时器，适合 `RestClient` 429 重试延迟断言；
  - `autoCompleteDelays: false`：按 `Advance(TimeSpan)` 手动步进时钟并按到期时间升序触发挂起的 `ITimer`，适合 `GlobalRateLimiter` 与 `DiscordRateLimiter` 的精确时序与并发排队测试。

### TestModels.cs
- **职责**: 供端到端 JSON 序列化测试使用的最小 AOT DTO（`TestUser`、`TestMessagePayload`，均实现 `IJsonModel<T>`）及源生成上下文 `TestJsonContext`（`SnakeCaseLower`）。

---

## Core/（核心基础类型）

### SnowflakeTests.cs（4 个测试）
- **目标类**: `Snowflake`、`DiscordEpoch`
- **主要测试场景**:
  - `ParseAndTryParse_SupportsStringCharSpanAndUtf8Span`：验证 `string`、`ReadOnlySpan<char>`、`ReadOnlySpan<byte>`（UTF-8）三种 `Parse`/`TryParse` 重载及非法输入返回 `false`。
  - `TryFormat_FormatsToCharAndUtf8Spans`：验证 `ISpanFormattable` 与 `IUtf8SpanFormattable` 写入正确文本，以及目标缓冲区过小时返回 `false`。
  - `FromTimestamp_RoundTripsAndValidatesDiscordEpochBounds`：验证 `FromTimestamp` 与 `UnixTimeMilliseconds`/`Timestamp` 毫秒级往返一致，以及早于 Discord Epoch（2015-01-01）时抛 `ArgumentOutOfRangeException`。
  - `ComparisonOperators_OrderByUnderlyingValue`：验证 `<`、`<=`、`>`、`>=` 与 `CompareTo` 按底层 `ulong` 排序。

### OptionalTests.cs（3 个测试）
- **目标类**: `Optional<T>`、`Optional`
- **主要测试场景**:
  - `DefaultAndNone_AreUnspecifiedAndThrowOnValueAccess`：验证 `default` 与 `Optional<T>.None` 的 `HasValue == false`、`TryGetValue`/`GetValueOrDefault`、`ToString()` 以及访问 `Value` 抛 `InvalidOperationException`。
  - `ImplicitConversionAndFactory_WrapValuesIncludingExplicitNull`：验证隐式转换（含显式 `null` 赋给 `Optional<string?>` 与 `Optional<Snowflake?>`）及 `Optional.From(0)` 均正确置 `HasValue == true`。
  - `Equality_DistinguishesUnspecifiedFromExplicitNullAndDefaultValues`：验证 `default(Optional<T>)` 与显式 `null`/`0`/`false` 严格判为不相等（确保 `JsonIgnoreCondition.WhenWritingDefault` 正确生效），并校验相等值与 `GetHashCode` 一致性。

### SerializationTests.cs（7 个方法 / 10 个用例）
- **目标类**: `SnowflakeJsonConverter`、`ImageHashJsonConverter`、`OptionalJsonConverter<T>`
- **主要测试场景**:
  - `Snowflake_SerializesAsStringAndDeserializesFromStringNumberEscapedAndSegmented`：验证 `Snowflake` 序列化为 JSON 字符串、字典键往返（`ReadAsPropertyName`/`WriteAsPropertyName`）、兼容数值与 Unicode 转义字符串，以及跨 `ReadOnlySequence<byte>` 分段读取。
  - `Snowflake_InvalidJsonTokens_ThrowJsonException`（4 组 Theory）：非数字字符串、负数、布尔值、超长溢出数字均抛 `JsonException`。
  - `ImageHash_RoundTripsValueAndNullableNullAndRejectsNonString`：验证 `ImageHash` 与 `ImageHash?` 往返及动图标识，非字符串令牌抛 `JsonException`。
  - `Optional_PatchSerialization_OmitsUnspecifiedAndWritesExplicitNullAndDefaultScalars`：验证 PATCH 序列化时未设置字段被省略，而显式 `null`（`string?`/`Snowflake?`）、`0`、`false`、`ImageHash` 及嵌套 DTO 均正确写出。
  - `Optional_Deserialization_DistinguishesOmittedFromExplicitNullAndValues`：验证反序列化准确区分省略字段（`HasValue == false`）、显式 `null`（`HasValue == true, Value == null`）与具体值。
  - `Optional_NonNullableValueTypeWithJsonNull_ThrowsJsonException`：非可空值类型（`Optional<int>`）遇到 JSON `null` 时抛 `JsonException`。
  - `Optional_WithoutWhenWritingDefault_ThrowsOnUnspecifiedWrite`：未标注 `WhenWritingDefault` 而直接序列化未设置 `Optional<T>` 时抛 `InvalidOperationException` 防呆。

---

## Rest/（HTTP 管道与限流）

### RateLimitInfoTests.cs（5 个方法 / 8 个用例）
- **目标类**: `RateLimitInfo`、`RateLimitScope`
- **主要测试场景**:
  - `Parse_ReadsAllDiscordHeadersAccurately`：通过 `headers.NonValidated` 完整解析 `X-RateLimit-Limit`、`Remaining`、带小数 `Reset`、`Reset-After`、`Retry-After`、`Bucket`、`Global` 及大小写不敏感的 `Scope`，并验证 `HasBucketHeaders == true`。
  - `Parse_ScopeGlobalWithoutGlobalHeader_SetsIsGlobalTrue`：仅存在 `X-RateLimit-Scope: global` 且未带 `X-RateLimit-Global` 时，兜底将 `IsGlobal` 置为 `true`。
  - `Parse_MissingHeaders_ReturnsEmptyInfo`：响应不含任何限流头时返回空字段且 `HasBucketHeaders == false`。
  - `TryParse429Body_ExtractsRetryAfterAndGlobalIgnoringExtraFields`：`Utf8JsonReader` 零反射提取 429 JSON 体里的浮点 `retry_after` 与 `global`，自动跳过无关字段与嵌套对象。
  - `TryParse429Body_InvalidOrIncompletePayload_ReturnsFalse`（4 组 Theory）：空串、Cloudflare HTML、缺少 `retry_after` 的 JSON、JSON 数组均安全返回 `false` 不抛异常。

### RateLimitTicketTests.cs（3 个测试）
- **目标类**: `RateLimitTicket`、`IRateLimitTicketCallback`、`NullRateLimiter`
- **主要测试场景**:
  - `Complete_InvokesCallbackOnceAndPreventsCancelOnDispose`：`Complete` 仅通知一次（重复调用幂等），随后 `Dispose` 不再触发 `Cancel`。
  - `DisposeWithoutComplete_InvokesCancelOnce`：未调用 `Complete` 时 `Dispose` 触发一次 `Cancel`（重复 `Dispose` 幂等）。
  - `NullRateLimiter_CompletesSynchronouslyWithNoOpTicket`：`NullRateLimiter.Instance.AcquireAsync` 同步完成且零分配，返回的默认票据 `Complete`/`Dispose` 均安全无副作用。

### GlobalRateLimiterTests.cs（4 个测试）
- **目标类**: `GlobalRateLimiter`
- **主要测试场景**:
  - `AcquireAsync_AllowsFullBurstImmediatelyWithoutArtificialDelay`：窗口限额内的突发请求全部同步完成、无任何人为延迟。
  - `AcquireAsync_SlidingWindowPreventsPhaseBoundaryBurst`：验证环形滑动窗口在 `t=0/400/900ms` 离散发满后，第 4、5 个请求严格分别等到 `t=1000ms` 与 `t=1400ms` 才放行，彻底杜绝固定窗口边界双倍突刺。
  - `AcquireAsync_CancelledWhileWaiting_DoesNotLeakWindowSlot`：等待全局窗口期间被 `CancellationToken` 取消后不占用任何槽位，窗口滑出后新请求可立即同步通过。
  - `PauseFor_BlocksAllAcquireAndPauseWaitersUntilExpired`：`PauseFor` 触发后同时阻塞 `WaitForGlobalPauseIfNeededAsync` 与 `AcquireAsync`，到期推进后统一放行且 `IsPaused` 恢复 `false`。

### DiscordRateLimiterTests.cs（11 个测试）
- **目标类**: `DiscordRateLimiter`、`DiscordRateLimiterOptions`、`RateLimitBucket`、`GlobalRateLimiter`、`RestClient`（联合集成）
- **主要测试场景**:
  - `UnknownRoute_ProbesSeriallyThenMigratesAndReleasesWaitingRequests`：未知路由首次探测仅放行 1 个请求、其余在临时桶排队；探测返回 `X-RateLimit-Bucket` 与剩余额度后迁移到正式桶并立刻唤醒排队请求，余量耗尽后按重置时间放行。
  - `DifferentRoutesWithSameServerBucketHash_ShareQuotaPerMajorResource`：两个不同路由模板返回同一服务端 `BucketHash` 且同一 `MajorResourceId` 时合并共享同一个正式桶额度。
  - `SameRouteDifferentMajorResources_DoNotBlockEachOther`：同一路由但不同 `MajorResourceId`（不同频道）互不阻塞。
  - `BucketQueue_ReleasesWaitersInStrictFifoOrderAcrossResets`：`Limit=1` 桶跨多次重置严格按 FIFO 顺序 `[1, 2, 3]` 放行排队请求。
  - `BucketBeforeGlobal_QueuedBucketRequestsDoNotConsumeGlobalQuota`：拥堵频道 1 门口排队的 5 个请求停在分桶层，不消耗 `globalLimit=2` 的全局配额，健康频道 2 仍可同步放行。
  - `Shared429_DoesNotPenalizeLocalRouteBucket`：收到 `scope=shared` 且 `Retry-After=1337s` 的 429 时不锁死本地路由桶，后续请求仍可立即通过。
  - `Global429_PausesNormalEndpointsButExemptsInteractionEndpoints`：`global=true` 429 暂停普通端点直到到期，但 `ExemptFromGlobal=true` 的 Interaction 回调端点不受影响立即可发。
  - `TicketCancel_RestoresBucketSlotAndWakesNextWaiter`：覆盖未知桶探测失败取消（释放探测锁）与正式桶预扣后取消（归还额度）两种场景，均立即唤醒下一个等待者不死锁。
  - `StaleResponseFromPreviousWindow_DoesNotCorruptNewWindowQuota`：第 1 窗口的慢响应在第 2 窗口开启后才带着 `Remaining=0` 返回，窗口代次隔离确保其不会污染新窗口余量。
  - `IdleBuckets_AreEvictedOpportunisticallyAfterTimeout`：推进超过 `BucketIdleTimeout` 与 `CleanupInterval` 后触发惰性清理，旧空闲桶被移除，`ActiveBucketCount` 仅保留活跃桶。
  - `RestClientIntegration_EnforcesBucketResetWithoutHitting429`：`RestClient` 与 `DiscordRateLimiter` 联调验证：首请求耗尽桶后，次请求在限流器内等待重置到点才发出，全程不产生多余 HTTP 请求或 429。

### RestClientTests.cs（18 个方法 / 19 个用例）
- **目标类**: `RestClient`、`RestRoute`、`RestBody`、`RestRequest`、`DiscordRestException`、`BaseUrl`、`BotToken`
- **主要测试场景**:
  - `SendAsync_GetJsonModel_DeserializesAndSetsHeadersAndUri`：`IJsonModel<T>` 源生成反序列化、相对路径裁剪前导 `/` 拼接 `BaseUrl`、`Bot` 鉴权头、`User-Agent` 格式、以及 `RateLimitRequest`/`ticket.Complete` 透传验证。
  - `SendAsync_NoResult_SucceedsOn204`：无返回值 `SendAsync` 正常处理 `204 No Content`。
  - `SendAsync_Generic_ReturnsDefaultOn204OrEmptyBody`（2 组 Theory）：`204 No Content` 与空体 `200 OK`（`Content-Length: 0`）均返回 `null` 不抛 `JsonException`。
  - `SendAsync_JsonBody_SendsUtf8JsonAndReusesBytesAcrossRetry`：`RestBody.FromJson` 输出 `application/json; charset=utf-8`，并在 429 重试时复用同一份 UTF-8 字节。
  - `SendAsync_FormBody_SendsFormUrlEncodedContent`：`RestBody.FromForm` 输出 `application/x-www-form-urlencoded` 并配合 `OverrideToken(Basic)` 发送。
  - `SendAsync_MultipartBody_RebuildsContentOnRetryWhenRetryable`：`canRetry: true` 的 Multipart 在 429 重试时重新调用工厂生成带动态 `boundary` 的 `MultipartFormDataContent`。
  - `SendAsync_MultipartBody_DoesNotRetryWhenNonRetryable`：`canRetry: false` 的 Multipart 遇 429 直接抛 `DiscordRestException`，不重试、不等待。
  - `SendAsync_AuthPrecedence_RespectsOverrideTokenAndUseAuthFlag`：验证鉴权优先级（`UseAuth=false` 无头、`UseAuth=false` + `OverrideToken` 优先用覆盖令牌、普通路由 + `OverrideToken` 覆盖默认 Bot 令牌）及对应 `RateLimitRequest.HasAuth`/`AuthScopeKey`。
  - `SendAsync_AuditLogReason_UrlEncodesUnicodeHeader`：中文及空格 `AuditLogReason` 自动 URL 编码写入 `X-Audit-Log-Reason`。
  - `SendAsync_429WithJsonBody_UsesExactRetryAfterAndEnrichesTicketInfo`：优先使用 429 JSON 中的 `retry_after: 64.57`（向上取整为 `64570ms`）并将 `IsGlobal`/`RetryAfterSeconds` 补全回传给 `ticket.Complete`。
  - `SendAsync_429SharedWithoutJsonBody_PrefersRetryAfterOverResetAfter`：无 JSON 体的 `shared` 429 优先采用 `Retry-After` 而非 `X-RateLimit-Reset-After`。
  - `SendAsync_429UserWithoutJsonBody_PrefersResetAfterOverRetryAfter`：无 JSON 体的 `user` 429 优先采用浮点精度的 `X-RateLimit-Reset-After`。
  - `SendAsync_429CloudflareHtml_FallsBackGracefully`：Cloudflare HTML 429 安全降级按 `Retry-After` 重试不抛 `JsonException`。
  - `SendAsync_429ExceedingMaxRetries_ThrowsDiscordRestException`：超过 `maxRateLimitRetries` 后停止重试并抛携带状态码与消息的 `DiscordRestException`。
  - `SendAsync_DiscordErrorJson_ThrowsStructuredExceptionAndCompletesTicket`：解析 Discord 错误体中的 `code`（如 `50013`）与 `message`，同时仍调用 `ticket.Complete`。
  - `SendAsync_NetworkException_CancelsTicketWithoutCompleting`：`HttpClient.SendAsync` 抛网络异常时 `ticket.Dispose` 触发 `Cancel` 而不调用 `Complete`。
  - `Dispose_DisposesOwnedLimiterAndRejectsSubsequentRequests`：`Dispose` 幂等、托管释放自定义限流器、释放后再次 `SendAsync` 抛 `ObjectDisposedException`。
  - `DefaultRateLimiter_EnforcesBucketResetOutOfTheBox`：验证不传 `rateLimiter` 时默认内置 `DiscordRateLimiter` 开箱生效，并遵循 `1.0s + 50ms` 默认缓冲按桶放行。
