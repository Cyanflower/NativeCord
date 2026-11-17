using System.Globalization;

namespace NativeCord;

public readonly record struct Snowflake(ulong Value) : IComparable<Snowflake>, ISpanFormattable, IUtf8SpanFormattable
{
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => Value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        => Value.TryFormat(destination, out charsWritten, format, provider ?? CultureInfo.InvariantCulture);

    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        => Value.TryFormat(utf8Destination, out bytesWritten, format, provider ?? CultureInfo.InvariantCulture);

    public static implicit operator string(Snowflake snowflake) => snowflake.Value.ToString(CultureInfo.InvariantCulture);

    public static implicit operator ulong(Snowflake snowflake) => snowflake.Value;

    public long UnixTimeMilliseconds => (long)((Value >> DiscordEpoch.TimestampShift) + DiscordEpoch.Timestamp);

    public DateTimeOffset Timestamp => DateTimeOffset.FromUnixTimeMilliseconds(UnixTimeMilliseconds);

    public int CompareTo(Snowflake other) => Value.CompareTo(other.Value);

    public static bool operator <(Snowflake left, Snowflake right) => left.Value < right.Value;

    public static bool operator <=(Snowflake left, Snowflake right) => left.Value <= right.Value;

    public static bool operator >(Snowflake left, Snowflake right) => left.Value > right.Value;

    public static bool operator >=(Snowflake left, Snowflake right) => left.Value >= right.Value;
}