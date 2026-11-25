using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

using NativeCord.Serialization;

namespace NativeCord.Tests.Core;

public sealed class SerializationTests
{
    private const ulong SampleSnowflakeValue = 175928847299117063UL;

    [Fact]
    public void Snowflake_SerializesAsStringAndDeserializesFromStringNumberEscapedAndSegmented()
    {
        SnowflakeEnvelope dto = new(
            Id: new Snowflake(SampleSnowflakeValue),
            OptionalId: null,
            Map: new Dictionary<Snowflake, string>
            {
                [new Snowflake(SampleSnowflakeValue)] = "alice"
            });

        string json = JsonSerializer.Serialize(dto, CoreSerializationTestContext.Default.SnowflakeEnvelope);
        Assert.Equal("""{"id":"175928847299117063","optional_id":null,"map":{"175928847299117063":"alice"}}""", json);

        // 字符串与字典键往返
        SnowflakeEnvelope? roundTripped = JsonSerializer.Deserialize(json, CoreSerializationTestContext.Default.SnowflakeEnvelope);
        Assert.NotNull(roundTripped);
        Assert.Equal(new Snowflake(SampleSnowflakeValue), roundTripped.Id);
        Assert.Null(roundTripped.OptionalId);
        Assert.Equal("alice", roundTripped.Map[new Snowflake(SampleSnowflakeValue)]);

        // 兼容数值字面量与 Unicode 转义字符串
        string numericAndEscaped = """{"id":175928847299117063,"optional_id":"\u0031\u0032\u0033","map":{"42":"ok"}}""";
        SnowflakeEnvelope? parsed = JsonSerializer.Deserialize(numericAndEscaped, CoreSerializationTestContext.Default.SnowflakeEnvelope);
        Assert.NotNull(parsed);
        Assert.Equal(new Snowflake(SampleSnowflakeValue), parsed.Id);
        Assert.Equal(new Snowflake(123UL), parsed.OptionalId);
        Assert.Equal("ok", parsed.Map[new Snowflake(42UL)]);

        // 跨 ReadOnlySequence 分段读取（验证 HasValueSequence 分支零异常）
        ReadOnlySequence<byte> segmented = CreateTwoSegmentSequence("\"1759288472"u8, "99117063\""u8);
        Utf8JsonReader reader = new(segmented);
        Assert.True(reader.Read());
        Snowflake fromSequence = new SnowflakeJsonConverter().Read(ref reader, typeof(Snowflake), JsonSerializerOptions.Default);
        Assert.Equal(new Snowflake(SampleSnowflakeValue), fromSequence);
    }

