# AGENTS.md — NativeCord

## 提示指令

- 在需要了解测试项目或定位受影响测试用例时，**优先查阅各测试项目的 `Index.md`**，避免全局扫描测试目录。
- 任何时候编写、修改或删除测试用例（`tests/`）或基准测试（`benchmarks/`），**必须在同一次修改中同步更新对应的 `Index.md`**；增删 `src/` 文件或变更核心架构约定时，同步更新本文件（`AGENTS.md`）。
- 注释规范：**XML 文档注释（`<summary>` / `<param>` / `<returns>`）保持英文**，代码内实现与设计说明注释（`//`）使用**中文**。
- 编码风格遵循仓库根目录 `.editorconfig`（4 空格缩进、`using` 分组且 `System` 优先、私有实例字段 `_camelCase`、私有静态字段 `s_camelCase`、明确类型优于隐式 `var`）。

---

## 项目概述

以 **NativeAOT 优先 · 高吞吐 · 低常驻内存** 为核心目标的 .NET Discord API 框架库（Class Library）。

- **工作目录**: `/home/cyanflower/Projects/NativeCord/`
- **解决方案**: `NativeCord.slnx`
- **构建与测试**:
  - `dotnet build NativeCord.slnx --configuration Release`
  - `dotnet test NativeCord.slnx --configuration Release`
  - `dotnet pack src/NativeCord/NativeCord.csproj --configuration Release --output ./nupkgs`
- **目标框架**: `net11.0`（开启 `<IsAotCompatible>true</IsAotCompatible>`、`<Nullable>enable</Nullable>`）
- **外部依赖原则**: 主库零外部 NuGet 依赖（`Microsoft.Extensions.Logging.Abstractions` 已由 .NET 11 BCL 内置）；**禁止引入 `System.Threading.RateLimiting`** 或反射驱动库。
- **CI**: `.github/workflows/ci.yml`（推送/PR 自动执行 `restore` → `build (Release + AOT 分析器)` → `test` → `pack`）。

---

## 核心架构

### REST 请求管道流程

```text
调用方组装 RestRequest (RestRoute + RestBody + AuditLogReason? + OverrideToken?)
        ↓
RestClient.SendAsync(...)              ← 对外仅暴露 2+1 个入口（无返回 / 显式 JsonTypeInfo / IJsonModel<T>）
        ↓
SendWithRetryAsync (非泛型核心管道)     ← NativeAOT 下整段 HTTP / 限流 / 重试逻辑仅编译一份机器码
        ↓
IRateLimiter.AcquireAsync(in RateLimitRequest)
   ├─ 1. 若处于 global 429 暂停期（且非 ExemptFromGlobal），先等全局暂停解除
   ├─ 2. 先抢「分桶额度」（未知路由串行探测→学到 BucketHash 后迁移合并；拥堵频道排队不消耗全局配额）
   ├─ 3. 再抢「全局滑动窗口 50/s 额度」（long[50] 环形数组 O(1) 零分配，防相位错位打穿，突发零延迟）
   └─ 返回值类型 RateLimitTicket（零堆分配）
        ↓
CreateHttpRequestMessage               ← 每次尝试新建（不可复用），动态挂载 Authorization 与 URL 编码后的 AuditLogReason
        ↓
HttpClient.SendAsync(..., ResponseHeadersRead)
        ↓
RateLimitInfo.Parse(headers.NonValidated)
        ↓
ticket.Complete(statusCode, in info)   ← 网络异常/取消跳出 using 时 Dispose 自动触发 Cancel 归还槽位防死锁
        ├─ 2xx: 204/空体返回 default；否则流式 JsonSerializer.DeserializeAsync(stream, JsonTypeInfo)
        ├─ 429: Utf8JsonReader 零反射解析 retry_after/global → 释放连接与票据 → 向上取整整毫秒 Task.Delay 重试
        └─ 其他: Utf8JsonReader 零反射提取 Discord code/message → 抛 DiscordRestException
```

### 限流器架构要点（`DiscordRateLimiter`）

