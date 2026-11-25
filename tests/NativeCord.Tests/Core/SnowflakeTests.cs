using System.Globalization;
using System.Text;

namespace NativeCord.Tests.Core;

public sealed class SnowflakeTests
{
    private const ulong SampleId = 175928847299117063UL;
    private const string SampleText = "175928847299117063";

    [Fact]
    public void ParseAndTryParse_SupportsStringCharSpanAndUtf8Span()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(SampleText);

        Assert.Equal(SampleId, Snowflake.Parse(SampleText, CultureInfo.InvariantCulture).Value);
        Assert.Equal(SampleId, Snowflake.Parse(SampleText.AsSpan(), CultureInfo.InvariantCulture).Value);
        Assert.Equal(SampleId, Snowflake.Parse(utf8, CultureInfo.InvariantCulture).Value);

        Assert.True(Snowflake.TryParse(SampleText, CultureInfo.InvariantCulture, out Snowflake fromString));
        Assert.True(Snowflake.TryParse(SampleText.AsSpan(), CultureInfo.InvariantCulture, out Snowflake fromChars));
        Assert.True(Snowflake.TryParse(utf8, CultureInfo.InvariantCulture, out Snowflake fromUtf8));

        Assert.Equal(SampleId, fromString.Value);
        Assert.Equal(SampleId, fromChars.Value);
        Assert.Equal(SampleId, fromUtf8.Value);

        Assert.False(Snowflake.TryParse("not-a-snowflake", CultureInfo.InvariantCulture, out _));
        Assert.False(Snowflake.TryParse("not-a-snowflake"u8, CultureInfo.InvariantCulture, out _));
    }

    [Fact]
    public void TryFormat_FormatsToCharAndUtf8Spans()
    {
        Snowflake snowflake = new(SampleId);

        Span<char> charBuffer = stackalloc char[32];
        Assert.True(snowflake.TryFormat(charBuffer, out int charsWritten, default, CultureInfo.InvariantCulture));
        Assert.Equal(SampleText, charBuffer[..charsWritten].ToString());

        Span<byte> utf8Buffer = stackalloc byte[32];
        Assert.True(snowflake.TryFormat(utf8Buffer, out int bytesWritten, default, CultureInfo.InvariantCulture));
        Assert.Equal(SampleText, Encoding.UTF8.GetString(utf8Buffer[..bytesWritten]));

        Span<char> tinyBuffer = stackalloc char[4];
        Assert.False(snowflake.TryFormat(tinyBuffer, out _, default, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FromTimestamp_RoundTripsAndValidatesDiscordEpochBounds()
    {
        DateTimeOffset expected = new(2026, 6, 15, 12, 30, 45, 123, TimeSpan.Zero);
        Snowflake snowflake = Snowflake.FromTimestamp(expected);

        Assert.Equal(expected.ToUnixTimeMilliseconds(), snowflake.UnixTimeMilliseconds);
        Assert.Equal(expected, snowflake.Timestamp);

        // 早于 Discord Epoch (2015-01-01T00:00:00Z) 应抛出 ArgumentOutOfRangeException
        DateTimeOffset beforeEpoch = new(2014, 12, 31, 23, 59, 59, TimeSpan.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => Snowflake.FromTimestamp(beforeEpoch));
    }

    [Fact]
    public void ComparisonOperators_OrderByUnderlyingValue()
    {
        Snowflake earlier = new(100UL);
        Snowflake later = new(200UL);

        Assert.True(earlier < later);
        Assert.True(earlier <= later);
        Assert.True(later > earlier);
        Assert.True(later >= earlier);
        Assert.True(earlier.CompareTo(later) < 0);
    }
}