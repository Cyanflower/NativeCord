namespace NativeCord;

/// <summary>
/// Represents an authentication token and its scheme for Discord HTTP requests.
/// </summary>
public readonly record struct BotToken(string Value, BotTokenType Type = BotTokenType.Bot)
{
    /// <summary>
    /// Formats the token as an HTTP <c>Authorization</c> header value.
    /// </summary>
    internal string ToAuthorizationHeader()
    {
        // 避免构造出仅有前缀（例如 "Bot "）的非法鉴权头
        ArgumentException.ThrowIfNullOrWhiteSpace(Value);
        return $"{Type.FormatString} {Value}";
    }
}