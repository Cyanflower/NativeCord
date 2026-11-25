using System.Diagnostics.CodeAnalysis;

namespace NativeCord;

/// <summary>
/// Represents a field that may be omitted (<see cref="HasValue"/> is <see langword="false"/>)
/// or explicitly specified—including explicitly <see langword="null"/>—in Discord PATCH payloads and partial events.
/// </summary>
/// <typeparam name="T">Underlying field type.</typeparam>
// 区分“未传该字段（default，不修改服务端原值）”与“显式传入 null（清空服务端字段）”两种语义
public readonly struct Optional<T> : IEquatable<Optional<T>>
{
    private readonly T _value;

    /// <summary>
    /// Indicates whether a value (including <see langword="null"/>) was explicitly provided.
    /// </summary>
    public bool HasValue { get; }

    /// <summary>
    /// Gets the specified value, or throws <see cref="InvalidOperationException"/> if unspecified.
    /// </summary>
    public T Value => HasValue
        ? _value
        : throw new InvalidOperationException($"Cannot access {nameof(Value)} on an unspecified {nameof(Optional<T>)} instance.");

    /// <summary>
    /// Creates an <see cref="Optional{T}"/> with an explicitly specified value.
    /// </summary>
    public Optional(T value)
    {
        _value = value;
        HasValue = true;
    }

    /// <summary>
    /// Represents an unspecified (omitted) field.
    /// </summary>
    public static Optional<T> None => default;

    /// <summary>
    /// Returns the stored value if specified, or <see langword="default"/> otherwise.
    /// </summary>
    public T? GetValueOrDefault() => HasValue ? _value : default;

    /// <summary>
    /// Returns the stored value if specified, or <paramref name="defaultValue"/> otherwise.
    /// </summary>
    public T GetValueOrDefault(T defaultValue) => HasValue ? _value : defaultValue;

    /// <summary>
    /// Attempts to retrieve the specified value without throwing.
    /// </summary>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return HasValue;
    }

    /// <summary>
    /// Implicitly wraps a value (including <see langword="null"/> for nullable <typeparamref name="T"/>) as a specified optional field.
    /// </summary>
    public static implicit operator Optional<T>(T value) => new(value);

    /// <inheritdoc />
    public bool Equals(Optional<T> other)
    {
        // 关键：HasValue=true 且 Value=null 与 default(HasValue=false) 必须判为不相等，
        // 这样 JsonIgnoreCondition.WhenWritingDefault 才会仅省略 None，而保留显式 null。
        if (HasValue != other.HasValue)
            return false;

        return !HasValue || EqualityComparer<T>.Default.Equals(_value, other._value);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Optional<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        if (!HasValue)
            return 0;

        return HashCode.Combine(true, _value);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (!HasValue)
            return "Unspecified";

        return _value?.ToString() ?? "null";
    }

    public static bool operator ==(Optional<T> left, Optional<T> right) => left.Equals(right);

    public static bool operator !=(Optional<T> left, Optional<T> right) => !left.Equals(right);
}

/// <summary>
/// Convenience factory methods for creating <see cref="Optional{T}"/> values.
/// </summary>
public static class Optional
{
    /// <summary>
    /// Wraps <paramref name="value"/> as a specified <see cref="Optional{T}"/>.
    /// </summary>
    public static Optional<T> From<T>(T value) => new(value);
}