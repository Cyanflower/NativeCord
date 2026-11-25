using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using NativeCord.Serialization;

namespace NativeCord;

[JsonConverter(typeof(SnowflakeJsonConverter))]
public readonly record struct Snowflake(ulong Value)
    : IComparable<Snowflake>,
      ISpanFormattable,
      IUtf8SpanFormattable,
      ISpanParsable<Snowflake>,
      IUtf8SpanParsable<Snowflake>
{
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => Value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        => Value.TryFormat(destination, out charsWritten, format, provider ?? CultureInfo.InvariantCulture);

    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        => Value.TryFormat(utf8Destination, out bytesWritten, format, provider ?? CultureInfo.InvariantCulture);

    public static Snowflake Parse(string s, IFormatProvider? provider)
        => new(ulong.Parse(s, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Snowflake result)
    {
        if (ulong.TryParse(s, provider ?? CultureInfo.InvariantCulture, out var value))
        {
            result = new Snowflake(value);
            return true;
        }

        result = default;
        return false;
    }

    public static Snowflake Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => new(ulong.Parse(s, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Snowflake result)
    {
        if (ulong.TryParse(s, provider ?? CultureInfo.InvariantCulture, out var value))
        {
            result = new Snowflake(value);
            return true;
        }

        result = default;
        return false;
    }

    public static Snowflake Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
        => new(ulong.Parse(utf8Text, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out Snowflake result)
    {
        if (ulong.TryParse(utf8Text, provider ?? CultureInfo.InvariantCulture, out var value))
        {
            result = new Snowflake(value);
            return true;
        }

        result = default;
        return false;
    }

    public static Snowflake FromTimestamp(DateTimeOffset timestamp)
    => FromUnixTimeMilliseconds(timestamp.ToUnixTimeMilliseconds());

    public static Snowflake FromUnixTimeMilliseconds(long unixTimeMilliseconds)
    {
        const long minTimestamp = (long)DiscordEpoch.Timestamp;

        const long maxTimestamp = (long)(DiscordEpoch.Timestamp + (ulong.MaxValue >> DiscordEpoch.TimestampShift));

        ArgumentOutOfRangeException.ThrowIfLessThan(unixTimeMilliseconds, minTimestamp);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(unixTimeMilliseconds, maxTimestamp);

        ulong elapsedMilliseconds = (ulong)(unixTimeMilliseconds - minTimestamp);
        return new Snowflake(elapsedMilliseconds << DiscordEpoch.TimestampShift);
    }

    public static implicit operator ulong(Snowflake snowflake) => snowflake.Value;

    public long UnixTimeMilliseconds => (long)((Value >> DiscordEpoch.TimestampShift) + DiscordEpoch.Timestamp);

    public DateTimeOffset Timestamp => DateTimeOffset.FromUnixTimeMilliseconds(UnixTimeMilliseconds);

    public int CompareTo(Snowflake other) => Value.CompareTo(other.Value);

    public static bool operator <(Snowflake left, Snowflake right) => left.Value < right.Value;

    public static bool operator <=(Snowflake left, Snowflake right) => left.Value <= right.Value;

    public static bool operator >(Snowflake left, Snowflake right) => left.Value > right.Value;

    public static bool operator >=(Snowflake left, Snowflake right) => left.Value >= right.Value;
}