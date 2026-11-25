namespace NativeCord.Tests.Core;

public sealed class OptionalTests
{
    [Fact]
    public void DefaultAndNone_AreUnspecifiedAndThrowOnValueAccess()
    {
        Optional<string?> fromDefault = default;
        Optional<string?> fromNone = Optional<string?>.None;

        Assert.False(fromDefault.HasValue);
        Assert.False(fromNone.HasValue);
        Assert.False(fromDefault.TryGetValue(out _));
        Assert.Null(fromDefault.GetValueOrDefault());
        Assert.Equal("fallback", fromDefault.GetValueOrDefault("fallback"));
        Assert.Equal("Unspecified", fromDefault.ToString());

        Assert.Throws<InvalidOperationException>(() => _ = fromDefault.Value);
    }

    [Fact]
    public void ImplicitConversionAndFactory_WrapValuesIncludingExplicitNull()
    {
        Optional<string?> text = "hello";
        Optional<string?> explicitNull = null;
        Optional<Snowflake?> snowflake = new Snowflake(12345UL);
        Optional<Snowflake?> nullSnowflake = null;
        Optional<int> explicitZero = Optional.From(0);

        Assert.True(text.HasValue);
        Assert.Equal("hello", text.Value);
        Assert.True(text.TryGetValue(out string? extracted) && extracted == "hello");

        Assert.True(explicitNull.HasValue);
        Assert.Null(explicitNull.Value);
        Assert.Equal("null", explicitNull.ToString());

        Assert.True(snowflake.HasValue);
        Assert.Equal(new Snowflake(12345UL), snowflake.Value);

        Assert.True(nullSnowflake.HasValue);
        Assert.Null(nullSnowflake.Value);

        Assert.True(explicitZero.HasValue);
        Assert.Equal(0, explicitZero.Value);
    }

    [Fact]
    public void Equality_DistinguishesUnspecifiedFromExplicitNullAndDefaultValues()
    {
        // WhenWritingDefault 依赖 default(Optional<T>) 与显式 null/0/false 不相等，必须严格区分
        Optional<string?> unspecifiedStr = default;
        Optional<string?> explicitNullStr = null;
        Optional<int> unspecifiedInt = default;
        Optional<int> explicitZero = 0;
        Optional<bool> unspecifiedBool = default;
        Optional<bool> explicitFalse = false;

        Assert.NotEqual(unspecifiedStr, explicitNullStr);
        Assert.True(unspecifiedStr != explicitNullStr);
        Assert.NotEqual(unspecifiedInt, explicitZero);
        Assert.NotEqual(unspecifiedBool, explicitFalse);

        Assert.Equal(Optional<string?>.None, unspecifiedStr);
        Assert.Equal(new Optional<string?>(null), explicitNullStr);
        Assert.Equal(new Optional<string?>(null).GetHashCode(), explicitNullStr.GetHashCode());
        Assert.True(Optional.From(42) == new Optional<int>(42));
    }
}