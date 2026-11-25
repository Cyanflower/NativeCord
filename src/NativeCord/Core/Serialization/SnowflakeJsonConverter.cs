using System.Globalization;

namespace NativeCord.Serialization;

/// <summary>
/// Zero-allocation, AOT-safe JSON converter for <see cref="Snowflake"/> values and dictionary property names.
/// </summary>
// Discord 协议规定 Snowflake 在 JSON 中以字符串传输（避免 JS 53 位精度丢失），读取时顺带兼容数值字面量；
// 全程直接操作 UTF-8 Span，避免 GetString()/ToString() 中间堆分配。
public sealed class SnowflakeJsonConverter : JsonConverter<Snowflake>
{
    // ulong.MaxValue = 18446744073709551615，恰好 20 个 ASCII 字节
    private const int MaxSnowflakeUtf8Length = 20;

    /// <inheritdoc />
    public override Snowflake Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return ReadFromStringToken(ref reader);

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetUInt64(out ulong numericValue))
            return new Snowflake(numericValue);

        throw new JsonException($"Expected a JSON string or unsigned integer for {nameof(Snowflake)}, got {reader.TokenType}.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Snowflake value, JsonSerializerOptions options)
    {
        Span<byte> utf8Buffer = stackalloc byte[MaxSnowflakeUtf8Length];
        value.TryFormat(utf8Buffer, out int bytesWritten, default, CultureInfo.InvariantCulture);
        writer.WriteStringValue(utf8Buffer[..bytesWritten]);
    }

    /// <inheritdoc />
    public override Snowflake ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // 支持以 Snowflake 作为键的字典（例如 Interaction resolved.users / resolved.channels）
        return ReadFromStringToken(ref reader);
    }

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, Snowflake value, JsonSerializerOptions options)
    {
        Span<byte> utf8Buffer = stackalloc byte[MaxSnowflakeUtf8Length];
        value.TryFormat(utf8Buffer, out int bytesWritten, default, CultureInfo.InvariantCulture);
        writer.WritePropertyName(utf8Buffer[..bytesWritten]);
    }

    private static Snowflake ReadFromStringToken(ref Utf8JsonReader reader)
    {
        // 绝大多数场景数据落在单一连续 Span 且无转义，直接零分配解析
        if (!reader.HasValueSequence && !reader.ValueIsEscaped)
        {
            if (Snowflake.TryParse(reader.ValueSpan, CultureInfo.InvariantCulture, out Snowflake snowflake))
                return snowflake;

            throw new JsonException($"Invalid {nameof(Snowflake)} string value.");
        }

        // 跨分段缓冲或包含 JSON 转义时，解码到栈上小缓冲再解析（最长 20 位数字，32 字节足够）
        Span<byte> buffer = stackalloc byte[32];
        long rawLength = reader.HasValueSequence
            ? reader.ValueSequence.Length
            : reader.ValueSpan.Length;

        // 即使含 \u0031 等转义，单个转义最多膨胀 6 倍，先检查原始长度防止超长恶意输入
        if (rawLength > buffer.Length * 6)
            throw new JsonException($"Invalid {nameof(Snowflake)} string value: token too long.");

        int written;
        try
        {
            written = reader.CopyString(buffer);
        }
        catch (ArgumentException ex)
        {
            throw new JsonException($"Invalid {nameof(Snowflake)} string value: decoded length exceeded buffer.", ex);
        }

        if (Snowflake.TryParse(buffer[..written], CultureInfo.InvariantCulture, out Snowflake parsed))
            return parsed;

        throw new JsonException($"Invalid {nameof(Snowflake)} string value.");
    }
}