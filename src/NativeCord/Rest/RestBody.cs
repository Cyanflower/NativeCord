using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NativeCord.Rest;

/// <summary>
/// Identifies the payload encoding carried by a <see cref="RestBody"/>.
/// </summary>
public enum RestBodyKind : byte
{
    None = 0,
    Json = 1,
    FormUrlEncoded = 2,
    Multipart = 3
}

// 用值类型 Tagged Union 替代 RequestBase<TReq> 类继承树：
// 1. 避免为每次请求分配描述对象，也无需引入复杂的对象池；
// 2. 在进入 RestClient 前收口请求体，把 SendAsync 的双泛型 <TReq, TRes> 降成单泛型 <TRes>，减少 NativeAOT 代码膨胀。

/// <summary>
/// Allocation-free request body descriptor unifying empty, JSON, form-urlencoded, and multipart payloads.
/// </summary>
public readonly struct RestBody
{
    private const string JsonContentType = "application/json; charset=utf-8";
    private const string FormUrlEncodedContentType = "application/x-www-form-urlencoded";

    private readonly ReadOnlyMemory<byte> _bytes;
    private readonly Func<HttpContent>? _contentFactory;
    // 反向存储为 _nonRetryable，确保 default(RestBody)（即 RestBody.Empty）天然满足 CanRetry == true
    private readonly bool _nonRetryable;

    private RestBody(
        RestBodyKind kind,
        ReadOnlyMemory<byte> bytes,
        Func<HttpContent>? contentFactory = null,
        bool canRetry = true)
    {
        Kind = kind;
        _bytes = bytes;
        _contentFactory = contentFactory;
        _nonRetryable = !canRetry;
    }

    /// <summary>
    /// The kind of payload contained in this body.
    /// </summary>
    public RestBodyKind Kind { get; }

    /// <summary>
    /// Whether this payload can safely be re-sent on a 429 rate-limit retry.
    /// </summary>
    public bool CanRetry => !_nonRetryable;

    /// <summary>
    /// An empty body for requests without a payload (such as GET or DELETE).
    /// </summary>
    public static RestBody Empty => default;

    /// <summary>
    /// Serializes a model to UTF-8 JSON using explicit AOT metadata.
    /// </summary>
    public static RestBody FromJson<T>(T value, JsonTypeInfo<T> jsonInfo)
    {
        ArgumentNullException.ThrowIfNull(jsonInfo);

        // 在进入重试循环前只序列化一次，429 重试时直接复用这份 UTF-8 字节；
        // 后续优化点：改为写入 ArrayPool<byte> 池化缓冲，并由 RestClient 在 finally 中归还。
        byte[] utf8Json = JsonSerializer.SerializeToUtf8Bytes(value, jsonInfo);
        return new RestBody(RestBodyKind.Json, utf8Json);
    }

    /// <summary>
    /// Serializes a model implementing <see cref="IJsonModel{TSelf}"/> to UTF-8 JSON.
    /// </summary>
    public static RestBody FromJson<T>(T value) where T : IJsonModel<T>
        => FromJson(value, T.JsonInfo);

    /// <summary>
    /// Wraps pre-serialized UTF-8 JSON bytes as a JSON request body.
    /// </summary>
    public static RestBody FromJson(ReadOnlyMemory<byte> utf8Json)
        => new(RestBodyKind.Json, utf8Json);

    /// <summary>
    /// Wraps pre-encoded UTF-8 <c>application/x-www-form-urlencoded</c> bytes (for example OAuth2 token requests).
    /// </summary>
    public static RestBody FromForm(ReadOnlyMemory<byte> utf8FormUrlEncoded)
        => new(RestBodyKind.FormUrlEncoded, utf8FormUrlEncoded);

    /// <summary>
    /// Creates a multipart body using a factory invoked on each attempt so dynamic boundaries are generated fresh.
    /// Set <paramref name="canRetry"/> to <see langword="false"/> if the underlying streams cannot be rewound.
    /// </summary>
    public static RestBody FromMultipart(Func<HttpContent> contentFactory, bool canRetry = false)
    {
        ArgumentNullException.ThrowIfNull(contentFactory);
        // MultipartFormDataContent 会自动生成带随机 boundary 的 Content-Type，
        // 且 HttpContent 在每次请求后会被释放，因此通过工厂在每次尝试时重新构建。
        return new RestBody(RestBodyKind.Multipart, ReadOnlyMemory<byte>.Empty, contentFactory, canRetry);
    }

    /// <summary>
    /// Creates a fresh <see cref="HttpContent"/> instance for a single HTTP attempt.
    /// </summary>
    internal HttpContent? CreateHttpContent()
    {
        // HttpRequestMessage 释放时会连带释放其 Content，因此每次重试都需要新建一个轻量的 HttpContent 包装
        return Kind switch
        {
            RestBodyKind.None => null,
            RestBodyKind.Json => CreateMemoryContent(_bytes, JsonContentType),
            RestBodyKind.FormUrlEncoded => CreateMemoryContent(_bytes, FormUrlEncodedContentType),
            RestBodyKind.Multipart => _contentFactory!(),
            _ => throw new UnreachableException($"Unsupported {nameof(RestBodyKind)}: {Kind}")
        };
    }

    private static ReadOnlyMemoryContent CreateMemoryContent(ReadOnlyMemory<byte> bytes, string contentType)
    {
        // ReadOnlyMemoryContent 只包裹切片不拷贝底层字节；
        // 使用 TryAddWithoutValidation 写入不可变字符串常量，既跳过格式校验，又避免跨线程共享可变的 MediaTypeHeaderValue。
        ReadOnlyMemoryContent content = new(bytes);
        content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        return content;
    }
}