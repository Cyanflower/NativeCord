using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace NativeCord.Serialization;

/// <summary>
/// AOT-safe closed-generic JSON converter for <see cref="Optional{T}"/> properties.
/// Pair with <c>[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]</c> so unspecified fields are omitted
/// while explicitly specified <see langword="null"/> values are serialized as JSON <c>null</c>.
/// </summary>
// 刻意采用封闭泛型转换器而非 JsonConverterFactory + MakeGenericType，避免 NativeAOT 触发 IL3050 或值类型元数据缺失。
// 对常用基础类型使用 typeof(T) 静态分支：JIT/NativeAOT 在单态化编译期会直接折叠为常量并消除死分支，既零反射又不要求额外注册基础类型 JsonTypeInfo。
public sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
{
    private static readonly SnowflakeJsonConverter s_snowflakeConverter = new();
    private static readonly ImageHashJsonConverter s_imageHashConverter = new();

    /// <summary>
    /// Enables <see cref="Read"/> to receive explicit JSON <c>null</c> tokens so <c>Optional&lt;T?&gt;</c>
    /// can distinguish explicit <c>null</c> (<c>HasValue == true</c>) from an omitted property (<c>HasValue == false</c>).
    /// </summary>
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            // 非可空值类型（如 Optional<int> / Optional<Snowflake>）遇到 JSON null 应严格报错，避免静默落回 0
            if (default(T) is not null)
                throw new JsonException($"Cannot convert JSON null to non-nullable type {typeof(T).Name}.");

            return new Optional<T>(default!);
        }

        // 编译期单态化快路径：覆盖 Discord PATCH 请求中最常见的标量与 Core 值类型
        if (typeof(T) == typeof(string))
            return new Optional<T>((T)(object)reader.GetString()!);

        if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?))
        {
            bool value = reader.GetBoolean();
            return new Optional<T>(WrapScalar<bool>(value));
        }

        if (typeof(T) == typeof(int) || typeof(T) == typeof(int?))
        {
            int value = reader.GetInt32();
            return new Optional<T>(WrapScalar<int>(value));
        }

        if (typeof(T) == typeof(long) || typeof(T) == typeof(long?))
        {
            long value = reader.GetInt64();
            return new Optional<T>(WrapScalar<long>(value));
        }

        if (typeof(T) == typeof(ulong) || typeof(T) == typeof(ulong?))
        {
            ulong value = reader.GetUInt64();
            return new Optional<T>(WrapScalar<ulong>(value));
        }

        if (typeof(T) == typeof(double) || typeof(T) == typeof(double?))
        {
            double value = reader.GetDouble();
            return new Optional<T>(WrapScalar<double>(value));
        }

        if (typeof(T) == typeof(Snowflake) || typeof(T) == typeof(Snowflake?))
        {
            Snowflake value = s_snowflakeConverter.Read(ref reader, typeof(Snowflake), options);
            return new Optional<T>(WrapScalar<Snowflake>(value));
        }

        if (typeof(T) == typeof(ImageHash) || typeof(T) == typeof(ImageHash?))
        {
            ImageHash value = s_imageHashConverter.Read(ref reader, typeof(ImageHash), options);
            return new Optional<T>(WrapScalar<ImageHash>(value));
        }

        // 复杂 DTO / 数组 / 集合类型：通过 AOT 安全的 options.GetTypeInfo 委托给源生成元数据
        JsonTypeInfo<T> typeInfo = ResolveTypeInfo(options);
        T? deserialized = JsonSerializer.Deserialize(ref reader, typeInfo);
        return new Optional<T>(deserialized!);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
    {
        // 防呆保护：未标记 WhenWritingDefault 时禁止把“未设置”误写成 null，防止意外清空 Discord 服务端字段
        if (!value.HasValue)
        {
            throw new InvalidOperationException(
                $"Cannot serialize an unspecified {nameof(Optional<T>)} value. " +
                $"Apply [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] to the property.");
        }

        T inner = value.Value;
        if (inner is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (typeof(T) == typeof(string))
        {
            writer.WriteStringValue((string)(object)inner);
            return;
        }

        if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?))
        {
            writer.WriteBooleanValue(UnwrapScalar<bool>(inner));
            return;
        }

        if (typeof(T) == typeof(int) || typeof(T) == typeof(int?))
        {
            writer.WriteNumberValue(UnwrapScalar<int>(inner));
            return;
        }

        if (typeof(T) == typeof(long) || typeof(T) == typeof(long?))
        {
            writer.WriteNumberValue(UnwrapScalar<long>(inner));
            return;
        }

        if (typeof(T) == typeof(ulong) || typeof(T) == typeof(ulong?))
        {
            writer.WriteNumberValue(UnwrapScalar<ulong>(inner));
            return;
        }

        if (typeof(T) == typeof(double) || typeof(T) == typeof(double?))
        {
            writer.WriteNumberValue(UnwrapScalar<double>(inner));
            return;
        }

        if (typeof(T) == typeof(Snowflake) || typeof(T) == typeof(Snowflake?))
        {
            s_snowflakeConverter.Write(writer, UnwrapScalar<Snowflake>(inner), options);
            return;
        }

        if (typeof(T) == typeof(ImageHash) || typeof(T) == typeof(ImageHash?))
        {
            s_imageHashConverter.Write(writer, UnwrapScalar<ImageHash>(inner), options);
            return;
        }

        JsonTypeInfo<T> typeInfo = ResolveTypeInfo(options);
        JsonSerializer.Serialize(writer, inner, typeInfo);
    }

    private static JsonTypeInfo<T> ResolveTypeInfo(JsonSerializerOptions options)
    {
        // options.GetTypeInfo 不触发 IL2026/IL3050，要求复杂类型 T 已注册在对应的 JsonSerializerContext 中
        if (options.TryGetTypeInfo(typeof(T), out JsonTypeInfo? rawInfo) && rawInfo is JsonTypeInfo<T> typedInfo)
            return typedInfo;

        throw new NotSupportedException(
            $"JsonTypeInfo metadata for '{typeof(T)}' was not found in {nameof(JsonSerializerOptions)}. " +
            $"Register [JsonSerializable(typeof({typeof(T).Name}))] on your JsonSerializerContext.");
    }

    // 仅在精确匹配 typeof(T) == typeof(TScalar) 或 typeof(TScalar?) 时才执行 Unsafe.As，
    // JIT/NativeAOT 会在编译期静态折叠为常量并消除其余死分支，实现零装箱且 100% 类型安全。
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T WrapScalar<TScalar>(TScalar value) where TScalar : struct
    {
        if (typeof(T) == typeof(TScalar))
            return Unsafe.As<TScalar, T>(ref value);

        if (typeof(T) == typeof(TScalar?))
        {
            TScalar? nullable = value;
            return Unsafe.As<TScalar?, T>(ref nullable);
        }

        throw new UnreachableException();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TScalar UnwrapScalar<TScalar>(T value) where TScalar : struct
    {
        if (typeof(T) == typeof(TScalar))
            return Unsafe.As<T, TScalar>(ref value);

        // 调用方 Write() 已提前拦截 inner is null，此处用 GetValueOrDefault 消除 CS8629 并省去多余的 HasValue 抛异常分支
        if (typeof(T) == typeof(TScalar?))
            return Unsafe.As<T, TScalar?>(ref value).GetValueOrDefault();

        throw new UnreachableException();
    }
}