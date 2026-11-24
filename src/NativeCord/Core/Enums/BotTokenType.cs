namespace NativeCord;

public enum BotTokenType
{
    Bot = 0,
    Bearer = 1,
    Basic = 2
}

public static class BotTokenTypeExtensions
{
    extension(BotTokenType format)
    {
        public string FormatString => format switch
        {
            BotTokenType.Bot => "Bot",
            BotTokenType.Bearer => "Bearer",
            BotTokenType.Basic => "Basic",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The enumeration value is undefined.")
        };
    }
}