- **按认证域完全隔离**：每个 `AuthScopeKey`（不同 Bot/Bearer/Basic Token）独立持有 `GlobalRateLimiter`、`BucketTemplate -> BucketHash` 路由表与分桶字典；无 `Authorization` 头的请求共享进程级匿名 IP 域（`_anonymousScope`）。
- **先分桶、后全局**：避免某个拥堵频道的排队请求白白耗尽 50 QPS 全局配额；若等待全局窗口期间取消，自动调用 `bucket.Cancel` 退还分桶额度。
- **同步零分配快路径**：常规未拥堵请求在非 `async` 入口直接同步完成 `AcquireAsync`，不产生 `async` 状态机与 `RateLimitTicket` 堆分配。
- **窗口代次隔离**：`RateLimitTicket` 的 `ticketToken` 编码桶窗口代数 `_windowGeneration`，防止上一窗口迟到的慢响应覆盖新窗口余量。
- **无后台常驻线程**：每桶仅在存在等待者且已知重置时刻时挂 1 个复用 `ITimer`；空闲桶在请求进入时按 `CleanupInterval` 惰性清理驱逐。

---

## 关键文件索引（`src/`）

```text
src/NativeCord/
├── NativeCord.csproj                  # 主库项目文件（net11.0, IsAotCompatible=true, InternalsVisibleTo NativeCord.Tests）
├── GlobalUsing.cs                     # 全局 using（System.Text.Json / Serialization / Serialization.Metadata）
│
├── Core/
│   ├── Common/
│   │   └── Prefab/
│   │       └── StringHandler.cs       # DefaultInterpolatedStringHandler 构造辅助
│   │
│   ├── Constants/
│   │   ├── DiscordEpoch.cs            # Discord Epoch (2015-01-01) 与 Snowflake 64 位位运算偏移/掩码常量
│   │   ├── EndPath.Api.cs             # Discord REST API 端点路径表（静态方法集合，后续可作 Route 生成输入）
│   │   ├── EndPath.Cdn.cs             # Discord CDN 相对路径表
│   │   ├── MagicNumber.cs             # 协议魔法数字常量（如 StickersApplicationId）
│   │   └── MagicString.cs             # 协议常量（CDN 动图前缀 "a_"、UserAgent/RepoUri/LibraryVersion、RateLimitScope 字符串）
│   │
│   ├── Entities/
│   │   ├── Snowflake.cs               # readonly record struct(ulong)，默认挂 SnowflakeJsonConverter；实现 IComparable/SpanFormattable/SpanParsable + FromTimestamp；禁用 implicit operator string
│   │   ├── ImageHash.cs               # readonly record struct(string)，默认挂 ImageHashJsonConverter；ISpanFormattable + IsAnimated 检测（后续计划优化为 UInt128 紧凑存储）
│   │   ├── Optional.cs                # readonly struct Optional<T> + Optional.From<T>()，严格区分“未传字段(default)”与“显式 null/0/false”
│   │   ├── BotToken.cs                # readonly record struct(string Value, BotTokenType Type)，internal ToAuthorizationHeader() 校验非空并格式化鉴权头
│   │   └── DiscordUrl.cs              # readonly record struct(string Value, string? Resource)，CDN URL 包装类型
│   │
│   ├── Serialization/
│   │   ├── SnowflakeJsonConverter.cs  # 零分配 AOT 转换器（UTF-8 Span 读写 + 字典键支持 + 分段/转义栈回退 + 兼容数值读取）
│   │   ├── ImageHashJsonConverter.cs  # AOT 转换器，隔离 JSON 线格式与底层存储
│   │   └── OptionalJsonConverter.cs   # 封闭泛型 OptionalJsonConverter<T>（HandleNull=true + 编译期单态化标量快路径 + options.TryGetTypeInfo，零 MakeGenericType）
│   │
│   ├── Enums/
│   │   ├── BotTokenType.cs            # Bot / Bearer / Basic 枚举及 FormatString 扩展属性
│   │   ├── ImageSize.cs               # CDN 支持的 2 的幂图片尺寸（16 ~ 4096）
│   │   └── ImageFormats/
│   │       ├── ImageFormat.cs         # internal 规范图片格式枚举（含 APNG / AnimatedWebP / Lottie）及 FormatString 扩展
│   │       ├── ImageFormatConverter.cs# StdImageFormat / StaticImageFormat / StickerImageFormat → ImageFormat 校验与转换
│   │       ├── StdImageFormat.cs      # 常规图片端点格式枚举
│   │       ├── StaticImageFormat.cs   # 纯静态图片端点格式枚举
│   │       └── StickerImageFormat.cs  # 贴纸端点格式枚举（对齐 Discord Sticker Format Types 数值）
│   │
│   └── Urls/
│       ├── BaseUrl.cs                 # BaseUrl record（ApiVersion/VoiceGatewayVersion/Rest/Cdn/Media/Invite），Default 单例与 GetRestApiBaseUri()（末尾带 '/'）
│       └── CdnEndpoints.cs            # internal CDN URL 构建器（自动处理 animated=true、size 夹逼与不支持尺寸的端点）
│
└── Rest/
    ├── IJsonModel.cs                  # IJsonModel<TSelf>（static abstract JsonTypeInfo<TSelf> JsonInfo），零反射 AOT 序列化契约
    ├── RestRoute.cs                   # readonly record struct：Method / Path / BucketTemplate / MajorResourceId / MajorResourceToken / UseAuth / ExemptFromGlobal → RateLimitKey
    ├── RestBody.cs                    # RestBodyKind + readonly struct RestBody（Tagged Union）：Empty / FromJson / FromForm / FromMultipart + CanRetry + internal CreateHttpContent()
    ├── RestRequest.cs                 # readonly struct 通用请求描述符（Route / Body / AuditLogReason / OverrideToken），刻意不携带响应泛型 <TRes>
    ├── RestClient.cs                  # sealed IDisposable 核心 HTTP 管道（2+1 SendAsync、非泛型 SendWithRetryAsync、SocketsHttpHandler 调优、动态鉴权、可注入 TimeProvider/Handler/RateLimiter）
    ├── DiscordRestException.cs        # 非 2xx 结构化异常（StatusCode / DiscordErrorCode / DiscordMessage / RawBody / RateLimit），Utf8JsonReader 零反射解析
    └── RateLimiting/
        ├── RateLimitKey.cs            # readonly record struct(BucketTemplate, MajorResourceId, MajorResourceToken)，未知路由临时分桶键
        ├── RateLimitInfo.cs           # RateLimitScope 枚举 + readonly record struct RateLimitInfo（headers.NonValidated 解析 + Utf8JsonReader 解析 429 JSON 体）
        ├── RateLimitTicket.cs         # IRateLimitTicketCallback + struct RateLimitTicket（零堆分配租约，Complete 幂等，未 Complete 时 Dispose 自动 Cancel）
        ├── IRateLimiter.cs            # RateLimitRequest + IRateLimiter 接口（AcquireAsync + IDisposable）
        ├── NullRateLimiter.cs         # 零分配空限流器单例（用于测试隔离或外部 REST 代理）
        ├── DiscordRateLimiterOptions.cs # 限流器配置（GlobalRequestsPerWindow=50 / GlobalWindow=1050ms / BucketResetBuffer=50ms / BucketIdleTimeout=10m / CleanupInterval=2m）
        ├── GlobalRateLimiter.cs       # internal sealed 滑动窗口全局限流器（定长 long[] 环形数组 + global 429 暂停屏障 + 同步零分配快路径）
        ├── RateLimitBucket.cs         # internal sealed 单桶状态机（直接实现 IRateLimitTicketCallback、FIFO 队列 + 单复用 ITimer、未知桶探测迁移、窗口代数隔离、shared/user 429 分流）
        └── DiscordRateLimiter.cs      # public sealed IRateLimiter 完整实现（AuthScope 隔离、先分桶后全局、惰性驱逐空闲桶）
```

