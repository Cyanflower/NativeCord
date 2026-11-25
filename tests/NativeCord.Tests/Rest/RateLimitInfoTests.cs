using System.Net;
using System.Text;

using NativeCord.Rest.RateLimiting;

namespace NativeCord.Tests.Rest;

public sealed class RateLimitInfoTests
{
    [Fact]
    public void Parse_ReadsAllDiscordHeadersAccurately()
    {
        using HttpResponseMessage response = new(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("X-RateLimit-Limit", "10");
        response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "9");
        response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", "1470173023.123");
        response.Headers.TryAddWithoutValidation("X-RateLimit-Reset-After", "64.57");
        response.Headers.TryAddWithoutValidation("Retry-After", "65");
        response.Headers.TryAddWithoutValidation("X-RateLimit-Bucket", "abcd1234");
        response.Headers.TryAddWithoutValidation("X-RateLimit-Global", "false");
        response.Headers.TryAddWithoutValidation("X-RateLimit-Scope", "SHARED");

        RateLimitInfo info = RateLimitInfo.Parse(response.Headers);

        Assert.True(info.HasBucketHeaders);
        Assert.Equal(10, info.Limit);
        Assert.Equal(9, info.Remaining);
        Assert.Equal(1470173023.123, info.Reset);
        Assert.Equal(64.57, info.ResetAfterSeconds);
        Assert.Equal(65.0, info.RetryAfterSeconds);
        Assert.Equal("abcd1234", info.Bucket);
        Assert.False(info.IsGlobal);
        Assert.Equal(RateLimitScope.Shared, info.Scope);
    }

    [Fact]
    public void Parse_ScopeGlobalWithoutGlobalHeader_SetsIsGlobalTrue()
    {
        using HttpResponseMessage response = new(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("X-RateLimit-Scope", "global");

        RateLimitInfo info = RateLimitInfo.Parse(response.Headers);

        Assert.True(info.IsGlobal);
        Assert.Equal(RateLimitScope.Global, info.Scope);
    }

    [Fact]
    public void Parse_MissingHeaders_ReturnsEmptyInfo()
    {
        using HttpResponseMessage response = new(HttpStatusCode.OK);

        RateLimitInfo info = RateLimitInfo.Parse(response.Headers);

        Assert.False(info.HasBucketHeaders);
        Assert.Null(info.Limit);
        Assert.Null(info.Remaining);
        Assert.Null(info.Bucket);
        Assert.False(info.IsGlobal);
        Assert.Equal(RateLimitScope.Unknown, info.Scope);
    }

    [Fact]
    public void TryParse429Body_ExtractsRetryAfterAndGlobalIgnoringExtraFields()
    {
        byte[] json = Encoding.UTF8.GetBytes("""
            {
              "message": "You are being rate limited.",
              "nested": { "ignored": [1, 2, 3] },
              "retry_after": 12.345,
              "global": true,
              "code": 20016
            }
            """);

        bool parsed = RateLimitInfo.TryParse429Body(json, out double retryAfter, out bool isGlobal);

        Assert.True(parsed);
        Assert.Equal(12.345, retryAfter);
        Assert.True(isGlobal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>429 Too Many Requests</html>")]
    [InlineData("""{"message":"no retry_after field"}""")]
    [InlineData("[1,2,3]")]
    public void TryParse429Body_InvalidOrIncompletePayload_ReturnsFalse(string body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);

        bool parsed = RateLimitInfo.TryParse429Body(bytes, out double retryAfter, out bool isGlobal);

        Assert.False(parsed);
        Assert.Equal(0, retryAfter);
        Assert.False(isGlobal);
    }
}