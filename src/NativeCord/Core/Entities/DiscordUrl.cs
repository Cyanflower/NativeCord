namespace NativeCord;

public readonly record struct DiscordUrl(string Value, string? Resource = null)
{
    public override string ToString() => Value;

    public static implicit operator string(DiscordUrl url) => url.Value;
}