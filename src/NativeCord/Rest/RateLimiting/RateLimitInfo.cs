using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NativeCord.Rest.RateLimiting;

/// <summary>
/// Represents the scope reported by Discord's <c>X-RateLimit-Scope</c> header.
/// </summary>
public enum RateLimitScope : byte
{
    Unknown = 0,
    User = 1,
    Shared = 2,
    Global = 3
}

// 使用 readonly record struct 避免每次 HTTP 响应都在堆上分配限流头对象

/// <summary>
/// Parsed Discord REST rate-limit metadata extracted from HTTP response headers.
/// </summary>
public readonly record struct RateLimitInfo(
    int? Limit,
    int? Remaining,
    double? Reset,
    double? ResetAfterSeconds,
    double? RetryAfterSeconds,
    string? Bucket,
    bool IsGlobal,
    RateLimitScope Scope)
{
    /// <summary>
    /// Indicates whether the response carried per-route bucket rate-limit headers.
    /// </summary>
    public bool HasBucketHeaders => Bucket is not null || Limit.HasValue || Remaining.HasValue;

    /// <summary>
    /// Parses Discord rate-limit headers from an HTTP response without header validation overhead.
    /// </summary>
    public static RateLimitInfo Parse(HttpResponseHeaders headers)
    {
        // 通过 NonValidated 直接读取原始头值，跳过 RFC 语法解析与额外分配
        HttpHeadersNonValidated nonValidated = headers.NonValidated;

        int? limit = TryGetInt(in nonValidated, "X-RateLimit-Limit");
        int? remaining = TryGetInt(in nonValidated, "X-RateLimit-Remaining");
        // 注意：Discord 的 X-RateLimit-Reset 是带毫秒小数的 Unix 秒时间戳（如 1470173023.123），必须用 double
        double? reset = TryGetDouble(in nonValidated, "X-RateLimit-Reset");
        // 限流器应优先使用相对时间 ResetAfterSeconds + 本地单调时钟，避免受本机系统时间偏差影响
        double? resetAfter = TryGetDouble(in nonValidated, "X-RateLimit-Reset-After");
        double? retryAfter = TryGetDouble(in nonValidated, "Retry-After");
        string? bucket = TryGetString(in nonValidated, "X-RateLimit-Bucket");
        bool isGlobal = TryGetBool(in nonValidated, "X-RateLimit-Global");
        RateLimitScope scope = ParseScope(TryGetString(in nonValidated, "X-RateLimit-Scope"));

        // 兜底：若仅下发了 X-RateLimit-Scope: global 而未带 X-RateLimit-Global，同样视为全局限流
        if (scope == RateLimitScope.Global)
            isGlobal = true;

        return new RateLimitInfo(limit, remaining, reset, resetAfter, retryAfter, bucket, isGlobal, scope);
    }

    /// <summary>
    /// Attempts to read Discord's 429 JSON response fields (<c>retry_after</c>, <c>global</c>)
    /// using <see cref="Utf8JsonReader"/> with zero reflection.
    /// </summary>
    internal static bool TryParse429Body(ReadOnlySpan<byte> utf8Body, out double retryAfterSeconds, out bool isGlobal)
    {
        retryAfterSeconds = 0;
        isGlobal = false;

        if (utf8Body.IsEmpty)
            return false;

        bool foundRetryAfter = false;
        try
        {
            // 直接用 Utf8JsonReader 扫描顶层字段：零反射、零 DTO 分配、100% NativeAOT 安全
            Utf8JsonReader reader = new(utf8Body);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return false;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                // Discord 429 响应体里的 retry_after 是带小数的精确秒数，比整数形式的 Retry-After 头更准确
                if (reader.ValueTextEquals("retry_after"u8))
                {
                    if (reader.Read() && reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out double value))
                    {
                        retryAfterSeconds = value;
                        foundRetryAfter = true;
                    }
                }
                else if (reader.ValueTextEquals("global"u8))
                {
                    if (reader.Read() && reader.TokenType is JsonTokenType.True or JsonTokenType.False)
                        isGlobal = reader.GetBoolean();
                }
                else
                {
                    reader.Skip();
                }
            }
        }
        catch (JsonException)
        {
            // 若 429 来自 Cloudflare 网关，响应体可能是 HTML 而非 JSON，安全降级由调用方改用响应头计算等待时间
            return false;
        }

        return foundRetryAfter;
    }

    private static string? TryGetString(in HttpHeadersNonValidated headers, string name)
    {
        if (!headers.TryGetValues(name, out HeaderStringValues values))
            return null;

        // HeaderStringValues 是值类型枚举器，直接取首个值，不产生 LINQ 或数组分配
        foreach (string value in values)
            return value;

        return null;
    }

    private static int? TryGetInt(in HttpHeadersNonValidated headers, string name)
    {
        string? raw = TryGetString(in headers, name);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
    }

    private static double? TryGetDouble(in HttpHeadersNonValidated headers, string name)
    {
        string? raw = TryGetString(in headers, name);
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : null;
    }

    private static bool TryGetBool(in HttpHeadersNonValidated headers, string name)
    {
        string? raw = TryGetString(in headers, name);
        return bool.TryParse(raw, out bool parsed) && parsed;
    }

    private static RateLimitScope ParseScope(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return RateLimitScope.Unknown;

        if (raw.Equals(MagicString.Rest.RateLimitScope.User, StringComparison.OrdinalIgnoreCase))
            return RateLimitScope.User;
        if (raw.Equals(MagicString.Rest.RateLimitScope.Shared, StringComparison.OrdinalIgnoreCase))
            return RateLimitScope.Shared;
        if (raw.Equals(MagicString.Rest.RateLimitScope.Global, StringComparison.OrdinalIgnoreCase))
            return RateLimitScope.Global;

        return RateLimitScope.Unknown;
    }
}