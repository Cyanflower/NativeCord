using System.Net;
using System.Text;
using System.Text.Json;

using NativeCord.Rest.RateLimiting;

namespace NativeCord.Rest;

/// <summary>
/// Represents a non-success HTTP response returned by the Discord REST API.
/// </summary>
public sealed class DiscordRestException : Exception
{
    /// <summary>
    /// HTTP status code returned by the server.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Discord JSON error code if present in the response body (for example <c>50013</c> Missing Permissions).
    /// </summary>
    public int? DiscordErrorCode { get; }

    /// <summary>
    /// Discord JSON error message if present in the response body.
    /// </summary>
    public string? DiscordMessage { get; }

    /// <summary>
    /// Raw UTF-8 response body text for diagnostics.
    /// </summary>
    public string RawBody { get; }

    /// <summary>
    /// Rate-limit headers parsed from the failed response.
    /// </summary>
    public RateLimitInfo RateLimit { get; }

    public DiscordRestException(
        HttpStatusCode statusCode,
        int? discordErrorCode,
        string? discordMessage,
        string rawBody,
        RateLimitInfo rateLimit)
        : base(FormatMessage(statusCode, discordErrorCode, discordMessage, rawBody))
    {
        StatusCode = statusCode;
        DiscordErrorCode = discordErrorCode;
        DiscordMessage = discordMessage;
        RawBody = rawBody;
        RateLimit = rateLimit;
    }

    /// <summary>
    /// Reads the response body and creates a <see cref="DiscordRestException"/>.
    /// </summary>
    internal static async Task<DiscordRestException> CreateAsync(
        HttpResponseMessage response,
        RateLimitInfo rateLimit,
        CancellationToken ct)
    {
        byte[] bodyBytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        return CreateFromBytes(response.StatusCode, bodyBytes, rateLimit);
    }

    /// <summary>
    /// Creates a <see cref="DiscordRestException"/> from an already buffered response body.
    /// </summary>
    internal static DiscordRestException CreateFromBytes(
        HttpStatusCode statusCode,
        ReadOnlySpan<byte> bodyBytes,
        RateLimitInfo rateLimit)
    {
        // 429 场景下响应体已经提前读入内存用于解析 retry_after，可直接复用该字节数组，避免二次读取响应流
        TryParseErrorFields(bodyBytes, out int? errorCode, out string? errorMessage);
        string rawBody = bodyBytes.IsEmpty ? string.Empty : Encoding.UTF8.GetString(bodyBytes);
        return new DiscordRestException(statusCode, errorCode, errorMessage, rawBody, rateLimit);
    }

    private static void TryParseErrorFields(ReadOnlySpan<byte> utf8Body, out int? errorCode, out string? errorMessage)
    {
        errorCode = null;
        errorMessage = null;

        if (utf8Body.IsEmpty)
            return;

        try
        {
            // 用 Utf8JsonReader 零反射提取 Discord 顶层 code / message 字段，兼容 NativeAOT
            Utf8JsonReader reader = new(utf8Body);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                if (reader.ValueTextEquals("code"u8))
                {
                    if (reader.Read() && reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int code))
                        errorCode = code;
                }
                else if (reader.ValueTextEquals("message"u8))
                {
                    if (reader.Read() && reader.TokenType == JsonTokenType.String)
                        errorMessage = reader.GetString();
                }
                else
                {
                    reader.Skip();
                }
            }
        }
        catch (JsonException)
        {
            // 非 JSON 错误响应（如 Cloudflare 返回的 5xx/429 HTML 页面），保留 RawBody 供排查
        }
    }

    private static string FormatMessage(HttpStatusCode statusCode, int? errorCode, string? errorMessage, string rawBody)
    {
        if (errorCode.HasValue && !string.IsNullOrEmpty(errorMessage))
            return $"Discord REST HTTP {(int)statusCode} ({statusCode}) [Code {errorCode.Value}]: {errorMessage}";

        if (!string.IsNullOrEmpty(errorMessage))
            return $"Discord REST HTTP {(int)statusCode} ({statusCode}): {errorMessage}";

        return string.IsNullOrEmpty(rawBody)
            ? $"Discord REST HTTP {(int)statusCode} ({statusCode})."
            : $"Discord REST HTTP {(int)statusCode} ({statusCode}): {rawBody}";
    }
}