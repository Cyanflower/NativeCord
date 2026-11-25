namespace NativeCord.Serialization;

/// <summary>
/// AOT-safe JSON converter for <see cref="ImageHash"/> values.
/// </summary>
// 独立转换层将 JSON 线格式与底层存储解耦：后续把 ImageHash 优化为 UInt128 紧凑结构时，所有 DTO 无需改动
public sealed class ImageHashJsonConverter : JsonConverter<ImageHash>
{
    /// <inheritdoc />
    public override ImageHash Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected a JSON string for {nameof(ImageHash)}, got {reader.TokenType}.");

        string? value = reader.GetString();
        return value is not null
            ? new ImageHash(value)
            : throw new JsonException($"Unexpected null string value for {nameof(ImageHash)}.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ImageHash value, JsonSerializerOptions options)
    {
        if (value.Value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.Value);
    }
}