---

## 关键设计约定与 AOT 红线

### 硬性约束

1. **零运行时反射**：所有 JSON 序列化/反序列化必须显式传递源生成的 `JsonTypeInfo<T>`（或通过 `IJsonModel<T>` 静态抽象接口）；协议级辅助解析（429 响应体、Discord 错误体）统一使用 `Utf8JsonReader`。测试项目强制开启 `<JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>` 守门。
2. **AOT 代码体积控制**：`RestBody` 在进入 `RestClient` 前收口请求体类型，将 `SendAsync<TReq, TRes>` 的 $M \times N$ 泛型组合降为单泛型 `<TRes>`；核心发送与重试循环 `SendWithRetryAsync` 保持非泛型。
3. **热路径低分配**：
   - `Snowflake` 禁止 `implicit operator string`，避免无意识堆分配。
   - 响应头读取统一使用 `response.Headers.NonValidated`，避免 RFC 校验解析开销。
   - HTTP 发送必须使用 `HttpCompletionOption.ResponseHeadersRead` + 流式反序列化。
   - `RestBody`、`RestRequest`、`RestRoute`、`RateLimitInfo`、`RateLimitTicket` 均为值类型，不引入类继承树或请求描述对象池。
4. **认证头归属**：`Authorization` 绝不写进 `HttpClient.DefaultRequestHeaders`；由 `RestClient.ResolveAuthorizationHeader` 按 `OverrideToken > (Route.UseAuth ? _defaultAuthHeader : null)` 动态挂载，兼容 Bot / Bearer / Basic / Webhook 无鉴权同池复用。
5. **时间精度防提前重试**：所有浮点秒转延迟均使用 `Math.Ceiling(seconds * 1000.0)` 向上取整到整毫秒，防止 `TimeSpan.FromSeconds` 与 `Task.Delay` 毫秒截断导致提前 1ms 发请求再次触发 429。
6. **资源释放安全**：`SendWithRetryAsync` 将 `RateLimitTicket`、`HttpRequestMessage`、`HttpResponseMessage` 限定在内层 `using` 块中，确保进入 429 `Task.Delay` 前已释放所有连接、对象与限流票据。

