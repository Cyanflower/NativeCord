
namespace NativeCord;

public sealed record BaseUrl
{
    public int ApiVersion { get; init; } = 10;
    public int VoiceGatewayVersion { get; init; } = 8;
    public string Rest { get; init; } = $"https://discord.com/api";
    public string Cdn { get; init; } = "https://cdn.discordapp.com";
    public string Media { get; init; } = "https://media.discordapp.net";
    public string Invite { get; init; } = "https://discord.gg";
}