    [Theory]
    [InlineData("""{"id":"not-a-snowflake","optional_id":null,"map":{}}""")]
    [InlineData("""{"id":"-1","optional_id":null,"map":{}}""")]
    [InlineData("""{"id":true,"optional_id":null,"map":{}}""")]
    [InlineData("""{"id":"99999999999999999999999999","optional_id":null,"map":{}}""")]
    public void Snowflake_InvalidJsonTokens_ThrowJsonException(string invalidJson)
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(invalidJson, CoreSerializationTestContext.Default.SnowflakeEnvelope));
    }

    [Fact]
    public void ImageHash_RoundTripsValueAndNullableNullAndRejectsNonString()
    {
        ImageHashEnvelope dto = new(new ImageHash("a_1234abcd"), null);
        string json = JsonSerializer.Serialize(dto, CoreSerializationTestContext.Default.ImageHashEnvelope);
        Assert.Equal("""{"avatar":"a_1234abcd","banner":null}""", json);

        ImageHashEnvelope? roundTripped = JsonSerializer.Deserialize(json, CoreSerializationTestContext.Default.ImageHashEnvelope);
        Assert.NotNull(roundTripped);
        Assert.Equal("a_1234abcd", roundTripped.Avatar.Value);
        Assert.True(roundTripped.Avatar.IsAnimated);
        Assert.Null(roundTripped.Banner);

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize("""{"avatar":123,"banner":null}""", CoreSerializationTestContext.Default.ImageHashEnvelope));
    }

    [Fact]
    public void Optional_PatchSerialization_OmitsUnspecifiedAndWritesExplicitNullAndDefaultScalars()
    {
        PatchPayload payload = new()
        {
            Nick = null, // 显式清空
            // Bio 未设置 -> 必须完全省略
            ChannelId = new Snowflake(999UL),
            ParentId = null, // 显式清空可空 Snowflake
            Position = 0, // 显式 0，不能被 WhenWritingDefault 误删
            Mute = false, // 显式 false，不能被 WhenWritingDefault 误删
            Avatar = new ImageHash("a_ff00"),
            Nested = new NestedTag("v1")
        };

        string json = JsonSerializer.Serialize(payload, CoreSerializationTestContext.Default.PatchPayload);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.False(root.TryGetProperty("bio", out _));
        Assert.True(root.TryGetProperty("nick", out JsonElement nick) && nick.ValueKind == JsonValueKind.Null);
        Assert.Equal("999", root.GetProperty("channel_id").GetString());
        Assert.True(root.TryGetProperty("parent_id", out JsonElement parentId) && parentId.ValueKind == JsonValueKind.Null);
        Assert.Equal(0, root.GetProperty("position").GetInt32());
        Assert.False(root.GetProperty("mute").GetBoolean());
        Assert.Equal("a_ff00", root.GetProperty("avatar").GetString());
        Assert.Equal("v1", root.GetProperty("nested").GetProperty("label").GetString());
    }

    [Fact]
    public void Optional_Deserialization_DistinguishesOmittedFromExplicitNullAndValues()
    {
        const string json = """
            {
              "nick": null,
              "channel_id": "999",
              "parent_id": null,
              "position": 0,
              "mute": false,
              "avatar": "a_ff00",
              "nested": { "label": "v1" }
            }
            """;

        PatchPayload? parsed = JsonSerializer.Deserialize(json, CoreSerializationTestContext.Default.PatchPayload);
        Assert.NotNull(parsed);

        Assert.False(parsed.Bio.HasValue);

        Assert.True(parsed.Nick.HasValue);
        Assert.Null(parsed.Nick.Value);

        Assert.True(parsed.ChannelId.HasValue);
        Assert.Equal(new Snowflake(999UL), parsed.ChannelId.Value);

        Assert.True(parsed.ParentId.HasValue);
        Assert.Null(parsed.ParentId.Value);

        Assert.True(parsed.Position.HasValue);
        Assert.Equal(0, parsed.Position.Value);

        Assert.True(parsed.Mute.HasValue);
        Assert.False(parsed.Mute.Value);

        Assert.True(parsed.Avatar.HasValue);
        Assert.Equal(new ImageHash("a_ff00"), parsed.Avatar.Value);

        Assert.True(parsed.Nested.HasValue);
        Assert.Equal("v1", parsed.Nested.Value?.Label);
    }

    [Fact]
    public void Optional_NonNullableValueTypeWithJsonNull_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize("""{"position":null}""", CoreSerializationTestContext.Default.PatchPayload));
    }

    [Fact]
    public void Optional_WithoutWhenWritingDefault_ThrowsOnUnspecifiedWrite()
    {
        MisconfiguredOptionalPayload bad = new();

        Assert.Throws<InvalidOperationException>(() =>
            JsonSerializer.Serialize(bad, CoreSerializationTestContext.Default.MisconfiguredOptionalPayload));
    }

    private static ReadOnlySequence<byte> CreateTwoSegmentSequence(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        ByteSegment start = new(first.ToArray());
        ByteSegment end = start.Append(second.ToArray());
        return new ReadOnlySequence<byte>(start, 0, end, end.Memory.Length);
    }

    private sealed class ByteSegment : ReadOnlySequenceSegment<byte>
    {
        public ByteSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public ByteSegment Append(ReadOnlyMemory<byte> memory)
        {
            ByteSegment next = new(memory)
            {
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = next;
            return next;
        }
    }
}

internal sealed record SnowflakeEnvelope(
    Snowflake Id,
    Snowflake? OptionalId,
    Dictionary<Snowflake, string> Map);

internal sealed record ImageHashEnvelope(
    ImageHash Avatar,
    ImageHash? Banner);

internal sealed record NestedTag(string Label);

internal sealed class PatchPayload
{
    [JsonConverter(typeof(OptionalJsonConverter<string>))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Nick { get; init; }

    [JsonConverter(typeof(OptionalJsonConverter<string>))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Bio { get; init; }

    [JsonConverter(typeof(OptionalJsonConverter<Snowflake?>))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<Snowflake?> ChannelId { get; init; }

    [JsonConverter(typeof(OptionalJsonConverter<Snowflake?>))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<Snowflake?> ParentId { get; init; }

    [JsonConverter(typeof(OptionalJsonConverter<int>))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> Position { get; init; }

    [JsonConverter(typeof(OptionalJsonConverter<bool>))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Mute { get; init; }

    [JsonConverter(typeof(OptionalJsonConverter<ImageHash?>))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ImageHash?> Avatar { get; init; }

    [JsonConverter(typeof(OptionalJsonConverter<NestedTag>))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<NestedTag?> Nested { get; init; }
}

internal sealed class MisconfiguredOptionalPayload
{
    // 刻意漏标 WhenWritingDefault，用于测试防呆保护
    [JsonConverter(typeof(OptionalJsonConverter<string>))]
    public Optional<string?> Nick { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(SnowflakeEnvelope))]
[JsonSerializable(typeof(ImageHashEnvelope))]
[JsonSerializable(typeof(PatchPayload))]
[JsonSerializable(typeof(NestedTag))]
[JsonSerializable(typeof(MisconfiguredOptionalPayload))]
internal sealed partial class CoreSerializationTestContext : JsonSerializerContext;