### 关键设计决策

| 决策 | 意图 |
|---|---|
| `RestClient` 默认内置 `DiscordRateLimiter` | 开箱即具备完整限流保护并自动托管释放；外部 REST 代理或隔离单测显式传 `NullRateLimiter.Instance` |
| `RestBody` 使用值类型 Tagged Union + `FromMultipart` 工厂 | 覆盖 Empty / JSON / Form / Multipart 四种载荷且避免类继承树与对象池；Multipart 每次重试重建动态 boundary 并通过 `CanRetry` 保护不可复位流 |
| `GlobalRateLimiter` 使用内联定长 `long[]` 滑动窗口 | 默认 400 字节常驻 L1 Cache，$O(1)$ 且数学上杜绝固定窗口相位错位打穿，同时保持突发 0ms 延迟 |
| 先抢分桶额度、再抢全局额度 | 单个拥堵频道排队的请求不会浪费 50 QPS 全局配额；全局等待取消时自动 `Cancel` 归还分桶额度 |
| `RateLimitBucket` 直接实现 `IRateLimitTicketCallback` | `RateLimitTicket` 仅存接口引用 + `long` 代次号，发券与回收全程零额外堆分配 |
| `RestClient` 与 `DiscordRateLimiter` 均支持注入 `TimeProvider` | 所有 429 重试与限流窗口测试 0ms 确定性执行，无真实睡眠、无 CI 抖动 |

---

## 测试

### 测试项目

| 项目 | 说明 | 代码索引 |
|---|---|---|
| `tests/NativeCord.Tests/` | 单元测试（Core 值类型、REST 管道、限流头解析、票据生命周期、全局滑动窗口与分桶限流器） | `tests/NativeCord.Tests/Index.md` |

> **Index.md 用途**：按目标类列出每个测试文件的覆盖场景；修改核心逻辑后先查 `Index.md` 定位受影响测试，修改测试后同步更新 `Index.md`。

### 运行测试

```bash
dotnet test NativeCord.slnx
dotnet test NativeCord.slnx --configuration Release --verbosity normal
```

### 测试现状（2026-11-25）

- **测试总数**: 62（全部开启 `JsonSerializerIsReflectionEnabledByDefault=false`，零外部网络依赖，使用 `MockHttpMessageHandler` + `FakeTimeProvider`）
  - `Core/SnowflakeTests`: 4
  - `Core/OptionalTests`: 3
  - `Core/SerializationTests`: 10
  - `Rest/RateLimitInfoTests`: 8
  - `Rest/RateLimitTicketTests`: 3
  - `Rest/GlobalRateLimiterTests`: 4
  - `Rest/DiscordRateLimiterTests`: 11
  - `Rest/RestClientTests`: 19

---

## 阶段进展

- [x] Core 基础值类型与常量（`Snowflake`、`ImageHash`、`Optional<T>`、`BotToken`、`BaseUrl`、CDN 端点与图片格式枚举）
- [x] AOT 零反射核心 JSON 转换器（`SnowflakeJsonConverter`、`ImageHashJsonConverter`、`OptionalJsonConverter<T>`）
- [x] AOT 友好 REST 管道（`RestRoute`、`RestBody`、`RestRequest`、`RestClient`、`IJsonModel<T>`、`DiscordRestException`）
- [x] Discord REST 限流器（`DiscordRateLimiter`：全局滑动窗口 + 两级分桶 + 未知路由串行探测迁移 + 窗口代次隔离 + 惰性清理）
- [x] 单元测试项目（`tests/NativeCord.Tests`）与 GitHub Actions CI（`build` + `test` + `pack`）
- [ ] 最小真实 DTO（`User`、`Message`、`GatewayBotInfo`）+ 上层 REST 端点封装 + `samples/AotSmoke` 真机与 NativeAOT publish 验证
- [ ] REST 端点封装与模型铺开（基于 Discord OpenAPI 规范）
- [ ] Gateway 层（WebSocket / 心跳 / 压缩 / 分片 / 零分配分发）
- [ ] 可选缓存层与 Interactions 源生成器
